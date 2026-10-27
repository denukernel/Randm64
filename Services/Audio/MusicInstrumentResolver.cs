using System.IO;
namespace Sm64DecompLevelViewer.Services.Audio;
internal sealed class MusicInstrumentResolver
{
    private readonly string _projectRoot;
    private readonly Sm64SoundBank _soundBank;
    private readonly Dictionary<string, string> _sequenceBankCache;
    public MusicInstrumentResolver(string root, Sm64SoundBank bank, Dictionary<string, string> cache) { _projectRoot = root; _soundBank = bank; _sequenceBankCache = cache; }
        internal string GetInstrumentName(string m64Path, byte sm64Inst)
        {
            if (string.IsNullOrEmpty(m64Path)) return sm64Inst.ToString();
            
            string fileName = System.IO.Path.GetFileName(m64Path).ToLower();
            
            if (fileName.Contains("bob_omb") || fileName.Contains("field") || fileName.Contains("grass") || fileName.Contains("lakitu") || fileName.Contains("victory"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Trumpet / Brass)";
                    case 1: return $"{sm64Inst} (Slap Bass)";
                    case 2: return $"{sm64Inst} (Steel Drums)";
                    case 3: return $"{sm64Inst} (Nylon Guitar)";
                    case 4: return $"{sm64Inst} (Flute / Camera Buzz)";
                    case 5: return $"{sm64Inst} (Marimba / Camera Shutter)";
                }
            }
            else if (fileName.Contains("slider") || fileName.Contains("slide") || fileName.Contains("snow") || fileName.Contains("mountain"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Marimba / Xylophone)";
                    case 1: return $"{sm64Inst} (Acoustic Guitar)";
                    case 2: return $"{sm64Inst} (Slap Bass)";
                    case 3: return $"{sm64Inst} (Trumpet / Brass)";
                    case 4: return $"{sm64Inst} (Flute / Whistle)";
                    case 5: return $"{sm64Inst} (Steel Drums)";
                }
            }
            else if (fileName.Contains("castle") || fileName.Contains("peach") || fileName.Contains("inside"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2: return $"{sm64Inst} (Strings Ensemble)";
                    case 3: return $"{sm64Inst} (Pizzicato Strings)";
                    case 4: return $"{sm64Inst} (Trombone)";
                    case 5:
                    case 6: return $"{sm64Inst} (Rhodes / E-Piano)";
                }
            }
            else if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks") || fileName.Contains("bay"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1: return $"{sm64Inst} (Strings Ensemble)";
                    case 2:
                    case 6: return $"{sm64Inst} (Synth Bass)";
                    case 14:
                    case 15: return $"{sm64Inst} (Rhodes / E-Piano)";
                }
            }
            else if (fileName.Contains("boss") || fileName.Contains("bowser_level") || fileName.Contains("road") || fileName.Contains("koopa"))
            {
                switch (sm64Inst)
                {
                    case 0: return $"{sm64Inst} (Distorted Guitar)";
                    case 1:
                    case 2: return $"{sm64Inst} (Synth Bass)";
                    case 3: return $"{sm64Inst} (Orchestra Hit)";
                    case 4: return $"{sm64Inst} (Alto Flute)";
                    case 6: return $"{sm64Inst} (Strings Ensemble)";
                }
            }
            else if (fileName.Contains("bowser_battle") || fileName.Contains("organ") || fileName.Contains("final"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4: return $"{sm64Inst} (Pipe Organ)";
                    case 5: return $"{sm64Inst} (Choir Aahs)";
                }
            }
            
            return sm64Inst.ToString();
        }
        internal byte MapSm64InstrumentToGm(string m64Path, byte channel, byte sm64Inst)
        {
            string fileName = System.IO.Path.GetFileName(m64Path).ToLower();
            
            // Grass theme / Bob-omb Battlefield (Bank 22 / 1B)
            if (fileName.Contains("bob_omb") || fileName.Contains("field") || fileName.Contains("grass") || fileName.Contains("lakitu") || fileName.Contains("victory"))
            {
                switch (sm64Inst)
                {
                    case 0: return 56;  // Trumpet / Brass
                    case 1: return 36;  // Slap Bass 1
                    case 2: return 114; // Steel Drums
                    case 3: return 24;  // Nylon Guitar
                    case 4: return 73;  // Flute (camera buzz)
                    case 5: return 12;  // Marimba (camera shutter)
                    default: return 114; // Steel Drums / Marimba
                }
            }
            
            // Slider / Snow theme / Cool Cool Mountain / Slide (Bank 0D / 0B)
            if (fileName.Contains("slider") || fileName.Contains("slide") || fileName.Contains("snow") || fileName.Contains("mountain"))
            {
                switch (sm64Inst)
                {
                    case 0: return 12;  // Marimba / Xylophone
                    case 1: return 24;  // Acoustic Guitar
                    case 2: return 36;  // Slap Bass 1
                    case 3: return 61;  // Brass Section / Trumpet
                    case 4: return 73;  // Flute / Whistle
                    case 5: return 114; // Steel Drums
                    default: return 12;
                }
            }

            // Castle Inside / Peach Message (Bank 0E / 1E)
            if (fileName.Contains("castle") || fileName.Contains("peach") || fileName.Contains("inside"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2: return 48;  // String Ensemble 1
                    case 3: return 45;  // Pizzicato Strings
                    case 4: return 57;  // Trombone / Synth Brass
                    case 5:
                    case 6: return 4;   // Rhodes / Electric Piano
                    default: return 48;
                }
            }

            // Water theme / Dire Dire Docks / Jolly Roger Bay (Bank 13)
            if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks") || fileName.Contains("bay"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1: return 48;  // String Ensemble 1
                    case 2:
                    case 6: return 38;  // Sine Bass / Synth Bass
                    case 14:
                    case 15: return 4;   // Crystal Rhodes / Electric Piano
                    default: return 4;
                }
            }

            // Boss theme / Bowser Levels / Koopa Road (Bank 12 / 19 / 1B)
            if (fileName.Contains("boss") || fileName.Contains("bowser_level") || fileName.Contains("road") || fileName.Contains("koopa"))
            {
                switch (sm64Inst)
                {
                    case 0: return 30;  // Distortion Guitar
                    case 1:
                    case 2: return 38;  // Synth Bass
                    case 3: return 55;  // Orchestra Hit
                    case 4: return 73;  // Alto Flute
                    case 6: return 48;  // String Ensemble 1
                    default: return 30;
                }
            }

            // Bowser Battle / Organ theme (Bank 1D_bowser_organ)
            if (fileName.Contains("bowser_battle") || fileName.Contains("organ") || fileName.Contains("final"))
            {
                switch (sm64Inst)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 3:
                    case 4: return 19;  // Pipe Organ
                    case 5: return 52;  // Choir Aahs
                    default: return 19;
                }
            }

            // Piranha Plant Lullaby (Bank 14_piranha_music_box)
            if (fileName.Contains("piranha") || fileName.Contains("lullaby"))
            {
                switch (sm64Inst)
                {
                    case 0: return 10;  // Music Box
                    case 1: return 8;   // Celesta
                    default: return 10;
                }
            }

            // Hazy Maze Cave / Underground (Bank 15)
            if (fileName.Contains("cave") || fileName.Contains("hazy") || fileName.Contains("underground"))
            {
                switch (sm64Inst)
                {
                    case 0: return 32;  // Acoustic Bass
                    case 1: return 11;  // Vibraphone
                    case 2: return 4;   // Electric Piano
                    default: return 11;
                }
            }

            // Merry-Go-Round / Spooky Carousel (Bank 21)
            if (fileName.Contains("carousel") || fileName.Contains("merry") || fileName.Contains("spooky"))
            {
                switch (sm64Inst)
                {
                    case 0: return 19;  // Pipe Organ
                    case 1: return 8;   // Celesta
                    case 2: return 24;  // Nylon Guitar
                    default: return 19;
                }
            }

            // Fallback GM General Map (Standard N64 instrument list mappings)
            switch (sm64Inst)
            {
                case 0: return 12;  // Marimba
                case 1: return 48;  // Strings
                case 2: return 61;  // Brass Section
                case 3: return 73;  // Flute
                case 4: return 36;  // Slap Bass 1
                case 5: return 24;  // Nylon Guitar
                case 6: return 19;  // Pipe Organ
                case 7: return 114; // Steel Drums
                case 8: return 11;  // Vibraphone
                case 9: return 56;  // Trumpet
                case 10: return 68; // Oboe
                case 11: return 33; // Acoustic Bass
                case 12: return 55; // Orchestra Hit
                case 13: return 30; // Distortion Guitar
                case 14: return 80; // Square Lead
                case 15: return 8;  // Celesta
                default: return sm64Inst;
            }
        }
        internal string GetBankIdForSequence(string m64Path)
        {
            if (_sequenceBankCache.TryGetValue(m64Path, out string? cachedBank)) return cachedBank;
            string Remember(string bank) { _sequenceBankCache[m64Path] = bank; return bank; }
            string fileName = System.IO.Path.GetFileNameWithoutExtension(m64Path).ToLower();
            string sequencesJsonPath = System.IO.Path.Combine(_projectRoot, "sound", "sequences.json");

            if (System.IO.File.Exists(sequencesJsonPath))
            {
                try
                {
                    string jsonText = System.IO.File.ReadAllText(sequencesJsonPath);
                    using (var doc = System.Text.Json.JsonDocument.Parse(jsonText))
                    {
                        var root = doc.RootElement;
                        foreach (var prop in root.EnumerateObject())
                        {
                            string key = prop.Name.ToLower();
                            if (fileName.Contains(key) || key.Contains(fileName))
                            {
                                var val = prop.Value;
                                if (val.ValueKind == System.Text.Json.JsonValueKind.Array && val.GetArrayLength() > 0)
                                {
                                    return Remember(val[0].GetString() ?? "22");
                                }
                                else if (val.ValueKind == System.Text.Json.JsonValueKind.Object)
                                {
                                    if (val.TryGetProperty("banks", out var banksProp) && banksProp.ValueKind == System.Text.Json.JsonValueKind.Array && banksProp.GetArrayLength() > 0)
                                    {
                                        return Remember(banksProp[0].GetString() ?? "22");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error reading sequences.json: {ex.Message}");
                }
            }

            // Expanded fallback heuristics if sequences.json is unavailable
            if (fileName.Contains("grass") || fileName.Contains("bob_omb") || fileName.Contains("field")) return Remember("22");
            if (fileName.Contains("castle") || fileName.Contains("inside")) return Remember("0E");
            if (fileName.Contains("water") || fileName.Contains("ocean") || fileName.Contains("docks")) return Remember("13");
            if (fileName.Contains("slide") || fileName.Contains("slider") || fileName.Contains("snow")) return Remember("0D");
            if (fileName.Contains("boss") || fileName.Contains("road")) return Remember("12");
            if (fileName.Contains("organ") || fileName.Contains("final") || fileName.Contains("battle")) return Remember("1D_bowser_organ");
            if (fileName.Contains("lakitu") || fileName.Contains("cutscene")) return Remember("1B");
            if (fileName.Contains("piranha") || fileName.Contains("lullaby")) return Remember("14_piranha_music_box");
            if (fileName.Contains("stairs")) return Remember("1C_endless_stairs");
            if (fileName.Contains("race")) return Remember("1A");
            if (fileName.Contains("powerup")) return Remember("17");
            if (fileName.Contains("metal")) return Remember("18");
            if (fileName.Contains("credits")) return Remember("25");
            if (fileName.Contains("ending")) return Remember("23");
            if (fileName.Contains("file")) return Remember("24");
            if (fileName.Contains("spooky")) return Remember("10");

            return Remember("22");
        }
        internal Sm64Instrument? ResolveInstrument(string m64Path, byte instrument, byte pitch)
        {
            try
            {
                return _soundBank.Resolve(GetBankIdForSequence(m64Path), instrument, pitch,
                    Sm64SoundBank.GetVersionForSequence(m64Path));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resolving SM64 instrument: {ex.Message}");
                return null;
            }
        }
        internal string GetSamplePathForInstrument(string m64Path, byte sm64Inst, byte pitch, out double tuning)
        {
            var instrument = ResolveInstrument(m64Path, sm64Inst, pitch);
            tuning = instrument?.Tuning ?? 0;
            return instrument?.SamplePath ?? string.Empty;
        }
}
