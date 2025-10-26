using UnityEngine;

[ExecuteAlways]
public class Grid3D : MonoBehaviour
{
    [Header("Grid Size (cells)")]
    [Min(1)] public int sizeX = 64;
    [Min(1)] public int sizeY = 6;   // must be >= 6 if you use Y up to 5
    [Min(1)] public int sizeZ = 64;

    [Header("Cell Metrics (meters)")]
    [Min(0.1f)] public float cellSize   = 4f;  // X/Z width per cell
    [Min(0.1f)] public float cellHeight = 3f;  // vertical height per layer

    [Header("World Origin")]
    public Vector3 origin = Vector3.zero;

    [Header("Gizmos")]
    public bool drawBounds      = true;
    public bool drawAllLayers   = true;
    public int  gizmoYLevel     = 0;
    public Color linesColor     = new(1, 1, 1, 0.15f);
    public Color boundsColor    = new(1, 1, 1, 0.10f);

    void OnValidate()
    {
        sizeX = Mathf.Max(1, sizeX);
        sizeY = Mathf.Max(1, sizeY);
        sizeZ = Mathf.Max(1, sizeZ);
        cellSize   = Mathf.Max(0.1f, cellSize);
        cellHeight = Mathf.Max(0.1f, cellHeight);
        gizmoYLevel = Mathf.Clamp(gizmoYLevel, 0, sizeY - 1);
    }

    // -------- WORLD <-> GRID --------
    public Vector3 GridToWorldCenter(int x, int y, int z)
    {
        return origin + new Vector3(
            (x + 0.5f) * cellSize,
            y * cellHeight,
            (z + 0.5f) * cellSize
        );
    }

    public Vector3 GridToWorldFloor(int x, int y, int z)
    {
        return origin + new Vector3(
            x * cellSize,
            y * cellHeight,
            z * cellSize
        );
    }

    public bool WorldToGrid(Vector3 world, out int x, out int y, out int z)
    {
        Vector3 p = world - origin;
        x = Mathf.FloorToInt(p.x / cellSize);
        y = Mathf.FloorToInt(p.y / cellHeight);
        z = Mathf.FloorToInt(p.z / cellSize);
        return InBounds(x, y, z);
    }

    public Vector3 SnapWorldToGrid(Vector3 world, int yLevel)
    {
        WorldToGrid(world, out int x, out _, out int z);
        x = Mathf.Clamp(x, 0, sizeX - 1);
        z = Mathf.Clamp(z, 0, sizeZ - 1);
        yLevel = Mathf.Clamp(yLevel, 0, sizeY - 1);
        return GridToWorldCenter(x, yLevel, z);
    }

    // -------- BOUNDS --------
    public bool InBounds(int x, int y, int z)
    {
        return (uint)x < (uint)sizeX && (uint)y < (uint)sizeY && (uint)z < (uint)sizeZ;
    }

    public void ClampToBounds(ref int x, ref int y, ref int z)
    {
        x = Mathf.Clamp(x, 0, sizeX - 1);
        y = Mathf.Clamp(y, 0, sizeY - 1);
        z = Mathf.Clamp(z, 0, sizeZ - 1);
    }

    public Bounds CellBounds(int x, int y, int z)
    {
        return new Bounds(GridToWorldCenter(x, y, z), new Vector3(cellSize, cellHeight, cellSize));
    }

    public Bounds GridBounds()
    {
        Vector3 size = new(sizeX * cellSize, sizeY * cellHeight, sizeZ * cellSize);
        return new Bounds(origin + 0.5f * size, size);
    }

    // -------- GIZMOS --------
    void OnDrawGizmosSelected()
    {
        Gizmos.color = linesColor;
        if (drawAllLayers)
        {
            for (int y = 0; y < sizeY; y++) DrawLayerLines(y);
        }
        else
        {
            DrawLayerLines(Mathf.Clamp(gizmoYLevel, 0, sizeY - 1));
        }

        if (drawBounds)
        {
            Gizmos.color = boundsColor;
            var gb = GridBounds();
            Gizmos.DrawWireCube(gb.center, gb.size);
        }
    }

    void DrawLayerLines(int y)
    {
        float s = cellSize;
        float yWorld = y * cellHeight;
        Vector3 o = origin + new Vector3(0, yWorld, 0);

        for (int z = 0; z <= sizeZ; z++)
        {
            Vector3 a = o + new Vector3(0, 0, z * s);
            Vector3 b = o + new Vector3(sizeX * s, 0, z * s);
            Gizmos.DrawLine(a, b);
        }
        for (int x = 0; x <= sizeX; x++)
        {
            Vector3 a = o + new Vector3(x * s, 0, 0);
            Vector3 b = o + new Vector3(x * s, 0, sizeZ * s);
            Gizmos.DrawLine(a, b);
        }
    }
}
