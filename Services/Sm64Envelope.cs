namespace Sm64DecompLevelViewer.Services;

// US/JP ADSR runs four audio updates per 60 Hz game frame. Interpolate
// between those updates at the output rate rather than stepping the PCM.
internal sealed class Sm64Envelope(Sm64EnvelopePoint[] points, byte releaseRate, int sampleRate)
{
    private int _index, _remaining;
    private double _value, _step;
    private bool _hang;
    public bool Finished { get; private set; }

    public double Next(bool releasing)
    {
        if (Finished) return 0;
        if (releasing)
        {
            _value = Math.Max(0, _value - Math.Max(1, (int)releaseRate) * 24.0 / 32767 * 240 / sampleRate);
            if (_value <= 100.0 / 32767) Finished = true;
        }
        else if (points.Length == 0)
        {
            _value = Math.Min(1, _value + 240.0 / sampleRate);
        }
        else if (!_hang)
        {
            for (int commands = 0; _remaining == 0 && !_hang && !Finished && commands < 32; commands++)
            {
                if (_index >= points.Length) { _hang = true; break; }
                var point = points[_index++];
                if (point.Delay == -1) _hang = true;
                else if (point.Delay == -2) _index = Math.Clamp(point.Target, 0, points.Length);
                else if (point.Delay == -3) _index = 0;
                else if (point.Delay <= 0) Finished = true;
                else
                {
                    _remaining = Math.Max(1, (int)Math.Round(point.Delay * sampleRate / 240.0));
                    _step = (Math.Clamp(point.Target / 32767.0, 0, 1) - _value) / _remaining;
                }
            }
            if (_remaining > 0) { _value += _step; _remaining--; }
        }
        // US playback.c squares ADSR amplitude after scaling by ~1/23000.
        double level = _value * (points.Length == 0 ? 1 : 32767.0 / 23000.0);
        return Finished ? 0 : level * level;
    }
}
