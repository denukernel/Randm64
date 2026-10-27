using System.Collections.Concurrent;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer.Rendering;

public partial class GeometryRenderer
{
    public volatile MeshEditSession? MeshSession;
    public volatile bool MeshFaceSelection;
    public volatile bool MeshBoxToolActive;
    public volatile bool TexturePaintingEnabled;
    public volatile bool CollisionSurfacePaintingEnabled;
    public volatile GeometryEditTarget MeshTarget = GeometryEditTarget.MeshOnly;
    public volatile string? CollisionSurfaceSymbol;
    public volatile int CollisionSurfaceForce;
    public volatile bool CollisionSurfaceForceValid = true;
    public volatile bool MeshSessionBusy;
    public volatile string? MeshPaintSymbol;
    public event Action<string>? MeshToolStatus;
    private readonly ConcurrentQueue<Action> meshActions = new();
    private MeshEditSession.Move? meshDrag;
    private int[] meshSelection = Array.Empty<int>();
    private Vector3 dragAnchor, dragNormal, lastDragDelta;
    private Vector2 dragStart;
    private readonly HashSet<int> paintedStroke = new();
    private bool paintStroke;
    private bool selectionIsCollision, collisionStroke;
    private GeometryEditTarget selectionTarget;
    private bool meshPositionsDirty;
    private readonly List<Models.ModelVertex> meshGpuOrder = new();
    private float[] meshGpuData = Array.Empty<float>();

