using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class GeoLayoutParser
{
    // Regex patterns for GEO commands
    private static readonly Regex TranslatePattern = new(
        @"GEO_TRANSLATE\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TranslateWithDlPattern = new(
        @"GEO_TRANSLATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex RotatePattern = new(
        @"GEO_ROTATE\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex RotateWithDlPattern = new(
        @"GEO_ROTATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex ScalePattern = new(
        @"GEO_SCALE\s*\(\s*\w+\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex ScaleWithDlPattern = new(
        @"GEO_SCALE_WITH_DL\s*\(\s*\w+\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*,\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TranslateRotatePattern = new(
        @"GEO_TRANSLATE_ROTATE\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TranslateRotateWithDlPattern = new(
        @"GEO_TRANSLATE_ROTATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex DisplayListPattern = new(
        @"GEO_DISPLAY_LIST\s*\(\s*\w+\s*,\s*(\w+)\s*\)",
        RegexOptions.Compiled);

    public GeoNode? ParseGeoLayout(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Geo layout file not found: {filePath}");
            return null;
        }

        var content = File.ReadAllText(filePath);
        var root = new GeoNode(GeoNodeType.Root);

        ParseAllCommands(content, root);

        return root;
    }

    private void ParseAllCommands(string content, GeoNode root)
    {
        foreach (Match match in TranslatePattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Translate);
            node.Translation = new Vector3(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value)
            );
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_TRANSLATE: {node.Translation}");
        }

        foreach (Match match in TranslateWithDlPattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Translate);
            node.Translation = new Vector3(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value)
            );
            node.DisplayListName = match.Groups[4].Value;
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_TRANSLATE_WITH_DL: {node.Translation}, DL: {node.DisplayListName}");
        }

        foreach (Match match in RotatePattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Rotate);
            node.Rotation = new Vector3(
                ConvertAngleToDegrees(match.Groups[1].Value),
                ConvertAngleToDegrees(match.Groups[2].Value),
                ConvertAngleToDegrees(match.Groups[3].Value)
            );
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_ROTATE: {node.Rotation} degrees");
        }

        foreach (Match match in RotateWithDlPattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Rotate);
            node.Rotation = new Vector3(
                ConvertAngleToDegrees(match.Groups[1].Value),
                ConvertAngleToDegrees(match.Groups[2].Value),
                ConvertAngleToDegrees(match.Groups[3].Value)
            );
            node.DisplayListName = match.Groups[4].Value;
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_ROTATE_WITH_DL: {node.Rotation} degrees, DL: {node.DisplayListName}");
        }

        foreach (Match match in ScalePattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Scale);
            node.Scale = ConvertScale(match.Groups[1].Value);
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_SCALE: {node.Scale}");
        }

        foreach (Match match in ScaleWithDlPattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.Scale);
            node.Scale = ConvertScale(match.Groups[1].Value);
            node.DisplayListName = match.Groups[2].Value;
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_SCALE_WITH_DL: {node.Scale}, DL: {node.DisplayListName}");
        }

        foreach (Match match in TranslateRotatePattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.TranslateRotate);
            node.Translation = new Vector3(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value)
            );
            node.Rotation = new Vector3(
                ConvertAngleToDegrees(match.Groups[4].Value),
                ConvertAngleToDegrees(match.Groups[5].Value),
                ConvertAngleToDegrees(match.Groups[6].Value)
            );
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_TRANSLATE_ROTATE: T:{node.Translation}, R:{node.Rotation} degrees");
        }

        foreach (Match match in TranslateRotateWithDlPattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.TranslateRotate);
            node.Translation = new Vector3(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value)
            );
            node.Rotation = new Vector3(
                ConvertAngleToDegrees(match.Groups[4].Value),
                ConvertAngleToDegrees(match.Groups[5].Value),
                ConvertAngleToDegrees(match.Groups[6].Value)
            );
            node.DisplayListName = match.Groups[7].Value;
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_TRANSLATE_ROTATE_WITH_DL: T:{node.Translation}, R:{node.Rotation} degrees, DL: {node.DisplayListName}");
        }

        foreach (Match match in DisplayListPattern.Matches(content))
        {
            var node = new GeoNode(GeoNodeType.DisplayList);
            node.DisplayListName = match.Groups[1].Value;
            root.Children.Add(node);
            Console.WriteLine($"Found GEO_DISPLAY_LIST: {node.DisplayListName}");
        }
    }

    public string? GetPrimaryDisplayListName(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        string content = File.ReadAllText(filePath);
        
        // Try to find any DISPLAY_LIST command
        var match = DisplayListPattern.Match(content);
        if (match.Success) return match.Groups[1].Value;

        // Try commands with DL
        match = TranslateWithDlPattern.Match(content);
        if (match.Success) return match.Groups[4].Value;

        match = RotateWithDlPattern.Match(content);
        if (match.Success) return match.Groups[4].Value;

        match = ScaleWithDlPattern.Match(content);
        if (match.Success) return match.Groups[2].Value;

        match = TranslateRotateWithDlPattern.Match(content);
        if (match.Success) return match.Groups[7].Value;

        return null;
    }

    private float ConvertAngleToDegrees(string angleStr)
    {
        long angleUnits;
        if (angleStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            angleUnits = Convert.ToInt64(angleStr, 16);
        else
            angleUnits = long.Parse(angleStr);

        // Convert: (angleUnits / 65536.0) * 360.0
        return (float)((angleUnits / 65536.0) * 360.0);
    }

    private float ConvertScale(string scaleStr)
    {
        long scaleUnits;
        if (scaleStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            scaleUnits = Convert.ToInt64(scaleStr, 16);
        else
            scaleUnits = long.Parse(scaleStr);

        // Convert: scaleUnits / 65536.0
        return (float)(scaleUnits / 65536.0);
    }

    public Dictionary<string, Matrix4> ExtractTransformations(GeoNode root)
    {
        var transforms = new Dictionary<string, Matrix4>();

        foreach (var child in root.Children)
        {
            if (child.DisplayListName != null)
            {
                var transform = BuildTransformMatrix(child);
                transforms[child.DisplayListName] = transform;
                Console.WriteLine($"Transform for {child.DisplayListName}: T:{child.Translation}, R:{child.Rotation}, S:{child.Scale}");
            }
        }

        return transforms;
    }

    private Matrix4 BuildTransformMatrix(GeoNode node)
    {
        var translation = Matrix4.CreateTranslation(node.Translation);
        
        // Convert degrees to radians for OpenTK
        var rotX = MathHelper.DegreesToRadians(node.Rotation.X);
        var rotY = MathHelper.DegreesToRadians(node.Rotation.Y);
        var rotZ = MathHelper.DegreesToRadians(node.Rotation.Z);
        
        var rotation = Matrix4.CreateRotationX(rotX) *
                       Matrix4.CreateRotationY(rotY) *
                       Matrix4.CreateRotationZ(rotZ);
        
        var scale = Matrix4.CreateScale(node.Scale);

        // Combine: Scale * Rotation * Translation
        return scale * rotation * translation;
    }

    public List<GeoNode> ParsedJoints { get; } = new();
    public Dictionary<string, int> DlToJointIndex { get; } = new();

    public Dictionary<string, Matrix4> ParseActorGeoLayout(string filePath)
    {
        var transforms = new Dictionary<string, Matrix4>();
        ParsedJoints.Clear();
        DlToJointIndex.Clear();

        if (!File.Exists(filePath)) return transforms;

        try
        {
            string content = File.ReadAllText(filePath);
            var arrayRegex = new Regex(@"const\s+GeoLayout\s+(\w+)\[\]\s*=\s*\{([\s\S]*?)\};", RegexOptions.Compiled);
            var matches = arrayRegex.Matches(content);
            if (matches.Count == 0) return transforms;

            var layouts = new Dictionary<string, string>();
            string lastLayoutName = "";
            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string body = match.Groups[2].Value;
                layouts[name] = body;
                lastLayoutName = name;
            }

            if (!string.IsNullOrEmpty(lastLayoutName))
            {
                var translationStack = new Stack<Vector3>();
                var scaleStack = new Stack<float>();
                var jointIndexStack = new Stack<int>();
                var visitedLayouts = new HashSet<string>();

                int nextJointIndex = 0;
                TraverseLayout(lastLayoutName, layouts, transforms, Vector3.Zero, 1.0f,
                               translationStack, scaleStack, jointIndexStack, -1, ref nextJointIndex, visitedLayouts);
                
                Console.WriteLine($"Parsed geo layout: {lastLayoutName}. Joint count: {ParsedJoints.Count}. Reference DL transforms: {transforms.Count}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing actor geo layout hierarchy: {ex.Message}");
        }

        return transforms;
    }

    private void TraverseLayout(
        string layoutName,
        Dictionary<string, string> layouts,
        Dictionary<string, Matrix4> transforms,
        Vector3 currentTranslation,
        float currentScale,
        Stack<Vector3> translationStack,
        Stack<float> scaleStack,
        Stack<int> jointIndexStack,
        int parentJointIndex,
        ref int nextJointIndex,
        HashSet<string> visitedLayouts)
    {
        if (visitedLayouts.Contains(layoutName)) return;
        visitedLayouts.Add(layoutName);

        if (!layouts.TryGetValue(layoutName, out string? body)) return;

        var lines = body.Split('\n');
        Vector3 activeJointTranslation = currentTranslation;
        float activeJointScale = currentScale;
        int activeJointIndex = parentJointIndex;

        int currentDepth = 0;
        int switchCaseActiveDepth = -1;
        int switchCaseBranchIndex = 0;

        int savedJointIndex = nextJointIndex;
        int maxNextJointIndex = nextJointIndex;

        // Matches: GEO_ANIMATED_PART(layer, x, y, z, displayList)
        var animatedPartRegex = new Regex(@"GEO_ANIMATED_PART\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*([^)]+)\)", RegexOptions.Compiled);
        
        // Matches: GEO_DISPLAY_LIST(layer, displayList)
        var displayListRegex = new Regex(@"GEO_DISPLAY_LIST\s*\(\s*\w+\s*,\s*([^)]+)\)", RegexOptions.Compiled);
        
        // Matches: GEO_SCALE(param, scaleVal)
        var scaleRegex = new Regex(@"GEO_SCALE\s*\(\s*\w+\s*,\s*(0x[0-9a-fA-F]+|\d+)\s*\)", RegexOptions.Compiled);

        // Matches: GEO_TRANSLATE_ROTATE(layer, x, y, z, rx, ry, rz)
        var translateRotateRegex = new Regex(@"GEO_TRANSLATE_ROTATE\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*[^,]+\s*,\s*[^,]+\s*,\s*[^)]+\)", RegexOptions.Compiled);

        // Matches: GEO_TRANSLATE_ROTATE_WITH_DL(layer, x, y, z, rx, ry, rz, displayList)
        var translateRotateWithDlRegex = new Regex(@"GEO_TRANSLATE_ROTATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*[^,]+\s*,\s*[^,]+\s*,\s*[^,]+\s*,\s*([^)]+)\)", RegexOptions.Compiled);

        // Matches: GEO_TRANSLATE_WITH_DL(layer, x, y, z, displayList)
        var translateWithDlRegex = new Regex(@"GEO_TRANSLATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*([^)]+)\)", RegexOptions.Compiled);

        // Matches: GEO_BRANCH(param, layout)
        var branchRegex = new Regex(@"GEO_BRANCH\s*\(\s*\d+\s*,\s*([^)]+)\)", RegexOptions.Compiled);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Contains("GEO_RETURN()"))
            {
                break;
            }

            if (trimmed.Contains("GEO_OPEN_NODE()"))
            {
                currentDepth++;
                translationStack.Push(currentTranslation);
                scaleStack.Push(currentScale);
                jointIndexStack.Push(parentJointIndex);

                currentTranslation = activeJointTranslation;
                currentScale = activeJointScale;
                parentJointIndex = activeJointIndex;
            }
            else if (trimmed.Contains("GEO_CLOSE_NODE()"))
            {
                currentDepth--;
                if (switchCaseActiveDepth != -1 && currentDepth <= switchCaseActiveDepth)
                {
                    switchCaseActiveDepth = -1;
                    nextJointIndex = maxNextJointIndex;
                }

                if (translationStack.Count > 0)
                {
                    currentTranslation = translationStack.Pop();
                    activeJointTranslation = currentTranslation;
                }
                if (scaleStack.Count > 0)
                {
                    currentScale = scaleStack.Pop();
                    activeJointScale = currentScale;
                }
                if (jointIndexStack.Count > 0)
                {
                    parentJointIndex = jointIndexStack.Pop();
                    activeJointIndex = parentJointIndex;
                }
            }
            else if (trimmed.StartsWith("GEO_SWITCH_CASE"))
            {
                switchCaseActiveDepth = currentDepth;
                switchCaseBranchIndex = 0;
                savedJointIndex = nextJointIndex;
                maxNextJointIndex = nextJointIndex;
            }

            // For each branch of a switch case, reset nextJointIndex to savedJointIndex to overlay joint indices
            if (switchCaseActiveDepth != -1 && currentDepth == switchCaseActiveDepth + 1)
            {
                if (trimmed.StartsWith("GEO_BRANCH") || 
                    trimmed.StartsWith("GEO_DISPLAY_LIST") || 
                    trimmed.StartsWith("GEO_ANIMATED_PART") || 
                    trimmed.StartsWith("GEO_TRANSLATE") ||
                    trimmed.StartsWith("GEO_NODE_START") ||
                    trimmed.StartsWith("GEO_SCALE"))
                {
                    maxNextJointIndex = Math.Max(maxNextJointIndex, nextJointIndex);
                    nextJointIndex = savedJointIndex;
                    switchCaseBranchIndex++;
                }
            }

            if (trimmed.StartsWith("GEO_SCALE"))
            {
                var match = scaleRegex.Match(trimmed);
                if (match.Success)
                {
                    float scaleVal = ConvertScale(match.Groups[1].Value);
                    activeJointScale = currentScale * scaleVal;
                }
            }
            else if (trimmed.StartsWith("GEO_BRANCH"))
            {
                var match = branchRegex.Match(trimmed);
                if (match.Success)
                {
                    string targetLayoutName = match.Groups[1].Value.Trim();
                    
                    // Stacks must be cloned to avoid recursion interference
                    var subTranslationStack = new Stack<Vector3>(new Stack<Vector3>(translationStack));
                    var subScaleStack = new Stack<float>(new Stack<float>(scaleStack));
                    var subJointIndexStack = new Stack<int>(new Stack<int>(jointIndexStack));
                    var subVisitedLayouts = new HashSet<string>(visitedLayouts);

                    TraverseLayout(targetLayoutName, layouts, transforms, currentTranslation, currentScale, 
                                   subTranslationStack, subScaleStack, subJointIndexStack, parentJointIndex, ref nextJointIndex, subVisitedLayouts);
                }
            }
            else
            {
                // Check animated part
                var match = animatedPartRegex.Match(trimmed);
                if (match.Success)
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);
                    int z = int.Parse(match.Groups[3].Value);
                    string dl = match.Groups[4].Value.Trim();

                    activeJointTranslation = currentTranslation + new Vector3(x, y, z) * currentScale;

                    int thisJointIndex = nextJointIndex++;
                    var jointNode = new GeoNode(GeoNodeType.Other)
                    {
                        JointIndex = thisJointIndex,
                        ParentIndex = parentJointIndex,
                        Translation = new Vector3(x, y, z),
                        DisplayListName = dl
                    };
                    
                    while (ParsedJoints.Count <= thisJointIndex)
                    {
                        ParsedJoints.Add(null!);
                    }
                    ParsedJoints[thisJointIndex] = jointNode;
                    activeJointIndex = thisJointIndex;

                    if (dl != "NULL" && dl != "0" && !string.IsNullOrEmpty(dl))
                    {
                        if (!transforms.ContainsKey(dl))
                        {
                            var mat = Matrix4.CreateScale(activeJointScale) * Matrix4.CreateTranslation(activeJointTranslation);
                            transforms[dl] = mat;
                        }
                        if (!DlToJointIndex.ContainsKey(dl))
                        {
                            DlToJointIndex[dl] = thisJointIndex;
                        }
                    }
                    continue;
                }

                // Check display list
                match = displayListRegex.Match(trimmed);
                if (match.Success)
                {
                    string dl = match.Groups[1].Value.Trim();
                    if (dl != "NULL" && dl != "0" && !string.IsNullOrEmpty(dl))
                    {
                        if (!transforms.ContainsKey(dl))
                        {
                            var mat = Matrix4.CreateScale(activeJointScale) * Matrix4.CreateTranslation(activeJointTranslation);
                            transforms[dl] = mat;
                        }
                        if (!DlToJointIndex.ContainsKey(dl))
                        {
                            DlToJointIndex[dl] = activeJointIndex;
                        }
                    }
                    continue;
                }

                // Check translate rotate with DL
                match = translateRotateWithDlRegex.Match(trimmed);
                if (match.Success)
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);
                    int z = int.Parse(match.Groups[3].Value);
                    string dl = match.Groups[4].Value.Trim();

                    activeJointTranslation = currentTranslation + new Vector3(x, y, z) * currentScale;

                    if (dl != "NULL" && dl != "0" && !string.IsNullOrEmpty(dl))
                    {
                        if (!transforms.ContainsKey(dl))
                        {
                            var mat = Matrix4.CreateScale(activeJointScale) * Matrix4.CreateTranslation(activeJointTranslation);
                            transforms[dl] = mat;
                        }
                        if (!DlToJointIndex.ContainsKey(dl))
                        {
                            DlToJointIndex[dl] = activeJointIndex;
                        }
                    }
                    continue;
                }

                // Check translate rotate (no DL, updates joint center)
                match = translateRotateRegex.Match(trimmed);
                if (match.Success)
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);
                    int z = int.Parse(match.Groups[3].Value);

                    activeJointTranslation = currentTranslation + new Vector3(x, y, z) * currentScale;
                    continue;
                }

                // Check translate with DL
                match = translateWithDlRegex.Match(trimmed);
                if (match.Success)
                {
                    int x = int.Parse(match.Groups[1].Value);
                    int y = int.Parse(match.Groups[2].Value);
                    int z = int.Parse(match.Groups[3].Value);
                    string dl = match.Groups[4].Value.Trim();

                    activeJointTranslation = currentTranslation + new Vector3(x, y, z) * currentScale;

                    if (dl != "NULL" && dl != "0" && !string.IsNullOrEmpty(dl))
                    {
                        if (!transforms.ContainsKey(dl))
                        {
                            var mat = Matrix4.CreateScale(activeJointScale) * Matrix4.CreateTranslation(activeJointTranslation);
                            transforms[dl] = mat;
                        }
                        if (!DlToJointIndex.ContainsKey(dl))
                        {
                            DlToJointIndex[dl] = activeJointIndex;
                        }
                    }
                    continue;
                }
            }
        }
        
        visitedLayouts.Remove(layoutName);
    }
}
