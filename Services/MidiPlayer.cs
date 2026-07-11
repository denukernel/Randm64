using System;
using System.Runtime.InteropServices;

namespace Sm64DecompLevelViewer.Services
{
    public class MidiPlayer : IDisposable
    {
        [DllImport("winmm.dll")]
        private static extern int midiOutOpen(out IntPtr lphMidiOut, int uDeviceID, IntPtr dwCallback, IntPtr dwInstance, int dwFlags);

        [DllImport("winmm.dll")]
        private static extern int midiOutClose(IntPtr hMidiOut);

        [DllImport("winmm.dll")]
        private static extern int midiOutShortMsg(IntPtr hMidiOut, int dwMsg);

        private IntPtr _hMidiOut = IntPtr.Zero;
        private bool _isOpen = false;

        public MidiPlayer()
        {
            Open();
        }

        public void Open()
        {
            if (_isOpen) return;
            // -1 is the MIDI Mapper device ID (default Microsoft GS Wavetable Synth)
            int result = midiOutOpen(out _hMidiOut, -1, IntPtr.Zero, IntPtr.Zero, 0);
            _isOpen = (result == 0);
        }

        public void NoteOn(byte channel, byte pitch, byte velocity)
        {
            if (!_isOpen) return;
            // MIDI Note On message: 0x90 | channel, pitch, velocity
            int msg = 0x90 | (channel & 0x0F) | ((pitch & 0x7F) << 8) | ((velocity & 0x7F) << 16);
            midiOutShortMsg(_hMidiOut, msg);
        }

        public void NoteOff(byte channel, byte pitch)
        {
            if (!_isOpen) return;
            // MIDI Note Off message: 0x80 | channel, pitch, 0
            int msg = 0x80 | (channel & 0x0F) | ((pitch & 0x7F) << 8);
            midiOutShortMsg(_hMidiOut, msg);
        }

        public void ProgramChange(byte channel, byte patch)
        {
            if (!_isOpen) return;
            // MIDI Program Change message: 0xC0 | channel, patch, 0
            int msg = 0xC0 | (channel & 0x0F) | ((patch & 0x7F) << 8);
            midiOutShortMsg(_hMidiOut, msg);
        }

        public void ControlChange(byte channel, byte control, byte value)
        {
            if (!_isOpen) return;
            // MIDI Control Change message: 0xB0 | channel, control, value
            int msg = 0xB0 | (channel & 0x0F) | ((control & 0x7F) << 8) | ((value & 0x7F) << 16);
            midiOutShortMsg(_hMidiOut, msg);
        }

        public void Close()
        {
            if (!_isOpen) return;
            midiOutClose(_hMidiOut);
            _hMidiOut = IntPtr.Zero;
            _isOpen = false;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
