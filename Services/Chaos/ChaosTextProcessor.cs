using System.IO;
namespace Sm64DecompLevelViewer.Services.Chaos;
internal sealed class ChaosTextProcessor
{
    private readonly string _projectRoot;
    private readonly Random _random;
    private readonly ChaosQueueState _queue;
    public ChaosTextProcessor(string root, Random random, ChaosQueueState queue) { _projectRoot = root; _random = random; _queue = queue; }
    public void Apply(ChaosOptions options, IProgress<ChaosProgress> progress, CancellationToken token = default)
    {
        var gameTextCorruptor = new GameTextCorruptor(_random);
            var randomizeText = options.RandomizeText;
            var textMode = options.TextMode;
                    // Apply game text randomization if requested
                    if (randomizeText)
                    {
                        progress.Report(new ChaosProgress("Randomizing game text and dialogs...", null));

                        string textDir = Path.Combine(_projectRoot, "text");
                        if (Directory.Exists(textDir))
                        {
                            var textFiles = Directory.GetFiles(textDir, "*.h", SearchOption.AllDirectories)
                                .Concat(Directory.GetFiles(textDir, "*.inc.c", SearchOption.AllDirectories)).Order(StringComparer.Ordinal).ToArray();
                            foreach (var file in textFiles)
                            {
                                token.ThrowIfCancellationRequested();
                                try
                                {
                                    string backupPath = file + ".bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(file, backupPath);
                                    }
                                    string readPath = (_queue.IsRunning && _queue.IsAccumulating && _queue.FilesModified.Contains(file)) ? file : backupPath;
                                    string content = File.ReadAllText(readPath);
                                    string mutated = gameTextCorruptor.RandomizeGameText(content, textMode);
                                    File.WriteAllText(file, mutated);
                                    File.SetLastWriteTime(file, DateTime.Now);
                                    if (_queue.IsRunning)
                                    {
                                        _queue.FilesModified.Add(file);
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                                {
                                    throw new IOException($"Error randomizing text file {file}: {ex.Message}", ex);
                                }
                            }
                        }
                    }

    }
}
