using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Models;

public class ModelVertex
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public int S { get; set; }
    public int T { get; set; }
    public string? SourceArrayName { get; set; }
    public int SourceArrayIndex { get; set; }

    public byte NX { get; set; }
    public byte NY { get; set; }
    public byte NZ { get; set; }
    public byte Alpha { get; set; }

    // Skeletal animation support
    public int JointIndex { get; set; } = -1;
    public int RefX { get; set; }
    public int RefY { get; set; }
    public int RefZ { get; set; }
    public int RestX { get; set; }
    public int RestY { get; set; }
    public int RestZ { get; set; }
    public Vector3 ReferenceNormal { get; set; }
    public Vector4 VertexColor { get; set; }
    public GeoBillboard? Billboard { get; set; }
    public Vector3 BillboardPosition { get; set; }

    public ModelVertex(int x, int y, int z, int s, int t, byte nx, byte ny, byte nz, byte alpha)
    {
        X = x;
        Y = y;
        Z = z;
        RefX = x;
        RefY = y;
        RefZ = z;
        RestX = x;
        RestY = y;
        RestZ = z;
        S = s;
        T = t;
        NX = nx;
        NY = ny;
        NZ = nz;
        Alpha = alpha;
        ReferenceNormal = new Vector3(unchecked((sbyte)nx), unchecked((sbyte)ny), unchecked((sbyte)nz)) / 127f;
        VertexColor = new Vector4(nx, ny, nz, alpha) / 255f;
    }

    public void SetNormal(Vector3 normal)
    {
        if (normal.LengthSquared > 0) normal.Normalize();
        NX = unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(normal.X * 127), -127, 127));
        NY = unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(normal.Y * 127), -127, 127));
        NZ = unchecked((byte)(sbyte)Math.Clamp((int)MathF.Round(normal.Z * 127), -127, 127));
    }

    public (float nx, float ny, float nz) GetNormalizedNormal()
    {
        sbyte nx = unchecked((sbyte)NX);
        sbyte ny = unchecked((sbyte)NY);
        sbyte nz = unchecked((sbyte)NZ);

        return (nx / 127.0f, ny / 127.0f, nz / 127.0f);
    }
}
