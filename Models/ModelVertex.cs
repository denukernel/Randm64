namespace Sm64DecompLevelViewer.Models;

public class ModelVertex
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public int S { get; set; }
    public int T { get; set; }

    public byte NX { get; set; }
    public byte NY { get; set; }
    public byte NZ { get; set; }
    public byte Alpha { get; set; }

    public ModelVertex(int x, int y, int z, int s, int t, byte nx, byte ny, byte nz, byte alpha)
    {
        X = x;
        Y = y;
        Z = z;
        S = s;
        T = t;
        NX = nx;
        NY = ny;
        NZ = nz;
        Alpha = alpha;
    }

    public (float nx, float ny, float nz) GetNormalizedNormal()
    {
        sbyte nx = unchecked((sbyte)NX);
        sbyte ny = unchecked((sbyte)NY);
        sbyte nz = unchecked((sbyte)NZ);

        return (nx / 127.0f, ny / 127.0f, nz / 127.0f);
    }
}
