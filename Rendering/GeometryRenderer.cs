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

public partial class GeometryRenderer : GameWindow
{
    private CollisionMesh? _collisionMesh;
    private VisualMesh? _visualMesh;
    private List<LevelObject>? _objects;
    private const float VISUAL_OBJECT_SIZE = 100f;
    
    private Matrix4 _view;
    private Matrix4 _projection;
    private Vector3 _cameraPosition;
    private Vector3 _cameraTarget;
    private float _cameraDistance = 5000f;
    private float _cameraYaw = 45f;
    private float _cameraPitch = 30f;
    private bool _isRightMouseDown = false;
    private Vector2 _lastMousePos;

    // Collision rendering (wireframe)
    private int _collisionShaderProgram;
    private int _collisionVao;
    private int _collisionVbo;
    private int _collisionVertexCount;

    // Visual rendering (solid with lighting)
    private int _visualShaderProgram;
    private int _visualVao;
    private int _visualVbo;
    private int _visualVertexCount;

    // Object rendering (colored cubes)
    private int _objectShaderProgram;
    private int _objectVao;
    private int _objectVbo;
    private int _objectVertexCount;

    // Object selection
    private LevelObject? _selectedObject;
    private int _selectedObjectIndex = -1;

    private bool _showCollision = true;
    private bool _showVisual = true;
    private bool _showObjects = true;
    public bool ShowCollision { get => _showCollision; set => _showCollision = value; }
    public bool ShowVisual { get => _showVisual; set => _showVisual = value; }
    public bool ShowObjectMarkers { get => _showObjects; set { _showObjects = value; _needsObjectUpload = true; } }
    public bool SkyboxEnabled { get; set; } = true;
    public bool WaterEnabled { get; set; } = true;
    public bool LevelActorsEnabled { get; set; }
    public bool AnimateLevelActors { get; set; } = true;
    private string? _skyboxName;
    private readonly LevelSkyboxRenderer _skyboxRenderer = new();
    private readonly System.Diagnostics.Stopwatch _sceneClock = System.Diagnostics.Stopwatch.StartNew();
    private bool _waterDirty = true;
    private MeshGpu? _waterGpu;
    private bool _frameLevelRequested;
    private LevelActorPreloader? _levelActorPreloader;
    // Capture/validation tools can request a fully populated deterministic first frame.
    public bool ProgressiveActorLoading { get; set; } = true;
    private LevelActorCatalog? _synchronousActorCatalog;
    private LevelObjectPlacement? _objectPlacement;
    private Vector3 PreviewPosition(LevelObject obj) => (_objectPlacement ??= new LevelObjectPlacement(_projectRoot)).Position(obj,_collisionMesh);
    private readonly Dictionary<string, LevelActorAsset> _levelActorAssets = new();
    public int LoadedActorModels => _levelActorAssets.Count;

    private bool _needsObjectUpload = false;
    private bool _needsVisualMeshUpload = false;
    private bool _needsCollisionMeshUpload = false;
    private bool _needsTextureReload = false;
    private volatile bool _needsLevelAssetsRefresh;
    private readonly object _meshReloadLock = new();
    private VisualMesh? _pendingVisualMesh;
    private CollisionMesh? _pendingCollisionMesh;
    private bool _isLoaded = false;
    private volatile bool _closeRequested;
    public void RequestCloseThreadSafe() => _closeRequested = true;
    private readonly object _objectLock = new object();
    private HashSet<int> _hiddenSubModels = new(); // Track which sub-models are hidden
    private TextureLoader? _textureLoader;
    private string _projectRoot;
    private List<DrawCall> _drawCalls = new();

    // Skeletal animation fields
    private readonly object _animationLock = new();
    private MarioAnimation? _activeAnimation;
    private int _currentFrame = 0;
    private float _frameTimer = 0f;
    public bool UseActorLighting { get; set; }

    private bool _animationPlaying = true;
    private bool _animationPoseDirty;
    private bool _frameActorRequested;
    public bool LoopAnimationPreview { get; set; } = true;
    public float AnimationSpeed { get; set; } = 1;
    public int AnimationFrame { get { lock (_animationLock) return _currentFrame; } }
    public bool AnimationPlaying { get { lock (_animationLock) return _animationPlaying; } }

    public void SetAnimationPlaying(bool playing)
    {
        lock (_animationLock) _animationPlaying = playing;
    }

    public void SeekAnimationFrame(int frame)
    {
        lock (_animationLock)
        {
            _currentFrame = Math.Clamp(frame, 0, Math.Max(0, (_activeAnimation?.FrameCount ?? 1) - 1));
            _frameTimer = 0;
            _animationPoseDirty = true;
        }
    }

