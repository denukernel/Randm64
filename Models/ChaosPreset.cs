using System.Collections.Generic;

namespace Sm64DecompLevelViewer
{
    public class ChaosPreset
    {
        public Dictionary<string, double> IntensityOverrides { get; set; } = new();
        public Services.Chaos.M64MutationSettings MusicSettings { get; set; } = new();
        public int GameLogicCycleFrames { get; set; } = 300;
        public string PresetName { get; set; } = string.Empty;
        public double Intensity { get; set; } = 10;
        public int TargetLevelIndex { get; set; } = 0;
        public string ChaosSeed { get; set; } = string.Empty;
        public bool UseRandomSeed { get; set; } = true;
        
        public bool TargetMusicNotes { get; set; } = true;
        public int MusicNotesMode { get; set; } = 0;
        public List<int> ActiveM64Modes { get; set; } = new() { 0, 1, 2, 3, 4, 5 };
        public bool M64All { get; set; } = true;
        public bool M64Select { get; set; } = false;
        public bool M64KeepStructure { get; set; } = true;

        public bool TargetSounds { get; set; } = true;
        public bool ShuffleSounds { get; set; } = false;
        public bool ExcludeInstrumentsShuffle { get; set; } = false;
        public bool ExcludeSfxShuffle { get; set; } = false;
        public bool ShuffleSfxOnly { get; set; } = false;
        public bool SfxIdentityShuffle { get; set; } = true;
        public bool SfxPitchVariation { get; set; } = false;
        public List<int>? ActiveSfxModes { get; set; }
        public int SfxRandomizerMode { get; set; } = 0;

        public bool ReplaceSfx { get; set; } = false;

        public bool RandomizeDl { get; set; } = false;
        public int DlRandomizerMode { get; set; } = 0;
        public bool DlExclusion { get; set; } = false;

        public bool TargetModels { get; set; } = true;
        public bool TargetGoddard { get; set; } = true;
        public int GoddardMode { get; set; } = 0;
        public bool RandomizeMarioColors { get; set; } = false;
        public int MarioColorsArea { get; set; } = 0;
        public int MarioColorsMode { get; set; } = 0;

        public bool GlitchAnimations { get; set; } = false;
        public int AnimationGlitcherMode { get; set; } = 0;

        public bool GlitchHud { get; set; } = false;
        public int HudGlitcherMode { get; set; } = 0;

        public bool RandomizeTextures { get; set; } = false;
        public int TextureRandomizeMode { get; set; } = 0;
        public bool ReplaceTexturesCustom { get; set; } = false;
        public List<TextureReplacerWindow.TextureReplacementRule> ReplaceTexturesRules { get; set; } = new();
        public bool TextureRandomizeSelectedOnly { get; set; } = false;
        public List<string> TextureRandomizeSelectedPaths { get; set; } = new();

        public int ModeIndex { get; set; } = 0;
        public bool RandomizeSkybox { get; set; } = false;
        public bool RandomizeText { get; set; } = false;
        public int TextRandomizeMode { get; set; } = 0;

        public bool ChaosLogicJumpWeird { get; set; } = false;
        public bool ChaosLogicJumpDeath { get; set; } = false;
        public bool LimboMario { get; set; } = false;
        public int LimboMarioMode { get; set; } = 0;
        public bool AlienSoundCheat { get; set; } = false;
        public bool AudioDistortion { get; set; } = false;
        public List<int> ActiveAudioDistortionModes { get; set; } = new() { 0 };
        public bool GameLogicRandomizer { get; set; } = false;
        public List<int> ActiveGameLogicModes { get; set; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
        public bool GameLogicSimultaneous { get; set; } = true;

        public bool ScrambleTitleScreen { get; set; } = false;
        public int TitleScreenScramblerMode { get; set; } = 0;
        public bool RandomizeCutsceneCamera { get; set; } = false;
        public int CutsceneCameraMode { get; set; } = 0;
        public bool LakituCameraChaos { get; set; } = false;
        public int LakituCameraMode { get; set; } = 0;
        public bool StartLevelChaos { get; set; } = false;
        public int StartLevelIndex { get; set; } = 0;
    }

    public class ChaosQueueFile
    {
        public List<ChaosPreset> Queue { get; set; } = new();
        public List<ChaosPreset> Stockpile { get; set; } = new();
        public bool Accumulate { get; set; } = true;
    }

}
