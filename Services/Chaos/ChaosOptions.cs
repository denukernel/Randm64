namespace Sm64DecompLevelViewer.Services.Chaos;

internal sealed class ChaosOptions
{
    public Dictionary<string, double> IntensityOverrides { get; init; } = new();
    public M64MutationSettings MusicSettings { get; init; } = new();
    public int GameLogicCycleFrames { get; init; } = 300;
    public double IntensityFor(string tool) => IntensityOverrides.TryGetValue(tool, out var value) ? value : Intensity;
    public required int Seed { get; init; }
    public required double Intensity { get; init; }
    public required bool TargetMusic { get; init; }
    public required bool M64KeepStructure { get; init; }
    public required bool TargetSounds { get; init; }
    public required bool ShuffleSounds { get; init; }
    public required bool ExcludeInstruments { get; init; }
    public required bool ExcludeSfx { get; init; }
    public required bool ShuffleSfxOnly { get; init; }
    public required bool RandomizeDl { get; init; }
    public required int DlMode { get; init; }
    public required bool RandomizeCollision { get; init; }
    public required int CollisionMode { get; init; }
    public required bool TargetModels { get; init; }
    public required bool TargetGoddard { get; init; }
    public required int GoddardMode { get; init; }
    public required int Mode { get; init; }
    public required bool RandomizeMarioColors { get; init; }
    public required int MarioColorsArea { get; init; }
    public required int MarioColorsMode { get; init; }
    public required bool RandomizeSkybox { get; init; }
    public required bool RandomizeText { get; init; }
    public required int TextMode { get; init; }
    public required int[] M64Modes { get; init; }
    public required bool RandomizeTextures { get; init; }
    public required int TextureMode { get; init; }
    public required bool GlitchAnimations { get; init; }
    public required int AnimMode { get; init; }
    public required bool GlitchHud { get; init; }
    public required int HudMode { get; init; }
    public required bool ReplaceSfx { get; init; }
    public required bool ReplaceTexturesCustom { get; init; }
    public required bool JumpWeird { get; init; }
    public required bool JumpDeath { get; init; }
    public required bool LimboMario { get; init; }
    public required int LimboMarioMode { get; init; }
    public required bool AudioDistortion { get; init; }
    public required bool AlienSound { get; init; }
    public required bool WackierSounds { get; init; }
    public required bool WackyInstruments { get; init; }
    public required bool AdsrScrambler { get; init; }
    public required bool VibratoPitch { get; init; }
    public required bool DmaBitcrusher { get; init; }
    public required bool GameLogicRandomizer { get; init; }
    public required bool ScrambleTitleScreen { get; init; }
    public required int TitleScramblerMode { get; init; }
    public required bool RandomizeCutsceneCamera { get; init; }
    public required int CutsceneCameraMode { get; init; }
    public required bool LakituCameraChaos { get; init; }
    public required int LakituCameraMode { get; init; }
    public required bool StartLevelChaos { get; init; }
    public required string StartLevelConstant { get; init; }
    public required string SelectedLevel { get; init; }
    public required bool DlExclusion { get; init; }
    public int[]? SfxModes { get; init; }
    public int[] SelectedSfxModes => SfxModes ?? (SfxMode == 0 ? [] : [SfxMode]);
    public required int SfxMode { get; init; }
    public required bool SfxIdentityShuffle { get; init; }
    public required bool SfxPitchVariation { get; init; }
    public required bool SelectSpecificM64 { get; init; }
    public required List<string> SelectedM64Files { get; init; }
    public required bool RandomizeSelectedOnly { get; init; }
    public required List<int> ActiveGameLogicModes { get; init; }
    public required bool GameLogicSimultaneous { get; init; }
    public required List<string> SelectedTextures { get; init; }
    public required List<ChaosReplacement> SfxReplacementRules { get; init; }
    public required List<ChaosReplacement> TextureReplacementRules { get; init; }
}

internal sealed record ChaosReplacement(string TargetPath, string ReplacementPath);
internal sealed record ChaosProgress(string? Status = null, int? Percent = null);
