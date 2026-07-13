using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Sm64DecompLevelViewer
{
    public partial class RomAddressWindow : Window
    {
        public class RomMapEntry
        {
            public string Start { get; set; } = string.Empty;
            public string End { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        private class SegmentInfo
        {
            public string Name { get; set; } = string.Empty;
            public ulong RamStart { get; set; }
            public ulong RamEnd { get; set; }
            public ulong RomStart { get; set; }
        }

        private readonly List<RomMapEntry> _defaultEntries;
        private List<RomMapEntry> _displayEntries = new();
        private List<RomMapEntry> _filteredEntries = new();

        private readonly Dictionary<string, SegmentInfo> _segments = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ulong> _symbols = new(StringComparer.OrdinalIgnoreCase);
        private string? _projectRoot;

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

        public RomAddressWindow(string? projectRootPath)
        {
            InitializeComponent();
            _projectRoot = projectRootPath;

            _defaultEntries = new List<RomMapEntry>
            {
                new RomMapEntry { Start = "000000", End = "000040", Type = "header", Description = "ROM header" },
                new RomMapEntry { Start = "000040", End = "001000", Type = "asm", Description = "boot assembly" },
                new RomMapEntry { Start = "001000", End = "0E6258", Type = "asm", Description = "ASM copied to 80246000-8032B258" },
                new RomMapEntry { Start = "0E6260", End = "0E6330", Type = "bin", Description = "rsp boot" },
                new RomMapEntry { Start = "0E6330", End = "0E7330", Type = "bin", Description = "rsp graphics" },
                new RomMapEntry { Start = "0E7740", End = "0E7F40", Type = "bin", Description = "rsp audio" },
                new RomMapEntry { Start = "0E8950", End = "0E8A48", Type = "ptr", Description = "TableInteractions" },
                new RomMapEntry { Start = "0E8FA8", End = "0E8FF0", Type = "ptr", Description = "TableCameraTransitions" },
                new RomMapEntry { Start = "0E9098", End = "0E90E0", Type = "ptr", Description = "TableCameraSL" },
                new RomMapEntry { Start = "0E90E0", End = "0E9128", Type = "ptr", Description = "TableCameraTHI" },
                new RomMapEntry { Start = "0E9128", End = "0E91D0", Type = "ptr", Description = "TableCameraHMC" },
                new RomMapEntry { Start = "0E91D0", End = "0E9248", Type = "ptr", Description = "TableCameraSSL" },
                new RomMapEntry { Start = "0E9248", End = "0E9338", Type = "ptr", Description = "TableCameraRR" },
                new RomMapEntry { Start = "0E9338", End = "0E9368", Type = "ptr", Description = "TableCameraMetal" },
                new RomMapEntry { Start = "0E9368", End = "0E93B0", Type = "ptr", Description = "TableCameraCCM" },
                new RomMapEntry { Start = "0E93B0", End = "0E96F8", Type = "ptr", Description = "TableCameraInside" },
                new RomMapEntry { Start = "0E96F8", End = "0E9CB0", Type = "ptr", Description = "TableCameraBBH" },
                new RomMapEntry { Start = "0E9CB0", End = "0E9D50", Type = "ptr", Description = "TableLevelCinematicCamera" },
                new RomMapEntry { Start = "0EA4D4", End = "0EA534", Type = "ptr", Description = "TableCutScenePeachEnd" },
                new RomMapEntry { Start = "0EA534", End = "0EA544", Type = "ptr", Description = "TableCutSceneGrandStar" },
                new RomMapEntry { Start = "0EA544", End = "0EA554", Type = "ptr", Description = "TableCutScene0FTodo" },
                new RomMapEntry { Start = "0EA554", End = "0EA564", Type = "ptr", Description = "TableCutSceneDoorWarp" },
                new RomMapEntry { Start = "0EA564", End = "0EA56C", Type = "ptr", Description = "TableCutSceneEndWaving" },
                new RomMapEntry { Start = "0EA56C", End = "0EA574", Type = "ptr", Description = "TableCutSceneEndCredits" },
                new RomMapEntry { Start = "0EA574", End = "0EA59C", Type = "ptr", Description = "TableCutSceneDoor00" },
                new RomMapEntry { Start = "0EA59C", End = "0EA5C4", Type = "ptr", Description = "TableCutSceneDoor01" },
                new RomMapEntry { Start = "0EA5C4", End = "0EA5DC", Type = "ptr", Description = "TableCutSceneDoor0A" },
                new RomMapEntry { Start = "0EA5DC", End = "0EA5F4", Type = "ptr", Description = "TableCutSceneDoor0B" },
                new RomMapEntry { Start = "0EA5F4", End = "0EA60C", Type = "ptr", Description = "TableCutSceneEnterPainting" },
                new RomMapEntry { Start = "0EA60C", End = "0EA624", Type = "ptr", Description = "TableCutSceneStarSpawn" },
                new RomMapEntry { Start = "0EA624", End = "0EA634", Type = "ptr", Description = "TableCutSceneSpecialStarSpawn" },
                new RomMapEntry { Start = "0EA634", End = "0EA63C", Type = "ptr", Description = "TableCutSceneEnterPainting" },
                new RomMapEntry { Start = "0EA63C", End = "0EA64C", Type = "ptr", Description = "TableCutSceneExitPaintingDeath" },
                new RomMapEntry { Start = "0EA64C", End = "0EA65C", Type = "ptr", Description = "TableCutSceneExitPaintingSuccess" },
                new RomMapEntry { Start = "0EA65C", End = "0EA674", Type = "ptr", Description = "TableCutScene11Todo" },
                new RomMapEntry { Start = "0EA674", End = "0EA69C", Type = "ptr", Description = "TableCutSceneIntroPeach" },
                new RomMapEntry { Start = "0EA69C", End = "0EA6AC", Type = "ptr", Description = "TableCutScenePrepareCannon" },
                new RomMapEntry { Start = "0EA6AC", End = "0EA6BC", Type = "ptr", Description = "TableCutSceneExitWaterfall" },
                new RomMapEntry { Start = "0EA6BC", End = "0EA6CC", Type = "ptr", Description = "TableCutSceneFallToCastleGrounds" },
                new RomMapEntry { Start = "0EA6CC", End = "0EA6DC", Type = "ptr", Description = "TableCutSceneEnterPyramidTop" },
                new RomMapEntry { Start = "0EA6DC", End = "0EA6F4", Type = "ptr", Description = "TableCutScene26Todo" },
                new RomMapEntry { Start = "0EA6F4", End = "0EA6FC", Type = "ptr", Description = "TableCutSceneDeath1" },
                new RomMapEntry { Start = "0EA6FC", End = "0EA70C", Type = "ptr", Description = "TableCutSceneEnterPool" },
                new RomMapEntry { Start = "0EA70C", End = "0EA714", Type = "ptr", Description = "TableCutSceneDeath2" },
                new RomMapEntry { Start = "0EA714", End = "0EA71C", Type = "ptr", Description = "TableCutSceneBBHDeath" },
                new RomMapEntry { Start = "0EA71C", End = "0EA72C", Type = "ptr", Description = "TableCutSceneQuicksandDeath" },
                new RomMapEntry { Start = "0EA72C", End = "0EA734", Type = "ptr", Description = "TableCutScene1ATodo" },
                new RomMapEntry { Start = "0EA734", End = "0EA74C", Type = "ptr", Description = "TableCutSceneEnterBowserPlatform" },
                new RomMapEntry { Start = "0EA74C", End = "0EA754", Type = "ptr", Description = "TableCutSceneStarDance1" },
                new RomMapEntry { Start = "0EA754", End = "0EA75C", Type = "ptr", Description = "TableCutSceneStarDance2" },
                new RomMapEntry { Start = "0EA75C", End = "0EA764", Type = "ptr", Description = "TableCutSceneStarDance3" },
                new RomMapEntry { Start = "0EA764", End = "0EA76C", Type = "ptr", Description = "TableCutSceneKeyDance" },
                new RomMapEntry { Start = "0EA76C", End = "0EA774", Type = "ptr", Description = "TableCutSceneCapSwitchPress" },
                new RomMapEntry { Start = "0EA774", End = "0EA784", Type = "ptr", Description = "TableCutSceneSlidingDoorOpen" },
                new RomMapEntry { Start = "0EA784", End = "0EA794", Type = "ptr", Description = "TableCutSceneUnlockKeyDoor" },
                new RomMapEntry { Start = "0EA794", End = "0EA7A4", Type = "ptr", Description = "TableCutSceneExitBowserSuccess" },
                new RomMapEntry { Start = "0EA7A4", End = "0EA7B4", Type = "ptr", Description = "TableCutScene1CTodo" },
                new RomMapEntry { Start = "0EA7B4", End = "0EA7C4", Type = "ptr", Description = "TableCutSceneBBHExitSuccess" },
                new RomMapEntry { Start = "0EA7C4", End = "0EA7D4", Type = "ptr", Description = "TableCutSceneNonPaintingDeath" },
                new RomMapEntry { Start = "0EA7D4", End = "0EA7EC", Type = "ptr", Description = "TableCutSceneDialog" },
                new RomMapEntry { Start = "0EA7EC", End = "0EA804", Type = "ptr", Description = "TableCutSceneReadMessage" },
                new RomMapEntry { Start = "0EB06C", End = "0EB074", Type = "ptr", Description = "TableMrIParticleActions" },
                new RomMapEntry { Start = "0EB074", End = "0EB084", Type = "ptr", Description = "TableMrIActions" },
                new RomMapEntry { Start = "0EB0AC", End = "0EB0BC", Type = "ptr", Description = "TableCapSwitchActions" },
                new RomMapEntry { Start = "0EB0BC", End = "0EB0E0", Type = "ptr", Description = "TableKingBobombActions" },
                new RomMapEntry { Start = "0EB140", End = "0EB15C", Type = "ptr", Description = "TableOpenedCannonActions" },
                new RomMapEntry { Start = "0EB198", End = "0EB1A8", Type = "ptr", Description = "TableChuckyaActions" },
                new RomMapEntry { Start = "0EB224", End = "0EB22C", Type = "ptr", Description = "TableCoinInsideBooActions" },
                new RomMapEntry { Start = "0EB298", End = "0EB2AC", Type = "ptr", Description = "TableGrindelThwompActions" },
                new RomMapEntry { Start = "0EB2DC", End = "0EB2EC", Type = "ptr", Description = "TableTumblingBridgeActions" },
                new RomMapEntry { Start = "0EB318", End = "0EB32C", Type = "ptr", Description = "TableElevatorActions" },
                new RomMapEntry { Start = "0EB370", End = "0EB380", Type = "ptr", Description = "TableLittleCageActions" },
                new RomMapEntry { Start = "0EB3E8", End = "0EB3F8", Type = "ptr", Description = "TableHeaveHoActions" },
                new RomMapEntry { Start = "0EB408", End = "0EB410", Type = "ptr", Description = "TableJumpingBoxActions" },
                new RomMapEntry { Start = "0EB420", End = "0EB42C", Type = "ptr", Description = "TableBetaBooKeyInsideActions" },
                new RomMapEntry { Start = "0EB43C", End = "0EB450", Type = "ptr", Description = "TableBulletBillActions" },
                new RomMapEntry { Start = "0EB450", End = "0EB45C", Type = "ptr", Description = "TableBowserTailAnchorActions" },
                new RomMapEntry { Start = "0EB4C8", End = "0EB518", Type = "ptr", Description = "TableBowserActions" },
                new RomMapEntry { Start = "0EB67C", End = "0EB688", Type = "ptr", Description = "TableFallingBowserPlatformActions" },
                new RomMapEntry { Start = "0EB7A0", End = "0EB7C0", Type = "ptr", Description = "TableUkikiOpenCageActions" },
                new RomMapEntry { Start = "0EB830", End = "0EB840", Type = "ptr", Description = "TableRotatingCwFireBarsActions" },
                new RomMapEntry { Start = "0EB8D8", End = "0EB8F8", Type = "ptr", Description = "TableToxBoxActions" },
                new RomMapEntry { Start = "0EB900", End = "0EB924", Type = "ptr", Description = "TablePiranhaPlantActions" },
                new RomMapEntry { Start = "0EBB1C", End = "0EBB38", Type = "ptr", Description = "TableBowserPuzzlePieceActions" },
                new RomMapEntry { Start = "0EBB38", End = "0EBB44", Type = "ptr", Description = "TableTuxiesMotherActions" },
                new RomMapEntry { Start = "0EBB44", End = "0EBB5C", Type = "ptr", Description = "TableSmallPenguinActions" },
                new RomMapEntry { Start = "0EBB5C", End = "0EBB68", Type = "ptr", Description = "TableFishActions" },
                new RomMapEntry { Start = "0EBB68", End = "0EBB74", Type = "ptr", Description = "TableFishGroupActions" },
                new RomMapEntry { Start = "0EBB74", End = "0EBB84", Type = "ptr", Description = "TableBirdChirpChirpActions" },
                new RomMapEntry { Start = "0EBB84", End = "0EBB90", Type = "ptr", Description = "TableCheepCheepActions" },
                new RomMapEntry { Start = "0EBC20", End = "0EBC38", Type = "ptr", Description = "TableExclamationBoxActions" },
                new RomMapEntry { Start = "0EBC68", End = "0EBC74", Type = "ptr", Description = "TableTweesterActions" },
                new RomMapEntry { Start = "0EBC98", End = "0EBCB0", Type = "ptr", Description = "TableBooActions" },
                new RomMapEntry { Start = "0EBCB0", End = "0EBCC4", Type = "ptr", Description = "TableBooGivingStarActions" },
                new RomMapEntry { Start = "0EBCC4", End = "0EBCD4", Type = "ptr", Description = "TableBooWithCageActions" },
                new RomMapEntry { Start = "0EBCE4", End = "0EBD0C", Type = "ptr", Description = "TableWhompActions" },
                new RomMapEntry { Start = "0F5580", End = "102D08", Type = "asm", Description = "ASM copied to 80378800-80385F88" },
                new RomMapEntry { Start = "108590", End = "108614", Type = "ptr", Description = "GeoLayoutJumpTable" },
                new RomMapEntry { Start = "108638", End = "10872C", Type = "ptr", Description = "LevelScriptJumpTable" },
                new RomMapEntry { Start = "108730", End = "108810", Type = "ptr", Description = "BehaviorJumpTable" },
                new RomMapEntry { Start = "108A10", End = "108A40", Type = "level", Description = "main entry" },
                new RomMapEntry { Start = "108A40", End = "114750", Type = "MIO0", Description = "font graphics" },
                new RomMapEntry { Start = "114750", End = "1279B0", Type = "MIO0", Description = "mario" },
                new RomMapEntry { Start = "1279B0", End = "12A7E0", Type = "Geo Layout", Description = "water sparkles mario" },
                new RomMapEntry { Start = "12A7E0", End = "132850", Type = "MIO0", Description = "yoshiegg owl thwomp" },
                new RomMapEntry { Start = "132850", End = "132C60", Type = "Geo Layout", Description = "owl thwomp bullet heave" },
                new RomMapEntry { Start = "132C60", End = "134A70", Type = "MIO0", Description = "big bully" },
                new RomMapEntry { Start = "134A70", End = "134D20", Type = "Geo Layout", Description = "bully blargg" },
                new RomMapEntry { Start = "134D20", End = "13B5D0", Type = "MIO0", Description = "kingbobomb" },
                new RomMapEntry { Start = "13B5D0", End = "13B910", Type = "Geo Layout", Description = "kingbobomb bubble" },
                new RomMapEntry { Start = "13B910", End = "145C10", Type = "MIO0", Description = "sea creatures texture" },
                new RomMapEntry { Start = "145C10", End = "145E90", Type = "Geo Layout", Description = "sea creatures" },
                new RomMapEntry { Start = "145E90", End = "151B70", Type = "MIO0", Description = "vulture pokey" },
                new RomMapEntry { Start = "151B70", End = "1521D0", Type = "Geo Layout", Description = "klepto eyerock pokey" },
                new RomMapEntry { Start = "1521D0", End = "1602E0", Type = "MIO0", Description = "monkey" },
                new RomMapEntry { Start = "1602E0", End = "160670", Type = "Geo Layout", Description = "mole monkey fwoosh" },
                new RomMapEntry { Start = "160670", End = "1656E0", Type = "MIO0", Description = "spindrift penguin snowman" },
                new RomMapEntry { Start = "1656E0", End = "165A50", Type = "Geo Layout", Description = "spindrift penguin blizzard" },
                new RomMapEntry { Start = "165A50", End = "166BD0", Type = "MIO0", Description = "checkerboard question" },
                new RomMapEntry { Start = "166BD0", End = "166C60", Type = "Geo Layout", Description = "cap switch" },
                new RomMapEntry { Start = "166C60", End = "16D5C0", Type = "MIO0", Description = "piano books" },
                new RomMapEntry { Start = "16D5C0", End = "16D870", Type = "Geo Layout", Description = "bookend chair piano boo" },
                new RomMapEntry { Start = "16D870", End = "180540", Type = "MIO0", Description = "peach toadstool" },
                new RomMapEntry { Start = "180540", End = "180BB0", Type = "Geo Layout", Description = "birds peach yoshi" },
                new RomMapEntry { Start = "180BB0", End = "187FA0", Type = "MIO0", Description = "enemy" },
                new RomMapEntry { Start = "187FA0", End = "188440", Type = "Geo Layout", Description = "bubba wiggler lakitu" },
                new RomMapEntry { Start = "188440", End = "1B9070", Type = "MIO0", Description = "bowser" },
                new RomMapEntry { Start = "1B9070", End = "1B9CC0", Type = "Geo Layout", Description = "bowser flames bomb" },
                new RomMapEntry { Start = "1B9CC0", End = "1C3DB0", Type = "MIO0", Description = "treasure chest fish" },
                new RomMapEntry { Start = "1C3DB0", End = "1C4230", Type = "Geo Layout", Description = "skeeter fish manta chest" },
                new RomMapEntry { Start = "1C4230", End = "1D7C90", Type = "MIO0", Description = "koopa whomp" },
                new RomMapEntry { Start = "1D7C90", End = "1D8310", Type = "Geo Layout", Description = "koopa log piranha whomp chomp" },
                new RomMapEntry { Start = "1D8310", End = "1E4BF0", Type = "MIO0", Description = "lakitu toad" },
                new RomMapEntry { Start = "1E4BF0", End = "1E51F0", Type = "Geo Layout", Description = "lakitu toad mips boo" },
                new RomMapEntry { Start = "1E51F0", End = "1E7D90", Type = "MIO0", Description = "chillychief moneybag" },
                new RomMapEntry { Start = "1E7D90", End = "1E7EE0", Type = "Geo Layout", Description = "moneybag" },
                new RomMapEntry { Start = "1E7EE0", End = "1F1B30", Type = "MIO0", Description = "mri swoop" },
                new RomMapEntry { Start = "1F1B30", End = "1F2200", Type = "Geo Layout", Description = "mri swoop snufit dorrie scuttlebug" },
                new RomMapEntry { Start = "1F2200", End = "2008D0", Type = "MIO0", Description = "chuckya shyguy goomba" },
                new RomMapEntry { Start = "2008D0", End = "201410", Type = "Geo Layout", Description = "cannon box switch enemies" },
                new RomMapEntry { Start = "201410", End = "218DA0", Type = "MIO0", Description = "doors trees coins" },
                new RomMapEntry { Start = "218DA0", End = "219E00", Type = "Geo Layout", Description = "coins pipe doors maps trees" },
                new RomMapEntry { Start = "219E00", End = "21F4C0", Type = "behavior", Description = "behavior data" },
                new RomMapEntry { Start = "21F4C0", End = "2577B8", Type = "asm", Description = "ASM copied to 8016F000-801A72F8" },
                new RomMapEntry { Start = "269EA0", End = "26A3A0", Type = "level", Description = "game over level" },
                new RomMapEntry { Start = "26A3A0", End = "26F420", Type = "MIO0", Description = "wood trademark" },
                new RomMapEntry { Start = "26F420", End = "2708C0", Type = "MIO0", Description = "debug level select" },
                new RomMapEntry { Start = "2708C0", End = "2739A0", Type = "MIO0", Description = "mario bg" },
                new RomMapEntry { Start = "2A6120", End = "2A65B0", Type = "level", Description = "main menu level" },
                new RomMapEntry { Start = "2A65B0", End = "2ABCA0", Type = "MIO0", Description = "main menu segment7" },
                new RomMapEntry { Start = "2ABCA0", End = "2AC6B0", Type = "level", Description = "main level scripts" },
                new RomMapEntry { Start = "2AC6B0", End = "2B8F10", Type = "MIO0", Description = "water skybox" },
                new RomMapEntry { Start = "2B8F10", End = "2C73D0", Type = "MIO0", Description = "ccm skybox" },
                new RomMapEntry { Start = "2C73D0", End = "2D0040", Type = "MIO0", Description = "clouds skybox" },
                new RomMapEntry { Start = "2D0040", End = "2D64F0", Type = "MIO0", Description = "bifs skybox" },
                new RomMapEntry { Start = "2D64F0", End = "2E7880", Type = "MIO0", Description = "wdw skybox" },
                new RomMapEntry { Start = "2E7880", End = "2F14E0", Type = "MIO0", Description = "cloud floor skybox" },
                new RomMapEntry { Start = "2F14E0", End = "2FB1B0", Type = "MIO0", Description = "ssl skybox" },
                new RomMapEntry { Start = "2FB1B0", End = "301CD0", Type = "MIO0", Description = "bbh skybox" },
                new RomMapEntry { Start = "301CD0", End = "30CEC0", Type = "MIO0", Description = "bidw skybox" },
                new RomMapEntry { Start = "30CEC0", End = "31E1D0", Type = "MIO0", Description = "bits skybox" },
                new RomMapEntry { Start = "31E1D0", End = "326E40", Type = "MIO0", Description = "lll textures" },
                new RomMapEntry { Start = "326E40", End = "32D070", Type = "MIO0", Description = "bbh textures" },
                new RomMapEntry { Start = "32D070", End = "334B30", Type = "MIO0", Description = "bob textures" },
                new RomMapEntry { Start = "334B30", End = "33D710", Type = "MIO0", Description = "jrb textures" },
                new RomMapEntry { Start = "33D710", End = "341140", Type = "MIO0", Description = "rr textures" },
                new RomMapEntry { Start = "341140", End = "347A50", Type = "MIO0", Description = "ccm textures" },
                new RomMapEntry { Start = "347A50", End = "34E760", Type = "MIO0", Description = "hmc textures" },
                new RomMapEntry { Start = "34E760", End = "351960", Type = "MIO0", Description = "ttc textures" },
                new RomMapEntry { Start = "351960", End = "357350", Type = "MIO0", Description = "ttm textures" },
                new RomMapEntry { Start = "357350", End = "35ED10", Type = "MIO0", Description = "wf textures" },
                new RomMapEntry { Start = "35ED10", End = "365980", Type = "MIO0", Description = "castle grounds textures" },
                new RomMapEntry { Start = "365980", End = "36F530", Type = "MIO0", Description = "inside castle textures" },
                new RomMapEntry { Start = "36F530", End = "371C40", Type = "MIO0", Description = "flower textures" },
                new RomMapEntry { Start = "371C40", End = "3828C0", Type = "MIO0", Description = "bbh segment7" },
                new RomMapEntry { Start = "3828C0", End = "383950", Type = "level", Description = "bbh level" },
                new RomMapEntry { Start = "383950", End = "395C90", Type = "MIO0", Description = "ccm segment7" },
                new RomMapEntry { Start = "395C90", End = "396340", Type = "level", Description = "ccm level" },
                new RomMapEntry { Start = "396340", End = "3CF0D0", Type = "MIO0", Description = "inside castle segment7" },
                new RomMapEntry { Start = "3CF0D0", End = "3D0DC0", Type = "level", Description = "inside castle level" },
                new RomMapEntry { Start = "3D0DC0", End = "3E6A00", Type = "MIO0", Description = "hmc segment7" },
                new RomMapEntry { Start = "3E6A00", End = "3E76B0", Type = "level", Description = "hmc level" },
                new RomMapEntry { Start = "3E76B0", End = "3FB990", Type = "MIO0", Description = "ssl segment7" },
                new RomMapEntry { Start = "3FB990", End = "3FC2B0", Type = "level", Description = "ssl level" },
                new RomMapEntry { Start = "3FC2B0", End = "405A60", Type = "MIO0", Description = "bob segment7" },
                new RomMapEntry { Start = "405A60", End = "405FB0", Type = "level", Description = "bob level" },
                new RomMapEntry { Start = "405FB0", End = "40E840", Type = "MIO0", Description = "sl segment7" },
                new RomMapEntry { Start = "40E840", End = "40ED70", Type = "level", Description = "sl level" },
                new RomMapEntry { Start = "40ED70", End = "419F90", Type = "MIO0", Description = "wdw segment7" },
                new RomMapEntry { Start = "419F90", End = "41A760", Type = "level", Description = "wdw level" },
                new RomMapEntry { Start = "41A760", End = "423B20", Type = "MIO0", Description = "jrb segment7" },
                new RomMapEntry { Start = "423B20", End = "4246D0", Type = "level", Description = "jrb level" },
                new RomMapEntry { Start = "4246D0", End = "42C6E0", Type = "MIO0", Description = "thi segment7" },
                new RomMapEntry { Start = "42C6E0", End = "42CF20", Type = "level", Description = "thi level" },
                new RomMapEntry { Start = "42CF20", End = "437400", Type = "MIO0", Description = "ttc segment7" },
                new RomMapEntry { Start = "437400", End = "437870", Type = "level", Description = "ttc level" },
                new RomMapEntry { Start = "437870", End = "44A140", Type = "MIO0", Description = "rr segment7" },
                new RomMapEntry { Start = "44A140", End = "44ABC0", Type = "level", Description = "rr level" },
                new RomMapEntry { Start = "44ABC0", End = "4545E0", Type = "MIO0", Description = "castle grounds segment7" },
                new RomMapEntry { Start = "4545E0", End = "454E00", Type = "level", Description = "castle grounds level" },
                new RomMapEntry { Start = "454E00", End = "45BF60", Type = "MIO0", Description = "bidw segment7" },
                new RomMapEntry { Start = "45BF60", End = "45C600", Type = "level", Description = "bidw level" },
                new RomMapEntry { Start = "45C600", End = "461220", Type = "MIO0", Description = "vanish cap segment7" },
                new RomMapEntry { Start = "461220", End = "4614D0", Type = "level", Description = "vanish cap level" },
                new RomMapEntry { Start = "4614D0", End = "46A840", Type = "MIO0", Description = "bifs segment7" },
                new RomMapEntry { Start = "46A840", End = "46B090", Type = "level", Description = "bifs level" },
                new RomMapEntry { Start = "46B090", End = "46C1A0", Type = "MIO0", Description = "secret aquarium segment7" },
                new RomMapEntry { Start = "46C1A0", End = "46C3A0", Type = "level", Description = "secret aquarium level" },
                new RomMapEntry { Start = "46C3A0", End = "477D00", Type = "MIO0", Description = "bits segment7" },
                new RomMapEntry { Start = "477D00", End = "4784A0", Type = "level", Description = "bits level" },
                new RomMapEntry { Start = "4784A0", End = "48C9B0", Type = "MIO0", Description = "lll segment7" },
                new RomMapEntry { Start = "48C9B0", End = "48D930", Type = "level", Description = "lll level" },
                new RomMapEntry { Start = "48D930", End = "495A60", Type = "MIO0", Description = "ddd segment7" },
                new RomMapEntry { Start = "495A60", End = "496090", Type = "level", Description = "ddd level" },
                new RomMapEntry { Start = "496090", End = "49DA50", Type = "MIO0", Description = "wf segment7" },
                new RomMapEntry { Start = "49DA50", End = "49E710", Type = "level", Description = "wf level" },
                new RomMapEntry { Start = "49E710", End = "4AC4B0", Type = "MIO0", Description = "cake end" },
                new RomMapEntry { Start = "4AC4B0", End = "4AC570", Type = "level", Description = "cake end level" },
                new RomMapEntry { Start = "4AC570", End = "4AF670", Type = "MIO0", Description = "castle courtyard segment7" },
                new RomMapEntry { Start = "4AF670", End = "4AF930", Type = "level", Description = "castle courtyard level" },
                new RomMapEntry { Start = "4AF930", End = "4B7F10", Type = "MIO0", Description = "secret slide segment7" },
                new RomMapEntry { Start = "4B7F10", End = "4B80D0", Type = "level", Description = "secret slide level" },
                new RomMapEntry { Start = "4B80D0", End = "4BE9E0", Type = "MIO0", Description = "metal cap segment7" },
                new RomMapEntry { Start = "4BE9E0", End = "4BEC30", Type = "level", Description = "metal cap level" },
                new RomMapEntry { Start = "4BEC30", End = "4C2700", Type = "MIO0", Description = "wing cap segment7" },
                new RomMapEntry { Start = "4C2700", End = "4C2920", Type = "level", Description = "wing cap level" },
                new RomMapEntry { Start = "4C2920", End = "4C41C0", Type = "MIO0", Description = "bidw platform segment7" },
                new RomMapEntry { Start = "4C41C0", End = "4C4320", Type = "level", Description = "bidw platform level" },
                new RomMapEntry { Start = "4C4320", End = "4CD930", Type = "MIO0", Description = "rainbow bonus segment7" },
                new RomMapEntry { Start = "4CD930", End = "4CDBD0", Type = "level", Description = "rainbow bonus level" },
                new RomMapEntry { Start = "4CDBD0", End = "4CE9F0", Type = "MIO0", Description = "bifs platform segment7" },
                new RomMapEntry { Start = "4CE9F0", End = "4CEC00", Type = "level", Description = "bifs platform level" },
                new RomMapEntry { Start = "4CEC00", End = "4D14F0", Type = "MIO0", Description = "bits platform segment7" },
                new RomMapEntry { Start = "4D14F0", End = "4D1910", Type = "level", Description = "bits platform level" },
                new RomMapEntry { Start = "4D1910", End = "4EB1F0", Type = "MIO0", Description = "ttm segment7" },
                new RomMapEntry { Start = "4EB1F0", End = "4EC000", Type = "level", Description = "ttm level" },
                new RomMapEntry { Start = "4EC000", End = "579C26", Type = "bin", Description = "mario animation" },
                new RomMapEntry { Start = "57B720", End = "593560", Type = "m64", Description = "ADSR/Controls" },
                new RomMapEntry { Start = "593560", End = "7B0860", Type = "m64", Description = "Raw SFX Data/Table" },
                new RomMapEntry { Start = "7B0860", End = "7CC620", Type = "m64", Description = "Music files" },
                new RomMapEntry { Start = "7CC620", End = "7CC6C0", Type = "instrset", Description = "Instrument Set" },
                new RomMapEntry { Start = "7CC6C0", End = "7FFFFF", Type = "padding", Description = "Padding (End of 8MB ROM)" }
            };

            LoadDefaultMap();
            AutoLoadProjectMap();
        }

        private void LoadDefaultMap()
        {
            _displayEntries = new List<RomMapEntry>(_defaultEntries);
            ApplyFilter();
            ActiveMapLabel.Text = "Map File: Default (original static ROM map)";
        }

        private void AutoLoadProjectMap()
        {
            if (string.IsNullOrEmpty(_projectRoot) || !Directory.Exists(_projectRoot))
            {
                // Fallback to check WSL path directly
                string wslPath = @"\\wsl.localhost\Ubuntu-20.04\root\sm64";
                if (Directory.Exists(wslPath))
                {
                    _projectRoot = wslPath;
                }
            }

            if (!string.IsNullOrEmpty(_projectRoot) && Directory.Exists(_projectRoot))
            {
                // Look for maps in build folder
                string buildDir = Path.Combine(_projectRoot, "build");
                if (Directory.Exists(buildDir))
                {
                    try
                    {
                        var mapFiles = Directory.GetFiles(buildDir, "*.map", SearchOption.AllDirectories);
                        if (mapFiles.Length > 0)
                        {
                            // Load first found map file
                            string mapPath = mapFiles[0];
                            LoadMapFile(mapPath);
                        }
                    }
                    catch { }
                }
            }
        }

        private void LoadMapFile(string mapPath)
        {
            ParseMapFile(mapPath);
            RebuildDisplayEntries();
            ActiveMapLabel.Text = $"Map File: {Path.GetFileName(mapPath)} (compiled addresses loaded)";
        }

        private void ParseMapFile(string mapPath)
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
                MessageBox.Show($"Error parsing map file: {ex.Message}", "Map Parser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ExtractSegmentName(string sym)
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

        private ulong? TranslateRamToRom(ulong ramAddr)
        {
            if (ramAddr < 0x04000000)
            {
                return ramAddr; // Already a ROM offset address!
            }
            foreach (var seg in _segments.Values)
            {
                if (ramAddr >= seg.RamStart && ramAddr < seg.RamEnd)
                {
                    return (ramAddr - seg.RamStart) + seg.RomStart;
                }
            }
            return null;
        }

        private void RebuildDisplayEntries()
        {
            _displayEntries.Clear();

            if (ShowAllSymbolsCheck.IsChecked == true)
            {
                // Show ALL parsed symbols from the map file
                foreach (var sym in _symbols.OrderBy(s => s.Value))
                {
                    ulong? romAddr = TranslateRamToRom(sym.Value);
                    if (romAddr.HasValue)
                    {
                        _displayEntries.Add(new RomMapEntry
                        {
                            Start = romAddr.Value.ToString("X6"),
                            End = string.Empty,
                            Type = "symbol",
                            Description = $"{sym.Key} (RAM: {sym.Value:X8})"
                        });
                    }
                }

                // Also list the parsed segments as full blocks
                foreach (var seg in _segments.Values.OrderBy(s => s.RomStart))
                {
                    _displayEntries.Add(new RomMapEntry
                    {
                        Start = seg.RomStart.ToString("X6"),
                        End = (seg.RomStart + (seg.RamEnd - seg.RamStart)).ToString("X6"),
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
                    var newEntry = new RomMapEntry
                    {
                        Start = def.Start,
                        End = def.End,
                        Type = def.Type,
                        Description = def.Description
                    };

                    bool updated = false;

                    // 1. Check if it's a curated segment description
                    if (CuratedSegmentMap.TryGetValue(def.Description, out string? segName) && _segments.TryGetValue(segName, out var seg))
                    {
                        newEntry.Start = seg.RomStart.ToString("X6");
                        newEntry.End = (seg.RomStart + (seg.RamEnd - seg.RamStart)).ToString("X6");
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
                                // Recalculate end if we can find length or keep default length
                                if (ulong.TryParse(def.Start, System.Globalization.NumberStyles.HexNumber, null, out ulong origStart) &&
                                    ulong.TryParse(def.End, System.Globalization.NumberStyles.HexNumber, null, out ulong origEnd))
                                {
                                    ulong len = origEnd - origStart;
                                    newEntry.End = (romAddr.Value + len).ToString("X6");
                                }
                            }
                            updated = true;
                        }
                    }

                    _displayEntries.Add(newEntry);
                }
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string filter = SearchBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(filter))
            {
                _filteredEntries = new List<RomMapEntry>(_displayEntries);
            }
            else
            {
                _filteredEntries = _displayEntries.Where(entry =>
                    entry.Start.ToLower().Contains(filter) ||
                    entry.End.ToLower().Contains(filter) ||
                    entry.Type.ToLower().Contains(filter) ||
                    entry.Description.ToLower().Contains(filter)
                ).ToList();
            }

            AddressGrid.ItemsSource = _filteredEntries;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        private void ShowAllSymbolsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_symbols.Count > 0)
            {
                RebuildDisplayEntries();
            }
            else
            {
                MessageBox.Show("Please load a compiled .map file first to view all custom symbols.", "Map File Required", MessageBoxButton.OK, MessageBoxImage.Information);
                ShowAllSymbolsCheck.IsChecked = false;
            }
        }

        private void LoadMap_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Compiled Linker Map File (*.map)",
                Filter = "Linker Map Files (*.map)|*.map|All Files (*.*)|*.*",
                InitialDirectory = _projectRoot ?? AppDomain.CurrentDomain.BaseDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                LoadMapFile(dialog.FileName);
            }
        }

        private void AddressGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.Start, "Start Address");
            }
        }

        private void CopyStart_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.Start, "Start Address");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyEnd_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                CopyTextToClipboard(entry.End, "End Address");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyFull_Click(object sender, RoutedEventArgs e)
        {
            if (AddressGrid.SelectedItem is RomMapEntry entry)
            {
                string text = $"{entry.Start}\t{entry.End}\t{entry.Type}\t{entry.Description}";
                CopyTextToClipboard(text, "Full Entry");
            }
            else
            {
                MessageBox.Show("Please select an entry from the list first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CopyTextToClipboard(string text, string label)
        {
            try
            {
                Clipboard.SetText(text);
                // Mini feedback on status bar, or simple silent copy to avoid annoying popup, or messagebox
                // Let's use a non-blocking dialog or messagebox
                MessageBox.Show($"{label} copied to clipboard: {text}", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
