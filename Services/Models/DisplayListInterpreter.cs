using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;
using static Sm64DecompLevelViewer.Services.Models.ModelSourcePatterns;
using static Sm64DecompLevelViewer.Services.Models.ModelParsingPrimitives;

namespace Sm64DecompLevelViewer.Services.Models;
internal sealed class DisplayListInterpreter
{
    internal void ParseDisplayLists(string content, Dictionary<string, List<ModelVertex>> vertexArrays, VisualMesh mesh, Dictionary<string, Matrix4>? transformations, List<GeoDisplayListInstance>? displayListInstances = null)
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
            var textureCommands = new TextureCommandState();
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
                    0, textureCommands);
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
    internal void ExecuteDisplayList(
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
        int depth, TextureCommandState textureCommands)
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
        var geometryMatches = Regex.Matches(dlContent, @"gsSP(Set|Clear)GeometryMode\(([^)]*)\)");
        var setLightsMatches = Regex.Matches(dlContent, @"gsSPSetLights1\(\s*(\w+)\s*\)");
        var branchMatches = Regex.Matches(dlContent, @"gsSPBranchList\(\s*(\w+)\s*\)");

        var commands = new List<(int pos, string type, Match match)>();
        foreach (Match m in spVertexMatches) commands.Add((m.Index, "vertex", m));
        foreach (Match m in sp2TriMatches) commands.Add((m.Index, "tri2", m));
        foreach (Match m in sp1TriMatches) commands.Add((m.Index, "tri1", m));
        foreach (Match m in setTexImgMatches) commands.Add((m.Index, "texture", m));
        foreach (var call in CSourceCalls.Read(dlContent, n => n is "gsDPSetTile" or "gsDPLoadBlock" or "gsDPLoadTile" or "gsDPSetTileSize" || n.StartsWith("gsDPLoadTexture")))
        {
            string raw = call.Text(dlContent);
            if (call.Name == "gsDPSetTile") commands.Add((call.Start, "tile", Regex.Match(raw, @"gsDPSetTile\(([\s\S]*)\)")));
            else if (call.Name.StartsWith("gsDPLoadTexture")) commands.Add((call.Start, "loadTexture", Regex.Match(raw, @"gsDPLoadTexture\w*\(\s*([^,]+)\s*,([\s\S]*)\)")));
            else commands.Add((call.Start, "textureRestore", Regex.Match(raw, @"[\s\S]+")));
        }
        foreach (Match m in spLightMatches) commands.Add((m.Index, "light", m));
        foreach (Match m in spDisplayListMatches) commands.Add((m.Index, "calldl", m));
        foreach (Match m in textureStateMatches) commands.Add((m.Index, "textureState", m));
        foreach (Match m in Regex.Matches(dlContent,@"gsSPTexture\(\s*(0x[\da-fA-F]+|\d+)\s*,\s*(0x[\da-fA-F]+|\d+)\s*,")) commands.Add((m.Index,"textureScale",m));
        foreach (Match m in combineMatches) commands.Add((m.Index, "combine", m));
        foreach (Match m in geometryMatches) commands.Add((m.Index, "geometry", m));
        foreach (Match m in setLightsMatches) commands.Add((m.Index, "light", m));
        foreach (Match m in branchMatches) commands.Add((m.Index, "branchdl", m));

        commands.Sort((a, b) => a.pos.CompareTo(b.pos));
        var triangleCommands = commands.Where(c => c.type == "tri1" || c.type == "tri2").Select((c, i) => (c.pos, i)).ToDictionary(c => c.pos, c => c.i);

        foreach (var (pos, type, match) in commands)
        {
            textureCommands.Track(type, match.Value);
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
                var fields = match.Groups[2].Value.Split(',').Select(s => s.Trim()).ToArray();
                bool fourBit = match.Value.StartsWith("gsDPLoadTextureBlock_4b");
                int wrap = fourBit ? 4 : 5;
                int bits = fourBit ? 4 : fields.Length > 1 ? fields[1] switch
                    { "G_IM_SIZ_4b" => 4, "G_IM_SIZ_8b" => 8, "G_IM_SIZ_16b" => 16, "G_IM_SIZ_32b" => 32, _ => 0 } : 0;
                int widthIndex = fourBit ? 1 : 2;
                int width = fields.Length > widthIndex && int.TryParse(fields[widthIndex], out int parsedWidth) ? parsedWidth : 0;
                currentTextureSymbol = textureCommands.SetImageAddress(match.Groups[1].Value, bits, width);
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
                currentTextureSymbol = textureCommands.SetImageAddress(match.Groups[1].Value);
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
                        depth + 1, textureCommands);
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
                            SourceTransform = instance.Transform,
                            SourceUvScale = textureScale,
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
                        { SourceTextureRestore = textureCommands.Snapshot(), SourceTextureImage = currentTextureSymbol, SourceTextureEnabled = textureEnabled, SourceDisplayList = actualDlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 0, ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureOffset = textureEnabled ? textureCommands.ImageOffset : Vector2.Zero,
                            TextureAddressS = addressS, TextureAddressT = addressT,
                            LightingEnabled = lightingEnabled, DecalTexture = textureEnabled && decalTexture });
                }
                if (v4 < rspVertexBuffer.Length && v5 < rspVertexBuffer.Length && v6 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v4], rspVertexBuffer[v5], rspVertexBuffer[v6], activeSymbol)
                        { SourceTextureRestore = textureCommands.Snapshot(), SourceTextureImage = currentTextureSymbol, SourceTextureEnabled = textureEnabled, SourceDisplayList = actualDlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 1, ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureOffset = textureEnabled ? textureCommands.ImageOffset : Vector2.Zero,
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
                        { SourceTextureRestore = textureCommands.Snapshot(), SourceTextureImage = currentTextureSymbol, SourceTextureEnabled = textureEnabled, SourceDisplayList = actualDlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 0, ShadeLightSymbol = currentLightSymbol, BlendTextureWithShade = textureEnabled && blendTextureWithShade,
                            TextureOffset = textureEnabled ? textureCommands.ImageOffset : Vector2.Zero,
                            TextureAddressS = addressS, TextureAddressT = addressT,
                            LightingEnabled = lightingEnabled, DecalTexture = textureEnabled && decalTexture });
                }
            }
        }
    }
    internal void ProcessDisplayList(
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
        var triangleCommands = commands.Where(c => c.type == "tri1" || c.type == "tri2").Select((c, i) => (c.pos, i)).ToDictionary(c => c.pos, c => c.i);

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
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol) { SourceDisplayList = dlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 0 });
                }
                if (v4 < rspVertexBuffer.Length && v5 < rspVertexBuffer.Length && v6 < rspVertexBuffer.Length)
                {
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v4], rspVertexBuffer[v5], rspVertexBuffer[v6], activeSymbol) { SourceDisplayList = dlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 1 });
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
                    mesh.Triangles.Add(new ModelTriangle(rspVertexBuffer[v1], rspVertexBuffer[v2], rspVertexBuffer[v3], activeSymbol) { SourceDisplayList = dlName, SourceCommandIndex = triangleCommands[pos], SourceTrianglePart = 0 });
                }
            }
        }
    }
}
