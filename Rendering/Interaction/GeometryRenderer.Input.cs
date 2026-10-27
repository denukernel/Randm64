using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;
using System.IO;
using System.Linq;

namespace Sm64DecompLevelViewer.Rendering;
public partial class GeometryRenderer
{
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // Request focus when clicked

        Console.WriteLine($"Mouse button pressed: {e.Button}");

        bool isControlDown = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
        bool isShiftDown = e.Modifiers.HasFlag(KeyModifiers.Shift) || KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
        if(e.Button==MouseButton.Left && TryPlaceOnTerrainAtScreenPosition(new Vector2(MouseState.X,MouseState.Y),isShiftDown)) return;

        if (e.Button == MouseButton.Left && MeshEditingEnabled)
        {
            if (!TerrainPlacementEnabled) BeginMeshInteraction(new Vector2(MouseState.X, MouseState.Y));
            return;
        }

        if (e.Button == MouseButton.Left && isControlDown)
        {
            var mouseState = MouseState;
            Vector2 mousePos = new Vector2(mouseState.X, mouseState.Y);

            // Compute ray direction from screen position
            float ndcX = (2.0f * mousePos.X) / ClientSize.X - 1.0f;
            float ndcY = 1.0f - (2.0f * mousePos.Y) / Math.Max(1, ClientSize.Y);

            Vector4 nearPointNDC = new Vector4(ndcX, ndcY, -1.0f, 1.0f);
            Vector4 farPointNDC = new Vector4(ndcX, ndcY, 1.0f, 1.0f);
            Matrix4 invViewProj = Matrix4.Invert(_view * _projection);
            Vector4 nearPointWorld = nearPointNDC * invViewProj;
            Vector4 farPointWorld = farPointNDC * invViewProj;
            Vector3 nearPoint = nearPointWorld.Xyz / nearPointWorld.W;
            Vector3 farPoint = farPointWorld.Xyz / farPointWorld.W;

            Vector3 rayOrigin = nearPoint;
            Vector3 rayDir = Vector3.Normalize(farPoint - nearPoint);

            float closestColDist = float.MaxValue;
            int closestColTri = (_showCollision || CollisionPaintingEnabled) ? GetClosestCollisionTriangle(rayOrigin, rayDir, out closestColDist) : -1;

            float closestVisDist = float.MaxValue;
            int closestVisTri = _showVisual && !CollisionPaintingEnabled ? GetClosestVisualTriangle(rayOrigin, rayDir, out closestVisDist) : -1;

            if (closestColTri >= 0 && closestColDist < closestVisDist)
            {
                CollisionTriangleSelected?.Invoke(closestColTri);
                
                var tri = _collisionMesh!.Triangles[closestColTri];
                var v0 = _collisionMesh.Vertices[tri.V1];
                SelectedMeshPoint = new Vector3(v0.X, v0.Y, v0.Z);
            }
            else if (closestVisTri >= 0)
            {
                Vector3 intersectionPoint = rayOrigin + rayDir * closestVisDist;
                int closestVertexIndex = -1;
                float closestVertexDist = float.MaxValue;

                if (_visualMesh != null)
                {
                    if (_visualMesh.SubMeshes.Count > 0)
                    {
                        int globalVertexIndex = 0;
                        for (int s = 0; s < _visualMesh.SubMeshes.Count; s++)
                        {
                            var sub = _visualMesh.SubMeshes[s];
                            if (!sub.IsVisible)
                            {
                                globalVertexIndex += sub.Vertices.Count;
                                continue;
                            }
                            for (int v = 0; v < sub.Vertices.Count; v++)
                            {
                                var vert = sub.Vertices[v];
                                float d = Vector3.Distance(intersectionPoint, new Vector3(vert.X, vert.Y, vert.Z));
                                if (d < closestVertexDist)
                                {
                                    closestVertexDist = d;
                                    closestVertexIndex = globalVertexIndex;
                                }
                                globalVertexIndex++;
                            }
                        }
                    }
                    else
                    {
                        for (int v = 0; v < _visualMesh.Vertices.Count; v++)
                        {
                            var vert = _visualMesh.Vertices[v];
                            float d = Vector3.Distance(intersectionPoint, new Vector3(vert.X, vert.Y, vert.Z));
                            if (d < closestVertexDist)
                            {
                                closestVertexDist = d;
                                closestVertexIndex = v;
                            }
                        }
                    }
                }

                if (closestVertexIndex >= 0)
                {
                    VisualVertexSelected?.Invoke(closestVertexIndex);
                    
                    if (_visualMesh != null)
                    {
                        if (_visualMesh.SubMeshes.Count > 0)
                        {
                            int globalVertexIndex = 0;
                            ModelVertex? selectedVert = null;
                            for (int s = 0; s < _visualMesh.SubMeshes.Count; s++)
                            {
                                var sub = _visualMesh.SubMeshes[s];
                                if (closestVertexIndex >= globalVertexIndex && closestVertexIndex < globalVertexIndex + sub.Vertices.Count)
                                {
                                    selectedVert = sub.Vertices[closestVertexIndex - globalVertexIndex];
                                    break;
                                }
                                globalVertexIndex += sub.Vertices.Count;
                            }
                            if (selectedVert != null)
                            {
                                SelectedMeshPoint = new Vector3(selectedVert.X, selectedVert.Y, selectedVert.Z);
                            }
                        }
                        else if (closestVertexIndex < _visualMesh.Vertices.Count)
                        {
                            var selectedVert = _visualMesh.Vertices[closestVertexIndex];
                            SelectedMeshPoint = new Vector3(selectedVert.X, selectedVert.Y, selectedVert.Z);
                        }
                    }
                }
            }
            return;
        }

