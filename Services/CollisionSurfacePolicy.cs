using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed record CollisionSurfaceChoice(string Name, bool IsSupported, string Reason)
{
    public override string ToString() => Name;
}

public sealed class CollisionSurfacePolicy
{
    private static readonly HashSet<string> General = new(("SURFACE_DEFAULT SURFACE_BURNING SURFACE_0004 SURFACE_HANGABLE SURFACE_SLOW " +
        "SURFACE_CLOSE_CAMERA SURFACE_WATER SURFACE_INTANGIBLE SURFACE_VERY_SLIPPERY SURFACE_SLIPPERY SURFACE_NOT_SLIPPERY SURFACE_TTM_VINES " +
        "SURFACE_SHALLOW_QUICKSAND SURFACE_DEEP_QUICKSAND SURFACE_INSTANT_QUICKSAND SURFACE_DEEP_MOVING_QUICKSAND SURFACE_SHALLOW_MOVING_QUICKSAND " +
        "SURFACE_QUICKSAND SURFACE_MOVING_QUICKSAND SURFACE_FLOWING_WATER SURFACE_WALL_MISC SURFACE_NOISE_DEFAULT SURFACE_NOISE_SLIPPERY " +
        "SURFACE_HORIZONTAL_WIND SURFACE_INSTANT_MOVING_QUICKSAND SURFACE_ICE SURFACE_HARD SURFACE_HARD_SLIPPERY SURFACE_HARD_VERY_SLIPPERY SURFACE_HARD_NOT_SLIPPERY " +
        "SURFACE_BOSS_FIGHT_CAMERA SURFACE_CAMERA_FREE_ROAM SURFACE_THI3_WALLKICK SURFACE_CAMERA_8_DIR SURFACE_CAMERA_MIDDLE SURFACE_CAMERA_ROTATE_RIGHT " +
        "SURFACE_CAMERA_ROTATE_LEFT SURFACE_CAMERA_BOUNDARY SURFACE_NOISE_VERY_SLIPPERY_73 SURFACE_NOISE_VERY_SLIPPERY_74 SURFACE_NOISE_VERY_SLIPPERY " +
        "SURFACE_NO_CAM_COLLISION SURFACE_NO_CAM_COLLISION_77 SURFACE_NO_CAM_COL_VERY_SLIPPERY SURFACE_NO_CAM_COL_SLIPPERY SURFACE_SWITCH SURFACE_VANISH_CAP_WALLS").Split(' '));
    private readonly Dictionary<string, int> definitions = new();
    private readonly HashSet<int> instantWarps = new();
    private readonly HashSet<string> warpNodes = new();
    private readonly string level;
    private readonly int sandSpeedCount = 4, waterSpeedCount = 4;
    public IReadOnlyList<CollisionSurfaceChoice> Choices { get; }

    public CollisionSurfacePolicy(string projectRoot, string levelDirectory, int area)
    {
        level = Path.GetFileName(levelDirectory);
        string header = Path.Combine(projectRoot, "include", "surface_terrains.h");
        if (File.Exists(header))
        {
            foreach (Match m in Regex.Matches(CSourceCalls.MaskComments(File.ReadAllText(header)), @"#define\s+(SURFACE_\w+)\s+(0x[\da-fA-F]+)"))
                if (!m.Groups[1].Value.StartsWith("SURFACE_CLASS_") && !m.Groups[1].Value.StartsWith("SURFACE_FLAG_")) definitions[m.Groups[1].Value] = Convert.ToInt32(m.Groups[2].Value, 16);
        }
        else foreach (string name in General.Concat(new[] { "SURFACE_DEATH_PLANE", "SURFACE_VERTICAL_WIND", "SURFACE_WARP", "SURFACE_LOOK_UP_WARP", "SURFACE_PAINTING_WARP_D3", "SURFACE_MGR_MUSIC" })) definitions[name] = 0;
        string script = Path.Combine(levelDirectory, "script.c");
        if (File.Exists(script))
        {
            string text = ActorSourceReader.Read(script, preserveOffsets: true);
            var calls = CSourceCalls.Read(text, n => n is "AREA" or "END_AREA" or "WARP_NODE" or "INSTANT_WARP");
            var areas = calls.Where(c => c.Name == "AREA").Select(c => Number(c.Argument(text, 0))).Where(i => i.HasValue).Select(i => i!.Value).ToHashSet();
            int current = -1;
            foreach (var call in calls)
            {
                if (call.Name == "AREA") current = Number(call.Argument(text, 0)) ?? -1;
                if (call.Name == "END_AREA") current = -1;
                if (current != area) continue;
                if (call.Name == "WARP_NODE") warpNodes.Add(call.Argument(text, 0));
                if (call.Name == "INSTANT_WARP" && call.Arguments.Count == 5 && Number(call.Argument(text, 0)) is int index &&
                    Number(call.Argument(text, 1)) is int destination && areas.Contains(destination)) instantWarps.Add(index);
            }
        }
        int Count(string path, string name)
        {
            if (!File.Exists(path)) return 4;
            var m = Regex.Match(CSourceCalls.MaskComments(File.ReadAllText(path)), @"\b" + name + @"\s*\[\]\s*=\s*\{([^}]+)\}");
            return m.Success ? m.Groups[1].Value.Split(',').Count(s => s.Trim().Length > 0) : 4;
        }
        sandSpeedCount = Count(Path.Combine(projectRoot, "src", "game", "mario_step.c"), "sMovingSandSpeeds");
        waterSpeedCount = Count(Path.Combine(projectRoot, "src", "game", "mario_actions_submerged.c"), "sWaterCurrentSpeeds");
        Choices = definitions.Keys.OrderBy(n => n).Select(n => new CollisionSurfaceChoice(n, Reason(n) == null, Reason(n) ?? "supported in this area")).ToArray();
    }

