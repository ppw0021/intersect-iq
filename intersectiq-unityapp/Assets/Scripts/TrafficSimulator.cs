using System;
using System.Collections.Generic;
using UnityEngine;

public class TrafficSimulator : MonoBehaviour
{
    [Header("Surface")]
    [Tooltip("Transform that holds the rendered grid surface (must have a Renderer in hierarchy).")]
    public Transform gridSurface;

    [Header("Grid")]
    [Tooltip("Grid width in cells; must match what the JSON expects (the loader warns if different).")]
    public int gridWidth = 6;

    [Tooltip("Grid height in cells; must match what the JSON expects (the loader warns if different).")]
    public int gridHeight = 6;

    [Header("Placement Height")]
    [Tooltip("Y offset applied to every placement relative to the surface's Y center.")]
    [SerializeField] private float heightOffset = 0f;

    [Header("Prefabs")]
    [Tooltip("Prefab used for the center/intersection. Required to correctly rebuild the 'isCenter' item.")]
    [SerializeField] private GameObject centerPrefab;

    [Tooltip("All placeable prefabs (must match names saved in JSON).")]
    public List<GameObject> placeablePrefabs = new List<GameObject>();

    [Header("Car Spawner (Auto-Fill)")]
    [Tooltip("Prefab to spawn on edge road cells that face the intersection.")]
    [SerializeField] private GameObject carSpawnerPrefab;

    [Tooltip("If true, treat road yaw as cardinal-only when deciding if it faces the intersection.")]
    [SerializeField] private bool snapRoadYawToCardinals = true;

    [Tooltip("Minimum dot(forward, dirToIntersection) needed to count as 'facing towards'. 0 = any inward-ish, 0.5 ≈ within 60°, 0.707 ≈ within 45°.")]
    [Range(-1f, 1f)]
    [SerializeField] private float facingDotThreshold = 0.5f;

    // Internals
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;
    private readonly List<GameObject> spawned = new List<GameObject>();

    private bool[,] roadCells;
    private bool[,] spawnerCells;
    private float[,] roadYawDeg;

    private bool intersectionFound = false;
    private Vector2 intersectionCenterGrid;
    private Vector3 intersectionCenterWorld;

    // Center rectangle (grid coords) for adjacency checks
    private int centerX0, centerZ0, centerW = 1, centerH = 1;

    [Serializable]
    public class PlacedItem
    {
        public string prefabName;
        public bool isCenter;
        public int x, z;
        public int w = 1, h = 1;
        public float yaw;
    }

    [Serializable]
    public class PlacementSave
    {
        public int gridWidth;
        public int gridHeight;
        public float heightOffset;
        public List<PlacedItem> items = new List<PlacedItem>();
    }

    // Exit nodes you can path to after entering the intersection
    [Serializable]
    public struct ExitNode
    {
        public int x, z;              // grid cell
        public Vector3 world;         // world-space waypoint (center of cell, slightly nudged outward)
        public Vector3 forward;       // outward lane forward
        public string side;           // "North"/"South"/"East"/"West"
    }

    private readonly List<ExitNode> exits = new List<ExitNode>();
    public IReadOnlyList<ExitNode> IntersectionExits => exits;

    void Awake()
    {
        if (!gridSurface)
        {
            Debug.LogError("[TrafficSimulator] Assign gridSurface.");
            enabled = false;
            return;
        }

        surfaceRenderer = gridSurface.GetComponentInChildren<Renderer>();
        if (!surfaceRenderer)
        {
            Debug.LogError("[TrafficSimulator] gridSurface must have a Renderer in its hierarchy.");
            enabled = false;
            return;
        }

        surfaceBounds = surfaceRenderer.bounds;
        surfaceY = surfaceBounds.center.y;

        roadCells = new bool[gridWidth, gridHeight];
        spawnerCells = new bool[gridWidth, gridHeight];
        roadYawDeg = new float[gridWidth, gridHeight];

        LoadFromJson(SceneParameters.GetSavedJSON());
    }

    public void ClearAll()
    {
        foreach (var go in spawned)
            if (go) Destroy(go);

        spawned.Clear();
        roadCells = new bool[gridWidth, gridHeight];
        spawnerCells = new bool[gridWidth, gridHeight];
        roadYawDeg = new float[gridWidth, gridHeight];
        intersectionFound = false;
        exits.Clear();
    }

