using System.IO;
using System.Text.RegularExpressions;
namespace Sm64DecompLevelViewer.Services.Rom;
internal sealed class RomMapDocument
{
    internal sealed class SegmentInfo
    {
        public string Name { get; set; } = "";
        public ulong RamStart { get; set; }
        public ulong RamEnd { get; set; }
        public ulong RomStart { get; set; }
        public ulong RomEnd { get; set; }
    }
    internal Dictionary<string, SegmentInfo> Segments { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, ulong> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, SegmentInfo> _segments => Segments;
    private Dictionary<string, ulong> _symbols => Symbols;
        private static readonly Dictionary<string, string> CuratedSymbolMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "TableInteractions", "TableInteractions" },
            { "TableCameraTransitions", "TableCameraTransitions" },
            { "TableCameraSL", "TableCameraSL" },
            { "TableCameraTHI", "TableCameraTHI" },
            { "TableCameraHMC", "TableCameraHMC" },
            { "TableCameraSSL", "TableCameraSSL" },
            { "TableCameraRR", "TableCameraRR" },
            { "TableCameraMetal", "TableCameraMetal" },
            { "TableCameraCCM", "TableCameraCCM" },
            { "TableCameraInside", "TableCameraInside" },
            { "TableCameraBBH", "TableCameraBBH" },
            { "TableLevelCinematicCamera", "TableLevelCinematicCamera" },
            { "TableCutScenePeachEnd", "TableCutScenePeachEnd" },
            { "TableCutSceneGrandStar", "TableCutSceneGrandStar" },
            { "TableCutScene0FTodo", "TableCutScene0FTodo" },
            { "TableCutSceneDoorWarp", "TableCutSceneDoorWarp" },
            { "TableCutSceneEndWaving", "TableCutSceneEndWaving" },
            { "TableCutSceneEndCredits", "TableCutSceneEndCredits" },
            { "TableCutSceneDoor00", "TableCutSceneDoor00" },
            { "TableCutSceneDoor01", "TableCutSceneDoor01" },
            { "TableCutSceneDoor0A", "TableCutSceneDoor0A" },
            { "TableCutSceneDoor0B", "TableCutSceneDoor0B" },
            { "TableCutSceneEnterCannon", "TableCutSceneEnterCannon" },
            { "TableCutSceneStarSpawn", "TableCutSceneStarSpawn" },
            { "TableCutSceneSpecialStarSpawn", "TableCutSceneSpecialStarSpawn" },
            { "TableCutSceneEnterPainting", "TableCutSceneEnterPainting" },
            { "TableCutSceneExitPaintingDeath", "TableCutSceneExitPaintingDeath" },
            { "TableCutSceneExitPaintingSuccess", "TableCutSceneExitPaintingSuccess" },
            { "TableCutScene11Todo", "TableCutScene11Todo" },
            { "TableCutSceneIntroPeach", "TableCutSceneIntroPeach" },
            { "TableCutScenePrepareCannon", "TableCutScenePrepareCannon" },
            { "TableCutSceneExitWaterfall", "TableCutSceneExitWaterfall" },
            { "TableCutSceneFallToCastleGrounds", "TableCutSceneFallToCastleGrounds" },
            { "TableCutSceneEnterPyramidTop", "TableCutSceneEnterPyramidTop" },
            { "TableCutScene26Todo", "TableCutScene26Todo" },
            { "TableCutSceneDeath1", "TableCutSceneDeath1" },
            { "TableCutSceneEnterPool", "TableCutSceneEnterPool" },
            { "TableCutSceneDeath2", "TableCutSceneDeath2" },
            { "TableCutSceneBBHDeath", "TableCutSceneBBHDeath" },
            { "TableCutSceneQuicksandDeath", "TableCutSceneQuicksandDeath" },
            { "TableCutScene1ATodo", "TableCutScene1ATodo" },
            { "TableCutSceneEnterBowserPlatform", "TableCutSceneEnterBowserPlatform" },
            { "TableCutSceneStarDance1", "TableCutSceneStarDance1" },
            { "TableCutSceneStarDance2", "TableCutSceneStarDance2" },
            { "TableCutSceneStarDance3", "TableCutSceneStarDance3" },
            { "TableCutSceneKeyDance", "TableCutSceneKeyDance" },
            { "TableCutSceneCapSwitchPress", "TableCutSceneCapSwitchPress" },
            { "TableCutSceneSlidingDoorOpen", "TableCutSceneSlidingDoorOpen" },
            { "TableCutSceneUnlockKeyDoor", "TableCutSceneUnlockKeyDoor" },
            { "TableCutSceneExitBowserSuccess", "TableCutSceneExitBowserSuccess" },
            { "TableCutScene1CTodo", "TableCutScene1CTodo" },
            { "TableCutSceneBBHExitSuccess", "TableCutSceneBBHExitSuccess" },
            { "TableCutSceneNonPaintingDeath", "TableCutSceneNonPaintingDeath" },
            { "TableCutSceneDialog", "TableCutSceneDialog" },
            { "TableCutSceneReadMessage", "TableCutSceneReadMessage" },
            { "TableMrIParticleActions", "TableMrIParticleActions" },
            { "TableMrIActions", "sMrIActions" },
            { "TableCapSwitchActions", "sCapSwitchActions" },
            { "TableKingBobombActions", "sKingBobombActions" },
            { "TableOpenedCannonActions", "sOpenedCannonActions" },
            { "TableChuckyaActions", "sChuckyaActions" },
            { "TableCoinInsideBooActions", "sCoinInsideBooActions" },
            { "TableGrindelThwompActions", "sGrindelThwompActions" },
            { "TableTumblingBridgeActions", "sTumblingBridgeActions" },
            { "TableElevatorActions", "sElevatorActions" },
            { "TableLittleCageActions", "sLittleCageActions" },
            { "TableHeaveHoActions", "sHeaveHoActions" },
            { "TableJumpingBoxActions", "sJumpingBoxActions" },
            { "TableBetaBooKeyInsideActions", "sBetaBooKeyInsideActions" },
            { "TableBulletBillActions", "sBulletBillActions" },
            { "TableBowserTailAnchorActions", "sBowserTailAnchorActions" },
            { "TableBowserActions", "sBowserActions" },
            { "TableFallingBowserPlatformActions", "sFallingBowserPlatformActions" },
            { "TableUkikiOpenCageActions", "sUkikiOpenCageActions" },
            { "TableRotatingCwFireBarsActions", "sRotatingCwFireBarsActions" },
            { "TableToxBoxActions", "sToxBoxActions" },
            { "TablePiranhaPlantActions", "sPiranhaPlantActions" },
            { "TableBowserPuzzlePieceActions", "sBowserPuzzlePieceActions" },
            { "TableTuxiesMotherActions", "sTuxiesMotherActions" },
            { "TableSmallPenguinActions", "sSmallPenguinActions" },
            { "TableFishActions", "sFishActions" },
            { "TableFishGroupActions", "sFishGroupActions" },
            { "TableBirdChirpChirpActions", "sBirdChirpChirpActions" },
            { "TableCheepCheepActions", "sCheepCheepActions" },
            { "TableExclamationBoxActions", "sExclamationBoxActions" },
            { "TableTweesterActions", "sTweesterActions" },
            { "TableBooActions", "sBooActions" },
            { "TableBooGivingStarActions", "sBooGivingStarActions" },
            { "TableBooWithCageActions", "sBooWithCageActions" },
            { "TableWhompActions", "sWhompActions" },
            { "GeoLayoutJumpTable", "GeoLayoutJumpTable" },
            { "LevelScriptJumpTable", "LevelScriptJumpTable" },
            { "BehaviorJumpTable", "BehaviorJumpTable" },
            { "ADSR/Controls", "gSoundDataADSR" },
            { "Raw SFX Data/Table", "gSoundDataRaw" },
            { "Music files", "gMusicData" },
            { "Instrument Set", "gBankSetsData" }
        };
        private static readonly Dictionary<string, string> CuratedSegmentMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "ROM header", "boot" },
            { "boot assembly", "boot" },
            { "rsp boot", "rspboot" },
            { "behavior data", "behavior" },
            { "game over level", "intro" },
            { "main entry", "entry" },
            { "mario bg", "segment2" },
            { "main menu segment7", "menu" },
            { "bbh level", "bbh" },
            { "ccm level", "ccm" },
            { "inside castle level", "castle_inside" },
            { "hmc level", "hmc" },
            { "ssl level", "ssl" },
            { "bob level", "bob" },
            { "sl level", "sl" },
            { "wdw level", "wdw" },
            { "jrb level", "jrb" },
            { "thi level", "thi" },
            { "ttc level", "ttc" },
            { "rr level", "rr" },
            { "castle grounds level", "castle_grounds" },
            { "bidw level", "bowser_1" },
            { "bifs level", "bowser_2" },
            { "bits level", "bowser_3" },
            { "lll level", "lll" },
            { "ddd level", "ddd" },
            { "wf level", "wf" },
            { "cake end level", "ending" }
        };
        internal void ParseMapFile(string mapPath)
        {
            _segments.Clear();
            _symbols.Clear();

            try
            {
                var lines = File.ReadAllLines(mapPath);

                // Regex patterns
                var segPattern = new Regex(@"^\s+0x(?<val>[0-9a-fA-F]+)\s+(?<sym>_[a-zA-Z0-9_]+Segment(Start|End|RomStart|RomEnd))");
                var symPattern = new Regex(@"^\s+0x(?<addr>[0-9a-fA-F]{8,16})\s+(?<name>[a-zA-Z0-9_]+)\s*$");

                // Pass 1: Parse segment markers
                foreach (var line in lines)
                {
                    var match = segPattern.Match(line);
                    if (match.Success)
                    {
                        ulong val = Convert.ToUInt64(match.Groups["val"].Value, 16);
                        string sym = match.Groups["sym"].Value;

                        string segName = ExtractSegmentName(sym);
                        if (string.IsNullOrEmpty(segName)) continue;

                        if (!_segments.TryGetValue(segName, out var seg))
                        {
                            seg = new SegmentInfo { Name = segName };
                            _segments[segName] = seg;
                        }

                        if (sym.EndsWith("SegmentStart")) seg.RamStart = val;
                        else if (sym.EndsWith("SegmentEnd")) seg.RamEnd = val;
                        else if (sym.EndsWith("SegmentRomStart")) seg.RomStart = val;
                        else if (sym.EndsWith("SegmentRomEnd")) seg.RomEnd = val;
                    }
                }

                // Pass 2: Parse symbols
                foreach (var line in lines)
                {
                    var match = symPattern.Match(line);
                    if (match.Success)
                    {
                        ulong addr = Convert.ToUInt64(match.Groups["addr"].Value, 16);
                        string name = match.Groups["name"].Value;

                        if (name.StartsWith("_") && name.Contains("Segment")) continue;

                        _symbols[name] = addr;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("map parsing failed: " + ex.Message, ex);
            }
        }
        internal string ExtractSegmentName(string sym)
        {
            if (sym.StartsWith("_"))
            {
                int segIndex = sym.IndexOf("Segment");
                if (segIndex > 1)
                {
                    return sym.Substring(1, segIndex - 1);
                }
            }
            return string.Empty;
        }
        internal ulong? TranslateRamToRom(ulong ramAddr)
        {
            return RomAddressMapping.Translate(ramAddr, _segments.Values.Select(s => new RomAddressMapping.Segment(s.RamStart, s.RamEnd, s.RomStart, s.RomEnd)));
        }
        internal List<T> BuildEntries<T>(IEnumerable<T> _defaultEntries, bool allSymbols) where T : global::Sm64DecompLevelViewer.Models.Rom.RomMapEntry, new()
        {
            var _displayEntries = new List<T>();

            if (allSymbols)
            {
                // Show ALL parsed symbols from the map file
                foreach (var sym in _symbols.OrderBy(s => s.Value))
                {
                    ulong? romAddr = TranslateRamToRom(sym.Value);
                    if (romAddr.HasValue)
                    {
                        _displayEntries.Add(new T
                        {
                            Start = romAddr.Value.ToString("X6"),
                            End = string.Empty,
                            Type = "symbol",
                            Description = $"{sym.Key} (RAM: {sym.Value:X8})"
                        });
                    }
                }

                // Also list the parsed segments as full blocks
                foreach (var seg in _segments.Values.Where(s => s.RomEnd > s.RomStart).OrderBy(s => s.RomStart))
                {
                    _displayEntries.Add(new T
                    {
                        Start = seg.RomStart.ToString("X6"),
                        End = seg.RomEnd.ToString("X6"),
                        Type = "segment",
                        Description = $"{seg.Name} segment (RAM: {seg.RamStart:X8} - {seg.RamEnd:X8})"
                    });
                }
            }
            else
            {
                // Build dynamic curated list based on the loaded map symbols
                foreach (var def in _defaultEntries)
                {
                    var newEntry = new T
                    {
                        Start = def.Start,
                        End = def.End,
                        Type = def.Type,
                        Description = def.Description
                    };

                    bool updated = false;

                    // 1. Check if it's a curated segment description
                    if (CuratedSegmentMap.TryGetValue(def.Description, out string? segName) && _segments.TryGetValue(segName, out var seg) && seg.RomEnd > seg.RomStart)
                    {
                        newEntry.Start = seg.RomStart.ToString("X6");
                        newEntry.End = seg.RomEnd.ToString("X6");
                        updated = true;
                    }

                    // 2. Check if it's a curated symbol description
                    if (!updated && CuratedSymbolMap.TryGetValue(def.Description, out string? symName) && _symbols.TryGetValue(symName, out ulong ramAddr))
                    {
                        ulong? romAddr = TranslateRamToRom(ramAddr);
                        if (romAddr.HasValue)
                        {
                            newEntry.Start = romAddr.Value.ToString("X6");

                            // Check for music/sound files specific range end calculation
                            if (symName == "gSoundDataADSR" && _symbols.TryGetValue("gSoundDataRaw", out ulong rawAddr) && TranslateRamToRom(rawAddr) is ulong rRaw)
                            {
                                newEntry.End = rRaw.ToString("X6");
                            }
                            else if (symName == "gSoundDataRaw" && _symbols.TryGetValue("gMusicData", out ulong musAddr) && TranslateRamToRom(musAddr) is ulong rMus)
                            {
                                newEntry.End = rMus.ToString("X6");
                            }
                            else if (symName == "gMusicData" && _symbols.TryGetValue("gBankSetsData", out ulong bnkAddr) && TranslateRamToRom(bnkAddr) is ulong rBnk)
                            {
                                newEntry.End = rBnk.ToString("X6");
                            }
                            else if (symName == "gBankSetsData" && _symbols.TryGetValue("_assetsSegmentRomEnd", out ulong endAddr))
                            {
                                newEntry.End = endAddr.ToString("X6");
                            }
                            else
                            {
                                newEntry.End = string.Empty; // no symbol size is known
                            }
                            updated = true;
                        }
                    }

                    if (updated) _displayEntries.Add(newEntry);
                }
            }

            return _displayEntries;
        }
}