    public void SetActiveAnimation(MarioAnimation? anim)
    {
        lock (_animationLock)
        {
            _activeAnimation = anim;
            _currentFrame = anim == null ? 0 : ((anim.Flags & 2) != 0 ? anim.FrameCount - 1 : anim.StartFrame);
            _frameTimer = 0;
            _animationPlaying = true;
            // Apply vertices only on the OpenGL owner thread, including frame zero.
            _animationPoseDirty = true;
            _frameActorRequested = UseActorLighting;
        }
    }
    // Spline path editing fields
    public List<Services.SplinePoint>? EditSplinePoints;
    public string? SplinePathName { get; set; }
    public int SelectedSplinePointIndex = -1;
    public event Action? SplinePointModified;
    private int _splineVao;
    private int _splineVbo;
    private int _splineVertexCount;

    public event Action<int>? ObjectSelected;
    public event Action<Vector3>? TerrainPointPicked;
    public event Action? TerrainPickCanceled;
    public volatile bool TerrainPlacementEnabled;
    public volatile bool MeshEditingEnabled;
    public volatile bool CollisionPaintingEnabled;
    public string? TerrainPlacementExcludedModel;

    public Vector3? GetTerrainPointAtScreenPosition(Vector2 screenPosition)
    {
        if(!_showVisual || _visualMesh==null) return null;
        float x=2*screenPosition.X/Math.Max(1,ClientSize.X)-1,y=1-2*screenPosition.Y/Math.Max(1,ClientSize.Y);
        var inverse=Matrix4.Invert(_view*_projection);
        var near=new Vector4(x,y,-1,1)*inverse; var far=new Vector4(x,y,1,1)*inverse;
        var origin=near.Xyz/near.W; var direction=Vector3.Normalize(far.Xyz/far.W-origin);
        float closest=float.MaxValue;
        void Search(IReadOnlyList<ModelVertex> vertices,IReadOnlyList<ModelTriangle> triangles)
        {
            foreach(var t in triangles)
            {
                if(t.V1<0||t.V2<0||t.V3<0||t.V1>=vertices.Count||t.V2>=vertices.Count||t.V3>=vertices.Count) continue;
                var a=vertices[t.V1]; var b=vertices[t.V2]; var c=vertices[t.V3];
                if(TerrainPlacementExcludedModel!=null && a.SourceArrayName?.StartsWith(TerrainPlacementExcludedModel+"_v",StringComparison.Ordinal)==true) continue;
                if(RayIntersections.RayIntersectsTriangle(origin,direction,new Vector3(a.X,a.Y,a.Z),new Vector3(b.X,b.Y,b.Z),new Vector3(c.X,c.Y,c.Z),out float distance) && distance<closest)
                    closest=distance;
            }
        }
        if(_visualMesh.SubMeshes.Count>0)
        {
            foreach(var mesh in _visualMesh.SubMeshes.Where(m=>m.IsVisible)) Search(mesh.Vertices,mesh.Triangles);
        }
        else Search(_visualMesh.Vertices,_visualMesh.Triangles);
        return closest<float.MaxValue ? origin+direction*closest : null;
    }
    public bool TryPlaceOnTerrainAtScreenPosition(Vector2 position,bool shiftHeld)
    {
        if(!TerrainPlacementEnabled || !shiftHeld) return false;
        var point=GetTerrainPointAtScreenPosition(position);
        if(point.HasValue) TerrainPointPicked?.Invoke(point.Value);
        return true;
    }
    public event Action<int>? CollisionTriangleSelected;
    public event Action<int>? VisualVertexSelected;
    public event Action? CopyRequested;
    public event Action? PasteRequested;
    public event Action? RemoveObjectRequested;
    public event Action? ViewSettingsChanged;

    public float CameraMoveSpeed { get; set; } = 800f;
    public bool LevelNavigationEnabled { get; set; }
    public bool IsPlayingPreview { get; set; } = false;
    public float ObjectMoveSpeed { get; set; } = 500f;
    public float CameraRotationSensitivity { get; set; } = 0.02f;

    private Vector3? _selectedMeshPoint;
    public Vector3? SelectedMeshPoint
    {
        get => _selectedMeshPoint;
        set
        {
            _selectedMeshPoint = value;
            _needsObjectUpload = true;
        }
    }

    public void FocusOnPoint(Vector3 point)
    {
        Vector3 currentOffset = _cameraPosition - _cameraTarget;
        if (currentOffset.LengthSquared < 10)
        {
            currentOffset = new Vector3(0, 1000, 2000);
        }
        else
        {
            currentOffset = Vector3.Normalize(currentOffset) * 1500;
        }

        _cameraTarget = point;
        _cameraPosition = point + currentOffset;

        Vector3 dir = Vector3.Normalize(_cameraPosition - _cameraTarget);
        _cameraDistance = (_cameraTarget - _cameraPosition).Length;
        _cameraYaw = (float)MathHelper.RadiansToDegrees(Math.Atan2(dir.Z, dir.X));
        _cameraPitch = (float)MathHelper.RadiansToDegrees(Math.Asin(dir.Y));
    }

