using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Sm64DecompLevelViewer.Services
{
    public class SampleSynthPlayer : IDisposable
    {
        private const int WAVE_MAPPER = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public short wFormatTag;
            public short nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public short nBlockAlign;
            public short wBitsPerSample;
            public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr phwo, int uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, int fdwOpen);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hwo);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hwo);

        private class Voice
        {
            public int Channel;
            public int Pitch;
            public float[] Samples;
            public int Channels;
            public double Position;
            public double PitchRatio;
            public double VolumeLeft;
            public double VolumeRight;
            public bool ShouldLoop;
            public bool IsFinished;
            public bool IsStopping;
            public float FadeVolume = 1.0f;
        }

        private class CachedSample
        {
            public float[] Samples;
            public int SampleRate;
            public int Channels;
            public bool IsLooped;
        }

        private class AudioBuffer
        {
            public IntPtr hHeader; // allocated on native heap via AllocHGlobal
            public byte[] Bytes;
            public GCHandle DataHandle; // pins Bytes
        }

        private const int NUM_BUFFERS = 4;
        private const int BUFFER_DURATION_MS = 30;
        private const int SAMPLE_RATE = 44100;
        private const int CHANNELS = 2;
        private const int BYTES_PER_SAMPLE = 2;
        private const int FRAME_SIZE = CHANNELS * BYTES_PER_SAMPLE;
        private const int BUFFER_SIZE = (SAMPLE_RATE * BUFFER_DURATION_MS / 1000) * FRAME_SIZE;

        private static readonly Dictionary<string, CachedSample> _sampleCache = new();

        private readonly object _lock = new();
        private readonly List<Voice> _activeVoices = new();

        private AudioBuffer[] _buffers;
        private IntPtr _hWaveOut = IntPtr.Zero;
        private readonly AutoResetEvent _bufferEvent = new(false);
        private Thread? _mixerThread;
        private volatile bool _running;
        public bool EnableReleaseFade { get; set; } = true;
        
        private float[] _reverbBufferL = new float[2646];
        private float[] _reverbBufferR = new float[2646];
        private int _reverbWritePos = 0;

        public SampleSynthPlayer(string conversationId)
        {
            try
            {
                Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error starting SampleSynthPlayer: {ex.Message}");
            }
        }

        private void Start()
        {
            if (_hWaveOut != IntPtr.Zero) return;

            WAVEFORMATEX format = new WAVEFORMATEX
            {
                wFormatTag = 1, // PCM
                nChannels = CHANNELS,
                nSamplesPerSec = SAMPLE_RATE,
                wBitsPerSample = (short)(BYTES_PER_SAMPLE * 8),
                nBlockAlign = (short)FRAME_SIZE,
                nAvgBytesPerSec = SAMPLE_RATE * FRAME_SIZE,
                cbSize = 0
            };

            const int CALLBACK_EVENT = 0x00050000;
            IntPtr hEvent = _bufferEvent.SafeWaitHandle.DangerousGetHandle();

            int result = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref format, hEvent, IntPtr.Zero, CALLBACK_EVENT);
            if (result != 0)
            {
                _hWaveOut = IntPtr.Zero;
                throw new Exception($"Failed to open waveOut device: {result}");
            }

            _buffers = new AudioBuffer[NUM_BUFFERS];
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));

            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                var buf = new AudioBuffer();
                buf.Bytes = new byte[BUFFER_SIZE];
                buf.DataHandle = GCHandle.Alloc(buf.Bytes, GCHandleType.Pinned);
                
                WAVEHDR header = new WAVEHDR
                {
                    lpData = buf.DataHandle.AddrOfPinnedObject(),
                    dwBufferLength = BUFFER_SIZE,
                    dwFlags = 0,
                    dwLoops = 0
                };

                buf.hHeader = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(header, buf.hHeader, false);

                int prepResult = waveOutPrepareHeader(_hWaveOut, buf.hHeader, headerSize);
                if (prepResult != 0)
                {
                    throw new Exception($"Failed to prepare waveOut header: {prepResult}");
                }

                _buffers[i] = buf;
            }

            _running = true;
            _mixerThread = new Thread(MixerLoop)
            {
                IsBackground = true,
                Name = "SampleSynthPlayerMixerThread",
                Priority = ThreadPriority.Highest
            };
            _mixerThread.Start();

            // Queue initial silence
            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                Array.Clear(_buffers[i].Bytes, 0, _buffers[i].Bytes.Length);
                waveOutWrite(_hWaveOut, _buffers[i].hHeader, headerSize);
            }
        }

        private void MixerLoop()
        {
            int framesPerBuffer = BUFFER_SIZE / FRAME_SIZE;
            short[] mixBuffer = new short[framesPerBuffer * CHANNELS];
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));

            while (_running)
            {
                _bufferEvent.WaitOne(100);
                if (!_running) break;

                for (int i = 0; i < NUM_BUFFERS; i++)
                {
                    var buf = _buffers[i];
                    
                    WAVEHDR hdr = Marshal.PtrToStructure<WAVEHDR>(buf.hHeader);

                    if ((hdr.dwFlags & 0x00000001) != 0) // WHDR_DONE
                    {
                        hdr.dwFlags &= ~0x00000001;
                        Marshal.StructureToPtr(hdr, buf.hHeader, false);

                        MixAudio(mixBuffer, framesPerBuffer);
                        Buffer.BlockCopy(mixBuffer, 0, buf.Bytes, 0, BUFFER_SIZE);

                        if (_running && _hWaveOut != IntPtr.Zero)
                        {
                            waveOutWrite(_hWaveOut, buf.hHeader, headerSize);
                        }
                    }
                }
            }
        }

        private void MixAudio(short[] outBuffer, int framesToMix)
        {
            Array.Clear(outBuffer, 0, outBuffer.Length);

            List<Voice> voices;
            lock (_lock)
            {
                voices = _activeVoices.Where(v => !v.IsFinished).ToList();
            }

            if (voices.Count == 0) return;

            for (int f = 0; f < framesToMix; f++)
            {
                float mixedL = 0f;
                float mixedR = 0f;

                foreach (var voice in voices)
                {
                    if (voice.IsFinished) continue;

                    int frameLen = voice.Samples.Length / voice.Channels;
                    int idx1 = (int)voice.Position;

                    if (idx1 >= frameLen)
                    {
                        if (voice.ShouldLoop)
                        {
                            voice.Position = voice.Position % frameLen;
                            idx1 = (int)voice.Position;
                        }
                        else
                        {
                            voice.IsFinished = true;
                            continue;
                        }
                    }

                    int idx2 = idx1 + 1;
                    if (idx2 >= frameLen)
                    {
                        if (voice.ShouldLoop)
                        {
                            idx2 = 0;
                        }
                        else
                        {
                            idx2 = idx1;
                        }
                    }

                    double t = voice.Position - idx1;
                    
                    if (voice.Channels == 2)
                    {
                        int left1 = idx1 * 2;
                        int right1 = idx1 * 2 + 1;
                        int left2 = idx2 * 2;
                        int right2 = idx2 * 2 + 1;

                        float sampleL = (float)(voice.Samples[left1] * (1.0 - t) + voice.Samples[left2] * t);
                        float sampleR = (float)(voice.Samples[right1] * (1.0 - t) + voice.Samples[right2] * t);

                        if (voice.IsStopping)
                        {
                            if (EnableReleaseFade)
                            {
                                voice.FadeVolume -= 1.0f / 6615f;
                                if (voice.FadeVolume <= 0f)
                                {
                                    voice.FadeVolume = 0f;
                                    voice.IsFinished = true;
                                }
                            }
                            else
                            {
                                voice.FadeVolume = 0f;
                                voice.IsFinished = true;
                            }
                        }

                        sampleL *= voice.FadeVolume;
                        sampleR *= voice.FadeVolume;

                        mixedL += sampleL * (float)voice.VolumeLeft;
                        mixedR += sampleR * (float)voice.VolumeRight;
                    }
                    else
                    {
                        float sampleVal = (float)(voice.Samples[idx1] * (1.0 - t) + voice.Samples[idx2] * t);

                        if (voice.IsStopping)
                        {
                            if (EnableReleaseFade)
                            {
                                voice.FadeVolume -= 1.0f / 6615f;
                                if (voice.FadeVolume <= 0f)
                                {
                                    voice.FadeVolume = 0f;
                                    voice.IsFinished = true;
                                }
                            }
                            else
                            {
                                voice.FadeVolume = 0f;
                                voice.IsFinished = true;
                            }
                        }

                        sampleVal *= voice.FadeVolume;

                        mixedL += sampleVal * (float)voice.VolumeLeft;
                        mixedR += sampleVal * (float)voice.VolumeRight;
                    }

                    voice.Position += voice.PitchRatio;
                }

                // Apply simple, beautiful stereo hall reverb
                float delaySampleL = _reverbBufferL[_reverbWritePos];
                float delaySampleR = _reverbBufferR[_reverbWritePos];

                float wetL = mixedL + delaySampleL * 0.45f;
                float wetR = mixedR + delaySampleR * 0.45f;

                _reverbBufferL[_reverbWritePos] = wetL;
                _reverbBufferR[_reverbWritePos] = wetR;

                _reverbWritePos = (_reverbWritePos + 1) % 2646;

                // Combine dry and wet signals
                mixedL = mixedL * 0.70f + delaySampleL * 0.30f;
                mixedR = mixedR * 0.70f + delaySampleR * 0.30f;

                if (mixedL > 1.0f) mixedL = 1.0f;
                else if (mixedL < -1.0f) mixedL = -1.0f;

                if (mixedR > 1.0f) mixedR = 1.0f;
                else if (mixedR < -1.0f) mixedR = -1.0f;

                outBuffer[f * 2] = (short)(mixedL * 32767f);
                outBuffer[f * 2 + 1] = (short)(mixedR * 32767f);
            }

            lock (_lock)
            {
                _activeVoices.RemoveAll(v => v.IsFinished);
            }
        }

        public void PlayNote(byte channel, byte pitch, string samplePath, byte velocity, byte channelVolume, byte channelPan, double tuning = 0.0)
        {
            Task.Run(() =>
            {
                try
                {
                    var sample = GetOrLoadSample(samplePath);
                    if (sample == null || sample.Samples == null || sample.Samples.Length == 0) return;

                    bool shouldLoop = sample.IsLooped;
                    string sampleName = Path.GetFileName(samplePath).ToLower();
                    if (sampleName.Contains("piano") ||
                        sampleName.Contains("harpsichord") ||
                        sampleName.Contains("music_box") ||
                        sampleName.Contains("banjo") ||
                        sampleName.Contains("guitar") ||
                        sampleName.Contains("timpani") ||
                        sampleName.Contains("orchestra_hit") ||
                        sampleName.Contains("pizzicato"))
                    {
                        shouldLoop = false;
                    }
                    double vol = (velocity / 127.0) * (channelVolume / 127.0) * 0.30;

                    double leftPan = 1.0;
                    double rightPan = 1.0;
                    if (channelPan < 64)
                    {
                        rightPan = channelPan / 64.0;
                    }
                    else if (channelPan > 64)
                    {
                        leftPan = (127 - channelPan) / 63.0;
                    }

                    // N64 Audio Engine Pitch Math:
                    // reference pitch is D#2 (note 39)
                    double baseRate = tuning > 0.0 ? 32000.0 * tuning : sample.SampleRate;
                    double playbackRate = baseRate * Math.Pow(2.0, (pitch - 39) / 12.0);
                    double pitchRatio = playbackRate / SAMPLE_RATE;

                    // Instantly fade out existing voice on same channel and pitch
                    StopNote(channel, pitch);

                    var voice = new Voice
                    {
                        Channel = channel,
                        Pitch = pitch,
                        Samples = sample.Samples,
                        Channels = sample.Channels,
                        Position = 0.0,
                        PitchRatio = pitchRatio,
                        VolumeLeft = vol * leftPan,
                        VolumeRight = vol * rightPan,
                        ShouldLoop = shouldLoop,
                        IsFinished = false,
                        IsStopping = false,
                        FadeVolume = 1.0f
                    };

                    lock (_lock)
                    {
                        _activeVoices.Add(voice);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error playing note: {ex.Message}");
                }
            });
        }

        public void StopNote(byte channel, byte pitch)
        {
            lock (_lock)
            {
                foreach (var voice in _activeVoices)
                {
                    if (voice.Channel == channel && voice.Pitch == pitch)
                    {
                        voice.IsStopping = true;
                    }
                }
            }
        }

        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var voice in _activeVoices)
                {
                    voice.IsStopping = true;
                }
            }
        }

        private static CachedSample? GetOrLoadSample(string samplePath)
        {
            if (string.IsNullOrEmpty(samplePath) || !File.Exists(samplePath)) return null;

            lock (_sampleCache)
            {
                if (_sampleCache.TryGetValue(samplePath, out var cached))
                {
                    return cached;
                }
            }

            try
            {
                byte[] aiffBytes = SafeReadAllBytes(samplePath);
                byte[] wavBytes = AiffWavTranscoder.ConvertAiffToWav(aiffBytes);
                if (wavBytes == null || wavBytes.Length == 0) return null;

                var sample = ParseWavBytes(wavBytes);
                if (sample != null)
                {
                    sample.IsLooped = CheckAiffLooped(aiffBytes);
                    lock (_sampleCache)
                    {
                        _sampleCache[samplePath] = sample;
                    }
                }
                return sample;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading sample {samplePath}: {ex.Message}");
                return null;
            }
        }

        private static bool CheckAiffLooped(byte[] aiffData)
        {
            try
            {
                if (aiffData.Length < 12) return false;
                int pos = 12;
                while (pos + 8 <= aiffData.Length)
                {
                    string chunkName = System.Text.Encoding.ASCII.GetString(aiffData, pos, 4);
                    pos += 4;
                    int chunkSize = (aiffData[pos] << 24) | (aiffData[pos + 1] << 16) | (aiffData[pos + 2] << 8) | aiffData[pos + 3];
                    pos += 4;

                    if (chunkName == "INST")
                    {
                        if (chunkSize >= 14 && pos + 14 <= aiffData.Length)
                        {
                            int playMode = (aiffData[pos + 8] << 8) | aiffData[pos + 9];
                            return (playMode == 1 || playMode == 2);
                        }
                    }
                    pos += (chunkSize + 1) & ~1;
                }
            }
            catch
            {
            }
            return false;
        }

        private static byte[] SafeReadAllBytes(string filePath)
        {
            try
            {
                var fi = new FileInfo(filePath);
                fi.Refresh();
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] bytes = new byte[fs.Length];
                    int bytesRead = 0;
                    int numToRead = bytes.Length;
                    while (numToRead > 0)
                    {
                        int n = fs.Read(bytes, bytesRead, numToRead);
                        if (n == 0) break;
                        bytesRead += n;
                        numToRead -= n;
                    }
                    return bytes;
                }
            }
            catch
            {
                return File.ReadAllBytes(filePath);
            }
        }

        private static CachedSample? ParseWavBytes(byte[] wavBytes)
        {
            if (wavBytes == null || wavBytes.Length < 44) return null;

            try
            {
                string riff = System.Text.Encoding.ASCII.GetString(wavBytes, 0, 4);
                string wave = System.Text.Encoding.ASCII.GetString(wavBytes, 8, 4);
                if (riff != "RIFF" || wave != "WAVE") return null;

                int channels = 0;
                int sampleRate = 0;
                int bitsPerSample = 0;
                int dataOffset = 0;
                int dataLength = 0;

                int pos = 12;
                while (pos + 8 <= wavBytes.Length)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(wavBytes, pos, 4);
                    int chunkSize = BitConverter.ToInt32(wavBytes, pos + 4);
                    pos += 8;

                    if (chunkId == "fmt ")
                    {
                        channels = BitConverter.ToInt16(wavBytes, pos + 2);
                        sampleRate = BitConverter.ToInt32(wavBytes, pos + 4);
                        bitsPerSample = BitConverter.ToInt16(wavBytes, pos + 14);
                    }
                    else if (chunkId == "data")
                    {
                        dataOffset = pos;
                        dataLength = chunkSize;
                        break;
                    }

                    pos += chunkSize;
                }

                if (channels == 0 || sampleRate == 0 || bitsPerSample == 0 || dataOffset == 0)
                {
                    return null;
                }

                if (dataOffset + dataLength > wavBytes.Length)
                {
                    dataLength = wavBytes.Length - dataOffset;
                }

                int bytesPerSample = bitsPerSample / 8;
                int totalSamples = dataLength / bytesPerSample;
                float[] samples = new float[totalSamples];

                if (bitsPerSample == 16)
                {
                    for (int i = 0; i < totalSamples; i++)
                    {
                        short val = BitConverter.ToInt16(wavBytes, dataOffset + i * 2);
                        samples[i] = val / 32768f;
                    }
                }
                else if (bitsPerSample == 8)
                {
                    for (int i = 0; i < totalSamples; i++)
                    {
                        byte val = wavBytes[dataOffset + i];
                        samples[i] = (val - 128) / 128f;
                    }
                }
                else
                {
                    return null;
                }

                return new CachedSample
                {
                    Samples = samples,
                    SampleRate = sampleRate,
                    Channels = channels
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing WAV bytes: {ex.Message}");
                return null;
            }
        }

        private static int GetMidiPitchFromNoteName(string name)
        {
            name = name.ToUpper();
            
            // Get filename without extension and locate the last part after underscore
            string fileName = Path.GetFileNameWithoutExtension(name);
            int lastUnderscore = fileName.LastIndexOf('_');
            string noteStr = lastUnderscore >= 0 ? fileName.Substring(lastUnderscore + 1) : fileName;
            
            if (noteStr.Length == 0) return 60;
            
            // Note part is before the octave number (which starts with a digit or '-')
            int octaveIndex = -1;
            for (int i = 0; i < noteStr.Length; i++)
            {
                if (char.IsDigit(noteStr[i]) || noteStr[i] == '-')
                {
                    octaveIndex = i;
                    break;
                }
            }
            
            if (octaveIndex == -1) return 60;
            
            string notePart = noteStr.Substring(0, octaveIndex);
            string octavePart = noteStr.Substring(octaveIndex);
            
            if (!int.TryParse(octavePart, out int octave)) octave = 4;
            
            int noteOffset = 0;
            switch (notePart[0])
            {
                case 'C': noteOffset = 0; break;
                case 'D': noteOffset = 2; break;
                case 'E': noteOffset = 4; break;
                case 'F': noteOffset = 5; break;
                case 'G': noteOffset = 7; break;
                case 'A': noteOffset = 9; break;
                case 'B': noteOffset = 11; break;
            }
            
            if (notePart.Contains("#")) noteOffset += 1;
            else if (notePart.Contains("B") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("E") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("A") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("D") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            else if (notePart.Contains("G") && notePart.Length > 1 && notePart[1] == 'B') noteOffset -= 1;
            
            return (octave + 1) * 12 + noteOffset;
        }

        public void Dispose()
        {
            if (!_running) return;

            _running = false;
            _bufferEvent.Set();

            if (_mixerThread != null && _mixerThread.IsAlive)
            {
                _mixerThread.Join(500);
            }

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);

                if (_buffers != null)
                {
                    int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
                    foreach (var buf in _buffers)
                    {
                        if (buf.hHeader != IntPtr.Zero)
                        {
                            waveOutUnprepareHeader(_hWaveOut, buf.hHeader, headerSize);
                            Marshal.FreeHGlobal(buf.hHeader);
                        }
                        if (buf.DataHandle.IsAllocated)
                        {
                            buf.DataHandle.Free();
                        }
                    }
                }

                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }

            _bufferEvent.Dispose();
        }
    }
}
