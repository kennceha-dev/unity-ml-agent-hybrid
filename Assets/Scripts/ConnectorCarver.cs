using System;
using System.Collections.Generic;
using UnityEngine;

public class ConnectorCarver : MonoBehaviour
{
    [Header("Refs")]
    public Grid3D grid;
    public RoomSpawner3D roomSrc;
    public Transform buildParent;

    [Header("Prefabs")]
    public GameObject P_FloorCell;
    public GameObject P_RampCell;

    [Header("Ramp Orientation")]
    [Tooltip("Rotation that makes the ramp prefab climb along +Z.")]
    public Vector3 rampModelEuler = new Vector3(-45f, 0f, 0f);

    [Header("Pathing")]
    [Range(0, 60)] public int extraEdgesPercent = 20;

    enum Occ : byte { Empty, Room, Hall, Ramp }
    Occ[,,] occ;

    void Reset()
    {
        grid = FindObjectOfType<Grid3D>();
        roomSrc = FindObjectOfType<RoomSpawner3D>();
    }

    [ContextMenu("Connect Rooms")]
    public void ConnectRooms()
    {
        if (!grid || !roomSrc) { Debug.LogError("Assign Grid3D + RoomSpawner3D"); return; }
        if (!P_FloorCell || !P_RampCell) { Debug.LogError("Assign prefabs"); return; }

        BuildOccupancyFromRooms();

        var rooms = new List<(Vector3Int c, RectInt r, int y)>();
        foreach (var rr in roomSrc.PlacedRooms)
        {
            var r = rr.rect;
            var c = new Vector3Int(r.xMin + r.width / 2, rr.y, r.yMin + r.height / 2);
            rooms.Add((c, r, rr.y));
        }
        if (rooms.Count < 2) { Debug.LogWarning("Need at least 2 rooms"); return; }

        var edges = BuildMST(rooms);
        var parent = buildParent ? buildParent : transform;

        foreach (var (ai, bi) in edges)
        {
            var A = rooms[ai];
            var B = rooms[bi];

            var doorA = PickDoorOutside(A.r, A.y, B.c);
            var doorB = PickDoorOutside(B.r, B.y, A.c);

            // if outside immediately over room, offset left/right
            doorA = OffsetIfBlocked(doorA);
            doorB = OffsetIfBlocked(doorB);

            var path = AStar(doorA.outside, doorB.outside);
            if (path.Count == 0) continue;
            StampPath(path, parent);
        }
    }

    // ---------------- occupancy ----------------
    void BuildOccupancyFromRooms()
    {
        occ = new Occ[grid.sizeX, grid.sizeY, grid.sizeZ];
        foreach (var rr in roomSrc.PlacedRooms)
        {
            var r = rr.rect; int y = rr.y;
            for (int x = r.xMin; x < r.xMax; x++)
            for (int z = r.yMin; z < r.yMax; z++)
                occ[x, y, z] = Occ.Room;
        }
    }

    bool InB(int x, int y, int z) => grid.InBounds(x, y, z);

    // ---------------- graph (MST) ----------------
    List<(int, int)> BuildMST(List<(Vector3Int c, RectInt r, int y)> rooms)
    {
        int n = rooms.Count;
        var used = new bool[n];
        var best = new float[n];
        var parent = new int[n];
        for (int i = 0; i < n; i++) { best[i] = float.PositiveInfinity; parent[i] = -1; }

        used[0] = true;
        for (int j = 1; j < n; j++) { best[j] = Dist2(rooms[0].c, rooms[j].c); parent[j] = 0; }

        var mst = new List<(int, int)>();
        for (int it = 0; it < n - 1; it++)
        {
            int k = -1; float mv = float.PositiveInfinity;
            for (int j = 0; j < n; j++) if (!used[j] && best[j] < mv) { mv = best[j]; k = j; }
            if (k == -1) break;
            used[k] = true;
            mst.Add((parent[k], k));
            for (int j = 0; j < n; j++) if (!used[j])
            {
                float d = Dist2(rooms[k].c, rooms[j].c);
                if (d < best[j]) { best[j] = d; parent[j] = k; }
            }
        }
        return mst;
    }

    static float Dist2(Vector3Int a, Vector3Int b)
    {
        int dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
        return dx * dx + dy * dy + dz * dz;
    }

    // ---------------- doors ----------------
    struct DoorPair { public Vector3Int inside, outside; public Vector2Int normal; }

