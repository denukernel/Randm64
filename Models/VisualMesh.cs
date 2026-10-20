using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Models;

public class SubMesh
{
    public List<ModelVertex> Vertices { get; set; } = new();
    public List<ModelTriangle> Triangles { get; set; } = new();
    public string SourceFile { get; set; } = string.Empty;
    public int SubModelNumber { get; set; }
    public bool IsVisible { get; set; } = true;
}

public sealed class GeoBillboard
{
    public int ParentJointIndex { get; set; } = -1;
    public Matrix4 RestWorldMatrix { get; set; } = Matrix4.Identity;
}

public class GeoDisplayListInstance
{
    public string DisplayListName { get; set; } = string.Empty;
    public Matrix4 Transform { get; set; } = Matrix4.Identity;
    public int JointIndex { get; set; } = -1;
    public Matrix4 JointWorldMatrix { get; set; } = Matrix4.Identity;
    public GeoBillboard? Billboard { get; set; }
}

/// <summary>
/// Container for visual mesh data (vertices and triangles with UVs and normals).
/// </summary>
public class VisualMesh
{
    public List<ModelVertex> Vertices { get; set; } = new();
    public List<ModelTriangle> Triangles { get; set; } = new();
    public List<SubMesh> SubMeshes { get; set; } = new(); // Track individual sub-models
    public string AreaName { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public string? MainDisplayListName { get; set; } // The first/main display list name found in the file
    public List<string> DisplayListNames { get; set; } = new(); // All display list names found in the file
    public Dictionary<string, string> TexturePaths { get; set; } = new(); // Mapping of texture symbols to PNG paths
    public Dictionary<string, Vector3> LightGroupColors { get; } = new(); // Mapping of N64 light groups to diffuse colors
    public string? SkyboxBin { get; set; } // The skybox binary name from level.yaml
    public string? LevelDirectory { get; set; }
    public int AreaIndex { get; set; }

    // Skeletal animation support
    public List<GeoNode> Joints { get; set; } = new();
    public Dictionary<string, int> DlToJointIndex { get; set; } = new();
    public List<GeoDisplayListInstance> DisplayListInstances { get; set; } = new();
    public float RootScale { get; set; } = 1.0f;
    public bool HasBillboards => DisplayListInstances.Any(instance => instance.Billboard != null);

    public int VertexCount => Vertices.Count;
    public int TriangleCount => Triangles.Count;

    public override string ToString()
    {
        return $"VisualMesh: {LevelName} - {AreaName} ({VertexCount} vertices, {TriangleCount} triangles)";
    }
}
