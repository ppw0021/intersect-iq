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
                intersectionCenterGrid = new Vector2(item.x + item.w * 0.5f, item.z + item.h * 0.5f);
                intersectionCenterWorld = FootprintCenterWorld(item.x, item.z, item.w, item.h);
            }
        }

        if (!intersectionFound)
        {
            Debug.LogWarning("[TrafficSimulator] No intersection found. Using grid midpoint as fallback.");
            intersectionCenterGrid = new Vector2(gridWidth * 0.5f, gridHeight * 0.5f);
            intersectionCenterWorld = FootprintCenterWorld((int)intersectionCenterGrid.x, (int)intersectionCenterGrid.y, 1, 1);
        }

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
}
