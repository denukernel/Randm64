using System.IO;
using System.Windows.Media.Imaging;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosAssetPreparation
{
    private readonly string _projectRoot;
    private readonly Random _random;
    private readonly ChaosQueueState _queue;
    public ChaosAssetPreparation(string root, Random random, ChaosQueueState queue) { _projectRoot = root; _random = random; _queue = queue; }
    public void Prepare(ChaosOptions options, IProgress<ChaosProgress> progress, CancellationToken token = default)
    {
        var soundCorruptor = new SoundCorruptor(_random);
        var textureCorruptor = new TextureCorruptor(_random);
            var shuffleSounds = options.ShuffleSounds;
            var excludeInstruments = options.ExcludeInstruments;
            var excludeSfx = options.ExcludeSfx;
            var shuffleSfxOnly = options.ShuffleSfxOnly;
            var mode = options.Mode;
            var replaceSfx = options.ReplaceSfx;
            var replaceTexturesCustom = options.ReplaceTexturesCustom;
            var sfxModes = options.SelectedSfxModes;
            var sfxIdentityShuffle = options.SfxIdentityShuffle;
            var sfxPitchVariation = options.SfxPitchVariation;
            var sfxReplacementRules = options.SfxReplacementRules;
            var textureReplacementRules = options.TextureReplacementRules;
                    // Perform sound shuffling first if requested
                    if (shuffleSounds)
                    {
                        progress.Report(new ChaosProgress("Randomizing and shuffling sound samples...", null));

                        string sampleDir = Path.Combine(_projectRoot, "sound", "samples");
                        if (Directory.Exists(sampleDir))
                        {
                            var aiffFilesList = Directory.GetFiles(sampleDir, "*.aiff", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray().ToList();
                            if (excludeInstruments)
                            {
                                aiffFilesList = aiffFilesList.Where(f => SfxSampleEffects.IsSfx(sampleDir, f)).ToList();
                            }
                            if (excludeSfx)
                            {
                                aiffFilesList = aiffFilesList.Where(f => !SfxSampleEffects.IsSfx(sampleDir, f)).ToList();
                            }
                            if (shuffleSfxOnly)
                            {
                                aiffFilesList = aiffFilesList.Where(f => SfxSampleEffects.IsSfx(sampleDir, f)).ToList();
                            }
                            var aiffFiles = aiffFilesList.ToArray();
                            if (aiffFiles.Length > 0)
                            {
                                // Ensure all are backed up first
                                foreach (var file in aiffFiles)
                                {
                                    token.ThrowIfCancellationRequested();
                                    string backupPath = file + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(file, backupPath);
                                    }
                                }

                                

                                
                                

                                // Define source backups to target mapping
                                string[] sourceBackups = new string[aiffFiles.Length];
                                if (sfxIdentityShuffle)
                                {
                                    // Shuffle
                                    var shuffledFiles = new List<string>(aiffFiles);
                                    int n = shuffledFiles.Count;
                                    while (n > 1)
                                    {
                                        n--;
                                        int k = _random.Next(n + 1);
                                        string temp = shuffledFiles[k];
                                        shuffledFiles[k] = shuffledFiles[n];
                                        shuffledFiles[n] = temp;
                                    }
                                    for (int i = 0; i < aiffFiles.Length; i++)
                                    {
                                        sourceBackups[i] = shuffledFiles[i] + ".bak";
                                    }
                                }
                                else
                                {
                                    for (int i = 0; i < aiffFiles.Length; i++)
                                    {
                                        sourceBackups[i] = aiffFiles[i] + ".bak";
                                    }
                                }

                                var shuffleInputs = aiffFiles.ToDictionary(f => f + ".bak", f => File.ReadAllBytes(
                                    _queue.IsRunning && _queue.IsAccumulating && _queue.FilesModified.Contains(f) ? f : f + ".bak"));
                                // Copy backups to active slots and apply modification
                                for (int i = 0; i < aiffFiles.Length; i++)
                                {
                                    string sourceBackup = sourceBackups[i];
                                    string targetFile = aiffFiles[i];
                                    token.ThrowIfCancellationRequested();
                                    File.WriteAllBytes(targetFile, shuffleInputs[sourceBackup]);
                                    _queue.PreparedAssets.Add(targetFile);
                                    _queue.FilesModified.Add(targetFile);

                                    // Apply our custom pitch / mode changes to the target file!
                                    if (sfxModes.Length > 0 || sfxPitchVariation)
                                    {
                                        soundCorruptor.ModifyAiffFile(targetFile, sfxModes, sfxPitchVariation);
                                    }

                                    File.SetLastWriteTime(targetFile, DateTime.Now);
                                }
                            }
                        }
                    }

                    // Perform SFX replacement if requested
                    if (replaceSfx && sfxReplacementRules.Count > 0)
                    {
                        progress.Report(new ChaosProgress("Replacing sound files...", null));

                        foreach (var rule in sfxReplacementRules)
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                // Make sure replacement is backed up
                                string replacementBackup = rule.ReplacementPath + ".bak";
                                if (!File.Exists(replacementBackup))
                                {
                                    File.Copy(rule.ReplacementPath, replacementBackup);
                                }

                                // Make sure target is backed up
                                string targetBackup = rule.TargetPath + ".bak";
                                if (!File.Exists(targetBackup))
                                {
                                    File.Copy(rule.TargetPath, targetBackup);
                                }

                                // Copy replacement backup to target file active slot
                                File.Copy(replacementBackup, rule.TargetPath, true);
                                _queue.PreparedAssets.Add(rule.TargetPath);
                                _queue.FilesModified.Add(rule.TargetPath);
                                File.SetLastWriteTime(rule.TargetPath, DateTime.Now);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                throw new IOException($"Error replacing sound file {rule.TargetPath}: {ex.Message}", ex);
                            }
                        }
                    }

                    // Perform Custom Texture replacement if requested
                    if (replaceTexturesCustom && textureReplacementRules.Count > 0)
                    {
                        progress.Report(new ChaosProgress("Applying custom texture replacements...", null));

                        foreach (var rule in textureReplacementRules)
                        {
                            token.ThrowIfCancellationRequested();
                            try
                            {
                                string targetPath = rule.TargetPath;
                                string repPath = rule.ReplacementPath;

                                if (!File.Exists(repPath) || !File.Exists(targetPath)) throw new FileNotFoundException("texture replacement input is missing");
                                if (File.Exists(repPath) && File.Exists(targetPath))
                                {
                                    // Make backup of target first
                                    string backupPath = targetPath + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(targetPath, backupPath);
                                    }

                                    // Read target dimensions
                                    int targetW = 64;
                                    int targetH = 64;
                                    double dpiX = 96;
                                    double dpiY = 96;
                                    using (var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                                    {
                                        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                        targetW = decoder.Frames[0].PixelWidth;
                                        targetH = decoder.Frames[0].PixelHeight;
                                        dpiX = decoder.Frames[0].DpiX;
                                        dpiY = decoder.Frames[0].DpiY;
                                    }

                                    // Get scaled pixels of replacement image
                                    byte[] pixels = textureCorruptor.GetTextureXPixels(repPath, targetW, targetH);

                                    // Write back as PNG to targetPath
                                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                                        targetW, targetH, dpiX, dpiY,
                                        System.Windows.Media.PixelFormats.Bgra32, null, pixels, targetW * 4);

                                    var encoder = new PngBitmapEncoder();
                                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                                    using (var outStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                                    {
                                        encoder.Save(outStream);
                                    }
                                    _queue.PreparedAssets.Add(targetPath);
                                    _queue.FilesModified.Add(targetPath);
                                    File.SetLastWriteTime(targetPath, DateTime.Now);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                throw new IOException($"Error replacing custom texture: {ex.Message}", ex);
                            }
                        }
                    }

    }
}