    public void QueueMeshAction(Action action) => meshActions.Enqueue(action);
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButton.Left) return;
        paintStroke = false;
        if (meshDrag == null) return;
        meshDrag = null;
        MeshToolStatus?.Invoke(MeshSession?.IsDirty == true ? "mesh changed; save mesh to write changes" : "vertex selected");
    }
    public void PrepareMeshSave() { meshDrag = null; paintStroke = false; MeshSessionBusy = true; }
    public void InstallMeshSession(MeshEditSession? session) => QueueMeshAction(() =>
    {
        if (meshDrag != null) MeshSession?.CancelMove(meshDrag);
        meshDrag = null;
        paintStroke = false;
        meshSelection = Array.Empty<int>();
        MeshSession = session;
        _needsVisualMeshUpload = true;
    });

    private void UpdateMeshPositionBuffer()
    {
        if (!meshPositionsDirty || MeshSession == null || !ReferenceEquals(_visualMesh, MeshSession.Scene)) return;
        if (meshGpuData.Length != meshGpuOrder.Count * 12 || meshGpuData.Length == 0) { _needsVisualMeshUpload = true; return; }
        for (int i = 0; i < meshGpuOrder.Count; i++)
        {
            var v = meshGpuOrder[i];
            meshGpuData[i * 12] = v.X; meshGpuData[i * 12 + 1] = v.Y; meshGpuData[i * 12 + 2] = v.Z;
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, _visualVbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, meshGpuData.Length * sizeof(float), meshGpuData);
        meshPositionsDirty = false;
    }

    private (Vector3 Origin, Vector3 Direction) MeshRay(Vector2 screen)
    {
        var inverse = Matrix4.Invert(_view * _projection);
        float x = 2 * screen.X / Math.Max(1, ClientSize.X) - 1;
        float y = 1 - 2 * screen.Y / Math.Max(1, ClientSize.Y);
        var near = new Vector4(x, y, -1, 1) * inverse;
        var far = new Vector4(x, y, 1, 1) * inverse;
        return (near.Xyz / near.W, Vector3.Normalize(far.Xyz / far.W - near.Xyz / near.W));
    }

    private int PickMeshFace(Vector2 screen, out Vector3 hit)
    {
        var ray = MeshRay(screen);
        int face = GetClosestVisualTriangle(ray.Origin, ray.Direction, out float distance);
        hit = ray.Origin + distance * ray.Direction;
        return face;
    }
    private int PickCollisionFace(Vector2 screen, out Vector3 hit)
    {
        var ray = MeshRay(screen);
        int face = GetClosestCollisionTriangle(ray.Origin, ray.Direction, out float distance);
        hit = ray.Origin + distance * ray.Direction;
        return face;
    }

    private void BeginMeshInteraction(Vector2 screen)
    {
        if (MeshSessionBusy || MeshBoxToolActive || MeshSession == null) return;
        if (TexturePaintingEnabled || CollisionSurfacePaintingEnabled)
        {
            paintStroke = true;
            collisionStroke = CollisionSurfacePaintingEnabled;
            paintedStroke.Clear();
            PaintAt(screen);
            return;
        }
        selectionIsCollision = MeshTarget == GeometryEditTarget.CollisionOnly;
        int face = selectionIsCollision ? PickCollisionFace(screen, out var hit) : PickMeshFace(screen, out hit);
        if (face < 0 && MeshTarget == GeometryEditTarget.MeshAndCollision) { selectionIsCollision = true; face = PickCollisionFace(screen, out hit); }
        if (face < 0) return;
        try
        {
            int[] indices;
            if (selectionIsCollision)
            {
                var tri = MeshSession.Collision!.Triangles[face];
                indices = new[] { tri.V1, tri.V2, tri.V3 };
            }
            else
            {
                var tri = MeshSession.Triangles[face];
                indices = new[] { tri.V1, tri.V2, tri.V3 };
            }
            if (!MeshFaceSelection)
            {
                // Pick only a visible vertex on the hit face, rather than searching every vertex in the level.
                int index = indices.MinBy(i => Vector3.DistanceSquared(hit, Position(i)));
                indices = selectionIsCollision ? MeshSession.WeldedCollisionVertex(index) : MeshSession.WeldedVertex(index);
            }
            else indices = selectionIsCollision ? MeshSession.CollisionFaceVertices(face) : MeshSession.FaceVertices(face);
            meshSelection = indices;
            selectionTarget = MeshTarget;
            meshDrag = selectionIsCollision ? MeshSession.BeginCollisionMove(indices, MeshTarget) : MeshSession.BeginMove(indices, MeshTarget);
            dragAnchor = indices.Select(Position).Aggregate(Vector3.Zero, (a, b) => a + b) / indices.Length;
            dragNormal = Vector3.Normalize(_cameraTarget - _cameraPosition);
            dragStart = screen;
            lastDragDelta = Vector3.Zero;
            SelectedMeshPoint = dragAnchor;
            MeshToolStatus?.Invoke(MeshFaceSelection ? "dragging face; hold x, y or z to constrain; shift snaps; escape cancels" : "dragging vertex; hold x, y or z to constrain; shift snaps; escape cancels");
        }
        catch (Exception ex) { MeshToolStatus?.Invoke(ex.Message); }
    }

    private Vector3 Position(int index)
    {
        if (selectionIsCollision) return MeshSession!.CollisionPosition(index);
        var v = MeshSession!.Vertices[index];
        return new Vector3(v.X, v.Y, v.Z);
    }

    private Vector3 DragPoint(Vector2 screen, Vector3? axis)
    {
        var ray = MeshRay(screen);
        if (axis.HasValue)
        {
            float b = Vector3.Dot(axis.Value, ray.Direction);
            float denominator = 1 - b * b;
            if (denominator < 0.0001f) return Vector3.Zero;
            var q = ray.Origin - dragAnchor;
            return axis.Value * ((Vector3.Dot(axis.Value, q) - b * Vector3.Dot(ray.Direction, q)) / denominator);
        }
        float dot = Vector3.Dot(ray.Direction, dragNormal);
        if (Math.Abs(dot) < 0.0001f) return dragAnchor;
        return ray.Origin + ray.Direction * (Vector3.Dot(dragAnchor - ray.Origin, dragNormal) / dot);
    }

    private void PaintAt(Vector2 screen)
    {
        int face = collisionStroke ? PickCollisionFace(screen, out _) : PickMeshFace(screen, out _);
        if (face < 0 || !paintedStroke.Add(face)) return;
        try
        {
            if (collisionStroke)
            {
                if (!CollisionSurfaceForceValid) throw new InvalidOperationException("enter a valid force parameter before painting collision");
                if (CollisionSurfaceSymbol == null) return;
                int? force = CollisionSourceValidation.HasForce(CollisionSurfaceSymbol) ? CollisionSurfaceForce : null;
                if (MeshSession!.PaintCollisionSurface(face, CollisionSurfaceSymbol, force))
                    MeshToolStatus?.Invoke("collision type applied: " + CollisionSurfaceSymbol + "; unsaved changes");
                return;
            }
            if (MeshPaintSymbol == null) return;
            if (MeshSession!.PaintFace(face, MeshPaintSymbol))
            {
                _needsVisualMeshUpload = true;
                MeshToolStatus?.Invoke("texture painted; unsaved mesh changes");
            }
        }
        catch (Exception ex) { MeshToolStatus?.Invoke(ex.Message); }
    }

    private void UpdateMeshInteraction()
    {
        while (meshActions.TryDequeue(out var action)) action();
        if (meshDrag != null && (!MeshEditingEnabled || MeshBoxToolActive || MeshTarget != selectionTarget || !IsFocused || KeyboardState.IsKeyPressed(Keys.Escape)))
        {
            MeshSession?.CancelMove(meshDrag);
            meshDrag = null;
            meshPositionsDirty = true;
            _needsCollisionMeshUpload = true;
            MeshToolStatus?.Invoke("drag canceled");
        }
        if (MeshSessionBusy || MeshSession == null) return;
        bool held = MouseState.IsButtonDown(MouseButton.Left);
        if (paintStroke)
        {
            if (!held || !MeshEditingEnabled || (!TexturePaintingEnabled && !CollisionSurfacePaintingEnabled) || !IsFocused) paintStroke = false;
            else if (MouseState.Delta.LengthSquared > 0) PaintAt(new Vector2(MouseState.X, MouseState.Y));
        }
        if (meshDrag == null) return;
        if (!held)
        {
            meshDrag = null;
            MeshToolStatus?.Invoke(MeshSession.IsDirty ? "mesh changed; save mesh to write changes" : "vertex selected");
            return;
        }
        if (MouseState.Delta.LengthSquared == 0) return;
        Vector3? axis = KeyboardState.IsKeyDown(Keys.X) ? Vector3.UnitX : KeyboardState.IsKeyDown(Keys.Y) ? Vector3.UnitY : KeyboardState.IsKeyDown(Keys.Z) ? Vector3.UnitZ : null;
        float snap = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift) ? 50 : 1;
        ApplyMeshDrag(new Vector2(MouseState.X, MouseState.Y), axis, snap);
    }

    private void ApplyMeshDrag(Vector2 screen, Vector3? axis, float snap)
    {
        if (meshDrag == null || MeshSession == null) return;
        var delta = DragPoint(screen, axis) - DragPoint(dragStart, axis);
        delta = new Vector3(MathF.Round(delta.X / snap), MathF.Round(delta.Y / snap), MathF.Round(delta.Z / snap)) * snap;
        if (delta == lastDragDelta) return;
        if (!MeshSession.PreviewMove(meshDrag, delta)) return;
        lastDragDelta = delta;
        SelectedMeshPoint = dragAnchor + delta;
        meshPositionsDirty = true;
        if (MeshSession.Collision != null) _needsCollisionMeshUpload = true;
    }

    public void NudgeMeshSelection(Vector3 delta)
    {
        QueueMeshAction(() =>
        {
            if (MeshSession == null || MeshSessionBusy || meshSelection.Length == 0 || selectionTarget != MeshTarget) { MeshToolStatus?.Invoke("select a vertex or face for the current movement target"); return; }
            try
            {
                var move = selectionIsCollision ? MeshSession.BeginCollisionMove(meshSelection, MeshTarget) : MeshSession.BeginMove(meshSelection, MeshTarget);
                if (MeshSession.PreviewMove(move, delta))
                {
                    meshPositionsDirty = true;
                    if (MeshSession.Collision != null) _needsCollisionMeshUpload = true;
                    MeshToolStatus?.Invoke("geometry changed; save to write changes");
                }
            }
            catch (Exception ex) { MeshToolStatus?.Invoke(ex.Message); }
        });
    }
}
