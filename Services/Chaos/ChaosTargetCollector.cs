using System.IO;
using System.Windows.Media.Imaging;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosTargetCollector
{
    private readonly string _projectRoot;
    private readonly Random _random;
    private readonly ChaosQueueState _queue;
    public ChaosTargetCollector(string root, Random random, ChaosQueueState queue) { _projectRoot = root; _random = random; _queue = queue; }
    public List<string> Collect(ChaosOptions options, IProgress<ChaosProgress> progress, CancellationToken token = default)
    {
            var intensity = options.IntensityFor("textures");
            var targetMusic = options.TargetMusic;
            var targetSounds = options.TargetSounds;
            var randomizeDl = options.RandomizeDl;
            var randomizeCollision = options.RandomizeCollision;
            var targetModels = options.TargetModels;
            var targetGoddard = options.TargetGoddard;
            var randomizeMarioColors = options.RandomizeMarioColors;
            var randomizeTextures = options.RandomizeTextures;
            var textureMode = options.TextureMode;
            var glitchAnimations = options.GlitchAnimations;
            var glitchHud = options.GlitchHud;
            var selectedLevel = options.SelectedLevel;
            var selectSpecificM64 = options.SelectSpecificM64;
            var selectedM64Files = options.SelectedM64Files;
            var randomizeSelectedOnly = options.RandomizeSelectedOnly;
            var selectedTextures = options.SelectedTextures;
                    List<string> filesToCorrupt = new();

                    // 1. Music Notes (.m64)
                    if (targetMusic)
                    {
                        
                        
                        

                        if (selectSpecificM64)
                        {
                            foreach (var file in selectedM64Files)
                            {
                                token.ThrowIfCancellationRequested();
                                filesToCorrupt.Add(file);
                            }
                        }
                        else
                        {
                            string seqDir = Path.Combine(_projectRoot, "sound", "sequences");
                            if (Directory.Exists(seqDir))
                            {
                                var m64Files = Directory.GetFiles(seqDir, "*.m64", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
                                foreach (var file in m64Files)
                                {
                                    token.ThrowIfCancellationRequested();
                                    string fileName = Path.GetFileName(file).ToLower();
                                    if (fileName.Contains("00_sound_player") || fileName.Contains("sfx"))
                                    {
                                        continue;
                                    }
                                    filesToCorrupt.Add(file);
                                }
                            }
                        }
                    }

                    // 2. Sounds (.aiff)
                    if (targetSounds)
                    {
                        string sampleDir = Path.Combine(_projectRoot, "sound", "samples");
                        if (Directory.Exists(sampleDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(sampleDir, "*.aiff", SearchOption.AllDirectories).Where(f => !SfxSampleEffects.IsSfx(sampleDir, f)).Order(StringComparer.Ordinal));
                        }
                    }

                    // 3. Display Lists / Models / Collisions (.c, .inc.c)
                    if (randomizeDl || targetModels || randomizeCollision)
                    {
                        string levelsDir = Path.Combine(_projectRoot, "levels");
                        if (Directory.Exists(levelsDir))
                        {
                            if (selectedLevel != "All Levels")
                            {
                                string targetDir = Path.Combine(levelsDir, selectedLevel);
                                if (Directory.Exists(targetDir))
                                {
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(levelsDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                filesToCorrupt.AddRange(Directory.GetFiles(levelsDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                            }
                        }

                        // Actors only get scanned if "All Levels" is selected (since they are globally shared across all levels)
                        if (selectedLevel == "All Levels")
                        {
                            string actorsDir = Path.Combine(_projectRoot, "actors");
                            if (Directory.Exists(actorsDir))
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(actorsDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                filesToCorrupt.AddRange(Directory.GetFiles(actorsDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                            }
                        }

                        string editorLevelsDir = Path.Combine(_projectRoot, "leveleditor", "levels");
                        if (Directory.Exists(editorLevelsDir))
                        {
                            if (selectedLevel != "All Levels")
                            {
                                string targetDir = Path.Combine(editorLevelsDir, selectedLevel);
                                if (Directory.Exists(targetDir))
                                {
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                    filesToCorrupt.AddRange(Directory.GetFiles(targetDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                }
                            }
                            else
                            {
                                filesToCorrupt.AddRange(Directory.GetFiles(editorLevelsDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                                filesToCorrupt.AddRange(Directory.GetFiles(editorLevelsDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                            }
                        }
                    }

                    // 3b. Mario Clothing Colors
                    if (randomizeMarioColors)
                    {
                        string marioDir = Path.Combine(_projectRoot, "actors", "mario");
                        if (Directory.Exists(marioDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(marioDir, "*.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                            filesToCorrupt.AddRange(Directory.GetFiles(marioDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                        }
                    }

                    // 4. Goddard (dynlist .c files)
                    if (targetGoddard)
                    {
                        string goddardDynDir = Path.Combine(_projectRoot, "src", "goddard", "dynlists");
                        if (Directory.Exists(goddardDynDir))
                        {
                            var files = Directory.GetFiles(goddardDynDir, "*.c", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray();
                            foreach (var f in files)
                            {
                                token.ThrowIfCancellationRequested();
                                if (!f.EndsWith("dynlist_mario_master.c", StringComparison.OrdinalIgnoreCase))
                                {
                                    filesToCorrupt.Add(f);
                                }
                            }
                        }
                    }

                    // 5. Textures (.png)
                    if (randomizeTextures)
                    {
                        string texDir = Path.Combine(_projectRoot, "textures");
                        string actorsDir = Path.Combine(_projectRoot, "actors");

                        var pngFilesList = new List<string>();
                        if (Directory.Exists(texDir))
                        {
                            pngFilesList.AddRange(Directory.GetFiles(texDir, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                        }
                        if (Directory.Exists(actorsDir))
                        {
                            pngFilesList.AddRange(Directory.GetFiles(actorsDir, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                        }

                        
                        

                        if (randomizeSelectedOnly)
                        {
                            var selectedSet = new HashSet<string>(selectedTextures.Select(p => Path.Combine(_projectRoot, p.Replace('/', Path.DirectorySeparatorChar))), StringComparer.OrdinalIgnoreCase);
                            pngFilesList = pngFilesList.Where(f => selectedSet.Contains(f)).ToList();
                        }
                        // Explicit replacements take priority over shuffle and pixel effects.
                        var pngFiles = pngFilesList.Where(f => !options.ReplaceTexturesCustom ||
                            !options.TextureReplacementRules.Any(r => string.Equals(r.TargetPath, f, StringComparison.OrdinalIgnoreCase))).ToArray();

                            if (textureMode == 0) // Texture Swapping (Shuffle)
                            {
                                progress.Report(new ChaosProgress("Grouping and swapping textures of matching sizes...", null));

                                // Group images by size
                                var sizeGroups = new Dictionary<string, List<string>>();
                                foreach (var file in pngFiles)
                                {
                                    token.ThrowIfCancellationRequested();
                                    try
                                    {
                                        // Create backup first
                                        string backupPath = file + ".bak";
                                        if (!File.Exists(backupPath))
                                        {
                                            File.Copy(file, backupPath);
                                        }

                                        // Read dimensions quickly
                                        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                                        {
                                            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                                            int w = decoder.Frames[0].PixelWidth;
                                            int h = decoder.Frames[0].PixelHeight;
                                            string sizeKey = $"{w}_{h}";
                                            if (!sizeGroups.ContainsKey(sizeKey))
                                            {
                                                sizeGroups[sizeKey] = new List<string>();
                                            }
                                            sizeGroups[sizeKey].Add(file);
                                        }
                                    }
                                    catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { throw new InvalidDataException("invalid texture: " + file, ex); }
                                }

                                // Shuffle matching size files
                                foreach (var group in sizeGroups.Values)
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (group.Count < 2) continue;

                                    var shuffled = new List<string>(group);
                                    int n = shuffled.Count;
                                    while (n > 1)
                                    {
                                        n--;
                                        int k = _random.Next(n + 1);
                                        string temp = shuffled[k];
                                        shuffled[k] = shuffled[n];
                                        shuffled[n] = temp;
                                    }

                                    var shuffleInputs = group.ToDictionary(f => f + ".bak", f => File.ReadAllBytes(
                                        _queue.IsRunning && _queue.IsAccumulating && _queue.FilesModified.Contains(f) ? f : f + ".bak"));
                                    // Swap content using backup files
                                    for (int i = 0; i < group.Count; i++)
                                    {
                                        if (_random.NextDouble() < (intensity / 100.0))
                                        {
                                            string sourceBackup = shuffled[i] + ".bak";
                                            string targetFile = group[i];
                                            token.ThrowIfCancellationRequested();
                                            File.WriteAllBytes(targetFile, shuffleInputs[sourceBackup]);
                                            if (_queue.IsRunning) _queue.FilesModified.Add(targetFile);
                                            File.SetLastWriteTime(targetFile, DateTime.Now);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Add to list for sequential pixel corruption
                                foreach (var file in pngFiles)
                                {
                                    token.ThrowIfCancellationRequested();
                                    filesToCorrupt.Add(file);
                                }
                            }
                    }

                    // 6. HUD drawing
                    if (glitchHud)
                    {
                        string hudFile = Path.Combine(_projectRoot, "src", "game", "hud.c");
                        if (File.Exists(hudFile))
                        {
                            filesToCorrupt.Add(hudFile);
                        }
                    }

                    // 7. Mario Animations
                    if (glitchAnimations)
                    {
                        string animDir = Path.Combine(_projectRoot, "assets", "anims");
                        if (Directory.Exists(animDir))
                        {
                            filesToCorrupt.AddRange(Directory.GetFiles(animDir, "*.inc.c", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());
                        }
                    }

                    filesToCorrupt = filesToCorrupt.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        return filesToCorrupt.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
    }
}
