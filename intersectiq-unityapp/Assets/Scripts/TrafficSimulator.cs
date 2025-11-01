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

    // Internals
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;

    private readonly List<GameObject> spawned = new List<GameObject>();

    [Serializable]
    public class PlacedItem
    {
        public string prefabName;
        public bool isCenter;
        public int x, z;         // top-left grid cell
        public int w = 1, h = 1; // footprint in cells
        public float yaw;        // 0/90/180/270
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
            enabled = false; return;
        }

        surfaceRenderer = gridSurface.GetComponentInChildren<Renderer>();
        if (!surfaceRenderer)
        {
            Debug.LogError("[TrafficSimulator] gridSurface must have a Renderer in its hierarchy.");
            enabled = false; return;
        }

        LoadFromJson(SceneParameters.GetSavedJSON());

        surfaceBounds = surfaceRenderer.bounds;
        surfaceY = surfaceBounds.center.y;
    }

    public void ClearAll()
    {
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i]) Destroy(spawned[i]);
        }
        spawned.Clear();
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

        if (loaded.gridWidth != gridWidth || loaded.gridHeight != gridHeight)
        {
            Debug.LogWarning($"[TrafficSimulator] Save grid {loaded.gridWidth}x{loaded.gridHeight} " +
                             $"!= current {gridWidth}x{gridHeight}. Using current grid for placement.");
        }

        ClearAll();

        foreach (var item in loaded.items)
        {
            SpawnItem(item);
        }
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
        int x0 = Mathf.Clamp(data.x, 0, Mathf.Max(0, gridWidth - w));
        int z0 = Mathf.Clamp(data.z, 0, Mathf.Max(0, gridHeight - h));

        Vector3 pos = FootprintCenterWorld(x0, z0, w, h);
        Quaternion rot = Quaternion.Euler(0f, data.yaw, 0f);

        var go = Instantiate(prefab, pos, rot);

        if (data.isCenter)
        {
            var s = go.transform.localScale;
            go.transform.localScale = new Vector3(s.x * w, s.y, s.z * h);
        }

        spawned.Add(go);
    }

    GameObject ResolvePrefab(PlacedItem data)
    {
        if (data.isCenter)
        {
            if (!centerPrefab)
            {
                Debug.LogWarning("[TrafficSimulator] Center prefab not assigned.");
                return null;
            }
            return centerPrefab;
        }

        if (placeablePrefabs != null)
        {
            foreach (var p in placeablePrefabs)
            {
                if (p && string.Equals(p.name, data.prefabName, StringComparison.Ordinal))
                    return p;
            }
        }
        return null;
    }

    Vector3 FootprintCenterWorld(int cx, int cz, int w, int h)
    {
        var min = surfaceBounds.min;
        float sizeX = surfaceBounds.size.x;
        float sizeZ = surfaceBounds.size.z;

        float cellX = sizeX / gridWidth;
        float cellZ = sizeZ / gridHeight;

        float worldX = min.x + (cx + w * 0.5f) * cellX;
        float worldZ = min.z + (cz + h * 0.5f) * cellZ;

        return new Vector3(worldX, surfaceY + heightOffset, worldZ);
    }
}