    public GeometryRenderer(GameWindowSettings gameWindowSettings, NativeWindowSettings nativeWindowSettings, string projectRoot)
        : base(gameWindowSettings, nativeWindowSettings)
    {
        _projectRoot = projectRoot;
        _cameraPosition = new Vector3(0, 1000, 3000);
        _cameraTarget = Vector3.Zero;
        
        // Update title with controls
        Title += " | Controls: [WASD] Move | [Right-Click] Rotate | [C] Collision | [V] Visual | [1-9] Hide Sub-Model | [0] Show All | [R] Reset";
    }

    public void LoadMesh(CollisionMesh mesh)
    {
        _collisionMesh = mesh;
        if (_isLoaded) UploadCollisionMeshData();
    }

    public void LoadMeshThreadSafe(CollisionMesh mesh)
    {
        lock (_meshReloadLock) _pendingCollisionMesh = mesh;
    }

    public void LoadVisualMesh(VisualMesh mesh)
    {
        _visualMesh = mesh;
        if (_isLoaded) UploadVisualMeshData();
    }

    public void LoadVisualMeshThreadSafe(VisualMesh mesh)
    {
        lock (_meshReloadLock) _pendingVisualMesh = mesh;
    }

    public void ReloadTexturesThreadSafe()
    {
        _needsTextureReload = true;
    }

    public void SetObjects(List<LevelObject> objects)
    {
        lock (_objectLock)
        {
            _objects = objects;
            _needsObjectUpload = true;
        }
    }

    public void SelectObject(int index)
    {
        lock (_objectLock)
        {
            if (_objects != null && index >= -1 && index < _objects.Count)
            {
                _selectedObjectIndex = index;
                _selectedObject = (index >= 0) ? _objects[index] : null;
                _needsObjectUpload = true;
            }
        }
    }

    public Vector3 GetCameraTarget()
    {
        return _cameraTarget;
    }

    public void SetCameraPositionAndTarget(Vector3 position, Vector3 target)
    {
        if ((position - target).LengthSquared < 0.001f) target = position - Vector3.UnitZ;
        _cameraPosition = position;
        _cameraTarget = target;
        
        Vector3 dir = Vector3.Normalize(position - target);
        _cameraDistance = (target - position).Length;
        _cameraYaw = (float)MathHelper.RadiansToDegrees(Math.Atan2(dir.Z, dir.X));
        _cameraPitch = (float)MathHelper.RadiansToDegrees(Math.Asin(dir.Y));
    }

    public void UpdateObject(int index, LevelObject obj)
    {
        lock (_objectLock)
        {
            if (_objects != null && index >= 0 && index < _objects.Count)
            {
                _objects[index] = obj;
                _needsObjectUpload = true;
            }
        }
    }

    private float _clearR = 0.1f;
    private float _clearG = 0.1f;
    private float _clearB = 0.15f;

    public void SetSkyboxBackground(string? skyboxBin)
    {
        _skyboxName = skyboxBin;
        float r = 0.1f, g = 0.1f, b = 0.15f; // default dark gray-blue
        
        if (skyboxBin != null)
        {
            switch (skyboxBin.ToLower())
            {
                case "water":
                case "clouds":
                case "cloud_floor":
                case "ccm":
                case "wdw":
                    // Sky blue (matches Quad64 clear color)
                    r = 0.4f; g = 0.6f; b = 1.0f;
                    break;
                case "bitfs":
                case "bits":
                    // Dark reddish black
                    r = 0.12f; g = 0.05f; b = 0.05f;
                    break;
                case "ssl":
                    // Sandy yellow
                    r = 0.8f; g = 0.65f; b = 0.5f;
                    break;
                case "bbh":
                case "spooky":
                    // Pitch black
                    r = 0.0f; g = 0.0f; b = 0.0f;
                    break;
            }
        }

        _clearR = r;
        _clearG = g;
        _clearB = b;

        if (_isLoaded)
        {
            GL.ClearColor(r, g, b, 1.0f);
        }
    }

    protected override void OnLoad()
    {
        base.OnLoad();

        GL.ClearColor(_clearR, _clearG, _clearB, 1.0f);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _textureLoader = new TextureLoader(_projectRoot);
        _collisionShaderProgram = ShaderPrograms.CreateCollisionShader();
        _visualShaderProgram = ShaderPrograms.CreateVisualShader();
        _objectShaderProgram = ShaderPrograms.CreateObjectShader();

        UpdateProjection();
        
        // Ensure window has focus for keyboard/mouse input
        _isLoaded = true;
        
        // Upload any data that was loaded before the window was running
        UploadAllData();
        
        Focus();
        Console.WriteLine("3D Viewer window loaded and focused");
    }

