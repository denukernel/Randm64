using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using static Sm64DecompLevelViewer.Services.Models.ModelSourcePatterns;
using static Sm64DecompLevelViewer.Services.Models.ModelParsingPrimitives;

namespace Sm64DecompLevelViewer.Services.Models;
internal sealed class ModelTextureResolver
{
    public void ParseTextureMapping(string levelPath, VisualMesh mesh, string projectRoot)
    {
        try
        {
            string textureIncPath = Path.Combine(levelPath, "texture.inc.c");
            if (!File.Exists(textureIncPath)) return;

            string content = ActorSourceReader.Read(textureIncPath,keepIncludes:true);
            var symbolMatches = TextureSymbolPattern.Matches(content);
            var paths = new TexturePathLookup(projectRoot);

            foreach (Match symbolMatch in symbolMatches)
            {
                string symbol = symbolMatch.Groups[1].Value;
                int startIndex = symbolMatch.Index + symbolMatch.Length;
                
                // Find next symbol or end of file to limit search for #include
                int nextIndex = content.Length;
                var nextMatch = TextureSymbolPattern.Match(content, startIndex);
                if (nextMatch.Success) nextIndex = nextMatch.Index;

                string subContent = content.Substring(startIndex, nextIndex - startIndex);
                var includeMatch = IncludePattern.Match(subContent);

                if (includeMatch.Success)
                {
                    string includePath = includeMatch.Groups[1].Value; // e.g., "levels/castle_inside/12.rgba16.inc.c"
                    
                    // Convert .inc.c to .png
                    string pngPath = includePath.Replace(".inc.c", ".png");
                    
                    // Try to resolve the actual file path
                    string? resolvedPath = paths.Resolve(pngPath);
                    if (resolvedPath != null)
                    {
                        mesh.TexturePaths[symbol] = resolvedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing texture mapping: {ex.Message}");
        }
    }
    // Scope filesystem indexes to one mapping pass so edits are visible on reload.
    private sealed class TexturePathLookup(string projectRoot)
    {
        private string[]? _buildDirectories;
        private readonly Dictionary<string, Dictionary<string, string>> _trees = new(StringComparer.OrdinalIgnoreCase);

        public string? Resolve(string relativePath)
        {
            if (_buildDirectories == null)
            {
                string build = Path.Combine(projectRoot, "build");
                try { _buildDirectories = Directory.Exists(build) ? Directory.GetDirectories(build) : Array.Empty<string>(); }
                catch { _buildDirectories = Array.Empty<string>(); }
            }
            foreach (string directory in _buildDirectories)
            {
                string path = Path.Combine(directory, "textures", relativePath);
                if (File.Exists(path)) return path;
            }
            foreach (string tree in new[] { "texturelevels", "leveleditor" })
            {
                if (!_trees.TryGetValue(tree, out var files))
                {
                    files = new(StringComparer.OrdinalIgnoreCase);
                    string directory = Path.Combine(projectRoot, tree, "textures");
                    if (Directory.Exists(directory))
                        foreach (string file in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
                            files.TryAdd(Path.GetFileName(file), file);
                    _trees[tree] = files;
                }
                if (files.TryGetValue(Path.GetFileName(relativePath), out string? path)) return path;
            }
            string rootPath = Path.Combine(projectRoot, relativePath);
            return File.Exists(rootPath) ? rootPath : null;
        }
    }
    public void ParseActorTextureMapping(string actorDir, string modelFilePath, VisualMesh mesh, string projectRoot)
    {
        try
        {
            if (!File.Exists(modelFilePath)) return;

            string content = ActorSourceReader.Read(modelFilePath,keepIncludes:true);
            var symbolMatches = TextureSymbolPattern.Matches(content);

            foreach (Match symbolMatch in symbolMatches)
            {
                string symbol = symbolMatch.Groups[1].Value;
                int startIndex = symbolMatch.Index + symbolMatch.Length;
                
                int nextIndex = content.Length;
                var nextMatch = TextureSymbolPattern.Match(content, startIndex);
                if (nextMatch.Success) nextIndex = nextMatch.Index;

                string subContent = content.Substring(startIndex, nextIndex - startIndex);
                var includeMatch = IncludePattern.Match(subContent);

                if (includeMatch.Success)
                {
                    string includePath = includeMatch.Groups[1].Value; // e.g., "actors/goomba/goomba_body.rgba16.inc.c"
                    string pngPath = includePath.Replace(".inc.c", ".png");
                    
                    // The png file is inside the actor's directory or project root
                    string resolvedPath = Path.Combine(projectRoot, pngPath);
                    if (!File.Exists(resolvedPath))
                    {
                        resolvedPath = Path.Combine(actorDir, Path.GetFileName(pngPath));
                    }

                    if (File.Exists(resolvedPath))
                    {
                        mesh.TexturePaths[symbol] = resolvedPath;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing actor texture mapping: {ex.Message}");
        }
    }
    public void ResolveSharedTextures(VisualMesh mesh, string textureBin, string projectRoot)
    {
        try
        {
            // Collect all unique texture symbols referenced by triangles that are not yet resolved
            var symbolsToResolve = mesh.Triangles
                .Select(t => t.TextureName)
                .Where(name => name != null && !mesh.TexturePaths.ContainsKey(name))
                .Distinct()
                .ToList();
            var folderFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (string? symbol in symbolsToResolve)
            {
                if (symbol == null) continue;

                // Match segment 9 (e.g. outside_09003800) or segment 2 (e.g. segment2_02000000)
                var match = System.Text.RegularExpressions.Regex.Match(symbol, @"^(\w+)_0[29]([0-9a-fA-F]{6})$");
                if (match.Success)
                {
                    string prefix = match.Groups[1].Value; // e.g. "outside", "segment2"
                    string offsetStr = match.Groups[2].Value; // e.g. "003800"

                    // Parse offset
                    if (int.TryParse(offsetStr, System.Globalization.NumberStyles.HexNumber, null, out int offset))
                    {
                        // Formatted offset is normally formatted as a 5-digit hex string
                        // e.g. 09003800 -> offset 3800 -> "03800"
                        string hexOffset = (offset & 0xFFFF).ToString("X5");

                        // Determine the texture folder
                        string folderName = prefix;
                        if (symbol.Contains("_09") && !string.IsNullOrEmpty(textureBin))
                        {
                            folderName = textureBin;
                        }

                        string folderPath = Path.Combine(projectRoot, "textures", folderName);
                        if (!folderFiles.TryGetValue(folderPath, out var candidates))
                        {
                            candidates = Directory.Exists(folderPath) ? Directory.GetFiles(folderPath, "*.png") : Array.Empty<string>();
                            folderFiles[folderPath] = candidates;
                        }
                        if (candidates.Length > 0)
                        {
                            // Find files matching *.<hexOffset>.*.png
                            var files = candidates.Where(file => Path.GetFileName(file).Contains($".{hexOffset}.", StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (files.Length > 0)
                            {
                                mesh.TexturePaths[symbol] = files[0];
                                Console.WriteLine($"Resolved shared texture: {symbol} -> {files[0]}");
                                continue;
                            }
                            
                            // Try general search just in case format suffix is different
                            files = candidates.Where(file => Path.GetFileName(file).Contains(hexOffset, StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (files.Length > 0)
                            {
                                mesh.TexturePaths[symbol] = files[0];
                                Console.WriteLine($"Resolved shared texture (fallback): {symbol} -> {files[0]}");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error resolving shared textures: {ex.Message}");
        }
    }
}
