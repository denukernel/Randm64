using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class ModelParser
{
    // Regex patterns for parsing model data
    private static readonly Regex VtxArrayPattern = new(@"(?:static\s+)?(?:const\s+)?Vtx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    
    
    private static readonly Regex VtxDataPattern = new(
        @"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\},\s*\d+,\s*\{\s*(-?\d+),\s*(-?\d+)\},\s*\{(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2})\}\}\}",
        RegexOptions.Compiled);
    
    private static readonly Regex SpVertexPattern = new(@"gsSPVertex\(\s*&?(\w+)(?:(?:\s*\+\s*|\s*\[\s*)(\d+)\s*\]?)?\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Sp2TrianglesPattern = new(@"gsSP2Triangles\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+,\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    private static readonly Regex Sp1TrianglePattern = new(@"gsSP1Triangle\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    private static readonly Regex DisplayListPattern = new(@"const\s+Gfx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    private static readonly Regex SetTextureImagePattern = new(@"gsDPSetTextureImage\(\s*[^,]+\s*,\s*[^,]+\s*,\s*[^,]+\s*,\s*(\w+)\)", RegexOptions.Compiled);
    private static readonly Regex TextureSymbolPattern = new(@"(?:ALIGNED8\s+)?(?:static\s+)?const\s+(?:Texture|u8)\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    private static readonly Regex IncludePattern = new(@"#include\s+""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex gdSPLightsPattern = new(
        @"(?:static\s+)?(?:const\s+)?Lights1\s+(\w+)\s*=\s*gdSPDefLights1\(\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+)",
        RegexOptions.Compiled);
    private static readonly Regex SpLightPattern = new(@"gsSPLight\s*\(\s*&(\w+)\.(?:l(?:\[\d+\])?|a)\s*,\s*\d+\s*\)", RegexOptions.Compiled);
    private static readonly Regex MovtexArrayPattern = new(@"(?:static\s+)?Movtex\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    private static readonly Regex MovtexTrisPattern = new(@"MOV_TEX_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex MovtexRotTrisPattern = new(@"MOV_TEX_ROT_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
    private static readonly Regex MovtexBoxTrisPattern = new(@"MOV_TEX_4_BOX_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);

    public VisualMesh? ParseModelFile(
        string modelFilePath, 
        string areaName, 
        string levelName, 
        Dictionary<string, Matrix4>? transformations = null,
        Dictionary<string, int>? dlToJointIndex = null,
        List<GeoDisplayListInstance>? displayListInstances = null,
        float rootScale = 1.0f,
        string? sourceText = null)
    {
        try
        {
            if (sourceText == null && !File.Exists(modelFilePath))
            {
                Console.WriteLine($"Model file not found: {modelFilePath}");
                return null;
            }

            var fileContent = sourceText ?? ActorSourceReader.Read(modelFilePath);
            var mesh = new VisualMesh
            {
                AreaName = areaName,
                LevelName = levelName,
                DlToJointIndex = dlToJointIndex ?? new(),
                DisplayListInstances = displayListInstances ?? new(),
                RootScale = rootScale
            };

            var vertexArrays = ParseVertexArrays(fileContent);

            // Parse all gdSPDefLights1
            var lightMatches = gdSPLightsPattern.Matches(fileContent);
            foreach (Match match in lightMatches)
            {
                string lightGroupName = match.Groups[1].Value;
                byte r = ParseByteHelper(match.Groups[5].Value);
                byte g = ParseByteHelper(match.Groups[6].Value);
                byte b = ParseByteHelper(match.Groups[7].Value);
                
                mesh.LightGroupColors[lightGroupName] = new Vector3(r / 255.0f, g / 255.0f, b / 255.0f);
            }

            ParseDisplayLists(fileContent, vertexArrays, mesh, transformations, displayListInstances);

            Console.WriteLine($"Parsed visual mesh: {mesh}");
            return mesh;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing model file {modelFilePath}: {ex.Message}");
            return null;
        }
    }

    public Dictionary<string, List<ModelVertex>> ParseVertexArrays(string content)
    {
        var vertexArrays = new Dictionary<string, List<ModelVertex>>();

        var arrayMatches = VtxArrayPattern.Matches(content);
        
        foreach (Match arrayMatch in arrayMatches)
        {
            string arrayName = arrayMatch.Groups[1].Value;
            int startIndex = arrayMatch.Index + arrayMatch.Length;
            
            // Find the closing brace
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string arrayContent = content.Substring(startIndex, endIndex - startIndex);
            
            var vertices = new List<ModelVertex>();
            var vtxMatches = VtxDataPattern.Matches(arrayContent);
            
            foreach (Match vtxMatch in vtxMatches)
            {
                int x = int.Parse(vtxMatch.Groups[1].Value);
                int y = int.Parse(vtxMatch.Groups[2].Value);
                int z = int.Parse(vtxMatch.Groups[3].Value);
                int s = int.Parse(vtxMatch.Groups[4].Value);
                int t = int.Parse(vtxMatch.Groups[5].Value);
                byte nx = Convert.ToByte(vtxMatch.Groups[6].Value, 16);
                byte ny = Convert.ToByte(vtxMatch.Groups[7].Value, 16);
                byte nz = Convert.ToByte(vtxMatch.Groups[8].Value, 16);
                byte alpha = Convert.ToByte(vtxMatch.Groups[9].Value, 16);

                vertices.Add(new ModelVertex(x, y, z, s, t, nx, ny, nz, alpha));
            }

            if (vertices.Count > 0)
            {
                vertexArrays[arrayName] = vertices;
                Console.WriteLine($"  {arrayName}: {vertices.Count} vertices");
            }
        }

        var movtexMatches = MovtexArrayPattern.Matches(content);
        foreach (Match arrayMatch in movtexMatches)
        {
            string arrayName = arrayMatch.Groups[1].Value;
            int startIndex = arrayMatch.Index + arrayMatch.Length;
            
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }

            string arrayContent = content.Substring(startIndex, endIndex - startIndex);
            
            var vertices = new List<ModelVertex>();
            var trisMatches = MovtexTrisPattern.Matches(arrayContent);
            
            foreach (Match trisMatch in trisMatches)
            {
                int x = int.Parse(trisMatch.Groups[1].Value);
                int y = int.Parse(trisMatch.Groups[2].Value);
                int z = int.Parse(trisMatch.Groups[3].Value);
                int s = int.Parse(trisMatch.Groups[4].Value) * 1024;
                int t = int.Parse(trisMatch.Groups[5].Value) * 1024;

                vertices.Add(new ModelVertex(x, y, z, s, t, 0, 127, 0, 255));
            }

            if (vertices.Count == 0)
            {
                var rotTrisMatches = MovtexRotTrisPattern.Matches(arrayContent);
                foreach (Match rotMatch in rotTrisMatches)
                {
                    int x = int.Parse(rotMatch.Groups[1].Value);
                    int y = int.Parse(rotMatch.Groups[2].Value);
                    int z = int.Parse(rotMatch.Groups[3].Value);

                    vertices.Add(new ModelVertex(x, y, z, 0, 0, 0, 127, 0, 255));
                }
            }

            if (vertices.Count == 0)
            {
                var boxMatches = MovtexBoxTrisPattern.Matches(arrayContent);
                if (boxMatches.Count >= 4)
                {
                    for (int i = 0; i + 3 < boxMatches.Count; i += 4)
                    {
                        int x1 = int.Parse(boxMatches[i + 0].Groups[1].Value);
                        int z1 = int.Parse(boxMatches[i + 0].Groups[2].Value);
                        int x2 = int.Parse(boxMatches[i + 2].Groups[1].Value);
                        int z2 = int.Parse(boxMatches[i + 2].Groups[2].Value);

                        vertices.Add(new ModelVertex(x1, 0, z1, 0, 0, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x1, 0, z2, 0, 2048, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x2, 0, z2, 2048, 2048, 0, 127, 0, 255));
                        vertices.Add(new ModelVertex(x2, 0, z1, 2048, 0, 0, 127, 0, 255));
                    }
                }
            }

            if (vertices.Count > 0)
            {
                vertexArrays[arrayName] = vertices;
                Console.WriteLine($"  Movtex {arrayName}: {vertices.Count} vertices");
            }
        }

        return vertexArrays;
    }

    private void ParseDisplayLists(string content, Dictionary<string, List<ModelVertex>> vertexArrays, VisualMesh mesh, Dictionary<string, Matrix4>? transformations, List<GeoDisplayListInstance>? displayListInstances = null)
    {
        var dlMatches = DisplayListPattern.Matches(content);
        
        // Build display list content mapping and parse parent-child mappings
        var dlContents = new Dictionary<string, string>();
        foreach (Match dlMatch in dlMatches)
        {
            string dlName = dlMatch.Groups[1].Value;
            int startIndex = dlMatch.Index + dlMatch.Length;
            
            int braceCount = 1;
            int endIndex = startIndex;
            for (int i = startIndex; i < content.Length && braceCount > 0; i++)
            {
                if (content[i] == '{') braceCount++;
                if (content[i] == '}') braceCount--;
                endIndex = i;
            }
            string dlContent = content.Substring(startIndex, endIndex - startIndex);
            dlContents[dlName] = dlContent;
        }

        var spDisplayListRegex = new Regex(@"gsSPDisplayList\(\s*(\w+)\s*\)", RegexOptions.Compiled);

        // Pre-parse command lists for each display list exactly once
        var dlNodes = new Dictionary<string, List<(string type, string value)>>();
        foreach (var pair in dlContents)
        {
            string dlName = pair.Key;
            string dlContent = pair.Value;

            var spLightMatches = SpLightPattern.Matches(dlContent);
            var setTexImgMatches = SetTextureImagePattern.Matches(dlContent);
            var spDisplayListMatches = spDisplayListRegex.Matches(dlContent);

            var commands = new List<(int pos, string type, string value)>();
            foreach (Match m in spLightMatches)
                commands.Add((m.Index, "light", m.Groups[1].Value));
            foreach (Match m in setTexImgMatches)
                commands.Add((m.Index, "texture", m.Groups[1].Value));
            foreach (Match m in spDisplayListMatches)
                commands.Add((m.Index, "calldl", m.Groups[1].Value));

            commands.Sort((a, b) => a.pos.CompareTo(b.pos));
            dlNodes[dlName] = commands.Select(c => (c.type, c.value)).ToList();
        }

        var inheritedTextures = new Dictionary<string, string>();
        var inheritedLights = new Dictionary<string, string>();
        var inheritedJointIndices = new Dictionary<string, int>();
        var inheritedTransforms = new Dictionary<string, Matrix4>();

        // Seed BFS queue with direct values from geolayout
        var queue = new Queue<string>();
        if (mesh.DlToJointIndex != null)
        {
            foreach (var pair in mesh.DlToJointIndex)
            {
                inheritedJointIndices[pair.Key] = pair.Value;
                queue.Enqueue(pair.Key);
            }
        }
        if (transformations != null)
        {
            foreach (var pair in transformations)
            {
                inheritedTransforms[pair.Key] = pair.Value;
                if (!inheritedJointIndices.ContainsKey(pair.Key))
                {
                    queue.Enqueue(pair.Key);
                }
            }
        }

        // Seed wrapper _dl mappings (e.g., X -> X_dl)
        foreach (var dlName in dlContents.Keys)
        {
            if (!dlName.EndsWith("_dl"))
            {
                string dlNameWithSuffix = dlName + "_dl";
                if (dlContents.ContainsKey(dlNameWithSuffix))
                {
                    bool added = false;
                    if (inheritedJointIndices.TryGetValue(dlName, out int ji) && !inheritedJointIndices.ContainsKey(dlNameWithSuffix))
                    {
                        inheritedJointIndices[dlNameWithSuffix] = ji;
                        added = true;
                    }
                    if (inheritedTransforms.TryGetValue(dlName, out var trans) && !inheritedTransforms.ContainsKey(dlNameWithSuffix))
                    {
                        inheritedTransforms[dlNameWithSuffix] = trans;
                        added = true;
                    }
                    if (added)
                    {
                        queue.Enqueue(dlNameWithSuffix);
                    }
                }
            }
        }

        // Run dataflow queue propagation with cycle protection
        var updateCounts = new Dictionary<string, int>();
        while (queue.Count > 0)
        {
            string parentDl = queue.Dequeue();
            
            if (!dlNodes.TryGetValue(parentDl, out var commands))
            {
                continue;
            }

            string? activeLight = inheritedLights.TryGetValue(parentDl, out var l) ? l : null;
            string? activeTexture = inheritedTextures.TryGetValue(parentDl, out var t) ? t : null;
            int? activeJointIndex = inheritedJointIndices.TryGetValue(parentDl, out int ji) ? ji : null;
            Matrix4? activeTransform = inheritedTransforms.TryGetValue(parentDl, out var trans) ? trans : null;

            foreach (var cmd in commands)
            {
                if (cmd.type == "light")
                {
                    activeLight = cmd.value;
                }
                else if (cmd.type == "texture")
                {
                    activeTexture = cmd.value;
                }
                else if (cmd.type == "calldl")
                {
                    string childDl = cmd.value;
                    bool childChanged = false;

                    if (activeLight != null && (!inheritedLights.TryGetValue(childDl, out var currentL) || currentL != activeLight))
                    {
                        inheritedLights[childDl] = activeLight;
                        childChanged = true;
                    }
                    if (activeTexture != null && (!inheritedTextures.TryGetValue(childDl, out var currentT) || currentT != activeTexture))
                    {
                        inheritedTextures[childDl] = activeTexture;
                        childChanged = true;
                    }
                    if (activeJointIndex.HasValue && (!inheritedJointIndices.TryGetValue(childDl, out var currentJi) || currentJi != activeJointIndex.Value))
                    {
                        inheritedJointIndices[childDl] = activeJointIndex.Value;
                        childChanged = true;
                    }
                    if (activeTransform.HasValue && (!inheritedTransforms.TryGetValue(childDl, out var currentTrans) || currentTrans != activeTransform.Value))
                    {
                        inheritedTransforms[childDl] = activeTransform.Value;
                        childChanged = true;
                    }

                    if (childChanged)
                    {
                        int count = updateCounts.TryGetValue(childDl, out var c) ? c : 0;
                        if (count < 10)
                        {
                            updateCounts[childDl] = count + 1;
                            queue.Enqueue(childDl);
                        }
                    }
                }
            }
        }

        // Build set of reachable display lists to parse
        var keptDls = new HashSet<string>();
        if (mesh.DlToJointIndex != null && mesh.DlToJointIndex.Count > 0)
        {
            var keepQueue = new Queue<string>();
            foreach (var key in mesh.DlToJointIndex.Keys)
            {
                if (keptDls.Add(key))
                {
                    keepQueue.Enqueue(key);
                }
            }
            // Seed wrapper base/suffix name mappings
            foreach (var key in mesh.DlToJointIndex.Keys)
            {
                if (!key.EndsWith("_dl"))
                {
                    string dlNameWithSuffix = key + "_dl";
                    if (dlContents.ContainsKey(dlNameWithSuffix) && keptDls.Add(dlNameWithSuffix))
                    {
                        keepQueue.Enqueue(dlNameWithSuffix);
                    }
                }
                else
                {
                    string baseName = key.Substring(0, key.Length - 3);
                    if (dlContents.ContainsKey(baseName) && keptDls.Add(baseName))
                    {
                        keepQueue.Enqueue(baseName);
                    }
                }
            }

            while (keepQueue.Count > 0)
            {
                string parentDl = keepQueue.Dequeue();
                
                // Add suffix/base name counterparts if they exist
                if (!parentDl.EndsWith("_dl"))
                {
                    string dlNameWithSuffix = parentDl + "_dl";
                    if (dlContents.ContainsKey(dlNameWithSuffix) && keptDls.Add(dlNameWithSuffix))
                    {
                        keepQueue.Enqueue(dlNameWithSuffix);
                    }
                }
                else
                {
                    string baseName = parentDl.Substring(0, parentDl.Length - 3);
                    if (dlContents.ContainsKey(baseName) && keptDls.Add(baseName))
                    {
                        keepQueue.Enqueue(baseName);
                    }
                }

                if (dlNodes.TryGetValue(parentDl, out var commands))
                {
                    foreach (var cmd in commands)
                    {
                        if (cmd.type == "calldl")
                        {
                            string childDl = cmd.value;
                            if (dlContents.ContainsKey(childDl) && keptDls.Add(childDl))
                            {
                                keepQueue.Enqueue(childDl);
                            }
                        }
                    }
                }
            }
        }

        // Apply propagated joint indices to mesh.DlToJointIndex
        foreach (var pair in inheritedJointIndices)
        {
            mesh.DlToJointIndex[pair.Key] = pair.Value;
        }

        // Apply propagated transformations
        if (transformations != null)
        {
            foreach (var pair in inheritedTransforms)
            {
                transformations[pair.Key] = pair.Value;
            }
        }

        int[] rspVertexBuffer = new int[64];

        if (displayListInstances != null && displayListInstances.Count > 0)
        {
            var callStack = new HashSet<string>();
            // RSP material state survives both nested calls and successive geo nodes.
            string? currentTextureSymbol = null;
            string? currentLightSymbol = null;
            bool textureEnabled = false;
            var textureScale = Vector2.One;
            bool blendTextureWithShade = false;
            var addressS = TextureAddressMode.Repeat;
            var addressT = TextureAddressMode.Repeat;
            bool lightingEnabled = true;
            bool decalTexture = false;
            foreach (var inst in displayListInstances)
            {
                if (string.IsNullOrEmpty(inst.DisplayListName)) continue;

                mesh.DisplayListNames.Add(inst.DisplayListName);
                if (mesh.MainDisplayListName == null)
                {
                    mesh.MainDisplayListName = inst.DisplayListName;
                }

                int[] instanceRspBuffer = new int[64];

                callStack.Clear();
                callStack.Add(inst.DisplayListName);
                ExecuteDisplayList(
                    inst.DisplayListName,
                    inst,
                    dlContents,
                    vertexArrays,
                    mesh,
                    instanceRspBuffer,
                    ref currentTextureSymbol,
                    ref currentLightSymbol,
                    ref textureEnabled,
                    ref textureScale,
                    ref blendTextureWithShade,
                    ref addressS,
                    ref addressT,
                    ref lightingEnabled,
                    ref decalTexture,
                    callStack,
                    0);
            }
            return;
        }

        // Finally, parse the display lists that we keeping
        foreach (var pair in dlContents)
        {
            string dlName = pair.Key;
            string dlContent = pair.Value;

            if (mesh.DlToJointIndex != null && mesh.DlToJointIndex.Count > 0)
            {
                if (!keptDls.Contains(dlName))
                {
                    continue;
                }
            }

            mesh.DisplayListNames.Add(dlName);
            if (mesh.MainDisplayListName == null)
            {
                mesh.MainDisplayListName = dlName;
            }

            ProcessDisplayList(dlContent, vertexArrays, mesh, dlName, transformations, inheritedTextures, inheritedLights, rspVertexBuffer);
        }
    }

    private void ExecuteDisplayList(
        string dlName,
        GeoDisplayListInstance instance,
        Dictionary<string, string> dlContents,
        Dictionary<string, List<ModelVertex>> vertexArrays,
        VisualMesh mesh,
        int[] rspVertexBuffer,
        ref string? currentTextureSymbol,
        ref string? currentLightSymbol,
        ref bool textureEnabled,
        ref Vector2 textureScale,
        ref bool blendTextureWithShade,
        ref TextureAddressMode addressS,
        ref TextureAddressMode addressT,
        ref bool lightingEnabled,
        ref bool decalTexture,
        HashSet<string> callStack,
        int depth)
    {
        if (depth > 16) return;

        string actualDlName = dlName;
        if (!dlContents.TryGetValue(actualDlName, out string? dlContent))
        {
            if (dlContents.TryGetValue(dlName + "_dl", out dlContent))
            {
                actualDlName = dlName + "_dl";
            }
            else if (dlName.EndsWith("_dl") && dlContents.TryGetValue(dlName.Substring(0, dlName.Length - 3), out dlContent))
            {
                actualDlName = dlName.Substring(0, dlName.Length - 3);
            }
            else
            {
                return;
            }
        }

        var spVertexMatches = SpVertexPattern.Matches(dlContent);
        var sp2TriMatches = Sp2TrianglesPattern.Matches(dlContent);
        var sp1TriMatches = Sp1TrianglePattern.Matches(dlContent);
        var setTexImgMatches = SetTextureImagePattern.Matches(dlContent);
        var spLightMatches = SpLightPattern.Matches(dlContent);
        var spDisplayListMatches = new Regex(@"gsSPDisplayList\(\s*(\w+)\s*\)", RegexOptions.Compiled).Matches(dlContent);
        var textureStateMatches = Regex.Matches(dlContent, @"gsSPTexture\([^)]*,\s*(G_ON|G_OFF|1|0)\s*\)");
        var combineMatches = Regex.Matches(dlContent, @"gsDPSetCombineMode\(\s*(\w+)\s*,[^)]*\)");
        var tileMatches = Regex.Matches(dlContent, @"gsDPSetTile\(([^)]*)\)");
        var geometryMatches = Regex.Matches(dlContent, @"gsSP(Set|Clear)GeometryMode\(([^)]*)\)");
        var setLightsMatches = Regex.Matches(dlContent, @"gsSPSetLights1\(\s*(\w+)\s*\)");
        var branchMatches = Regex.Matches(dlContent, @"gsSPBranchList\(\s*(\w+)\s*\)");

        var commands = new List<(int pos, string type, Match match)>();
        foreach (Match m in spVertexMatches) commands.Add((m.Index, "vertex", m));
        foreach (Match m in sp2TriMatches) commands.Add((m.Index, "tri2", m));
        foreach (Match m in sp1TriMatches) commands.Add((m.Index, "tri1", m));
        foreach (Match m in setTexImgMatches) commands.Add((m.Index, "texture", m));
        foreach (Match m in Regex.Matches(dlContent, @"gsDPLoadTexture(?:Block(?:_4b)?|Tile(?:_4b)?)\(\s*(\w+)\s*,([^;]*?)\),"))
            commands.Add((m.Index, "loadTexture", m));
        foreach (Match m in spLightMatches) commands.Add((m.Index, "light", m));
        foreach (Match m in spDisplayListMatches) commands.Add((m.Index, "calldl", m));
        foreach (Match m in textureStateMatches) commands.Add((m.Index, "textureState", m));
        foreach (Match m in Regex.Matches(dlContent,@"gsSPTexture\(\s*(0x[\da-fA-F]+|\d+)\s*,\s*(0x[\da-fA-F]+|\d+)\s*,")) commands.Add((m.Index,"textureScale",m));
        foreach (Match m in combineMatches) commands.Add((m.Index, "combine", m));
        foreach (Match m in tileMatches) commands.Add((m.Index, "tile", m));
        foreach (Match m in geometryMatches) commands.Add((m.Index, "geometry", m));
        foreach (Match m in setLightsMatches) commands.Add((m.Index, "light", m));
        foreach (Match m in branchMatches) commands.Add((m.Index, "branchdl", m));

        commands.Sort((a, b) => a.pos.CompareTo(b.pos));

        foreach (var (pos, type, match) in commands)
        {
            if (type == "geometry")
            {
                if (Regex.IsMatch(match.Groups[2].Value, @"\bG_LIGHTING\b"))
                    lightingEnabled = match.Groups[1].Value == "Set";
            }
            else if (type == "tile")
            {
                var fields = match.Groups[1].Value.Split(',').Select(field => field.Trim()).ToArray();
                if (fields.Length == 12 && fields[4] is "G_TX_RENDERTILE" or "0")
                {
                    static TextureAddressMode Address(string flags) => flags.Contains("G_TX_CLAMP")
                        ? TextureAddressMode.Clamp : Regex.IsMatch(flags, @"\bG_TX_MIRROR\b")
                            ? TextureAddressMode.Mirror : TextureAddressMode.Repeat;
                    addressT = Address(fields[6]);
                    addressS = Address(fields[9]);
                }
            }
            else if (type == "combine")
            {
                blendTextureWithShade = match.Groups[1].Value == "G_CC_BLENDRGBFADEA";
                decalTexture = match.Groups[1].Value.StartsWith("G_CC_DECAL") || match.Groups[1].Value == "G_CC_REPLACE";
            }
            else if (type == "textureScale")
            {
                static float Scale(string value)
                {
                    int n = value.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(value[2..],16) : int.Parse(value);
                    return n == 65535 ? 1 : n / 65536f;
                }
                textureScale = new Vector2(Scale(match.Groups[1].Value),Scale(match.Groups[2].Value));
            }
            else if (type == "textureState")
            {
                textureEnabled = match.Groups[1].Value is "G_ON" or "1";
            }
            else if (type == "loadTexture")
            {
                currentTextureSymbol = match.Groups[1].Value;
                var fields = match.Groups[2].Value.Split(',').Select(s => s.Trim()).ToArray();
                int wrap = match.Value.StartsWith("gsDPLoadTextureBlock_4b") ? 4 : 5;
                if (fields.Length > wrap + 1)
                {
                    static TextureAddressMode Address(string flags) => flags.Contains("G_TX_CLAMP") ? TextureAddressMode.Clamp
                        : Regex.IsMatch(flags, @"\bG_TX_MIRROR\b") ? TextureAddressMode.Mirror : TextureAddressMode.Repeat;
                    addressS = Address(fields[wrap]);
                    addressT = Address(fields[wrap + 1]);
                }
            }
            else if (type == "texture")
            {
                currentTextureSymbol = match.Groups[1].Value;
            }
            else if (type == "light")
            {
                currentLightSymbol = match.Groups[1].Value;
            }
            else if (type is "calldl" or "branchdl")
            {
                string childDl = match.Groups[1].Value;
                if (!callStack.Contains(childDl))
                {
                    callStack.Add(childDl);
                    ExecuteDisplayList(
                        childDl,
                        instance,
                        dlContents,
                        vertexArrays,
                        mesh,
                        rspVertexBuffer,
                        ref currentTextureSymbol,
                        ref currentLightSymbol,
                        ref textureEnabled,
                    ref textureScale,
                        ref blendTextureWithShade,
                        ref addressS,
                        ref addressT,
                        ref lightingEnabled,
                        ref decalTexture,
                        callStack,
                        depth + 1);
                    callStack.Remove(childDl);
                }
                if (type == "branchdl") return;
            }
            else if (type == "vertex")
            {
                string arrayName = match.Groups[1].Value;
                int vtxArrayOffset = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value) ? int.Parse(match.Groups[2].Value) : 0;
                int count = int.Parse(match.Groups[3].Value);
                int destIndex = int.Parse(match.Groups[4].Value);

                if (vertexArrays.TryGetValue(arrayName, out var vtxList))
                {
                    Matrix4 localToJoint = Matrix4.Identity;
                    if (instance.JointIndex >= 0)
                    {
                        try
                        {
                            localToJoint = instance.Transform * Matrix4.Invert(instance.JointWorldMatrix);
                        }
                        catch
                        {
                            localToJoint = Matrix4.Identity;
                        }
                    }

                    for (int i = 0; i < count && (i + vtxArrayOffset) < vtxList.Count; i++)
                    {
                        var v = vtxList[i + vtxArrayOffset];
                        var rawPos = new Vector3(v.X, v.Y, v.Z);
                        var pJointLocal = Vector3.TransformPosition(rawPos, localToJoint);
                        var pRestWorld = Vector3.TransformPosition(rawPos, instance.Transform);

                        var vertex = new ModelVertex(
                            (int)Math.Round(pRestWorld.X),
                            (int)Math.Round(pRestWorld.Y),
                            (int)Math.Round(pRestWorld.Z),
                            (int)(v.S * textureScale.X), (int)(v.T * textureScale.Y), v.NX, v.NY, v.NZ, v.Alpha)
                        {
                            SourceArrayName = arrayName,
                            SourceArrayIndex = i + vtxArrayOffset,
                            RefX = (int)Math.Round(pJointLocal.X),
                            RefY = (int)Math.Round(pJointLocal.Y),
                            RefZ = (int)Math.Round(pJointLocal.Z),
                            RestX = (int)Math.Round(pRestWorld.X),
                            RestY = (int)Math.Round(pRestWorld.Y),
                            RestZ = (int)Math.Round(pRestWorld.Z),
                            JointIndex = instance.JointIndex
                        };
                        vertex.ReferenceNormal = Vector3.TransformNormal(v.ReferenceNormal, localToJoint);
                        vertex.SetNormal(Vector3.TransformNormal(v.ReferenceNormal, instance.Transform));
                        vertex.VertexColor = v.VertexColor;
                        vertex.Billboard = instance.Billboard;
                        if (instance.Billboard != null)
                            vertex.BillboardPosition = Vector3.TransformPosition(rawPos,
                                instance.Transform * Matrix4.Invert(instance.Billboard.RestWorldMatrix));

                        mesh.Vertices.Add(vertex);
                        int globalIndex = mesh.Vertices.Count - 1;
                        if (destIndex + i < rspVertexBuffer.Length)
                        {
                            rspVertexBuffer[destIndex + i] = globalIndex;
                        }
                    }
                }
            }
            else if (type == "tri2")
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);
                int v4 = int.Parse(match.Groups[4].Value);
                int v5 = int.Parse(match.Groups[5].Value);
                int v6 = int.Parse(match.Groups[6].Value);

                string? activeSymbol = textureEnabled ? currentTextureSymbol ?? currentLightSymbol : currentLightSymbol;
                if (v1 < rspVertexBuffer.Length && v2 < rspVertexBuffer.Length && v3 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol)
                        { ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureAddressS = addressS, TextureAddressT = addressT,
                            LightingEnabled = lightingEnabled, DecalTexture = textureEnabled && decalTexture });
                }
                if (v4 < rspVertexBuffer.Length && v5 < rspVertexBuffer.Length && v6 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v4], rspVertexBuffer[v5], rspVertexBuffer[v6], activeSymbol)
                        { ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureAddressS = addressS, TextureAddressT = addressT,
                            LightingEnabled = lightingEnabled, DecalTexture = textureEnabled && decalTexture });
                }
            }
            else if (type == "tri1")
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);

                string? activeSymbol = textureEnabled ? currentTextureSymbol ?? currentLightSymbol : currentLightSymbol;
                if (v1 < rspVertexBuffer.Length && v2 < rspVertexBuffer.Length && v3 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol)
                        { ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureAddressS = addressS, TextureAddressT = addressT,
                            LightingEnabled = lightingEnabled, DecalTexture = textureEnabled && decalTexture });
                }
            }
        }
    }

    private void ProcessDisplayList(
        string dlContent, 
        Dictionary<string, List<ModelVertex>> vertexArrays, 
        VisualMesh mesh, 
        string dlName, 
        Dictionary<string, Matrix4>? transformations,
        Dictionary<string, string> inheritedTextures,
        Dictionary<string, string> inheritedLights,
        int[] rspVertexBuffer)
    {
        Array.Clear(rspVertexBuffer, 0, rspVertexBuffer.Length);
        List<ModelVertex>? currentVertexBuffer = null;
        string? currentTextureSymbol = inheritedTextures.TryGetValue(dlName, out var t) ? t : null;
        string? currentLightSymbol = inheritedLights.TryGetValue(dlName, out var l) ? l : null;

        var spVertexMatches = SpVertexPattern.Matches(dlContent);
        var sp2TriMatches = Sp2TrianglesPattern.Matches(dlContent);
        var sp1TriMatches = Sp1TrianglePattern.Matches(dlContent);
        var setTexImgMatches = SetTextureImagePattern.Matches(dlContent);
        var spLightMatches = SpLightPattern.Matches(dlContent);

        var commands = new List<(int pos, string type, Match match)>();
        
        foreach (Match m in spVertexMatches)
            commands.Add((m.Index, "vertex", m));
        foreach (Match m in sp2TriMatches)
            commands.Add((m.Index, "tri2", m));
        foreach (Match m in sp1TriMatches)
            commands.Add((m.Index, "tri1", m));
        foreach (Match m in setTexImgMatches)
            commands.Add((m.Index, "texture", m));
        foreach (Match m in spLightMatches)
            commands.Add((m.Index, "light", m));

        commands.Sort((a, b) => a.pos.CompareTo(b.pos));

        bool hasVertexCmd = commands.Any(c => c.type == "vertex");
        if (!hasVertexCmd && vertexArrays.Count > 0)
        {
            foreach (var kvp in vertexArrays)
            {
                if (kvp.Key.Contains("movtex") || dlName.Contains(kvp.Key.Replace("movtex_tris_", "dl_").Replace("_data", "")))
                {
                    if (currentTextureSymbol == null)
                    {
                        if (kvp.Key.Contains("lava") || dlName.Contains("lava"))
                            currentTextureSymbol = "fire_09001800";
                        else if (kvp.Key.Contains("water") || dlName.Contains("water"))
                            currentTextureSymbol = "water_09000000";
                    }

                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        var v = kvp.Value[i];
                        var vertex = new ModelVertex(v.X, v.Y, v.Z, v.S, v.T, v.NX, v.NY, v.NZ, v.Alpha)
                        {
                            RefX = v.X,
                            RefY = v.Y,
                            RefZ = v.Z
                        };
                        mesh.Vertices.Add(vertex);
                        if (i < rspVertexBuffer.Length)
                        {
                            rspVertexBuffer[i] = mesh.Vertices.Count - 1;
                        }
                    }
                    break;
                }
            }
        }

        foreach (var (pos, type, match) in commands)
        {
            if (type == "texture")
            {
                currentTextureSymbol = match.Groups[1].Value;
            }
            else if (type == "light")
            {
                currentLightSymbol = match.Groups[1].Value;
            }
            else if (type == "vertex")
            {
                string arrayName = match.Groups[1].Value;
                int vtxArrayOffset = match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value) ? int.Parse(match.Groups[2].Value) : 0;
                int count = int.Parse(match.Groups[3].Value);
                int destIndex = int.Parse(match.Groups[4].Value);

                if (vertexArrays.ContainsKey(arrayName))
                {
                    currentVertexBuffer = vertexArrays[arrayName];
                    
                    Matrix4? transform = null;
                    if (transformations != null && dlName != null)
                    {
                        if (transformations.TryGetValue(dlName, out var trans))
                        {
                            transform = trans;
                        }
                    }

                    for (int i = 0; i < count && (i + vtxArrayOffset) < currentVertexBuffer.Count; i++)
                    {
                        var v = currentVertexBuffer[i + vtxArrayOffset];
                        var vertex = new ModelVertex(v.X, v.Y, v.Z, v.S, v.T, v.NX, v.NY, v.NZ, v.Alpha)
                        {
                            RefX = v.X,
                            RefY = v.Y,
                            RefZ = v.Z
                        };

                        if (mesh.DlToJointIndex != null && dlName != null)
                        {
                            if (mesh.DlToJointIndex.TryGetValue(dlName, out int jointIndex))
                            {
                                vertex.JointIndex = jointIndex;
                            }
                        }

                        if (transform.HasValue)
                        {
                            vertex = ApplySingleTransform(vertex, transform.Value);
                        }
                        vertex.RestX = vertex.X;
                        vertex.RestY = vertex.Y;
                        vertex.RestZ = vertex.Z;
                        mesh.Vertices.Add(vertex);

                        int globalIndex = mesh.Vertices.Count - 1;
                        if (destIndex + i < rspVertexBuffer.Length)
                        {
                            rspVertexBuffer[destIndex + i] = globalIndex;
                        }
                    }
                }
            }
            else if (type == "tri2")
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);
                int v4 = int.Parse(match.Groups[4].Value);
                int v5 = int.Parse(match.Groups[5].Value);
                int v6 = int.Parse(match.Groups[6].Value);

                string? activeSymbol = currentTextureSymbol ?? currentLightSymbol;
                
                if (v1 < rspVertexBuffer.Length && v2 < rspVertexBuffer.Length && v3 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol));
                }
                if (v4 < rspVertexBuffer.Length && v5 < rspVertexBuffer.Length && v6 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v4], rspVertexBuffer[v5], rspVertexBuffer[v6], activeSymbol));
                }
            }
            else if (type == "tri1")
            {
                int v1 = int.Parse(match.Groups[1].Value);
                int v2 = int.Parse(match.Groups[2].Value);
                int v3 = int.Parse(match.Groups[3].Value);

                string? activeSymbol = currentTextureSymbol ?? currentLightSymbol;
                if (v1 < rspVertexBuffer.Length && v2 < rspVertexBuffer.Length && v3 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol));
                }
            }
        }
    }

    public void ParseTextureMapping(string levelPath, VisualMesh mesh, string projectRoot)
    {
        try
        {
            string textureIncPath = Path.Combine(levelPath, "texture.inc.c");
            if (!File.Exists(textureIncPath)) return;

            string content = ActorSourceReader.Read(textureIncPath,keepIncludes:true);
            var symbolMatches = TextureSymbolPattern.Matches(content);

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
                    string resolvedPath = ResolveTexturePath(pngPath, levelPath, projectRoot);
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

    private string? ResolveTexturePath(string relativePath, string levelPath, string projectRoot)
    {
        // 1. Check build/*/textures (all built target versions and platforms)
        string buildDir = Path.Combine(projectRoot, "build");
        if (Directory.Exists(buildDir))
        {
            try
            {
                foreach (string subDir in Directory.GetDirectories(buildDir))
                {
                    string buildPath = Path.Combine(subDir, "textures", relativePath);
                    if (File.Exists(buildPath)) return buildPath;
                }
            }
            catch { }
        }

        // 2. Check leveleditor/textures (seen in project structure)
        // Need to extract the filename part e.g. "levels/castle_inside/12.rgba16.png" -> "inside/inside_castle_textures.08000.rgba16.png"
        // This is tricky because the naming convention changes. 
        // Let's try to match by filename if possible or check common locations.
        
        string fileName = Path.GetFileName(relativePath);
        
        // Check in texturelevels/textures
        string textureLevelsPath = Path.Combine(projectRoot, "texturelevels", "textures");
        if (Directory.Exists(textureLevelsPath))
        {
            var matchedFiles = Directory.GetFiles(textureLevelsPath, fileName, SearchOption.AllDirectories);
            if (matchedFiles.Length > 0) return matchedFiles[0];
        }

        // Check in leveleditor/textures
        string localTexturesPath = Path.Combine(projectRoot, "leveleditor", "textures");
        if (Directory.Exists(localTexturesPath))
        {
            var matchedFiles = Directory.GetFiles(localTexturesPath, fileName, SearchOption.AllDirectories);
            if (matchedFiles.Length > 0) return matchedFiles[0];
        }
        
        // 3. Check relative to project root
        string rootPath = Path.Combine(projectRoot, relativePath);
        if (File.Exists(rootPath)) return rootPath;

        return null;
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
                        if (Directory.Exists(folderPath))
                        {
                            // Find files matching *.<hexOffset>.*.png
                            var files = Directory.GetFiles(folderPath, $"*.{hexOffset}.*.png", SearchOption.TopDirectoryOnly);
                            if (files.Length > 0)
                            {
                                mesh.TexturePaths[symbol] = files[0];
                                Console.WriteLine($"Resolved shared texture: {symbol} -> {files[0]}");
                                continue;
                            }
                            
                            // Try general search just in case format suffix is different
                            files = Directory.GetFiles(folderPath, $"*{hexOffset}*.png", SearchOption.TopDirectoryOnly);
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

    public Dictionary<string, List<string>> FindModelFiles(string levelPath)
    {
        var modelFiles = new Dictionary<string, List<string>>();

        try
        {
            var areasPath = Path.Combine(levelPath, "areas");
            if (!Directory.Exists(areasPath))
            {
                return modelFiles;
            }

            var areaDirectories = Directory.GetDirectories(areasPath);
            foreach (var areaDir in areaDirectories)
            {
                var areaNumber = Path.GetFileName(areaDir);
                var areaName = $"Area {areaNumber}";
                var areaModelFiles = new List<string>();
                
                var geoDlRegex = new Regex(@"(?:GEO_DISPLAY_LIST|GEO_TRANSLATE_WITH_DL|GEO_ROTATE_WITH_DL|GEO_SCALE_WITH_DL|GEO_TRANSLATE_ROTATE_WITH_DL)\s*\(\s*(?:\w+\s*,\s*)*(\w+)\s*\)", RegexOptions.Compiled);
                
                var geoFiles = Directory.GetFiles(areaDir, "geo.inc.c", SearchOption.AllDirectories).ToList();
                var levelGeoC = Path.Combine(levelPath, "geo.c");
                if (File.Exists(levelGeoC)) geoFiles.Add(levelGeoC);

                var geoDls = new HashSet<string>();
                foreach (var gFile in geoFiles)
                {
                    try
                    {
                        string geoText = File.ReadAllText(gFile);
                        foreach (Match m in geoDlRegex.Matches(geoText))
                        {
                            geoDls.Add(m.Groups[1].Value);
                        }
                    }
                    catch { }
                }

                var subAreaDirectories = Directory.GetDirectories(areaDir)
                    .OrderBy(d => int.TryParse(Path.GetFileName(d), out int n) ? n : 999)
                    .ToArray();

                foreach (var subAreaDir in subAreaDirectories)
                {
                    bool hasOwnGeo = File.Exists(Path.Combine(subAreaDir, "geo.inc.c"));
                    var files = Directory.GetFiles(subAreaDir, "*.inc.c");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName == "collision.inc.c" || fileName == "geo.inc.c" || fileName == "macro.inc.c")
                            continue;

                        if (geoDls.Count > 0 && !hasOwnGeo)
                        {
                            string fileContent = File.ReadAllText(file);
                            bool matchesGeo = geoDls.Any(dl => fileContent.Contains(dl));
                            if (!matchesGeo)
                            {
                                LogService.Log($"[ScanDebugger] Skipping object subfolder file: {file}");
                                continue;
                            }
                        }

                        LogService.Log($"[ScanDebugger] Included level model file: {file}");
                        areaModelFiles.Add(file);
                    }
                }
                
                var movtextCandidates = new List<string>
                {
                    Path.Combine(areaDir, "movtext.inc.c"),
                    Path.Combine(levelPath, "movtext.inc.c")
                };
                foreach (var otherAreaDir in areaDirectories)
                {
                    movtextCandidates.Add(Path.Combine(otherAreaDir, "movtext.inc.c"));
                }

                foreach (var movtextPath in movtextCandidates.Distinct())
                {
                    if (File.Exists(movtextPath) && !areaModelFiles.Contains(movtextPath))
                    {
                        LogService.Log($"[ScanDebugger] Included Movtex file: {movtextPath}");
                        areaModelFiles.Add(movtextPath);
                    }
                }

                if (areaModelFiles.Count > 0)
                {
                    modelFiles[areaName] = areaModelFiles;
                    LogService.Log($"[ScanDebugger] Found {areaModelFiles.Count} model file(s) for {areaName}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error finding model files: {ex.Message}");
        }

        return modelFiles;
    }

    public VisualMesh? ParseMultipleModelFiles(
        List<string> modelFilePaths, 
        string areaName, 
        string levelName,
        Dictionary<string, Matrix4>? transformations = null)
    {
        if (modelFilePaths == null || modelFilePaths.Count == 0)
            return null;

        var mergedMesh = new VisualMesh
        {
            AreaName = areaName,
            LevelName = levelName
        };

        int totalVertices = 0;
        int totalTriangles = 0;
        int subModelIndex = 0;

        foreach (var filePath in modelFilePaths)
        {
            var subMesh = ParseModelFile(filePath, areaName, levelName);
            if (subMesh != null)
            {
                var pathParts = filePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                int subModelNumber = subModelIndex + 1;
                for (int i = pathParts.Length - 1; i >= 0; i--)
                {
                    if (int.TryParse(pathParts[i], out int num))
                    {
                        subModelNumber = num;
                        break;
                    }
                }

                if (transformations != null && transformations.Count > 0)
                {
                    bool transformed = false;
                    string? matchingDl = null;
                    foreach (var dlName in subMesh.DisplayListNames)
                    {
                        if (transformations.ContainsKey(dlName))
                        {
                            matchingDl = dlName;
                            break;
                        }
                    }

                    if (matchingDl != null)
                    {
                        Console.WriteLine($"Applying transformation to sub-model by matching DL: {matchingDl}");
                        ApplyTransform(subMesh.Vertices, transformations[matchingDl]);
                        transformed = true;
                    }

                    if (!transformed)
                    {
                        var transformKey = $"SubModel_{subModelNumber}";
                        if (transformations.ContainsKey(transformKey))
                        {
                            Console.WriteLine($"Applying transformation to sub-model {subModelNumber} by index");
                            ApplyTransform(subMesh.Vertices, transformations[transformKey]);
                        }
                    }
                }

                var subMeshEntry = new SubMesh
                {
                    SourceFile = filePath,
                    SubModelNumber = subModelNumber,
                    Vertices = new List<ModelVertex>(subMesh.Vertices),
                    Triangles = new List<ModelTriangle>(subMesh.Triangles),
                    IsVisible = true
                };
                
                int vertexOffset = mergedMesh.Vertices.Count;
                
                mergedMesh.Vertices.AddRange(subMesh.Vertices);
                
                foreach (var tri in subMesh.Triangles)
                {
                    var adjustedTri = new ModelTriangle(
                        tri.V1 + vertexOffset,
                        tri.V2 + vertexOffset,
                        tri.V3 + vertexOffset,
                        tri.TextureName
                    );
                    subMeshEntry.Triangles.Add(new ModelTriangle(tri.V1, tri.V2, tri.V3, tri.TextureName));
                    mergedMesh.Triangles.Add(adjustedTri);
                }
                
                mergedMesh.SubMeshes.Add(subMeshEntry);
                totalVertices += subMesh.VertexCount;
                totalTriangles += subMesh.TriangleCount;
                subModelIndex++;
            }
        }

        Console.WriteLine($"Merged {modelFilePaths.Count} model files: {totalVertices} vertices, {totalTriangles} triangles");
        Console.WriteLine($"Sub-models: {string.Join(", ", mergedMesh.SubMeshes.Select(sm => sm.SubModelNumber))}");
        return mergedMesh;
    }

    private void ApplyTransform(List<ModelVertex> vertices, Matrix4 transform)
    {
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = ApplySingleTransform(vertices[i], transform);
        }
        
        Console.WriteLine($"Applied transformation to {vertices.Count} vertices");
    }

    private ModelVertex ApplySingleTransform(ModelVertex vertex, Matrix4 transform)
    {
        var pos = new Vector3(vertex.X, vertex.Y, vertex.Z);
        var transformed = Vector3.TransformPosition(pos, transform);
        
        return new ModelVertex(
            (int)transformed.X,
            (int)transformed.Y,
            (int)transformed.Z,
            vertex.S,
            vertex.T,
            vertex.NX,
            vertex.NY,
            vertex.NZ,
            vertex.Alpha
        )
        {
            JointIndex = vertex.JointIndex,
            RefX = vertex.RefX,
            RefY = vertex.RefY,
            RefZ = vertex.RefZ
        };
    }

    private byte ParseByteHelper(string str)
    {
        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToByte(str, 16);
        return byte.Parse(str);
    }
}
