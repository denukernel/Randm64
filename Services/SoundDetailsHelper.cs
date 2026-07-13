using System;
using System.Collections.Generic;
using System.IO;

namespace Randm64.Services
{
    public static class SoundDetailsHelper
    {
        private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
        {
            // sfx_6: Object sounds heard on disk
            { "sfx_6/00", "Snufit throwing/spitting ball to Mario" },
            { "sfx_6/01", "Boss walk" },
            { "sfx_6/02", "Bowser roar" },
            { "sfx_6/03", "Bowser screaming" },
            { "sfx_6/04", "Bowser punching" },
            { "sfx_6/05", "Bowser breathing" },
            { "sfx_6/06", "Baby penguin sound" },
            { "sfx_6/07", "Simple kick" },
            { "sfx_6/08", "Boo laugh" },
            { "sfx_6/09", "Cannon opening" },
            { "sfx_6/0A", "Cannon positioning" },
            { "sfx_6/0B", "Cannon targeting" },
            { "sfx_6/0C", "Piranha plant dying" },
            { "sfx_6/0D", "Strange growing" },

            // sfx_4: General environment/object sounds heard on disk
            { "sfx_4/00", "Sliding sound" },
            { "sfx_4/01", "Quicksand sinking sound" },
            { "sfx_4/02", "Second sinking sound" },
            { "sfx_4/03", "General sinking sound" },
            { "sfx_4/04", "Sinking sound #5" },
            { "sfx_4/05", "Sinking sound #6" },
            { "sfx_4/06", "Sinking sound #7" },
            { "sfx_4/07", "Quicksand sand sinking sound" },
            { "sfx_4/08", "Shockwave sound" },
            { "sfx_4/09", "Imp shock sound" }
        };

        public static string GetDescription(string category, string fileName)
        {
            string cleanName = Path.GetFileNameWithoutExtension(fileName);
            // First check direct name matches (e.g. 05_step_ice)
            string directKey = $"{category}/{cleanName}";
            if (Descriptions.TryGetValue(directKey, out string directDesc))
            {
                return directDesc;
            }

            // Also check indexed hex/decimal prefix files (e.g. 0D_chain_chomp_bark -> check sfx_7/0D)
            if (cleanName.Length >= 2)
            {
                string hexPart = cleanName.Substring(0, 2);
                string indexKey = $"{category}/{hexPart}";
                if (Descriptions.TryGetValue(indexKey, out string indexDesc))
                {
                    return indexDesc;
                }
            }

            return "";
        }

        public static string FormatDisplayName(string category, string fileName)
        {
            string desc = GetDescription(category, fileName);
            if (string.IsNullOrEmpty(desc))
            {
                return fileName;
            }
            return $"{fileName} ({desc})";
        }
    }
}
