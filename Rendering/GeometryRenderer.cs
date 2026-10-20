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

public struct DrawCall
{
    public int StartIndex;
    public int VertexCount;
    public int TextureId;
    public Vector3? Color;
    public bool BlendTextureWithShade;
    public TextureAddressMode TextureAddressS;
    public TextureAddressMode TextureAddressT;
    public bool LightingEnabled;
    public bool DecalTexture;
    public float Opacity;
}

public class GeometryRenderer : GameWindow
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
    private LevelActorCatalog? _levelActorCatalog;
    private LevelObjectPlacement? _objectPlacement;
    private Vector3 PreviewPosition(LevelObject obj) => (_objectPlacement ??= new LevelObjectPlacement(_projectRoot)).Position(obj,_collisionMesh);
    private readonly Dictionary<string, LevelActorAsset> _levelActorAssets = new();
    public int LoadedActorModels => _levelActorAssets.Count;

    private sealed class MeshGpu
    {
        public int Vao, Vbo, VertexCount;
        public List<DrawCall> DrawCalls = new();
        public void Dispose() { if (Vao != 0) GL.DeleteVertexArray(Vao); if (Vbo != 0) GL.DeleteBuffer(Vbo); }
    }
    private sealed class LevelActorAsset
    {
        public VisualMesh Mesh = null!;
        public MarioAnimation? Animation;
        public MeshGpu Gpu = new();
        public int LastFrame = -1;
    }
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
                if(RayIntersectsTriangle(origin,direction,new Vector3(a.X,a.Y,a.Z),new Vector3(b.X,b.Y,b.Z),new Vector3(c.X,c.Y,c.Z),out float distance) && distance<closest)
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
    public event Action? ViewSettingsChanged;

    public float CameraMoveSpeed { get; set; } = 800f;
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
        _collisionShaderProgram = CreateCollisionShader();
        _visualShaderProgram = CreateVisualShader();
        _objectShaderProgram = CreateObjectShader();

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

    private int CreateCollisionShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            uniform mat4 uMVP;
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            out vec4 FragColor;
            void main()
            {
                FragColor = vec4(0.0, 1.0, 0.0, 1.0); // Green wireframe
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }

    private int CreateVisualShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aNormal;
            layout(location = 2) in vec2 aTexCoord;
            layout(location = 3) in vec4 aColor;
            
            uniform mat4 uMVP;
            uniform mat4 uModel;
            
            out vec3 vNormal;
            out vec3 vPosition;
            out vec2 vTexCoord;
            out vec4 vColor;
            
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
                vNormal = mat3(uModel) * aNormal;
                vPosition = aPosition;
                vTexCoord = aTexCoord;
                vColor = aColor;
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            in vec3 vNormal;
            in vec3 vPosition;
            in vec2 vTexCoord;
            in vec4 vColor;
            
            uniform vec3 uLightDir;
            uniform vec3 uFillLightDir;
            uniform bool uActorLighting;
            uniform sampler2D uTexture;
            uniform bool uUseTexture;
            uniform vec3 uFallbackColor;
            uniform bool uUseFallbackColor;
            uniform bool uBlendTextureWithShade;
            uniform int uTextureAddressS;
            uniform int uTextureAddressT;
            uniform bool uLightingEnabled;
            uniform bool uDecalTexture;
            uniform float uOpacity;
            uniform vec2 uTextureOffset;
            
            out vec4 FragColor;
            
            float addressCoordinate(float value, float texels, int mode)
            {
                if (mode == 2) value = 1.0 - abs(mod(value, 2.0) - 1.0);
                if (mode != 0) value = clamp(value, 0.5 / texels, 1.0 - 0.5 / texels);
                return value;
            }

            void main()
            {
                vec3 norm = normalize(vNormal);
                vec3 lightDir = normalize(uLightDir);
                
                float diff = max(dot(norm, lightDir), 0.0);
                float ambient = 0.3;
                float lighting = ambient + diff * 0.7;
                if (uActorLighting)
                    lighting = min(1.0, 0.55 + diff * 0.35 + max(dot(norm, normalize(uFillLightDir)), 0.0) * 0.2);
                if (!uLightingEnabled || uDecalTexture) lighting = 1.0;
                
                vec4 texColor = uUseFallbackColor ? vec4(uFallbackColor, 1.0) : vec4(0.7, 0.7, 0.8, 1.0);
                if (uUseTexture)
                {
                    vec2 texels = vec2(textureSize(uTexture, 0));
                    vec2 uv = (vTexCoord + vec2(0.5) + uTextureOffset) / texels;
                    uv.x = addressCoordinate(uv.x, texels.x, uTextureAddressS);
                    uv.y = addressCoordinate(uv.y, texels.y, uTextureAddressT);
                    texColor = texture(uTexture, uv);
                    if (!uBlendTextureWithShade && !uDecalTexture)
                        texColor *= uLightingEnabled ? vec4(uFallbackColor, 1.0) : vColor;
                }
                else if (!uLightingEnabled) texColor = vColor;
                if (uUseTexture && uBlendTextureWithShade)
                    texColor = vec4(mix(uFallbackColor, texColor.rgb, texColor.a), 1.0);
                if (texColor.a < 0.1) discard;

                vec3 color = texColor.rgb * lighting;
                FragColor = vec4(color, texColor.a * uOpacity);
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }

    private int CreateObjectShader()
    {
        string vertexShaderSource = @"
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            layout(location = 1) in vec3 aColor;
            
            uniform mat4 uMVP;
            
            out vec3 vColor;
            
            void main()
            {
                gl_Position = uMVP * vec4(aPosition, 1.0);
                vColor = aColor;
            }
        ";

        string fragmentShaderSource = @"
            #version 330 core
            in vec3 vColor;
            
            out vec4 FragColor;
            
            void main()
            {
                FragColor = vec4(vColor, 1.0);
            }
        ";

        return CompileShaderProgram(vertexShaderSource, fragmentShaderSource);
    }

    internal static int CompileShaderProgram(string vertexSource, string fragmentSource)
    {
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);

        GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vertexSuccess);
        if (vertexSuccess == 0)
        {
            string infoLog = GL.GetShaderInfoLog(vertexShader);
            Console.WriteLine($"Vertex Shader Error: {infoLog}");
        }

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);

        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fragmentSuccess);
        if (fragmentSuccess == 0)
        {
            string infoLog = GL.GetShaderInfoLog(fragmentShader);
            Console.WriteLine($"Fragment Shader Error: {infoLog}");
        }

        int shaderProgram = GL.CreateProgram();
        GL.AttachShader(shaderProgram, vertexShader);
        GL.AttachShader(shaderProgram, fragmentShader);
        GL.LinkProgram(shaderProgram);

        GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out int linkSuccess);
        if (linkSuccess == 0)
        {
            string infoLog = GL.GetProgramInfoLog(shaderProgram);
            Console.WriteLine($"Shader Program Link Error: {infoLog}");
        }

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return shaderProgram;
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

    private void UploadVisualMeshData()
    {
        try
        {
            if (_visualMesh == null || (_visualMesh.Triangles.Count == 0 && _visualMesh.SubMeshes.Count == 0))
            {
                _visualVertexCount = 0;
                _drawCalls.Clear();
                return;
            }

            // Group triangles by texture
            var meshTriangles = new List<ModelTriangle>();
            var meshVertices = new List<ModelVertex>();

            if (_visualMesh.SubMeshes.Count > 0)
            {
                foreach (var subMesh in _visualMesh.SubMeshes)
                {
                    if (!subMesh.IsVisible) continue;
                    
                    int vertexOffset = meshVertices.Count;
                    meshVertices.AddRange(subMesh.Vertices);
                    foreach (var tri in subMesh.Triangles)
                    {
                        meshTriangles.Add(new ModelTriangle(
                            tri.V1 + vertexOffset,
                            tri.V2 + vertexOffset,
                            tri.V3 + vertexOffset,
                            tri.TextureName));
                    }
                }
            }
            else
            {
                meshTriangles = _visualMesh.Triangles;
                meshVertices = _visualMesh.Vertices;
            }

            if (meshTriangles.Count == 0)
            {
                _visualVertexCount = 0;
                _drawCalls.Clear();
                return;
            }

            // Sort triangles by texture to minimize state changes
            var sortedTriangles = meshTriangles.OrderBy(t => t.TextureName ?? "").ToList();
            
            List<float> vertices = new List<float>();
            _drawCalls.Clear();

            string? lastTexture = null;
            string? lastShade = null;
            bool lastBlend = false;
            var lastAddressS = TextureAddressMode.Repeat;
            var lastAddressT = TextureAddressMode.Repeat;
            bool lastLighting = true, lastDecal = false;
            float lastOpacity = -1;
            DrawCall? currentDrawCall = null;

            foreach (var tri in sortedTriangles)
            {
                if (tri.V1 >= meshVertices.Count || tri.V2 >= meshVertices.Count || tri.V3 >= meshVertices.Count)
                    continue;

                // Start new draw call if texture changes
                if (tri.TextureName != lastTexture || tri.ShadeLightSymbol != lastShade ||
                    tri.BlendTextureWithShade != lastBlend || tri.TextureAddressS != lastAddressS ||
                    tri.TextureAddressT != lastAddressT || tri.LightingEnabled != lastLighting ||
                    tri.DecalTexture != lastDecal || tri.Opacity != lastOpacity || currentDrawCall == null)
                {
                    if (currentDrawCall != null) _drawCalls.Add(currentDrawCall.Value);

                    int textureId = 0;
                    Vector3? fallbackColor = null;

                    if (tri.TextureName != null)
                    {
                        if (_visualMesh.TexturePaths.TryGetValue(tri.TextureName, out string? relativePath))
                        {
                            textureId = _textureLoader?.LoadTexture(relativePath) ?? 0;
                        }
                        else if (_visualMesh.LightGroupColors.TryGetValue(tri.TextureName, out var col))
                        {
                            fallbackColor = col;
                        }
                    }

                    if (tri.ShadeLightSymbol != null &&
                        _visualMesh.LightGroupColors.TryGetValue(tri.ShadeLightSymbol, out var shadeColor))
                        fallbackColor = shadeColor;

                    currentDrawCall = new DrawCall
                    {
                        StartIndex = vertices.Count / 12,
                        VertexCount = 0,
                        TextureId = textureId,
                        Color = fallbackColor,
                        BlendTextureWithShade = tri.BlendTextureWithShade,
                        TextureAddressS = tri.TextureAddressS, TextureAddressT = tri.TextureAddressT,
                        LightingEnabled = tri.LightingEnabled, DecalTexture = tri.DecalTexture, Opacity = tri.Opacity
                    };
                    lastTexture = tri.TextureName;
                    lastShade = tri.ShadeLightSymbol;
                    lastBlend = tri.BlendTextureWithShade;
                    lastAddressS = tri.TextureAddressS;
                    lastAddressT = tri.TextureAddressT;
                    lastLighting = tri.LightingEnabled;
                    lastDecal = tri.DecalTexture;
                    lastOpacity = tri.Opacity;
                }

                // Add vertices for this triangle
                var vList = new[] { meshVertices[tri.V1], meshVertices[tri.V2], meshVertices[tri.V3] };
                foreach (var v in vList)
                {
                    // Position
                    vertices.Add(v.X); vertices.Add(v.Y); vertices.Add(v.Z);
                    // Normal
                    var n = v.GetNormalizedNormal();
                    vertices.Add(n.nx); vertices.Add(n.ny); vertices.Add(n.nz);
                    // UVs
                    // N64 UVs are signed s10.5 texel coordinates; the shader uses the actual image dimensions.
                    vertices.Add(v.S / 32.0f);
                    vertices.Add(v.T / 32.0f);
                    vertices.Add(v.VertexColor.X); vertices.Add(v.VertexColor.Y);
                    vertices.Add(v.VertexColor.Z); vertices.Add(v.VertexColor.W);
                }
                
                currentDrawCall = new DrawCall 
                { 
                    StartIndex = currentDrawCall.Value.StartIndex,
                    VertexCount = currentDrawCall.Value.VertexCount + 3,
                    TextureId = currentDrawCall.Value.TextureId,
                    Color = currentDrawCall.Value.Color,
                    BlendTextureWithShade = currentDrawCall.Value.BlendTextureWithShade,
                    TextureAddressS = currentDrawCall.Value.TextureAddressS,
                    TextureAddressT = currentDrawCall.Value.TextureAddressT,
                    LightingEnabled = currentDrawCall.Value.LightingEnabled,
                    DecalTexture = currentDrawCall.Value.DecalTexture,
                    Opacity = currentDrawCall.Value.Opacity
                };
            }

            if (currentDrawCall != null) _drawCalls.Add(currentDrawCall.Value);

            _visualVertexCount = vertices.Count / 12; // 12 floats per vertex (position, normal, UV, RGBA)

            if (_visualVertexCount == 0) return;

            // Animation/billboard updates reuse their buffers instead of leaking
            // a new VAO and VBO on every frame.
            if (_visualVao == 0) _visualVao = GL.GenVertexArray();
            if (_visualVbo == 0) _visualVbo = GL.GenBuffer();

            GL.BindVertexArray(_visualVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _visualVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.DynamicDraw);

            // Position attribute (location = 0)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Normal attribute (location = 1)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 12 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // UV attribute (location = 2)
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 12 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, 12 * sizeof(float), 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR in UploadVisualMeshData: {ex.Message}");
            _visualVertexCount = 0;
        }
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
        IsPlayingPreview = false;
        
        // Sync orbital variables to current state so user controls resume smoothly
        Vector3 dir = Vector3.Normalize(_cameraPosition - _cameraTarget);
        _cameraDistance = (_cameraPosition - _cameraTarget).Length;
        _cameraYaw = (float)MathHelper.RadiansToDegrees(Math.Atan2(dir.Z, dir.X));
        _cameraPitch = (float)MathHelper.RadiansToDegrees(Math.Asin(dir.Y));
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
        _levelActorCatalog ??= new LevelActorCatalog(_projectRoot, _visualMesh.LevelDirectory);
        LevelObject[] objects;
        lock (_objectLock) objects = _objects.Where(obj => !obj.IsDeleted).ToArray();
        foreach (var obj in objects)
        {
            if (!_levelActorAssets.TryGetValue(obj.ModelName, out var asset))
            {
                var resolved = _levelActorCatalog.Resolve(obj.ModelName);
                if (resolved == null) continue;
                asset = new LevelActorAsset { Mesh = resolved.Value.Mesh, Animation = resolved.Value.Animation };
                _levelActorAssets[obj.ModelName] = asset;
            }
            var rotation = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(obj.RX)) *
                Matrix4.CreateRotationY(MathHelper.DegreesToRadians(obj.RY)) * Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(obj.RZ));
            int frame = asset.Animation == null || !AnimateLevelActors ? 0 : asset.Animation.AdvanceFrame(asset.Animation.StartFrame,
                (int)(_sceneClock.Elapsed.TotalSeconds * 30), true);
            if (frame != asset.LastFrame || asset.Mesh.HasBillboards)
            {
                var localFacing = Vector3.TransformVector(_cameraPosition - _cameraTarget, Matrix4.Invert(rotation));
                new AnimationParser().ApplyFrame(asset.Mesh, asset.Animation, frame, localFacing);
                UploadAuxiliaryMesh(asset.Mesh, asset.Gpu);
                asset.LastFrame = frame;
            }
            DrawVisual(asset.Gpu, rotation * Matrix4.CreateTranslation(PreviewPosition(obj)), true);
        }
    }

    private void DrawVisual(MeshGpu data, Matrix4 model, bool brightLighting, Vector2 textureOffset = default)
    {
        Matrix4 mvp = model * _view * _projection;
            GL.UseProgram(_visualShaderProgram);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uTexture"), 0);
            
            int mvpLocation = GL.GetUniformLocation(_visualShaderProgram, "uMVP");
            GL.UniformMatrix4(mvpLocation, false, ref mvp);
            GL.UniformMatrix4(GL.GetUniformLocation(_visualShaderProgram, "uModel"), false, ref model);
            GL.Uniform2(GL.GetUniformLocation(_visualShaderProgram, "uTextureOffset"), textureOffset);
            
            int lightDirLocation = GL.GetUniformLocation(_visualShaderProgram, "uLightDir");
            var facing = Vector3.Normalize(_cameraPosition - _cameraTarget);
            var keyLight = brightLighting ? Vector3.Normalize(facing + Vector3.UnitY * 0.8f) : new Vector3(0.5f, 1.0f, 0.3f);
            GL.Uniform3(lightDirLocation, keyLight);
            GL.Uniform3(GL.GetUniformLocation(_visualShaderProgram, "uFillLightDir"), Vector3.Normalize(facing - Vector3.UnitY * 0.25f));
            GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uActorLighting"), brightLighting ? 1 : 0);

            int useTexLocation = GL.GetUniformLocation(_visualShaderProgram, "uUseTexture");

            GL.BindVertexArray(data.Vao);
            
            int useFallbackLocation = GL.GetUniformLocation(_visualShaderProgram, "uUseFallbackColor");
            int fallbackColorLocation = GL.GetUniformLocation(_visualShaderProgram, "uFallbackColor");
            int blendShadeLocation = GL.GetUniformLocation(_visualShaderProgram, "uBlendTextureWithShade");
            int addressSLocation = GL.GetUniformLocation(_visualShaderProgram, "uTextureAddressS");
            int addressTLocation = GL.GetUniformLocation(_visualShaderProgram, "uTextureAddressT");
            int lightingLocation = GL.GetUniformLocation(_visualShaderProgram, "uLightingEnabled");
            int decalLocation = GL.GetUniformLocation(_visualShaderProgram, "uDecalTexture");

            foreach (var drawCall in data.DrawCalls)
            {
                GL.Uniform1(blendShadeLocation, drawCall.BlendTextureWithShade ? 1 : 0);
                GL.Uniform1(addressSLocation, (int)drawCall.TextureAddressS);
                GL.Uniform1(addressTLocation, (int)drawCall.TextureAddressT);
                GL.Uniform1(lightingLocation, drawCall.LightingEnabled ? 1 : 0);
                GL.Uniform1(decalLocation, drawCall.DecalTexture ? 1 : 0);
                GL.Uniform1(GL.GetUniformLocation(_visualShaderProgram, "uOpacity"), drawCall.Opacity);
                if (drawCall.TextureId > 0)
                {
                    GL.Uniform1(useTexLocation, 1);
                    GL.Uniform1(useFallbackLocation, 0);
                    GL.Uniform3(fallbackColorLocation, drawCall.Color ?? Vector3.One);
                    GL.BindTexture(TextureTarget.Texture2D, drawCall.TextureId);
                }
                else
                {
                    GL.Uniform1(useTexLocation, 0);
                    if (drawCall.Color.HasValue)
                    {
                        GL.Uniform1(useFallbackLocation, 1);
                        GL.Uniform3(fallbackColorLocation, drawCall.Color.Value);
                    }
                    else
                    {
                        GL.Uniform1(useFallbackLocation, 0);
                    }
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }

                GL.DrawArrays(PrimitiveType.Triangles, drawCall.StartIndex, drawCall.VertexCount);
            }
            
            GL.BindVertexArray(0);
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

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus(); // Request focus when clicked

        Console.WriteLine($"Mouse button pressed: {e.Button}");

        bool isControlDown = KeyboardState.IsKeyDown(Keys.LeftControl) || KeyboardState.IsKeyDown(Keys.RightControl);
        bool isShiftDown = e.Modifiers.HasFlag(KeyModifiers.Shift) || KeyboardState.IsKeyDown(Keys.LeftShift) || KeyboardState.IsKeyDown(Keys.RightShift);
        if(e.Button==MouseButton.Left && TryPlaceOnTerrainAtScreenPosition(new Vector2(MouseState.X,MouseState.Y),isShiftDown)) return;

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
            int closestColTri = _showCollision ? GetClosestCollisionTriangle(rayOrigin, rayDir, out closestColDist) : -1;

            float closestVisDist = float.MaxValue;
            int closestVisTri = _showVisual ? GetClosestVisualTriangle(rayOrigin, rayDir, out closestVisDist) : -1;

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

            if (markerVisible && RayIntersectsBox(localOrigin, localDirection, Vector3.Zero, halfSize, out float distance))
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
                    if (RayIntersectsTriangle(rayOrigin, rayDir, World(triangle.V1), World(triangle.V2), World(triangle.V3), out float hit) && hit < closestDistance)
                    { closestDistance = hit; closestObjectIndex = i; }
                }
            }
        }

        if (_showVisual && closestObjectIndex >= 0 && GetClosestVisualTriangle(rayOrigin,rayDir,out float terrainDistance) >= 0 && terrainDistance + 1 < closestDistance)
            return -1;
        return closestObjectIndex;
    }

    private bool RayIntersectsBox(Vector3 rayOrigin, Vector3 rayDir, Vector3 boxCenter, float halfSize, out float distance)
    {
        distance = float.MaxValue;

        Vector3 boxMin = boxCenter - new Vector3(halfSize);
        Vector3 boxMax = boxCenter + new Vector3(halfSize);

        float tMin = 0.0f;
        float tMax = float.MaxValue;

        for (int i = 0; i < 3; i++)
        {
            float origin = i == 0 ? rayOrigin.X : (i == 1 ? rayOrigin.Y : rayOrigin.Z);
            float dir = i == 0 ? rayDir.X : (i == 1 ? rayDir.Y : rayDir.Z);
            float min = i == 0 ? boxMin.X : (i == 1 ? boxMin.Y : boxMin.Z);
            float max = i == 0 ? boxMax.X : (i == 1 ? boxMax.Y : boxMax.Z);

            if (MathF.Abs(dir) < 0.0001f)
            {
                if (origin < min || origin > max)
                    return false;
            }
            else
            {
                float t1 = (min - origin) / dir;
                float t2 = (max - origin) / dir;

                if (t1 > t2)
                {
                    float temp = t1;
                    t1 = t2;
                    t2 = temp;
                }

                tMin = MathF.Max(tMin, t1);
                tMax = MathF.Min(tMax, t2);

                if (tMin > tMax)
                    return false;
            }
        }

        distance = tMin;
        return true;
    }

    private bool RayIntersectsTriangle(Vector3 rayOrigin, Vector3 rayDir, Vector3 v0, Vector3 v1, Vector3 v2, out float distance)
    {
        distance = 0f;
        const float epsilon = 0.000001f;

        Vector3 edge1 = v1 - v0;
        Vector3 edge2 = v2 - v0;

        Vector3 h = Vector3.Cross(rayDir, edge2);
        float a = Vector3.Dot(edge1, h);

        if (a > -epsilon && a < epsilon)
            return false; // Ray is parallel to the triangle.

        float f = 1.0f / a;
        Vector3 s = rayOrigin - v0;
        float u = f * Vector3.Dot(s, h);

        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(rayDir, q);

        if (v < 0.0f || u + v > 1.0f)
            return false;

        // At this stage we can compute t to find out where the intersection point is on the line.
        float t = f * Vector3.Dot(edge2, q);

        if (t > epsilon) // ray intersection
        {
            distance = t;
            return true;
        }

        return false;
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

            if (RayIntersectsTriangle(rayOrigin, rayDir, v0, v1, v2, out float dist))
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

                    if (RayIntersectsTriangle(rayOrigin, rayDir, v0, v1, v2, out float dist))
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

                if (RayIntersectsTriangle(rayOrigin, rayDir, v0, v1, v2, out float dist))
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

    protected override void OnUpdateFrame(FrameEventArgs args)
    {
        if (_closeRequested) { Close(); return; }
        base.OnUpdateFrame(args);
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
            if (_activeAnimation != null && KeyboardState.IsKeyPressed(Keys.Space))
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
            _levelActorCatalog = null;
            _needsLevelAssetsRefresh = false;
        }

        if (IsPlayingPreview) return;
        float deltaTime = (float)args.Time;

        // WASD camera movement
        Vector3 forward = Vector3.Normalize(_cameraTarget - _cameraPosition);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));

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

        // Handle camera rotation with RIGHT mouse button
        if (MouseState.IsButtonDown(MouseButton.Right))
        {
            _cameraYaw += MouseState.Delta.X * CameraRotationSensitivity;
            _cameraPitch -= MouseState.Delta.Y * CameraRotationSensitivity;
            _cameraPitch = Math.Clamp(_cameraPitch, -89f, 89f);
        }

        // Handle zoom with mouse wheel
        _cameraDistance -= MouseState.ScrollDelta.Y * 50f;
        _cameraDistance = Math.Clamp(_cameraDistance, 100f, 50000f);

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
        if (KeyboardState.IsKeyPressed(Keys.F1))
        {
            CopyRequested?.Invoke();
        }

        if (KeyboardState.IsKeyPressed(Keys.F2))
        {
            PasteRequested?.Invoke();
        }

        // Object transformation
        if (_selectedObject != null)
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

    public void ResetCamera()
    {
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
}
