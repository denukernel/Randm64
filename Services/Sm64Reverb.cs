namespace Sm64DecompLevelViewer.Services;

// Preview of the US default audio-session ring: 0xC00 samples at 32 kHz,
// feedback 0x2FFF / 0x8000. The game can select different room presets;
// this does not emulate RSP fixed-point mixing or its resampling filter.
internal sealed class Sm64Reverb(int sampleRate)
{
    private readonly double[] _left = new double[(int)Math.Round(0xC00 * sampleRate / 32000.0)];
    private readonly double[] _right = new double[(int)Math.Round(0xC00 * sampleRate / 32000.0)];
    private int _position;

    public (double Left, double Right) Process(double sendLeft, double sendRight)
    {
        double left = _left[_position], right = _right[_position];
        _left[_position] = Math.Clamp(sendLeft + left * (0x2FFF / 32768.0), -1, 1);
        _right[_position] = Math.Clamp(sendRight + right * (0x2FFF / 32768.0), -1, 1);
        if (++_position == _left.Length) _position = 0;
        return (left, right);
    }

    public void Clear()
    {
        Array.Clear(_left);
        Array.Clear(_right);
        _position = 0;
    }
}
