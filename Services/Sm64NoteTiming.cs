namespace Sm64DecompLevelViewer.Services;

internal static class Sm64NoteTiming
{
    public static int GetDurationTicks(int delay, byte gate, byte commandType)
    {
        // SM64's gate is the silent fraction at the END of the delay.
        // note1 resets it to zero; zero means a full-length note, not a rest.
        int silentTicks = commandType == 1 ? 0 : (int)((long)Math.Max(0, delay) * gate / 256);
        return Math.Max(1, delay - silentTicks);
    }
}
