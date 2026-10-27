namespace Sm64DecompLevelViewer.Services.Build;
internal enum BuildTone { Success, Error }
internal sealed record BuildProgress(string? Title = null, string? Status = null, string? Log = null, BuildTone? Tone = null, bool? CanClose = null);