    private static int? Number(string value) => int.TryParse(value, out int n) ? n : value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out n) ? n : null;
    private bool Warp(string name, int id) => warpNodes.Contains(name) || warpNodes.Any(n => Number(n) == id);
    private string? Reason(string name)
    {
        if (!definitions.ContainsKey(name)) return "surface is not defined by this game build";
        if (definitions[name] >= 0x40 && definitions[name] < 0x65) return "this value is a terrain command rather than a collision surface";
        if (General.Contains(name)) return null;
        if (name is "SURFACE_DEATH_PLANE" or "SURFACE_VERTICAL_WIND") return Warp("WARP_NODE_DEATH", 0xF1) ? null : "this area has no death warp node";
        if (name == "SURFACE_WARP") return Warp("WARP_NODE_WARP_FLOOR", 0xF3) ? null : "this area has no floor warp node";
        if (name == "SURFACE_LOOK_UP_WARP") return Warp("WARP_NODE_TOTWC", 0xF2) ? null : "this area has no look-up warp node";
        if (name.StartsWith("SURFACE_INSTANT_WARP_") && definitions.TryGetValue(name, out int value))
            return instantWarps.Contains(value - 0x1B) ? null : "this instant warp index or destination area is not configured";
        if (name is "SURFACE_TIMER_START" or "SURFACE_TIMER_END") return level == "pss" ? null : "slide timer surfaces require the secret slide setup";
        if (name == "SURFACE_MGR_MUSIC") return "merry-go-round music requires its level behavior setup; keep existing configured surfaces";
        return "requires level-specific painting or behavior data; keep existing configured surfaces";
    }

    public void Validate(string name, int? force)
    {
        string? reason = Reason(name);
        if (reason != null) throw new InvalidDataException(name + ": " + reason);
        if (!CollisionSourceValidation.HasForce(name)) { if (force.HasValue) throw new InvalidDataException("this surface does not use a force parameter"); return; }
        if (!force.HasValue || force < short.MinValue || force > short.MaxValue) throw new InvalidDataException("force must fit signed 16-bit collision data");
        int count = name == "SURFACE_FLOWING_WATER" ? waterSpeedCount : name.Contains("MOVING_QUICKSAND") ? sandSpeedCount : 0;
        if (count > 0 && (force < 0 || (force.Value >> 8) >= count))
            throw new InvalidDataException($"force high byte must be 0 through {count - 1}; the game indexes its speed table without a bounds check");
    }

    public static CollisionSurfacePolicy ForPath(string collisionPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(collisionPath))!);
        string levelDirectory = directory.Parent?.Name == "areas" ? directory.Parent.Parent!.FullName : directory.FullName;
        string root = levelDirectory;
        for (var parent = directory; parent != null; parent = parent.Parent)
            if (File.Exists(Path.Combine(parent.FullName, "include", "surface_terrains.h"))) { root = parent.FullName; break; }
        return new(root, levelDirectory, int.TryParse(directory.Name, out int area) ? area : 1);
    }

    public static void ValidateChanges(string path, CollisionMesh mesh)
    {
        var old = new CollisionParser().ParseCollisionFile(path, "", "nonpersistent") ?? throw new InvalidDataException("cannot read the collision baseline");
        var policy = ForPath(path);
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            var t = mesh.Triangles[i];
            if (i < old.Triangles.Count && t.SurfaceType == old.Triangles[i].SurfaceType && t.SpecialParam == old.Triangles[i].SpecialParam && t.RawSpecialParam == old.Triangles[i].RawSpecialParam) continue;
            policy.Validate(t.SurfaceType, t.SpecialParam);
        }
    }
}
