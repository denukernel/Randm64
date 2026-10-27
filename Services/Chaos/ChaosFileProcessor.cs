using System.IO;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosFileProcessor
{
    private readonly Random _random;
    private readonly ChaosQueueState _queue;
    public ChaosFileProcessor(Random random, ChaosQueueState queue) { _random = random; _queue = queue; }
    public void Apply(List<string> filesToCorrupt, ChaosOptions options, IProgress<ChaosProgress> progress, CancellationToken token = default, List<string>? diagnostics = null)
    {
        var marioColorCorruptor = new MarioColorCorruptor(_random);
        var goddardCorruptor = new GoddardCorruptor(_random);
        var collisionCorruptor = new CollisionCorruptor(_random);
        var displayListCorruptor = new DisplayListCorruptor(_random);
        var hudCorruptor = new HudCorruptor(_random);
        var animationCorruptor = new AnimationCorruptor(_random);
        var sourceCorruptor = new SourceCorruptor(_random);
        var soundCorruptor = new SoundCorruptor(_random);
        var textureCorruptor = new TextureCorruptor(_random);
            var intensity = options.Intensity;
            var targetMusic = options.TargetMusic;
            var m64KeepStructure = options.M64KeepStructure;
            var randomizeDl = options.RandomizeDl;
            var dlMode = options.DlMode;
            var randomizeCollision = options.RandomizeCollision;
            var collisionMode = options.CollisionMode;
            var targetModels = options.TargetModels;
            var goddardMode = options.GoddardMode;
            var mode = options.Mode;
            var randomizeMarioColors = options.RandomizeMarioColors;
            var marioColorsArea = options.MarioColorsArea;
            var marioColorsMode = options.MarioColorsMode;
            var m64Modes = options.M64Modes;
            var textureMode = options.TextureMode;
            var animMode = options.AnimMode;
            var hudMode = options.HudMode;
            var replaceSfx = options.ReplaceSfx;
            var replaceTexturesCustom = options.ReplaceTexturesCustom;
            var sfxReplacementRules = options.SfxReplacementRules;
            var textureReplacementRules = options.TextureReplacementRules;
                    var failures = new List<string>();
                    int totalFiles = filesToCorrupt.Count;
                    if (totalFiles == 0)
                    {
                        if (targetMusic)
                            throw new InvalidOperationException("No M64 files matched the selected music targets.");
                        progress.Report(new ChaosProgress("No files found for selected targets.", null));
                        return;
                    }

                    int processed = 0;

                    foreach (var file in filesToCorrupt)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            if (ext == ".m64")
                            {
                                bool accumulate = _queue.IsRunning && _queue.IsAccumulating && _queue.FilesModified.Contains(file);
                                new M64Corruptor(_random).CorruptFile(file, m64KeepStructure ? m64Modes : Array.Empty<int>(), Math.Clamp(options.IntensityFor("M64"), 0, 100), !m64KeepStructure, accumulate, options.MusicSettings, diagnostics);
                                if (_queue.IsRunning) _queue.FilesModified.Add(file);
                            }
                            else
                            {
                                // Create backup before modifying if it doesn't already exist; restore original before corrupting
                                string backupPath = file + ".bak";
                                if (!File.Exists(backupPath))
                                {
                                    File.Copy(file, backupPath);
                                }
                                else if (!_queue.PreparedAssets.Contains(file) && (!_queue.IsRunning || !_queue.IsAccumulating || !_queue.FilesModified.Contains(file)))
                                {
                                    File.Copy(backupPath, file, true);
                                }
                                if (_queue.IsRunning)
                                {
                                    _queue.FilesModified.Add(file);
                                }

                            }

                            if (ext == ".aiff")
                            {
                                // If this sound is targeted for custom replacement, skip corrupting it so it stays clean and replaced
                                if (replaceSfx && sfxReplacementRules.Any(r => r.TargetPath == file))
                                {
                                    // Skip
                                }
                                else
                                {
                                    soundCorruptor.CorruptBinaryFile(file, options.IntensityFor("samples"), mode);
                                }
                            }
                            else if (ext == ".png")
                            {
                                if (replaceTexturesCustom && textureReplacementRules.Any(r => r.TargetPath == file))
                                {
                                    // Skip standard corruption, keep replaced texture clean
                                }
                                else
                                {
                                    textureCorruptor.CorruptTexturePixels(file, options.IntensityFor("textures"), textureMode);
                                }
                            }
                            else if (file.EndsWith("hud.c", StringComparison.OrdinalIgnoreCase))
                            {
                                hudCorruptor.CorruptHudFile(file, options.IntensityFor("HUD"), hudMode);
                            }
                            else if (file.Contains("anims") && file.EndsWith(".inc.c", StringComparison.OrdinalIgnoreCase))
                            {
                                animationCorruptor.CorruptAnimationFile(file, options.IntensityFor("animations"), animMode);
                            }
                            else if (file.Contains("goddard"))
                            {
                                goddardCorruptor.CorruptGoddardFile(file, options.IntensityFor("Goddard"), goddardMode);
                            }
                            else if (file.EndsWith(".inc.c", StringComparison.OrdinalIgnoreCase) || ext == ".c" || ext == ".h")
                            {
                                if (randomizeMarioColors && file.Contains("mario"))
                                {
                                    marioColorCorruptor.CorruptMarioColorsFile(file, marioColorsArea, marioColorsMode);
                                }
                                if (randomizeDl)
                                {
                                    displayListCorruptor.CorruptDisplayListFile(file, options.IntensityFor("display lists"), dlMode);
                                }
                                if (randomizeCollision && file.Contains("collision"))
                                {
                                    collisionCorruptor.CorruptCollisionFile(file, options.IntensityFor("collision"), collisionMode);
                                }
                                if (targetModels)
                                {
                                    sourceCorruptor.CorruptTextSourceFile(file, options.IntensityFor("models"), mode);
                                }
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        }

                        processed++;
                        int progressVal = (int)((processed / (double)totalFiles) * 100);
                        progress.Report(new ChaosProgress($"Processed {processed}/{totalFiles} files ({failures.Count} failed)...", progressVal));
                    }
                    if (failures.Count > 0)
                        throw new InvalidOperationException($"{failures.Count} file(s) failed during preparation; live files were not changed.\n" + string.Join("\n", failures.Take(12)));
    }
}
