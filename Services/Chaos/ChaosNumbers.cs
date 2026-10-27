namespace Sm64DecompLevelViewer.Services.Chaos;
internal static class ChaosNumbers
{
        internal static int ParseIntHexSafe(string val)
        {
            val = val.Trim();
            if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(val.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out int hex))
                    return hex;
            }
            if (int.TryParse(val, out int res))
                return res;
            return 0;
        }
}
