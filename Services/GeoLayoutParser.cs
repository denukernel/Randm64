using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public class GeoLayoutParser
{
    // Regex patterns for GEO commands
    private static readonly Regex TranslatePattern = new(
        @"GEO_TRANSLATE\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TranslateWithDlPattern = new(
        @"GEO_TRANSLATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(\w+)\s*\)",
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
        @"GEO_TRANSLATE_ROTATE\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TranslateRotateWithDlPattern = new(
        @"GEO_TRANSLATE_ROTATE_WITH_DL\s*\(\s*\w+\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(-?(?:0x[0-9a-fA-F]+|\d+))\s*,\s*(\w+)\s*\)",
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

    private int ParseIntHelper(string valStr)
    {
        if (valStr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            uint val = Convert.ToUInt32(valStr, 16);
            if (valStr.Length <= 6) // e.g. 0xFFFF or less, fits in s16
            {
                return (short)val;
            }
            return (int)val;
        }
        return int.Parse(valStr);
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
        
        var rotation = Matrix4.CreateRotationZ(rotZ) *
                       Matrix4.CreateRotationX(rotX) *
                       Matrix4.CreateRotationY(rotY);
        
        var scale = Matrix4.CreateScale(node.Scale);

        // Combine: Scale * Rotation * Translation
        return scale * rotation * translation;
    }

    public List<GeoNode> ParsedJoints { get; } = new();
    public Dictionary<string, int> DlToJointIndex { get; } = new();
    public List<GeoDisplayListInstance> DisplayListInstances { get; } = new();
    public float RootScale { get; set; } = 1.0f;

    private sealed class ActorNode(string command, string[] args)
    {
        public string Command = command;
        public string[] Args = args;
        public List<ActorNode> Children = new();
    }

    public Dictionary<string, Matrix4> ParseActorGeoLayout(string filePath, string? rootLayout = null, IEnumerable<string>? additionalFiles = null, bool includeAllRooms = false)
    {
        var transforms = new Dictionary<string, Matrix4>();
        ParsedJoints.Clear();
        DlToJointIndex.Clear();
        DisplayListInstances.Clear();
        RootScale = 1;
        if (!File.Exists(filePath)) return transforms;

        string content = ActorSourceReader.Read(filePath);
        if (additionalFiles != null)
            content += "\n" + string.Join("\n", additionalFiles.Where(path => path != filePath).Select(path => ActorSourceReader.Read(path)));
        content = Regex.Replace(content, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        var layouts = new Dictionary<string, List<ActorNode>>();
        foreach (Match array in Regex.Matches(content, @"(?:const\s+)?GeoLayout\s+(\w+)\s*\[\s*\]\s*=\s*\{([\s\S]*?)\};"))
        {
            var nodes = new List<ActorNode>();
            var stack = new Stack<List<ActorNode>>();
            layouts[array.Groups[1].Value] = nodes;
            foreach (Match command in Regex.Matches(array.Groups[2].Value, @"GEO_(\w+)\s*\(([^)]*)\)"))
            {
                string name = command.Groups[1].Value;
                if (name == "OPEN_NODE")
                {
                    // The original interpreter permits grouping opens with no intervening node.
                    if (nodes.Count == 0) nodes.Add(new ActorNode("NODE_START", Array.Empty<string>()));
                    stack.Push(nodes);
                    nodes = nodes[^1].Children;
                }
                else if (name == "CLOSE_NODE")
                {
                    // Some original layouts (including Boo) contain a redundant final close.
                    if (stack.Count == 0) continue;
                    nodes = stack.Pop();
                }
                else if (name is not "RETURN" and not "END")
                    nodes.Add(new ActorNode(name, command.Groups[2].Value.Split(',').Select(a => a.Trim()).ToArray()));
            }
            // Several original scripts terminate with an open grouping node. The
            // array boundary ends that traversal; the tree is already attached.
        }
        string preferred = Path.GetFileName(Path.GetDirectoryName(filePath)) + "_geo";
        string shell = Path.GetFileName(Path.GetDirectoryName(filePath)) + "_with_shell_geo";
        var referenced = layouts.Values.SelectMany(FlattenNodes).Where(node => node.Command is "BRANCH" or "BRANCH_AND_LINK")
            .Select(node => node.Args[^1]).ToHashSet();
        string? root = rootLayout != null && layouts.ContainsKey(rootLayout) ? rootLayout : layouts.ContainsKey(preferred) ? preferred : layouts.ContainsKey(shell) ? shell :
            layouts.Keys.FirstOrDefault(name => !referenced.Contains(name) && !name.Contains("shadow") && name.EndsWith("_geo"))
                ?? layouts.Keys.LastOrDefault();
        bool HasJoints(string name, HashSet<string> seen)
        {
            if (!seen.Add(name) || !layouts.TryGetValue(name, out var nodes)) return false;
            return nodes.SelectMany(Flatten).Any(node => node.Command == "ANIMATED_PART" ||
                (node.Command is "BRANCH" or "BRANCH_AND_LINK" && HasJoints(node.Args[^1], seen)));
        }
        if (rootLayout == null && root != null && !HasJoints(root, new()) && Directory.Exists(Path.Combine(Path.GetDirectoryName(filePath)!, "anims")))
            root = layouts.Keys.FirstOrDefault(name => !referenced.Contains(name) && HasJoints(name, new())) ?? root;
        if (root != null) VisitLayout(root, Matrix4.Identity, -1, new HashSet<string>());
        return transforms;

        void VisitLayout(string name, Matrix4 parent, int parentJoint, HashSet<string> calls, GeoBillboard? billboard = null)
        {
            if (!layouts.TryGetValue(name, out var nodes) || !calls.Add(name)) return;
            VisitNodes(nodes, parent, parentJoint, calls, billboard);
            calls.Remove(name);
        }

        static IEnumerable<ActorNode> Flatten(ActorNode node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));
        static IEnumerable<ActorNode> FlattenNodes(List<ActorNode> nodes) => nodes.SelectMany(Flatten);

        void VisitNodes(IEnumerable<ActorNode> nodes, Matrix4 parent, int parentJoint, HashSet<string> calls, GeoBillboard? billboard = null)
        {
            bool dynamicBodyScale = false;
            foreach (var node in nodes)
            {
                // Every sibling starts with its parent's transform and joint.
                Matrix4 world = parent;
                var nodeBillboard = billboard;
                int joint = parentJoint;
                var a = node.Args;
                float Number(int i) => ParseIntHelper(a[i]);
                Vector3 Position(int i) => new(Number(i), Number(i + 1), Number(i + 2));
                Matrix4 Rotation(int i) => Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Number(i))) *
                    Matrix4.CreateRotationY(MathHelper.DegreesToRadians(Number(i + 1))) *
                    Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Number(i + 2)));
                string? dl = null;
                switch (node.Command)
                {
                    case "ASM":
                        dynamicBodyScale = a[^1] == "geo_snufit_scale_body";
                        break;
                    case "BRANCH":
                    case "BRANCH_AND_LINK":
                        VisitLayout(a[^1], parent, parentJoint, calls, billboard);
                        break;
                    case "SWITCH_CASE":
                        // Room visibility depends on the player location. Editor
                        // overviews need all rooms; actor previews retain one state.
                        if (includeAllRooms && a.Length > 1 && a[1] == "geo_switch_area")
                        {
                            int first = DisplayListInstances.Count;
                            VisitNodes(node.Children, parent, parentJoint, calls, billboard);
                            // Room branches repeat shared walls and exterior geometry.
                            var unique = DisplayListInstances.Skip(first)
                                .DistinctBy(instance => (instance.DisplayListName, instance.Transform, instance.JointIndex)).ToList();
                            DisplayListInstances.RemoveRange(first, DisplayListInstances.Count - first);
                            DisplayListInstances.AddRange(unique);
                        }
                        else VisitNodes(node.Children.Take(1), parent, parentJoint, calls, billboard);
                        continue;
                    case "RENDER_RANGE":
                        if (Number(0) > 0) continue; // Select the near/high-detail LOD once.
                        break;
                    case "SCALE":
                    case "SCALE_WITH_DL":
                        float scale = ConvertScale(a[1]);
                        if (scale == 0 && dynamicBodyScale) scale = 1; // Snufit's idle body scale is supplied at runtime.
                        if (scale == 0) continue;
                        world = Matrix4.CreateScale(scale) * parent;
                        if (parentJoint < 0) RootScale *= scale;
                        if (node.Command.EndsWith("WITH_DL")) dl = a[^1];
                        break;
                    case "ANIMATED_PART":
                        world = Matrix4.CreateTranslation(Position(1)) * parent;
                        joint = ParsedJoints.Count;
                        ParsedJoints.Add(new GeoNode(GeoNodeType.Other)
                        {
                            JointIndex = joint, ParentIndex = parentJoint, Translation = Position(1),
                            RestWorldMatrix = world, DisplayListName = a[4]
                        });
                        dl = a[4];
                        break;
                    case "DISPLAY_LIST": dl = a[1]; break;
                    case "BILLBOARD":
                    case "BILLBOARD_WITH_PARAMS":
                    case "BILLBOARD_WITH_PARAMS_AND_DL":
                        if (a.Length >= 4) world = Matrix4.CreateTranslation(Position(1)) * parent;
                        nodeBillboard = new GeoBillboard { ParentJointIndex = parentJoint, RestWorldMatrix = world };
                        if (node.Command.EndsWith("AND_DL")) dl = a[^1];
                        break;
                    case "TRANSLATE":
                    case "TRANSLATE_NODE":
                    case "TRANSLATE_WITH_DL":
                    case "TRANSLATE_NODE_WITH_DL":
                        world = Matrix4.CreateTranslation(Position(1)) * parent;
                        if (node.Command.EndsWith("WITH_DL")) dl = a[^1];
                        break;
                    case "ROTATE":
                    case "ROTATION_NODE":
                    case "ROTATE_WITH_DL":
                    case "ROTATION_NODE_WITH_DL":
                        world = Rotation(1) * parent;
                        if (node.Command.EndsWith("WITH_DL")) dl = a[^1];
                        break;
                    case "TRANSLATE_ROTATE":
                    case "TRANSLATE_ROTATE_WITH_DL":
                        world = Rotation(4) * Matrix4.CreateTranslation(Position(1)) * parent;
                        if (node.Command.EndsWith("WITH_DL")) dl = a[^1];
                        break;
                }
                // Wing-cap callbacks hide these nodes for normal Mario in the game.
                if (dl is not null and not "NULL" and not "0" &&
                    dl != "mario_cap_wings" && dl != "mario_right_hand_cap_wings")
                {
                    DisplayListInstances.Add(new GeoDisplayListInstance
                    {
                        DisplayListName = dl, Transform = world, JointIndex = joint,
                        JointWorldMatrix = joint >= 0 ? ParsedJoints[joint].RestWorldMatrix : Matrix4.Identity,
                        Billboard = nodeBillboard
                    });
                    transforms.TryAdd(dl, world);
                    DlToJointIndex.TryAdd(dl, joint);
                }
                VisitNodes(node.Children, world, joint, calls, nodeBillboard);
                if (node.Command != "ASM") dynamicBodyScale = false;
            }
        }
    }
}