    DoorPair PickDoorOutside(RectInt r, int y, Vector3Int toward)
    {
        int cx = Mathf.Clamp(toward.x, r.xMin, r.xMax - 1);
        int cz = Mathf.Clamp(toward.z, r.yMin, r.yMax - 1);

        int left = Mathf.Abs(cx - r.xMin);
        int right = Mathf.Abs((r.xMax - 1) - cx);
        int down = Mathf.Abs(cz - r.yMin);
        int up = Mathf.Abs((r.yMax - 1) - cz);
        int m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(down, up));

        Vector2Int n = Vector2Int.zero;
        if (m == left) { cx = r.xMin; n = Vector2Int.left; }
        else if (m == right) { cx = r.xMax - 1; n = Vector2Int.right; }
        else if (m == down) { cz = r.yMin; n = Vector2Int.down; }
        else { cz = r.yMax - 1; n = Vector2Int.up; }

        var inside = new Vector3Int(cx, y, cz);
        var outside = new Vector3Int(cx + n.x, y, cz + n.y);
        return new DoorPair { inside = inside, outside = outside, normal = n };
    }

    DoorPair OffsetIfBlocked(DoorPair d)
    {
        // if the next tile in front of the door is still part of the same room, offset sideways
        var forward = d.normal;
        var ahead = d.outside + new Vector3Int(forward.x, 0, forward.y);
        if (!InB(ahead.x, ahead.y, ahead.z)) return d;
        if (occ[ahead.x, ahead.y, ahead.z] == Occ.Room)
        {
            // try left, then right
            Vector2Int left = new Vector2Int(-forward.y, forward.x);
            Vector2Int right = new Vector2Int(forward.y, -forward.x);
            var leftPos = d.outside + new Vector3Int(left.x, 0, left.y);
            var rightPos = d.outside + new Vector3Int(right.x, 0, right.y);

            if (InB(leftPos.x, leftPos.y, leftPos.z) && occ[leftPos.x, leftPos.y, leftPos.z] == Occ.Empty)
                d.outside = leftPos;
            else if (InB(rightPos.x, rightPos.y, rightPos.z) && occ[rightPos.x, rightPos.y, rightPos.z] == Occ.Empty)
                d.outside = rightPos;
        }
        return d;
    }

    // ---------------- A* pathfinding ----------------
    struct Node { public Vector3Int p; public int g, f; public Vector3Int parent; public bool hasParent; }
    static readonly Vector2Int[] DIR4 = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    List<Vector3Int> AStar(Vector3Int start, Vector3Int goal)
    {
        var open = new List<Node>();
        var map = new Dictionary<Vector3Int, Node>();
        var closed = new HashSet<Vector3Int>();
        Node s = new() { p = start, g = 0, f = Heu(start, goal), hasParent = false };
        open.Add(s); map[start] = s;

        int guard = 0;
        while (open.Count > 0 && guard++ < 40000)
        {
            int bi = 0; for (int i = 1; i < open.Count; i++) if (open[i].f < open[bi].f) bi = i;
            var cur = open[bi]; open.RemoveAt(bi); closed.Add(cur.p);
            if (cur.p == goal) return Reconstruct(cur, map);

            foreach (var d in DIR4)
            {
                var q = new Vector3Int(cur.p.x + d.x, cur.p.y, cur.p.z + d.y);
                if (!InB(q.x, q.y, q.z)) continue;
                if (!PlanarPassable(q, goal)) continue;
                TryRelax(cur, q, 10, goal, open, map, closed);
            }

            foreach (var d in DIR4)
            {
                var up = new Vector3Int(cur.p.x + d.x, cur.p.y + 1, cur.p.z + d.y);
                if (CanPlaceRamp(cur.p, d, up, goal))
                    TryRelax(cur, up, 14, goal, open, map, closed);

                var down = new Vector3Int(cur.p.x + d.x, cur.p.y - 1, cur.p.z + d.y);
                if (CanPlaceRampDown(cur.p, d, down, goal))
                    TryRelax(cur, down, 14, goal, open, map, closed);
            }
        }
        return new List<Vector3Int>();
    }

    void TryRelax(Node cur, Vector3Int q, int stepCost, Vector3Int goal,
                  List<Node> open, Dictionary<Vector3Int, Node> map, HashSet<Vector3Int> closed)
    {
        if (closed.Contains(q)) return;
        int tg = cur.g + stepCost;
        if (map.TryGetValue(q, out var old))
        {
            if (tg < old.g)
            {
                old.g = tg; old.f = tg + Heu(q, goal); old.parent = cur.p; old.hasParent = true;
                map[q] = old;
            }
        }
        else
        {
            var nn = new Node { p = q, g = tg, f = tg + Heu(q, goal), parent = cur.p, hasParent = true };
            open.Add(nn); map[q] = nn;
        }
    }

    static int Heu(Vector3Int a, Vector3Int b)
    {
        int dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y), dz = Mathf.Abs(a.z - b.z);
        return 10 * (dx + dz + dy);
    }

    List<Vector3Int> Reconstruct(Node end, Dictionary<Vector3Int, Node> map)
    {
        var path = new List<Vector3Int>();
        var cur = end;
        while (true)
        {
            path.Add(cur.p);
            if (!cur.hasParent) break;
            cur = map[cur.parent];
        }
        path.Reverse();
        return path;
    }

    bool PlanarPassable(Vector3Int q, Vector3Int goal)
    {
        var t = occ[q.x, q.y, q.z];
        if (t == Occ.Empty) return true;
        return q == goal;
    }

    bool CanPlaceRamp(Vector3Int baseCell, Vector2Int d, Vector3Int landing, Vector3Int goal)
    {
        int x = baseCell.x, y = baseCell.y, z = baseCell.z;
        if (!InB(x, y, z) || !InB(x, y + 1, z) || !InB(landing.x, landing.y, landing.z)) return false;
        if (occ[x, y, z] != Occ.Empty && baseCell != goal) return false;
        if (occ[x, y + 1, z] != Occ.Empty) return false;
        var land = occ[landing.x, landing.y, landing.z];
        if (!(land == Occ.Empty || landing == goal)) return false;
        return true;
    }

    bool CanPlaceRampDown(Vector3Int baseCell, Vector2Int d, Vector3Int landing, Vector3Int goal)
    {
        int x = baseCell.x, y = baseCell.y, z = baseCell.z;
        if (!InB(x, y, z) || !InB(x, y - 1, z) || !InB(landing.x, landing.y, landing.z)) return false;
        if (occ[x, y, z] != Occ.Empty && baseCell != goal) return false;
        if (occ[landing.x, landing.y + 1, landing.z] != Occ.Empty) return false;
        var land = occ[landing.x, landing.y, landing.z];
        if (!(land == Occ.Empty || landing == goal)) return false;
        return true;
    }

    // ---------------- stamping ----------------
    void StampPath(List<Vector3Int> path, Transform parent)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            var a = path[i];
            var b = path[i + 1];
            var step = b - a;

            if (Mathf.Abs(step.x) + Mathf.Abs(step.z) == 1 && Mathf.Abs(step.y) == 1)
            {
                Vector3Int baseCell = (step.y > 0) ? a : b;
                Vector2Int dir = new(Mathf.Clamp(step.x, -1, 1), Mathf.Clamp(step.z, -1, 1));
                PlaceRamp(baseCell, dir, parent);
                Vector3Int landing = (step.y > 0) ? b : a;
                if (occ[landing.x, landing.y, landing.z] == Occ.Empty)
                    PlaceFloor(landing, parent);
                continue;
            }

            if (occ[b.x, b.y, b.z] == Occ.Empty)
                PlaceFloor(b, parent);
        }
    }

    void PlaceFloor(Vector3Int p, Transform parent)
    {
        occ[p.x, p.y, p.z] = Occ.Hall;
        Instantiate(P_FloorCell, grid.GridToWorldCenter(p.x, p.y, p.z), Quaternion.identity, parent);
    }

    void PlaceRamp(Vector3Int baseCell, Vector2Int dir, Transform parent)
    {
        occ[baseCell.x, baseCell.y, baseCell.z] = Occ.Ramp;
        float yaw = DirToYaw(dir);
        Quaternion rot = Quaternion.Euler(rampModelEuler) * Quaternion.Euler(0f, yaw, 0f);
        Instantiate(P_RampCell, grid.GridToWorldCenter(baseCell.x, baseCell.y, baseCell.z), rot, parent);
    }

    static float DirToYaw(Vector2Int d)
    {
        if (d == Vector2Int.up) return 0f;
        if (d == Vector2Int.right) return 90f;
        if (d == Vector2Int.down) return 180f;
        if (d == Vector2Int.left) return 270f;
        return 0f;
    }
}