        if (e.Button == MouseButton.Left && _objects != null && _objects.Count > 0)
        {
            Console.WriteLine($"Left click detected, checking {_objects.Count} objects");
            
            var mouseState = MouseState;
            Vector2 mousePos = new Vector2(mouseState.X, mouseState.Y);
            Console.WriteLine($"Mouse position: {mousePos}");
            
            int clickedObjectIndex = GetObjectAtScreenPosition(mousePos);
            Console.WriteLine($"Clicked object index: {clickedObjectIndex}");

            if (clickedObjectIndex >= 0)
            {
                _selectedObjectIndex = clickedObjectIndex;
                _selectedObject = _objects[clickedObjectIndex];
                Console.WriteLine($"Selected object: {_selectedObject.ModelName} at ({_selectedObject.X}, {_selectedObject.Y}, {_selectedObject.Z})");
                
                ObjectSelected?.Invoke(_selectedObjectIndex);
                
                UploadObjectData();
            }
            else
            {
                _selectedObjectIndex = -1;
                _selectedObject = null;
                Console.WriteLine("No object selected");
                
                ObjectSelected?.Invoke(-1);
                
                UploadObjectData();
            }
        }
        else if (e.Button == MouseButton.Left)
        {
            Console.WriteLine($"Left click but objects is null or empty: objects={_objects?.Count ?? 0}");
        }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        Focus(); // Also grab focus on scroll
    }
    public int GetObjectAtScreenPosition(Vector2 screenPos)
    {
        if (_objects == null) return -1;
        // Convert screen coordinates to normalized device coordinates
        float x = (2.0f * screenPos.X) / ClientSize.X - 1.0f;
        float y = 1.0f - (2.0f * screenPos.Y) / Math.Max(1, ClientSize.Y);

        Vector4 nearPointNDC = new Vector4(x, y, -1.0f, 1.0f);
        Vector4 farPointNDC = new Vector4(x, y, 1.0f, 1.0f);

        Matrix4 invViewProj = Matrix4.Invert(_view * _projection);

        Vector4 nearPointWorld = nearPointNDC * invViewProj;
        Vector4 farPointWorld = farPointNDC * invViewProj;

        Vector3 nearPoint = nearPointWorld.Xyz / nearPointWorld.W;
        Vector3 farPoint = farPointWorld.Xyz / farPointWorld.W;

        Vector3 rayOrigin = nearPoint;
        Vector3 rayDir = Vector3.Normalize(farPoint - nearPoint);

        float closestDistance = float.MaxValue;
        int closestObjectIndex = -1;
        

        for (int i = 0; i < _objects!.Count; i++)
        {
            var obj = _objects[i];
            if (obj.IsDeleted) continue;

            Vector3 objCenter = PreviewPosition(obj);
            float halfSize = VISUAL_OBJECT_SIZE * (i == _selectedObjectIndex ? 1.5f : 1f) / 2;
            bool markerVisible = _showObjects || i == _selectedObjectIndex;
            bool actorVisible = LevelActorsEnabled && _levelActorAssets.ContainsKey(obj.ModelName);
            if (!markerVisible && !actorVisible) continue;
            var rotation = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RY));
            var inverse = Matrix4.Invert(rotation);
            var localOrigin = Vector3.TransformPosition(rayOrigin - objCenter, inverse);
            var localDirection = Vector3.TransformVector(rayDir, inverse);

            if (markerVisible && RayIntersections.RayIntersectsBox(localOrigin, localDirection, Vector3.Zero, halfSize, out float distance))
            {
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestObjectIndex = i;
                }
            }
            if (actorVisible)
            {
                var actor = _levelActorAssets[obj.ModelName].Mesh;
                var transform = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(obj.RX)) * rotation * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(obj.RZ));
                foreach (var triangle in actor.Triangles)
                {
                    Vector3 World(int index) { var v = actor.Vertices[index]; return Vector3.TransformPosition(new Vector3(v.X,v.Y,v.Z), transform) + objCenter; }
                    if (RayIntersections.RayIntersectsTriangle(rayOrigin, rayDir, World(triangle.V1), World(triangle.V2), World(triangle.V3), out float hit) && hit < closestDistance)
                    { closestDistance = hit; closestObjectIndex = i; }
                }
            }
        }

        if (_showVisual && closestObjectIndex >= 0 && GetClosestVisualTriangle(rayOrigin,rayDir,out float terrainDistance) >= 0 && terrainDistance + 1 < closestDistance)
            return -1;
        return closestObjectIndex;
    }
    private int GetClosestCollisionTriangle(Vector3 rayOrigin, Vector3 rayDir, out float closestDist)
    {
        closestDist = float.MaxValue;
        int closestTriIndex = -1;

        if (_collisionMesh == null || _collisionMesh.Triangles.Count == 0)
            return -1;

        for (int i = 0; i < _collisionMesh.Triangles.Count; i++)
        {
            var tri = _collisionMesh.Triangles[i];
            if (tri.V1 >= _collisionMesh.Vertices.Count || tri.V2 >= _collisionMesh.Vertices.Count || tri.V3 >= _collisionMesh.Vertices.Count)
                continue;

            var v0Raw = _collisionMesh.Vertices[tri.V1];
            var v1Raw = _collisionMesh.Vertices[tri.V2];
            var v2Raw = _collisionMesh.Vertices[tri.V3];

            Vector3 v0 = new Vector3(v0Raw.X, v0Raw.Y, v0Raw.Z);
            Vector3 v1 = new Vector3(v1Raw.X, v1Raw.Y, v1Raw.Z);
            Vector3 v2 = new Vector3(v2Raw.X, v2Raw.Y, v2Raw.Z);

            if (RayIntersections.RayIntersectsTriangle(rayOrigin, rayDir, v0, v1, v2, out float dist))
            {
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestTriIndex = i;
                }
            }
        }

        return closestTriIndex;
    }
    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        if (_closeRequested) { Close(); return; }
        base.OnUpdateFrame(args);
        UpdateMeshInteraction();
        if(TerrainPlacementEnabled && KeyboardState.IsKeyPressed(Keys.Escape)) TerrainPickCanceled?.Invoke();

        // Swap complete scenes only between frames, never while drawing or uploading the old buffers.
        lock (_meshReloadLock)
        {
            if (_pendingVisualMesh != null)
            {
                lock (_animationLock)
                {
                    _visualMesh = _pendingVisualMesh;
                    _animationPoseDirty = _activeAnimation != null;
                }
                _pendingVisualMesh = null;
                _needsVisualMeshUpload = true;
                _needsLevelAssetsRefresh = true;
                _waterDirty = true;
            }
            if (_pendingCollisionMesh != null)
            {
                _collisionMesh = _pendingCollisionMesh;
                _pendingCollisionMesh = null;
                _needsCollisionMeshUpload = true;
                _needsObjectUpload = true;
                _waterDirty = true;
            }
        }

        if (_frameLevelRequested && _visualMesh?.Vertices.Count > 0)
        {
            var vertices = _visualMesh.Vertices;
            var min = new Vector3(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z));
            var max = new Vector3(vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
            var center = (min + max) / 2;
            float distance = Math.Max(500, (max - min).Length * 1.25f);
            SetCameraPositionAndTarget(center + Vector3.Normalize(new Vector3(1, 0.65f, 1)) * distance, center);
            _frameLevelRequested = false;
        }

        lock (_animationLock)
        {
            if (_frameActorRequested && _visualMesh != null)
            {
                var facing = _cameraPosition - _cameraTarget;
                if (facing.LengthSquared < 0.001f) facing = new Vector3(0.3f, 0.2f, -1);
                facing.Normalize();
                var bounds = ActorModelLoader.GetPreviewBounds(_visualMesh, _activeAnimation, facing);
                var center = (bounds.Min + bounds.Max) / 2;
                float distance = MathF.Max(80, (bounds.Max - bounds.Min).Length * 1.4f);
                SetCameraPositionAndTarget(center + facing * distance, center);
                _frameActorRequested = false;
                _animationPoseDirty = true;
            }
            if (!LevelNavigationEnabled && _activeAnimation != null && KeyboardState.IsKeyPressed(Keys.Space))
                _animationPlaying = !_animationPlaying;
            if (_activeAnimation != null && _animationPlaying)
            {
                _frameTimer += (float)args.Time * 30 * Math.Clamp(AnimationSpeed, 0.1f, 4);
                int steps = (int)_frameTimer;
                if (steps > 0)
                {
                    _frameTimer -= steps;
                    _currentFrame = _activeAnimation.AdvanceFrame(_currentFrame, steps, LoopAnimationPreview);
                    _animationPoseDirty = true;
                }
            }
            if (_visualMesh != null && (_animationPoseDirty || _visualMesh.HasBillboards))
            {
                new AnimationParser().ApplyFrame(_visualMesh, _activeAnimation, _currentFrame, _cameraPosition - _cameraTarget);
                _needsVisualMeshUpload = true;
                _animationPoseDirty = false;
            }
        }
        if (_needsObjectUpload)
        {
            lock (_objectLock)
            {
                _needsObjectUpload = false;
                UploadObjectDataCore();
            }
        }

        if (_needsSplineUpload)
        {
            _needsSplineUpload = false;
            UploadSplineDataCore();
        }
        if (_needsVisualMeshUpload)
        {
            UploadVisualMeshData();
            _needsVisualMeshUpload = false;
        }
        UpdateMeshPositionBuffer();

        if (_needsCollisionMeshUpload)
        {
            UploadCollisionMeshData();
            _needsCollisionMeshUpload = false;
        }

        if (_needsTextureReload)
        {
            _textureLoader?.Dispose();
            _textureLoader = new TextureLoader(_projectRoot);
            if (_visualMesh != null) UploadVisualMeshData();
            foreach (var asset in _levelActorAssets.Values) asset.LastFrame = -1;
            _waterDirty = true;
            _needsTextureReload = false;
        }
        if (_needsLevelAssetsRefresh)
        {
            foreach (var asset in _levelActorAssets.Values) asset.Gpu.Dispose();
            _levelActorAssets.Clear();
            _levelActorPreloader?.Dispose();
            _levelActorPreloader = null;
            _synchronousActorCatalog = null;
            _needsLevelAssetsRefresh = false;
        }

        if (IsPlayingPreview || LevelNavigationEnabled && !IsFocused) return;
        float deltaTime = (float)args.Time;

        // WASD camera movement
        Vector3 forward = Vector3.Normalize(_cameraTarget - _cameraPosition);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

        if (LevelNavigationEnabled)
        {
            bool fast = KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
            bool precise = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
            var input = new Vector3(
                (KeyboardState.IsKeyDown(Keys.D) ? 1 : 0) - (KeyboardState.IsKeyDown(Keys.A) ? 1 : 0),
                (KeyboardState.IsKeyDown(Keys.Space) ? 1 : 0) - (KeyboardState.IsKeyDown(Keys.X) ? 1 : 0),
                (KeyboardState.IsKeyDown(Keys.W) ? 1 : 0) - (KeyboardState.IsKeyDown(Keys.S) ? 1 : 0));
            _cameraTarget += LevelCameraNavigation.Movement(_cameraYaw, input, CameraMoveSpeed, deltaTime, fast, precise);
            if (MouseState.IsButtonDown(MouseButton.Middle))
                _cameraTarget += LevelCameraNavigation.Pan(_cameraYaw, _cameraPitch, _cameraDistance, ClientSize.Y, MouseState.Delta) * (precise ? 0.25f : 1);
        }
        else
        {
            if (KeyboardState.IsKeyDown(Keys.W))
            {
                _cameraTarget += forward * CameraMoveSpeed * deltaTime;
            }
            if (KeyboardState.IsKeyDown(Keys.S))
            {
                _cameraTarget -= forward * CameraMoveSpeed * deltaTime;
            }
            if (KeyboardState.IsKeyDown(Keys.A))
            {
                _cameraTarget -= right * CameraMoveSpeed * deltaTime;
            }
            if (KeyboardState.IsKeyDown(Keys.D))
            {
                _cameraTarget += right * CameraMoveSpeed * deltaTime;
            }
            }
    
            // Handle camera rotation with RIGHT mouse button
            if (MouseState.IsButtonDown(MouseButton.Right))
            {
                float sensitivity = CameraRotationSensitivity * (LevelNavigationEnabled ? 10 : 1);
            if (LevelNavigationEnabled && (KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl))) sensitivity *= 0.25f;
                _cameraYaw += MouseState.Delta.X * sensitivity;
                _cameraPitch -= MouseState.Delta.Y * sensitivity;
                _cameraPitch = Math.Clamp(_cameraPitch, -89f, 89f);
            }
    
            // Handle zoom with mouse wheel
            if (LevelNavigationEnabled)
                _cameraDistance = LevelCameraNavigation.Zoom(_cameraDistance, MouseState.ScrollDelta.Y,
                    KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl));
            else
            {
                _cameraDistance -= MouseState.ScrollDelta.Y * 50f;
                _cameraDistance = Math.Clamp(_cameraDistance, 100f, 50000f);
            }
    
            // Toggle controls
            if (KeyboardState.IsKeyPressed(Keys.C))
            {
                _showCollision = !_showCollision;
                ViewSettingsChanged?.Invoke();
                Console.WriteLine($"Collision mesh: {(_showCollision ? "ON" : "OFF")}");
            }
    
            if (KeyboardState.IsKeyPressed(Keys.V))
            {
                _showVisual = !_showVisual;
                ViewSettingsChanged?.Invoke();
                Console.WriteLine($"Visual mesh: {(_showVisual ? "ON" : "OFF")}");
            }
    
            // F1/F2 Copy/Paste passthrough
            if (!MeshEditingEnabled && KeyboardState.IsKeyPressed(Keys.F1))
            {
                CopyRequested?.Invoke();
            }
    
            if (!MeshEditingEnabled && KeyboardState.IsKeyPressed(Keys.F2))
            {
                PasteRequested?.Invoke();
            }
            if (!MeshEditingEnabled && KeyboardState.IsKeyPressed(Keys.Delete))
            {
                RemoveObjectRequested?.Invoke();
            }
    
            // Object transformation
            if (!MeshEditingEnabled && _selectedObject != null)
            {
                bool modified = false;
                float objMoveSpeed = ObjectMoveSpeed * deltaTime; // Units per second
                int moveStep = 10; // Basic unit step for snapping or just movement
    
                // Movement: Up/Down/Left/Right/PageUp/PageDown
                if (KeyboardState.IsKeyDown(Keys.Up)) { _selectedObject.X += (int)(forward.X * objMoveSpeed + 0.5f); _selectedObject.Z += (int)(forward.Z * objMoveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Down)) { _selectedObject.X -= (int)(forward.X * objMoveSpeed + 0.5f); _selectedObject.Z -= (int)(forward.Z * objMoveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Left)) { _selectedObject.X -= (int)(right.X * objMoveSpeed + 0.5f); _selectedObject.Z -= (int)(right.Z * objMoveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Right)) { _selectedObject.X += (int)(right.X * objMoveSpeed + 0.5f); _selectedObject.Z += (int)(right.Z * objMoveSpeed + 0.5f); modified = true; }
                
                // Height movement
                if (KeyboardState.IsKeyDown(Keys.PageUp)) { _selectedObject.Y += (int)(objMoveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.PageDown)) { _selectedObject.Y -= (int)(objMoveSpeed + 0.5f); modified = true; }
    
                // Rotation (one-shot for precision)
                if (KeyboardState.IsKeyPressed(Keys.Q)) { _selectedObject.RY = (_selectedObject.RY + 15) % 360; modified = true; }
                if (KeyboardState.IsKeyPressed(Keys.E)) { _selectedObject.RY = (_selectedObject.RY - 15 + 360) % 360; modified = true; }
    
                if (modified)
                {
                    ObjectSelected?.Invoke(_selectedObjectIndex); // Notify UI of change
                    UploadObjectData(); // Re-render cubes
                }
            }
    
            // Spline point keyboard transformation
            if (EditSplinePoints != null && SelectedSplinePointIndex >= 0 && SelectedSplinePointIndex < EditSplinePoints.Count)
            {
                var p = EditSplinePoints[SelectedSplinePointIndex];
                bool modified = false;
                float moveSpeed = ObjectMoveSpeed * deltaTime;
                if (SplinePathName?.StartsWith("sIntroStartToPipe",StringComparison.Ordinal) == true)
                { forward.X = -forward.X; forward.Z = -forward.Z; right = -right; }
    
                if (KeyboardState.IsKeyDown(Keys.Up)) { p.X += (int)(forward.X * moveSpeed + 0.5f); p.Z += (int)(forward.Z * moveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Down)) { p.X -= (int)(forward.X * moveSpeed + 0.5f); p.Z -= (int)(forward.Z * moveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Left)) { p.X -= (int)(right.X * moveSpeed + 0.5f); p.Z -= (int)(right.Z * moveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.Right)) { p.X += (int)(right.X * moveSpeed + 0.5f); p.Z += (int)(right.Z * moveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.PageUp)) { p.Y += (int)(moveSpeed + 0.5f); modified = true; }
                if (KeyboardState.IsKeyDown(Keys.PageDown)) { p.Y -= (int)(moveSpeed + 0.5f); modified = true; }
    
                if (modified)
                {
                    UploadSplineData();
                    UploadObjectData();
                    SplinePointModified?.Invoke();
                }
            }
    
            // Sub-model visibility controls (1-9 to toggle hide, 0 to show all)
            if (KeyboardState.IsKeyPressed(Keys.D0) || KeyboardState.IsKeyPressed(Keys.KeyPad0))
            {
                _hiddenSubModels.Clear();
                if (_visualMesh != null)
                {
                    foreach (var subMesh in _visualMesh.SubMeshes)
                        subMesh.IsVisible = true;
                    UploadVisualMeshData(); // Re-upload with all visible
                }
            }
    
            // Check each number key explicitly
            var numberKeys = new[] {
                (Keys.D1, Keys.KeyPad1, 1),
                (Keys.D2, Keys.KeyPad2, 2),
                (Keys.D3, Keys.KeyPad3, 3),
                (Keys.D4, Keys.KeyPad4, 4),
                (Keys.D5, Keys.KeyPad5, 5),
                (Keys.D6, Keys.KeyPad6, 6),
                (Keys.D7, Keys.KeyPad7, 7),
                (Keys.D8, Keys.KeyPad8, 8),
                (Keys.D9, Keys.KeyPad9, 9)
            };
    
            foreach (var (mainKey, keypadKey, i) in numberKeys)
            {
                if (KeyboardState.IsKeyPressed(mainKey) || KeyboardState.IsKeyPressed(keypadKey))
                {
                    if (_hiddenSubModels.Contains(i))
                    {
                        _hiddenSubModels.Remove(i);
                    }
                    else
                    {
                        _hiddenSubModels.Add(i);
                    }
                    
                    if (_visualMesh != null)
                    {
                        var subMesh = _visualMesh.SubMeshes.FirstOrDefault(sm => sm.SubModelNumber == i);
                        if (subMesh != null)
                        {
                            subMesh.IsVisible = !_hiddenSubModels.Contains(i);
                            UploadVisualMeshData(); // Re-upload with updated visibility
                        }
                    }
                }
            }
    
            // Reset camera
            if (KeyboardState.IsKeyPressed(Keys.R))
            {
                ResetCamera();
                Console.WriteLine("Camera reset");
            }
        }
}