    public void LoadFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("[TrafficSimulator] LoadFromJson: empty JSON.");
            return;
        }

        PlacementSave loaded;
        try
        {
            loaded = JsonUtility.FromJson<PlacementSave>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("[TrafficSimulator] JSON parse failed: " + e.Message);
            return;
        }

        if (loaded == null || loaded.items == null)
        {
            Debug.LogWarning("[TrafficSimulator] LoadFromJson: no items in save.");
            return;
        }

        ClearAll();

        foreach (var item in loaded.items)
        {
            SpawnItem(item);
            if (item.isCenter)
            {
                intersectionFound = true;
                centerX0 = item.x;
                centerZ0 = item.z;
                centerW  = Mathf.Max(1, item.w);
                centerH  = Mathf.Max(1, item.h);

                intersectionCenterGrid  = new Vector2(item.x + item.w * 0.5f, item.z + item.h * 0.5f);
                intersectionCenterWorld = FootprintCenterWorld(item.x, item.z, item.w, item.h);
            }
        }

        if (!intersectionFound)
        {
            Debug.LogWarning("[TrafficSimulator] No intersection found. Using grid midpoint as fallback.");
            centerX0 = Mathf.Max(0, gridWidth  / 2 - 0);
            centerZ0 = Mathf.Max(0, gridHeight / 2 - 0);
            centerW  = 1;
            centerH  = 1;

            intersectionCenterGrid  = new Vector2(gridWidth * 0.5f, gridHeight * 0.5f);
            intersectionCenterWorld = FootprintCenterWorld((int)intersectionCenterGrid.x, (int)intersectionCenterGrid.y, 1, 1);
        }

        // build outward exit targets right after placing
        BuildIntersectionExits();

        AutoPlaceSpawnersOnEdges();
    }

    void SpawnItem(PlacedItem data)
    {
        var prefab = ResolvePrefab(data);
        if (!prefab)
        {
            Debug.LogWarning($"[TrafficSimulator] Prefab '{data.prefabName}' not found. Skipping.");
            return;
        }

        int w = Mathf.Max(1, data.w);
        int h = Mathf.Max(1, data.h);
        int x0 = Mathf.Clamp(data.x, 0, gridWidth - w);
        int z0 = Mathf.Clamp(data.z, 0, gridHeight - h);

        Vector3 pos = FootprintCenterWorld(x0, z0, w, h);
        Quaternion rot = Quaternion.Euler(0f, data.yaw, 0f);
        var go = Instantiate(prefab, pos, rot);

        if (data.isCenter)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * w, s.y, s.z * h);
        }

        if (IsRoadPrefab(prefab))
        {
            for (int dx = 0; dx < w; dx++)
                for (int dz = 0; dz < h; dz++)
                {
                    int gx = Mathf.Clamp(x0 + dx, 0, gridWidth - 1);
                    int gz = Mathf.Clamp(z0 + dz, 0, gridHeight - 1);
                    roadCells[gx, gz] = true;
                    roadYawDeg[gx, gz] = data.yaw;
                }
        }

        spawned.Add(go);
    }

    GameObject ResolvePrefab(PlacedItem data)
    {
        if (data.isCenter)
            return centerPrefab;

        foreach (var p in placeablePrefabs)
            if (p && string.Equals(p.name, data.prefabName, StringComparison.Ordinal))
                return p;

        return null;
    }

    Vector3 FootprintCenterWorld(int cx, int cz, int w, int h)
    {
        var min = surfaceBounds.min;
        float cellX = surfaceBounds.size.x / gridWidth;
        float cellZ = surfaceBounds.size.z / gridHeight;
        float worldX = min.x + (cx + w * 0.5f) * cellX;
        float worldZ = min.z + (cz + h * 0.5f) * cellZ;
        return new Vector3(worldX, surfaceY + heightOffset, worldZ);
    }

    bool IsRoadPrefab(GameObject prefab)
    {
        return prefab && prefab.GetComponent<RequiresCenterConnection>() != null;
    }

    void AutoPlaceSpawnersOnEdges()
    {
        if (!carSpawnerPrefab)
        {
            Debug.LogWarning("[TrafficSimulator] Auto-place skipped: carSpawnerPrefab not assigned.");
            return;
        }

        int placed = 0;
        for (int x = 0; x < gridWidth; x++)
        {
            placed += TryPlaceSpawnerAtEdgeCell(x, 0) ? 1 : 0;
            placed += TryPlaceSpawnerAtEdgeCell(x, gridHeight - 1) ? 1 : 0;
        }
        for (int z = 1; z < gridHeight - 1; z++)
        {
            placed += TryPlaceSpawnerAtEdgeCell(0, z) ? 1 : 0;
            placed += TryPlaceSpawnerAtEdgeCell(gridWidth - 1, z) ? 1 : 0;
        }

        Debug.Log($"[TrafficSimulator] Auto-placed {placed} car spawners on edge roads.");
    }

    bool TryPlaceSpawnerAtEdgeCell(int cx, int cz)
    {
        if (cx < 0 || cz < 0 || cx >= gridWidth || cz >= gridHeight) return false;
        if (!roadCells[cx, cz] || spawnerCells[cx, cz]) return false;

        float yawDeg = roadYawDeg[cx, cz];
        if (snapRoadYawToCardinals) yawDeg = SnapYawToCardinal(yawDeg);

        Vector3 roadFwd = YawToForward(yawDeg);
        Vector3 cellWorld = FootprintCenterWorld(cx, cz, 1, 1);
        Vector3 toIntersection = intersectionCenterWorld - cellWorld;
        toIntersection.y = 0f;

        if (toIntersection.sqrMagnitude < 1e-6f) return false;
        toIntersection.Normalize();

        if (Vector3.Dot(roadFwd, toIntersection) < facingDotThreshold) return false;

        Instantiate(carSpawnerPrefab, cellWorld, Quaternion.Euler(0f, yawDeg, 0f));
        spawnerCells[cx, cz] = true;
        return true;
    }

    // Exit discovery

    private void BuildIntersectionExits()
    {
        exits.Clear();

        // Cell sizes (for nudging the waypoint outward a bit)
        float cellX = surfaceBounds.size.x / gridWidth;
        float cellZ = surfaceBounds.size.z / gridHeight;

        // Helper to register an exit if the cell is a road and points outward
        void TryAddExit(int gx, int gz, Vector3 outward, string sideTag)
        {
            if (gx < 0 || gz < 0 || gx >= gridWidth || gz >= gridHeight) return;
            if (!roadCells[gx, gz]) return;

            float yawDeg = roadYawDeg[gx, gz];
            if (snapRoadYawToCardinals) yawDeg = SnapYawToCardinal(yawDeg);
            Vector3 fwd = YawToForward(yawDeg);

            // Must be pointing (roughly) outward
            if (Vector3.Dot(fwd, outward) < 0.6f) return;

            Vector3 world = FootprintCenterWorld(gx, gz, 1, 1);
            // Nudge the waypoint outward half a cell so cars don't sit on the center seam
            Vector3 nudge = new Vector3(outward.x * (cellX * 0.5f), 0f, outward.z * (cellZ * 0.5f));
            world += nudge;

            exits.Add(new ExitNode
            {
                x = gx, z = gz,
                world = world,
                forward = fwd,
                side = sideTag
            });
        }

        // For each side, look at the ring of cells directly adjacent to the center footprint
        // Left (West): cells at x = centerX0 - 1, z in [centerZ0 .. centerZ0+centerH-1]
        for (int z = centerZ0; z < centerZ0 + centerH; z++)
            TryAddExit(centerX0 - 1, z, new Vector3(-1, 0, 0), "West");

        // Right (East): cells at x = centerX0 + centerW
        for (int z = centerZ0; z < centerZ0 + centerH; z++)
            TryAddExit(centerX0 + centerW, z, new Vector3(1, 0, 0), "East");

        // Bottom (South): cells at z = centerZ0 - 1, x in [centerX0 .. centerX0+centerW-1]
        for (int x = centerX0; x < centerX0 + centerW; x++)
            TryAddExit(x, centerZ0 - 1, new Vector3(0, 0, -1), "South");

        // Top (North): cells at z = centerZ0 + centerH
        for (int x = centerX0; x < centerX0 + centerW; x++)
            TryAddExit(x, centerZ0 + centerH, new Vector3(0, 0, 1), "North");

        Debug.Log($"[TrafficSimulator] Found {exits.Count} intersection exits.");
    }

    /// Pick any exit
    public bool TryGetRandomExit(out ExitNode exit)
    {
        if (exits.Count == 0) { exit = default; return false; }
        int i = UnityEngine.Random.Range(0, exits.Count);
        exit = exits[i];
        return true;
    }

    /// Get nearest exit to a world position
    public ExitNode GetNearestExit(Vector3 fromWorld)
    {
        if (exits.Count == 0) return default;

        float best = float.MaxValue;
        int bestIdx = 0;
        for (int i = 0; i < exits.Count; i++)
        {
            float d = (exits[i].world - fromWorld).sqrMagnitude;
            if (d < best) { best = d; bestIdx = i; }
        }
        return exits[bestIdx];
    }


    static Vector3 YawToForward(float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
    }

    static float SnapYawToCardinal(float yawDeg)
    {
        yawDeg = Mathf.Repeat(yawDeg, 360f);
        float[] card = { 0f, 90f, 180f, 270f };
        float best = 0f, bestDist = float.MaxValue;
        foreach (float c in card)
        {
            float dist = Mathf.Abs(Mathf.DeltaAngle(yawDeg, c));
            if (dist < bestDist) { bestDist = dist; best = c; }
        }
        return best;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualize exits
        if (exits != null)
        {
            foreach (var ex in exits)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(ex.world, 0.15f);
                Gizmos.color = Color.white;
                Gizmos.DrawLine(ex.world, ex.world + ex.forward * 0.8f);
            }
        }

        // Existing gizmos for spawner detection
        Gizmos.color = Color.cyan;
        Vector3 center = intersectionCenterWorld;
        Gizmos.DrawWireSphere(center, 0.2f);
    }
#endif
}
