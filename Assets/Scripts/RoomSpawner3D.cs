using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public struct RoomRect3D { public int y; public RectInt rect; }

public class RoomSpawner3D : MonoBehaviour
{
    [Header("References")]
    public Grid3D grid;
    public Transform buildParent;
    public GameObject P_FloorCell;

    [Header("Proc Controls")]
    public int seed = 12345;
    [Min(1)] public int roomCount = 16;
    public Vector2Int roomSizeMinMax = new(4, 10);
    public int maxPlacementTries = 400;

    [Header("Placement Rules")]
    public int moat = 1;
    public int yMin = 0;
    public int yMax = 5;

    // Per-layer placed rooms (prevents overlap on the same Y layer)
    private readonly Dictionary<int, List<RectInt>> _placedPerY = new();
    private readonly List<RoomRect3D> _cache = new();
    private System.Random _rng;

    public IReadOnlyList<RoomRect3D> PlacedRooms {
        get {
            _cache.Clear();
            foreach (var kv in _placedPerY)
                foreach (var r in kv.Value)
                    _cache.Add(new RoomRect3D { y = kv.Key, rect = r });
            return _cache;
        }
    }

    [ContextMenu("Generate Rooms (3D)")]
    public void GenerateRooms3D()
    {
        if (!grid) { Debug.LogError("Assign Grid3D."); return; }
        if (!P_FloorCell) { Debug.LogError("Assign P_FloorCell."); return; }
        if (yMin < 0 || yMax < yMin) { Debug.LogError("Invalid yMin/yMax."); return; }
        if (grid.sizeY <= yMax) { Debug.LogError($"Grid sizeY={grid.sizeY} too small for yMax={yMax}. Set sizeY >= {yMax+1}."); return; }

        ClearBuilt();
        _placedPerY.Clear();
        _rng = new System.Random(seed);

        int tries = 0;
        while (TotalRoomsPlaced() < roomCount && tries < maxPlacementTries)
        {
            tries++;

            int y = _rng.Next(yMin, yMax + 1);
            int w = _rng.Next(roomSizeMinMax.x, roomSizeMinMax.y + 1);
            int d = _rng.Next(roomSizeMinMax.x, roomSizeMinMax.y + 1);

            int xMin = moat, zMin = moat;
            int xMax = grid.sizeX - moat - w;
            int zMax = grid.sizeZ - moat - d;
            if (xMax < xMin || zMax < zMin) break;

            int x = _rng.Next(xMin, xMax + 1);
            int z = _rng.Next(zMin, zMax + 1);

            var rect = new RectInt(x, z, w, d);
            if (!CanPlaceOnLayer(y, rect)) continue;

            AddRoomOnLayer(y, rect);
            StampRoomFloors(y, rect);
        }

        Debug.Log($"Placed {TotalRoomsPlaced()}/{roomCount} rooms on Y∈[{yMin}..{yMax}].");
    }

    bool CanPlaceOnLayer(int y, RectInt r)
    {
        bool inside = r.xMin >= 0 && r.yMin >= 0 &&
                      r.xMax <= grid.sizeX && r.yMax <= grid.sizeZ;
        if (!inside) return false;

        var expanded = new RectInt(r.xMin - moat, r.yMin - moat, r.width + 2*moat, r.height + 2*moat);

        if (_placedPerY.TryGetValue(y, out var list))
        {
            foreach (var placed in list)
                if (expanded.Overlaps(placed)) return false;
        }
        return true;
    }

    void AddRoomOnLayer(int y, RectInt r)
    {
        if (!_placedPerY.TryGetValue(y, out var list))
        {
            list = new List<RectInt>();
            _placedPerY[y] = list;
        }
        list.Add(r);
    }

    int TotalRoomsPlaced()
    {
        int total = 0;
        foreach (var kv in _placedPerY) total += kv.Value.Count;
        return total;
    }

    void StampRoomFloors(int y, RectInt r)
    {
        var parent = buildParent ? buildParent : transform;
        for (int x = r.xMin; x < r.xMax; x++)
        for (int z = r.yMin; z < r.yMax; z++)
        {
            Vector3 pos = grid.GridToWorldCenter(x, y, z);
            Instantiate(P_FloorCell, pos, Quaternion.identity, parent);
        }
    }

    [ContextMenu("Clear Built")]
    public void ClearBuilt()
    {
        var parent = buildParent ? buildParent : transform;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            if (Application.isEditor) DestroyImmediate(parent.GetChild(i).gameObject);
            else Destroy(parent.GetChild(i).gameObject);
        }
    }
}
