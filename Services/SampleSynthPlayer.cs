using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

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
            public double ChannelGain;
            public double TargetChannelGain;
            public double ReverbSend;
            public bool IsFinished;
            public bool IsStopping;
            public int Layer = -1;
            public AiffLoop? Loop;
            public int LoopsRemaining;
            public bool Backwards;
            public long FramesUntilRelease = long.MaxValue;
            public Sm64Envelope Envelope = null!;
        }

        private class CachedSample
        {
            public float[] Samples;
            public int SampleRate;
            public int Channels;
            public AiffLoop? Loop;
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
        private readonly Dictionary<byte, byte> _channelVolumeOverrides = new();
        private readonly Sm64Reverb _reverb = new(SAMPLE_RATE);

        private AudioBuffer[] _buffers;
        private IntPtr _hWaveOut = IntPtr.Zero;
        private readonly AutoResetEvent _bufferEvent = new(false);
        private Thread? _mixerThread;
        private volatile bool _running;

        public SampleSynthPlayer(string conversationId) : this(true) { }

        internal SampleSynthPlayer(bool openAudioDevice)
        {
            if (!openAudioDevice) return;
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

            // Queue initial silence
            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                Array.Clear(_buffers[i].Bytes, 0, _buffers[i].Bytes.Length);
                waveOutWrite(_hWaveOut, _buffers[i].hHeader, headerSize);
            }

            _running = true;
            _mixerThread = new Thread(MixerLoop)
            {
                IsBackground = true,
                Name = "SampleSynthPlayerMixerThread",
                Priority = ThreadPriority.Highest
            };
            _mixerThread.Start();
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

        internal void MixAudio(short[] outBuffer, int framesToMix)
        {
            Array.Clear(outBuffer);
            lock (_lock)
            {
                for (int f = 0; f < framesToMix; f++)
                {
                    double mixedL = 0, mixedR = 0, sendL = 0, sendR = 0;
                    foreach (var voice in _activeVoices)
                    {
                        if (voice.IsFinished) continue;
                        if (voice.FramesUntilRelease <= 0) voice.IsStopping = true;
                        else if (voice.FramesUntilRelease != long.MaxValue) voice.FramesUntilRelease--;
                        int frameCount = voice.Samples.Length / voice.Channels;
                        if (voice.Loop is AiffLoop loop && voice.LoopsRemaining != 0)
                        {
                            if (loop.PingPong)
                            {
                                // Reflect at sample centers; process even large resampling steps.
                                int reflections = 0;
                                while ((voice.Position > loop.End - 1 || (voice.Backwards && voice.Position < loop.Start)) && reflections++ < 1024)
                                {
                                    if (voice.Position > loop.End - 1)
                                    {
                                        voice.Position = 2 * (loop.End - 1) - voice.Position;
                                        voice.Backwards = true;
                                    }
                                    else
                                    {
                                        voice.Position = 2 * loop.Start - voice.Position;
                                        voice.Backwards = false;
                                    }
                                }
                            }
                            else if (voice.Position >= loop.End)
                            {
                                int wraps = (int)((voice.Position - loop.Start) / (loop.End - loop.Start));
                                if (voice.LoopsRemaining > 0) wraps = Math.Min(wraps, voice.LoopsRemaining);
                                voice.Position -= wraps * (loop.End - loop.Start);
                                if (voice.LoopsRemaining > 0) voice.LoopsRemaining -= wraps;
                            }
                        }
                        if (voice.Position < 0 || voice.Position >= frameCount)
                        {
                            voice.IsFinished = true;
                            continue;
                        }
                        int first = (int)voice.Position;
                        int second = Math.Min(first + 1, frameCount - 1);
                        if (voice.Loop is AiffLoop boundary && !boundary.PingPong && voice.LoopsRemaining != 0 && second >= boundary.End)
                            second = boundary.Start;
                        double fraction = voice.Position - first;
                        double envelope = voice.Envelope.Next(voice.IsStopping);
                        if (voice.Envelope.Finished) { voice.IsFinished = true; continue; }
                        // Smooth live slider changes over 10 ms without restarting
                        // the sample, changing its pan, or dividing by an old zero gain.
                        const double gainStep = 1.0 / (SAMPLE_RATE * 0.01);
                        voice.ChannelGain += Math.Clamp(voice.TargetChannelGain - voice.ChannelGain, -gainStep, gainStep);
                        envelope *= voice.ChannelGain;
                        double left = voice.Samples[first * voice.Channels] * (1 - fraction) + voice.Samples[second * voice.Channels] * fraction;
                        int rightChannel = voice.Channels == 2 ? 1 : 0;
                        double right = voice.Samples[first * voice.Channels + rightChannel] * (1 - fraction) + voice.Samples[second * voice.Channels + rightChannel] * fraction;
                        mixedL += left * voice.VolumeLeft * envelope;
                        mixedR += right * voice.VolumeRight * envelope;
                        sendL += left * voice.VolumeLeft * envelope * voice.ReverbSend;
                        sendR += right * voice.VolumeRight * envelope * voice.ReverbSend;
                        voice.Position += voice.Backwards ? -voice.PitchRatio : voice.PitchRatio;
                    }
                    var wet = _reverb.Process(sendL * 0.25, sendR * 0.25);
                    // Fixed headroom preserves dynamics as polyphony changes.
                    outBuffer[f * 2] = (short)(Math.Clamp(mixedL * 0.25 + wet.Left, -1, 1) * 32767);
                    outBuffer[f * 2 + 1] = (short)(Math.Clamp(mixedR * 0.25 + wet.Right, -1, 1) * 32767);
                }
                _activeVoices.RemoveAll(v => v.IsFinished);
            }
        }

        public static void Preload(string samplePath) => GetOrLoadSample(samplePath);

        public void PlayNote(byte channel, byte pitch, string samplePath, byte velocity, byte channelVolume,
            byte channelPan, double tuning = 0, Sm64Instrument? instrument = null, int layer = -1,
            double? durationSeconds = null, byte reverb = 0)
        {
            // Start in caller order. Per-note Task.Run allowed note-offs to overtake
            // note-ons and produced stuck notes and inconsistent chord attacks.
            var sample = GetOrLoadSample(samplePath);
            if (sample == null || sample.Samples.Length == 0) return;
            double baseRate = tuning > 0 ? 32000 * tuning : sample.SampleRate;
            double frequency = instrument?.IsPercussion == true ? 1 : Math.Pow(2, (pitch - 39) / 12.0);
            if (pitch >= 117 && instrument?.IsPercussion != true) frequency *= 0.5;
            double volume = Math.Pow(Math.Min((int)velocity, 127) / 127.0, 2);
            double pan = Math.Clamp((int)(instrument?.Pan ?? channelPan), 0, 127) / 127.0;
            var voice = new Voice
            {
                Channel = channel, Pitch = pitch, Layer = layer,
                Samples = sample.Samples, Channels = sample.Channels,
                PitchRatio = Math.Min(baseRate * frequency, 32000 * 3.99992) / SAMPLE_RATE,
                VolumeLeft = volume * Math.Cos(pan * Math.PI / 2),
                VolumeRight = volume * Math.Sin(pan * Math.PI / 2),
                ChannelGain = Math.Min((int)channelVolume, 127) / 127.0,
                TargetChannelGain = Math.Min((int)channelVolume, 127) / 127.0,
                ReverbSend = Math.Min((int)reverb, 127) / 127.0,
                Loop = sample.Loop, LoopsRemaining = sample.Loop?.Count ?? 0,
                FramesUntilRelease = durationSeconds.HasValue ? Math.Max(1, (long)Math.Round(durationSeconds.Value * SAMPLE_RATE)) : long.MaxValue,
                Envelope = new Sm64Envelope(instrument?.Envelope ?? Array.Empty<Sm64EnvelopePoint>(), instrument?.ReleaseRate ?? 32, SAMPLE_RATE)
            };
            lock (_lock)
            {
                if (_channelVolumeOverrides.TryGetValue(channel, out byte overrideVolume))
                    voice.ChannelGain = voice.TargetChannelGain = overrideVolume / 127.0;
                foreach (var previous in _activeVoices)
                    if (previous.Channel == channel && (layer >= 0 ? previous.Layer == layer : previous.Layer == -1 && previous.Pitch == pitch))
                        previous.IsStopping = true;
                _activeVoices.Add(voice);
            }
        }

        public void SetChannelVolume(byte channel, byte volume)
        {
            volume = (byte)Math.Min((int)volume, 127);
            lock (_lock)
            {
                _channelVolumeOverrides[channel] = volume;
                foreach (var voice in _activeVoices)
                    if (voice.Channel == channel) voice.TargetChannelGain = volume / 127.0;
            }
        }

        // Transport stop/seek retains edits; loading another sequence clears them.
        public void ClearChannelVolumeOverrides()
        {
            lock (_lock) _channelVolumeOverrides.Clear();
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

        public void StopChannel(byte channel)
        {
            lock (_lock)
            {
                foreach (var voice in _activeVoices)
                {
                    if (voice.Channel == channel)
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
                _activeVoices.Clear();
                _reverb.Clear();
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
                    sample.Loop = AiffLoopReader.Read(aiffBytes, sample.Samples.Length / sample.Channels);
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
