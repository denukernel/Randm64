using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed class IntroActorService
{
    private const float Angle = MathF.PI / 32768;
    private readonly string peachPath;
    private byte[] peachVersion;
    private float distance, pitch, yaw;
    private readonly List<SplinePoint> lakituOffsets, lakituFocus;
    public (Vector3 Position, Vector3 Focus) ReferenceCamera { get; }
    public IntroActorService(string root)
    {
        peachPath = Path.Combine(root, "src", "game", "behaviors", "intro_peach.inc.c");
        peachVersion = File.ReadAllBytes(peachPath);
        string active = ActorSourceReader.Read(peachPath, preserveOffsets: true);
        distance = Value(active, "oIntroPeachDistToCamera"); pitch = Value(active, "oIntroPeachPitchFromFocus"); yaw = Value(active, "oIntroPeachYawFromFocus");
        string camera = Path.Combine(root, "src", "game", "camera.c");
        var service = new CutsceneService();
        var positions = service.LoadSpline(camera, "sIntroStartToPipePosition"); var focuses = service.LoadSpline(camera, "sIntroStartToPipeFocus");
        if (positions.Count < 4 || focuses.Count < 4) throw new InvalidDataException("intro camera reference is missing");
        ReferenceCamera = CameraSplinePlayback.WorldPose("sIntroStartToPipe", CameraSplinePlayback.Evaluate(positions, 0, 0), CameraSplinePlayback.Evaluate(focuses, 0, 0));
        lakituOffsets = service.LoadSpline(camera, "gIntroLakituStartToPipeOffsetFromCamera");
        lakituFocus = service.LoadSpline(camera, "gIntroLakituStartToPipeFocus");
    }
    private static Match Assignment(string source, string field)
    {
        var matches = Regex.Matches(source, @"\b(?:o|obj)->" + field + @"\s*=\s*([-+]?(?:\d+(?:\.\d*)?|\.\d+))[fF]?\s*;");
        if (matches.Count != 1) throw new InvalidDataException("cannot uniquely edit peach setting: " + field);
        return matches[0];
    }
    private static float Value(string source, string field) => float.Parse(Assignment(source, field).Groups[1].Value, CultureInfo.InvariantCulture);
    private static (float Pitch, float Yaw) Angles(Vector3 delta) => (MathF.Atan2(delta.Y, new Vector2(delta.X, delta.Z).Length), MathF.Atan2(delta.X, delta.Z));
    private static Vector3 Polar(float dist, float pitch, float yaw) => new(MathF.Cos(pitch) * MathF.Sin(yaw) * dist, MathF.Sin(pitch) * dist, MathF.Cos(pitch) * MathF.Cos(yaw) * dist);
    private Vector3 PeachAt(Vector3 camera, Vector3 focus)
    {
        var angles = Angles(focus - camera);
        return camera + Polar(distance, angles.Pitch + pitch * Angle, angles.Yaw + yaw * Angle);
    }
    public Vector3 PeachWorldPosition => PeachAt(ReferenceCamera.Position, ReferenceCamera.Focus);
    public void SavePeachPosition(Vector3 world)
    {
        if (!float.IsFinite(world.X + world.Y + world.Z) || world.Length > 100000) throw new InvalidDataException("enter a finite peach position within 100000 world units");
        var relative = world - ReferenceCamera.Position;
        if (relative.Length < 1) throw new InvalidDataException("peach must be at least one unit from the intro camera");
        var cameraAngles = Angles(ReferenceCamera.Focus - ReferenceCamera.Position); var targetAngles = Angles(relative);
        float Wrap(float value) => (value + 32768) - MathF.Floor((value + 32768) / 65536) * 65536 - 32768;
        var changes = new Dictionary<string, float> { ["oIntroPeachDistToCamera"] = relative.Length, ["oIntroPeachPitchFromFocus"] = Wrap((targetAngles.Pitch - cameraAngles.Pitch) / Angle), ["oIntroPeachYawFromFocus"] = Wrap((targetAngles.Yaw - cameraAngles.Yaw) / Angle) };
        string source = File.ReadAllText(peachPath), active = ActorSourceReader.Read(peachPath, preserveOffsets: true, sourceText: source);
        var edits = changes.Select(p => (Match: Assignment(active, p.Key).Groups[1], Value: p.Value.ToString("0.0######", CultureInfo.InvariantCulture))).ToArray();
        foreach (var edit in edits.OrderByDescending(e => e.Match.Index)) source = source[..edit.Match.Index] + edit.Value + source[(edit.Match.Index + edit.Match.Length)..];
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        SourceFileTransaction.Commit(new Dictionary<string, byte[]> { [peachPath] = bytes }, new Dictionary<string, byte[]?> { [peachPath] = peachVersion });
        peachVersion = bytes; distance = changes["oIntroPeachDistToCamera"]; pitch = changes["oIntroPeachPitchFromFocus"]; yaw = changes["oIntroPeachYawFromFocus"];
    }
    public IntroActorPose[] Preview(Vector3 camera, Vector3 focus)
    {
        var peach = PeachAt(camera, focus);
        return new[]
        {
            new IntroActorPose("MODEL_PEACH", peach, new Vector3(0x400, 0x7500, -0x3700) * (360f / 65536), 100),
            SampleLakitu(camera, focus, null, 0)
        };
    }
    private IntroActorPose SampleLakitu(Vector3 camera, Vector3 focus, CameraSplinePlayback? lakituCursor, int frame)
    {
        // The game evaluates the array named Offset into its facing vector, and Focus into its position offset.
        var facing = lakituOffsets.Count >= 4 ? lakituCursor?.Advance(lakituOffsets) ?? CameraSplinePlayback.Evaluate(lakituOffsets, 0, 0) : Vector3.UnitZ;
        var offset = lakituFocus.Count >= 4 ? lakituCursor?.Advance(lakituFocus) ?? CameraSplinePlayback.Evaluate(lakituFocus, 0, 0) : new Vector3(0, 0, 300);
        var cam = Angles(focus - camera); var off = Angles(offset); var look = Angles(facing);
        var lakitu = camera + Polar(offset.Length, cam.Pitch + off.Pitch, cam.Yaw + off.Yaw);
        return new IntroActorPose("MODEL_LAKITU", lakitu, new Vector3(look.Pitch, look.Yaw, 0) * (180 / MathF.PI), frame);
    }
    public FlightPlayback CreateFlightPlayback() => new(this);

    // The selected start-to-pipe path is the flight shot, after the peach letter and fade.
    // Follow intro_lakitu.inc.c: initialize, fly spline, hold in world space, orbit the pipe, delete.
    public sealed class FlightPlayback
    {
        private readonly IntroActorService service;
        private readonly CameraSplinePlayback cursor = new();
        private IntroActorPose? last;
        private int frame, orbitFrame = -1;
        private float radius = 1400, bobAngle = -4096, bobSpeed = 2048, xOffset = -200;
        private int moveYaw = 0x8000;
        private float faceYaw = 0xC000;
        internal FlightPlayback(IntroActorService service) { this.service = service; }
        public IntroActorPose[] Advance(Vector3 camera, Vector3 focus)
        {
            int tick = frame++;
            if (tick == 0) return Array.Empty<IntroActorPose>();
            if (!cursor.Finished) last = service.SampleLakitu(camera, focus, cursor, tick - 1);
            else if (tick > 720 && orbitFrame < 0) orbitFrame = 0;
            else if (orbitFrame >= 0 && last != null)
            {
                if (orbitFrame > 98) return Array.Empty<IntroActorPose>();
                moveYaw += 0x200;
                radius += (100 - radius) * 0.03f;
                float facePitch = MathF.Atan2(last.Position.Y - 400, 200) / Angle;
                int delta = (short)((int)(moveYaw + 0x8000 - faceYaw));
                faceYaw += delta / 4;
                var position = new Vector3(-1128, 560, 4664) + Polar(radius, 0, moveYaw * Angle);
                position.Y += 150 * MathF.Cos(bobAngle * Angle);
                bobAngle += bobSpeed;
                bobSpeed += (512 - bobSpeed) * 0.05f;
                position.X += xOffset;
                xOffset += -xOffset * 0.05f;
                last = new IntroActorPose("MODEL_LAKITU", position, new Vector3(facePitch, faceYaw, 0) * (360f / 65536), tick - 1);
                orbitFrame++;
            }
            return last == null ? Array.Empty<IntroActorPose>() : new[] { last with { AnimationFrame = tick - 1 } };
        }
    }
}