    private void UploadAllData()
    {
        if (_collisionMesh != null) UploadCollisionMeshData();
        if (_visualMesh != null) UploadVisualMeshData();
        if (_objects != null) UploadObjectData();
    }

    private void UploadCollisionMeshData()
    {
        if (_collisionMesh == null || _collisionMesh.TriangleCount == 0)
            return;

        List<float> vertices = new List<float>();

        foreach (var tri in _collisionMesh.Triangles)
        {
            if (tri.V1 < _collisionMesh.Vertices.Count && tri.V2 < _collisionMesh.Vertices.Count && tri.V3 < _collisionMesh.Vertices.Count)
            {
                var v1 = _collisionMesh.Vertices[tri.V1];
                var v2 = _collisionMesh.Vertices[tri.V2];
                var v3 = _collisionMesh.Vertices[tri.V3];

                // Line 1: v1 -> v2
                vertices.Add(v1.X); vertices.Add(v1.Y); vertices.Add(v1.Z);
                vertices.Add(v2.X); vertices.Add(v2.Y); vertices.Add(v2.Z);

                // Line 2: v2 -> v3
                vertices.Add(v2.X); vertices.Add(v2.Y); vertices.Add(v2.Z);
                vertices.Add(v3.X); vertices.Add(v3.Y); vertices.Add(v3.Z);

                // Line 3: v3 -> v1
                vertices.Add(v3.X); vertices.Add(v3.Y); vertices.Add(v3.Z);
                vertices.Add(v1.X); vertices.Add(v1.Y); vertices.Add(v1.Z);
            }
        }

        _collisionVertexCount = vertices.Count / 3;

        if (_collisionVao == 0) _collisionVao = GL.GenVertexArray();
        if (_collisionVbo == 0) _collisionVbo = GL.GenBuffer();

        GL.BindVertexArray(_collisionVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _collisionVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GL.BindVertexArray(0);
    }

    private volatile bool _needsSplineUpload;
    public void UploadSplineData() => _needsSplineUpload = true;
    private void UploadSplineDataCore()
    {
        if (EditSplinePoints == null || EditSplinePoints.Count == 0)
        {
            _splineVertexCount = 0;
            return;
        }

        List<float> vertices = new List<float>();
        for (int i = 0; i + 3 < EditSplinePoints.Count; i++)
        {
            if (EditSplinePoints[i].Index == -1 || EditSplinePoints[i+1].Index == -1 || EditSplinePoints[i+2].Index == -1) break;
            for (int step=0;step<16;step++)
                foreach (float u in new[]{step/16f,(step+1)/16f})
                {
                    var p = CameraSplinePlayback.ToWorld(SplinePathName,CameraSplinePlayback.Evaluate(EditSplinePoints,i,u));
                    vertices.Add(p.X); vertices.Add(p.Y); vertices.Add(p.Z);
                    vertices.Add(0); vertices.Add(1); vertices.Add(1);
                }
        }

        _splineVertexCount = vertices.Count / 6;

        if (_splineVao == 0)
        {
            _splineVao = GL.GenVertexArray();
            _splineVbo = GL.GenBuffer();
        }

        GL.BindVertexArray(_splineVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _splineVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

        // Position attribute (location = 0)
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        // Color attribute (location = 1)
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public void UploadObjectData() => _needsObjectUpload = true;
    private void UploadObjectDataCore()
    {
        if ((_objects == null || _objects.Count == 0) && (EditSplinePoints == null || EditSplinePoints.Count == 0) && !SelectedMeshPoint.HasValue)
        {
            _objectVertexCount = 0;
            Console.WriteLine("No objects, splines, or selected mesh point to upload");
            return;
        }

        try
        {
            List<float> vertices = new List<float>();

            if (_objects != null)
            {
                for (int i = 0; i < _objects.Count; i++)
                {
                    var obj = _objects[i];
                    if (obj.IsDeleted) continue;
                    if (!_showObjects && i != _selectedObjectIndex) continue;

                    float cubeSize;
                    bool isSelected = (i == _selectedObjectIndex);
                    Vector3 color;

                    if (isSelected)
                    {
                        cubeSize = VISUAL_OBJECT_SIZE * 1.5f;
                        color = new Vector3(1.0f, 1.0f, 1.0f);
                    }
                    else
                    {
                        cubeSize = VISUAL_OBJECT_SIZE;
                        string modelName = obj.ModelName ?? "";
                        color = modelName.Contains("MODEL_NONE") ? new Vector3(1.0f, 1.0f, 0.0f) :
                                modelName.Contains("BUTTERFLY") ? new Vector3(1.0f, 0.5f, 1.0f) :
                                modelName.Contains("BOBOMB") ? new Vector3(1.0f, 0.0f, 0.0f) :
                                new Vector3(0.0f, 1.0f, 1.0f);
                    }

                    var previewPosition=PreviewPosition(obj);
                    float x = previewPosition.X;
                    float y = previewPosition.Y;
                    float z = previewPosition.Z;
                    float s = cubeSize / 2;

                    Vector3[] cubeVerts = new Vector3[]
                    {
                        new Vector3(-s, -s, -s), new Vector3(s, -s, -s),
                        new Vector3(s, s, -s), new Vector3(-s, s, -s),
                        new Vector3(-s, -s, s), new Vector3(s, -s, s),
                        new Vector3(s, s, s), new Vector3(-s, s, s)
                    };

                    float rad = MathHelper.DegreesToRadians(obj.RY);
                    float sin = (float)Math.Sin(rad);
                    float cos = (float)Math.Cos(rad);

                    for (int j = 0; j < cubeVerts.Length; j++)
                    {
                        float rx = cubeVerts[j].X * cos + cubeVerts[j].Z * sin;
                        float rz = -cubeVerts[j].X * sin + cubeVerts[j].Z * cos;
                        cubeVerts[j].X = rx + x;
                        cubeVerts[j].Z = rz + z;
                        cubeVerts[j].Y += y;
                    }

                    int[][] faces = new int[][]
                    {
                        new int[] {0,1,2}, new int[] {0,2,3}, // Front
                        new int[] {5,4,7}, new int[] {5,7,6}, // Back
                        new int[] {4,0,3}, new int[] {4,3,7}, // Left
                        new int[] {1,5,6}, new int[] {1,6,2}, // Right
                        new int[] {3,2,6}, new int[] {3,6,7}, // Top
                        new int[] {4,5,1}, new int[] {4,1,0}  // Bottom
                    };

                    foreach (var face in faces)
                    {
                        foreach (var idx in face)
                        {
                            vertices.Add(cubeVerts[idx].X);
                            vertices.Add(cubeVerts[idx].Y);
                            vertices.Add(cubeVerts[idx].Z);
                            vertices.Add(color.X);
                            vertices.Add(color.Y);
                            vertices.Add(color.Z);
                        }
                    }
                }
            }

            if (EditSplinePoints != null)
            {
                for (int i = 0; i < EditSplinePoints.Count; i++)
                {
                    var p = EditSplinePoints[i];
                    var world = CameraSplinePlayback.ToWorld(SplinePathName,new Vector3(p.X,p.Y,p.Z));
                    float cubeSize = 150f;
                    bool isSelected = (i == SelectedSplinePointIndex);

                    Vector3 color = isSelected ? new Vector3(1.0f, 1.0f, 0.0f) : new Vector3(0.0f, 0.8f, 0.8f);
                    float s = cubeSize / 2;

                    Vector3[] cubeVerts = new Vector3[]
                    {
                        new Vector3(-s, -s, -s) + world,
                        new Vector3(s, -s, -s) + world,
                        new Vector3(s, s, -s) + world,
                        new Vector3(-s, s, -s) + world,
                        new Vector3(-s, -s, s) + world,
                        new Vector3(s, -s, s) + world,
                        new Vector3(s, s, s) + world,
                        new Vector3(-s, s, s) + world
                    };

                    int[][] faces = new int[][]
                    {
                        new int[] {0,1,2}, new int[] {0,2,3}, // Front
                        new int[] {5,4,7}, new int[] {5,7,6}, // Back
                        new int[] {4,0,3}, new int[] {4,3,7}, // Left
                        new int[] {1,5,6}, new int[] {1,6,2}, // Right
                        new int[] {3,2,6}, new int[] {3,6,7}, // Top
                        new int[] {4,5,1}, new int[] {4,1,0}  // Bottom
                    };

                    foreach (var face in faces)
                    {
                        foreach (var idx in face)
                        {
                            vertices.Add(cubeVerts[idx].X);
                            vertices.Add(cubeVerts[idx].Y);
                            vertices.Add(cubeVerts[idx].Z);
                            vertices.Add(color.X);
                            vertices.Add(color.Y);
                            vertices.Add(color.Z);
                        }
                    }
                }
            }

            if (SelectedMeshPoint.HasValue)
            {
                float indicatorSize = 100f;
                float s = indicatorSize / 2;
                var pt = SelectedMeshPoint.Value;
                Vector3 color = new Vector3(1.0f, 0.2f, 0.0f); // Bright red-orange

                Vector3[] cubeVerts = new Vector3[]
                {
                    new Vector3(-s, -s, -s) + pt,
                    new Vector3(s, -s, -s) + pt,
                    new Vector3(s, s, -s) + pt,
                    new Vector3(-s, s, -s) + pt,
                    new Vector3(-s, -s, s) + pt,
                    new Vector3(s, -s, s) + pt,
                    new Vector3(s, s, s) + pt,
                    new Vector3(-s, s, s) + pt
                };

                int[][] faces = new int[][]
                {
                    new int[] {0,1,2}, new int[] {0,2,3}, // Front
                    new int[] {5,4,7}, new int[] {5,7,6}, // Back
                    new int[] {4,0,3}, new int[] {4,3,7}, // Left
                    new int[] {1,5,6}, new int[] {1,6,2}, // Right
                    new int[] {3,2,6}, new int[] {3,6,7}, // Top
                    new int[] {4,5,1}, new int[] {4,1,0}  // Bottom
                };

                foreach (var face in faces)
                {
                    foreach (var idx in face)
                    {
                        vertices.Add(cubeVerts[idx].X);
                        vertices.Add(cubeVerts[idx].Y);
                        vertices.Add(cubeVerts[idx].Z);
                        vertices.Add(color.X);
                        vertices.Add(color.Y);
                        vertices.Add(color.Z);
                    }
                }
            }

            _objectVertexCount = vertices.Count / 6;

            if (_objectVertexCount == 0)
                return;

            if (_objectVao == 0)
            {
                _objectVao = GL.GenVertexArray();
                _objectVbo = GL.GenBuffer();
            }

            GL.BindVertexArray(_objectVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _objectVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

            // Position attribute (location = 0)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Color attribute (location = 1)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            Console.WriteLine($"Uploaded objects/splines/selected mesh points. Total vertices: {_objectVertexCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading object data: {ex.Message}");
            _objectVertexCount = 0;
        }
    }

    protected override void OnResize(ResizeEventArgs e)
    {
        base.OnResize(e);
        unsafe { GLFW.GetFramebufferSize(WindowPtr, out int width, out int height); GL.Viewport(0, 0, width, height); }
        UpdateProjection(e.Width/(float)Math.Max(1,e.Height));
    }

    protected override void OnRenderFrame(FrameEventArgs args)
    {
        if (_closeRequested) return;
        base.OnRenderFrame(args);

        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        ApplyCutsceneFrame();
        UpdateCamera();

        if (SkyboxEnabled && _textureLoader != null)
            _skyboxRenderer.Draw(_projectRoot, _skyboxName, _textureLoader, _cameraPosition, _cameraTarget,
                ClientSize.X / (float)Math.Max(1, ClientSize.Y));

        Matrix4 mvp = _view * _projection;

        // Render visual mesh (solid)
        if (_showVisual && _visualMesh != null && _visualVertexCount > 0)
        {
            DrawVisual(CaptureVisualBuffers(), Matrix4.Identity, UseActorLighting);
        }

        DrawLevelActors();
        DrawWater();

        // Render collision mesh (wireframe) on top
        if (_showCollision && _collisionMesh != null && _collisionVertexCount > 0)
        {
            GL.UseProgram(_collisionShaderProgram);
            
            int mvpLocation = GL.GetUniformLocation(_collisionShaderProgram, "uMVP");
            GL.UniformMatrix4(mvpLocation, false, ref mvp);

            GL.BindVertexArray(_collisionVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _collisionVertexCount);
            GL.BindVertexArray(0);
        }

        // Render objects (colored cubes)
        if ((_showObjects || _selectedObjectIndex >= 0) && _objectVertexCount > 0)
        {
            GL.UseProgram(_objectShaderProgram);
            
            int mvpLocation = GL.GetUniformLocation(_objectShaderProgram, "uMVP");
            GL.UniformMatrix4(mvpLocation, false, ref mvp);

            GL.BindVertexArray(_objectVao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, _objectVertexCount);
            GL.BindVertexArray(0);
        }

        // Render spline path lines
        if (EditSplinePoints != null && _splineVertexCount > 0)
        {
            GL.UseProgram(_objectShaderProgram);
            
            int mvpLocation = GL.GetUniformLocation(_objectShaderProgram, "uMVP");
            GL.UniformMatrix4(mvpLocation, false, ref mvp);

            GL.BindVertexArray(_splineVao);
            GL.DrawArrays(PrimitiveType.Lines, 0, _splineVertexCount);
            GL.BindVertexArray(0);
        }

        SwapBuffers();
    }

    public void StopPreview()
    {
        // Applied between rendered frames, after any final camera/actor packet.
        Interlocked.Exchange(ref _stopCutsceneRequested, 1);
    }


    public void FrameLevel() => _frameLevelRequested = true;

    private MeshGpu CaptureVisualBuffers() => new()
        { Vao = _visualVao, Vbo = _visualVbo, VertexCount = _visualVertexCount, DrawCalls = _drawCalls };

    private void UploadAuxiliaryMesh(VisualMesh mesh, MeshGpu destination)
    {
        var original = CaptureVisualBuffers();
        var originalMesh = _visualMesh;
        _visualMesh = mesh;
        _visualVao = destination.Vao; _visualVbo = destination.Vbo;
        _drawCalls = destination.DrawCalls;
        try
        {
            UploadVisualMeshData();
            destination.Vao = _visualVao; destination.Vbo = _visualVbo;
            destination.VertexCount = _visualVertexCount; destination.DrawCalls = _drawCalls;
        }
        finally
        {
            _visualMesh = originalMesh;
            _visualVao = original.Vao; _visualVbo = original.Vbo;
            _visualVertexCount = original.VertexCount; _drawCalls = original.DrawCalls;
        }
    }

    private void DrawWater()
    {
        if (!WaterEnabled || _visualMesh?.LevelDirectory == null || _collisionMesh == null) return;
        if (_waterDirty)
        {
            var water = LevelWaterBuilder.Build(_projectRoot, _visualMesh, _collisionMesh);
            _waterGpu ??= new MeshGpu();
            UploadAuxiliaryMesh(water, _waterGpu);
            _waterDirty = false;
        }
        if (_waterGpu?.VertexCount > 0)
        {
            GL.DepthMask(false);
            DrawVisual(_waterGpu, Matrix4.Identity, false, new Vector2((float)_sceneClock.Elapsed.TotalSeconds * 4, 0));
            GL.DepthMask(true);
        }
    }

    private void DrawLevelActors()
    {
        if (!LevelActorsEnabled || _objects == null || _visualMesh?.LevelDirectory == null) return;
        LevelObject[] objects;
        lock (_objectLock) objects = _objects.Where(obj => !obj.IsDeleted && (!ShowIntroActors || obj.Behavior != "bhvCameraLakitu")).ToArray();

        foreach (var obj in objects)
            DrawLevelActor(obj.ModelName, PreviewPosition(obj), new Vector3(obj.RX, obj.RY, obj.RZ), null);
        if (ShowIntroActors)
            foreach (var pose in IntroPreviewActors)
                DrawLevelActor(pose.ModelName, pose.Position, pose.Rotation, pose.AnimationFrame);
    }

    private void DrawLevelActor(string modelName, Vector3 position, Vector3 angles, int? previewFrame)
    {
        if (!_levelActorAssets.TryGetValue(modelName, out var asset))
        {
            (VisualMesh Mesh, MarioAnimation? Animation)? resolved;
            if (ProgressiveActorLoading)
            {
                _levelActorPreloader ??= new LevelActorPreloader(_projectRoot, _visualMesh!.LevelDirectory!);
                if (!_levelActorPreloader.TryGet(modelName, out resolved)) return;
            }
            else
            {
                _synchronousActorCatalog ??= new LevelActorCatalog(_projectRoot, _visualMesh!.LevelDirectory!);
                resolved = _synchronousActorCatalog.Resolve(modelName);
            }
            if (resolved == null) return;
            asset = new LevelActorAsset { Mesh = resolved.Value.Mesh, Animation = resolved.Value.Animation };
            _levelActorAssets[modelName] = asset;
        }
        var rotation = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(angles.X)) *
            Matrix4.CreateRotationY(MathHelper.DegreesToRadians(angles.Y)) * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(angles.Z));
        int frame = asset.Animation == null || !AnimateLevelActors ? 0 : asset.Animation.AdvanceFrame(asset.Animation.StartFrame,
            previewFrame ?? (int)(_sceneClock.Elapsed.TotalSeconds * 30), true);
        if (frame != asset.LastFrame || asset.Mesh.HasBillboards)
        {
            var localFacing = Vector3.TransformVector(_cameraPosition - _cameraTarget, Matrix4.Invert(rotation));
            new AnimationParser().ApplyFrame(asset.Mesh, asset.Animation, frame, localFacing);
            UploadAuxiliaryMesh(asset.Mesh, asset.Gpu);
            asset.LastFrame = frame;
        }
        DrawVisual(asset.Gpu, rotation * Matrix4.CreateTranslation(position), true);
    }


    private void UpdateCamera()
    {
        if (IsPlayingPreview)
        {
            var forward = Vector3.Normalize(_cameraTarget-_cameraPosition);
            _view = Matrix4.LookAt(_cameraPosition, _cameraTarget,Math.Abs(forward.Y)>0.999f ? Vector3.UnitZ : Vector3.UnitY);
            return;
        }

        float radYaw = MathHelper.DegreesToRadians(_cameraYaw);
        float radPitch = MathHelper.DegreesToRadians(_cameraPitch);

        float x = _cameraDistance * MathF.Cos(radPitch) * MathF.Cos(radYaw);
        float y = _cameraDistance * MathF.Sin(radPitch);
        float z = _cameraDistance * MathF.Cos(radPitch) * MathF.Sin(radYaw);

        _cameraPosition = _cameraTarget + new Vector3(x, y, z);
        _view = Matrix4.LookAt(_cameraPosition, _cameraTarget, Vector3.UnitY);
    }

    private int GetClosestVisualTriangle(Vector3 rayOrigin, Vector3 rayDir, out float closestDist)
    {
        closestDist = float.MaxValue;
        int closestTriIndex = -1;

        if (_visualMesh == null || (_visualMesh.Vertices.Count == 0 && _visualMesh.SubMeshes.Count == 0))
            return -1;

        if (_visualMesh.SubMeshes.Count > 0)
        {
            int globalTriIndex = 0;
            for (int s = 0; s < _visualMesh.SubMeshes.Count; s++)
            {
                var subMesh = _visualMesh.SubMeshes[s];
                if (!subMesh.IsVisible) 
                {
                    globalTriIndex += subMesh.Triangles.Count;
                    continue;
                }

                for (int t = 0; t < subMesh.Triangles.Count; t++)
                {
                    var tri = subMesh.Triangles[t];
                    if (tri.V1 >= subMesh.Vertices.Count || tri.V2 >= subMesh.Vertices.Count || tri.V3 >= subMesh.Vertices.Count)
                    {
                        globalTriIndex++;
                        continue;
                    }

                    var v0Raw = subMesh.Vertices[tri.V1];
                    var v1Raw = subMesh.Vertices[tri.V2];
                    var v2Raw = subMesh.Vertices[tri.V3];

                    Vector3 v0 = new Vector3(v0Raw.X, v0Raw.Y, v0Raw.Z);
                    Vector3 v1 = new Vector3(v1Raw.X, v1Raw.Y, v1Raw.Z);
                    Vector3 v2 = new Vector3(v2Raw.X, v2Raw.Y, v2Raw.Z);

                    if (RayIntersections.RayIntersectsTriangle(rayOrigin, rayDir, v0, v1, v2, out float dist))
                    {
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            closestTriIndex = globalTriIndex;
                        }
                    }
                    globalTriIndex++;
                }
            }
        }
        else if (_visualMesh.Triangles.Count > 0)
        {
            for (int i = 0; i < _visualMesh.Triangles.Count; i++)
            {
                var tri = _visualMesh.Triangles[i];
                if (tri.V1 >= _visualMesh.Vertices.Count || tri.V2 >= _visualMesh.Vertices.Count || tri.V3 >= _visualMesh.Vertices.Count)
                    continue;

                var v0Raw = _visualMesh.Vertices[tri.V1];
                var v1Raw = _visualMesh.Vertices[tri.V2];
                var v2Raw = _visualMesh.Vertices[tri.V3];

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
        }

        return closestTriIndex;
    }

    private void UpdateProjection(float? aspect = null)
    {
        _projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(45f),
            aspect ?? ClientSize.X / (float)Math.Max(1, ClientSize.Y),
            10f,
            100000f
        );
    }

        public void ResetCamera()
        {
            if (LevelNavigationEnabled) { FrameLevel(); return; }
            _cameraDistance = 5000f;
            _cameraYaw = 45f;
            _cameraPitch = 30f;
        }
    
        protected override void OnUnload()
        {
            base.OnUnload();
    
            GL.DeleteProgram(_collisionShaderProgram);
            GL.DeleteProgram(_visualShaderProgram);
            GL.DeleteProgram(_objectShaderProgram);
            _skyboxRenderer.Dispose();
            _waterGpu?.Dispose();
            foreach (var asset in _levelActorAssets.Values) asset.Gpu.Dispose();
            _levelActorPreloader?.Dispose();
            _textureLoader?.Dispose();
            
            GL.DeleteVertexArray(_collisionVao);
            GL.DeleteBuffer(_collisionVbo);
            
            GL.DeleteVertexArray(_visualVao);
            GL.DeleteBuffer(_visualVbo);
            
            GL.DeleteVertexArray(_objectVao);
            GL.DeleteBuffer(_objectVbo);
    
            if (_splineVao != 0)
            {
                GL.DeleteVertexArray(_splineVao);
                GL.DeleteBuffer(_splineVbo);
            }
        }
        internal static int CompileShaderProgram(string vertexSource, string fragmentSource) => ShaderPrograms.CompileShaderProgram(vertexSource, fragmentSource);
    private void DrawVisual(MeshGpu data, Matrix4 model, bool brightLighting, Vector2 textureOffset = default) =>
        VisualMeshRenderer.Draw(data, model, brightLighting, textureOffset, _view, _projection, _visualShaderProgram, _cameraPosition, _cameraTarget);
    private void UploadVisualMeshData() => VisualMeshUploader.Upload(_visualMesh, _textureLoader,
        MeshSession?.Scene, meshGpuOrder, ref _visualVao, ref _visualVbo, ref _visualVertexCount, _drawCalls, ref meshGpuData, ref meshPositionsDirty);
}
