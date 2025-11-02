using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

    // CAR SPAWNER PLACEMENT
    [Header("Car Spawner (Placement)")]
    [Tooltip("Prefab that will be placed on top of a single road cell.")]
    [SerializeField] private GameObject carSpawnerPrefab;

    [Tooltip("Material used for the valid ghost look.")]
    [SerializeField] private Material ghostMaterial;

    [Tooltip("Material used for invalid ghost look.")]
    [SerializeField] private Material invalidMaterial;

    [Header("Ghost Pulse")]
    [Range(0f, 1f)] public float pulseMinAlpha = 0.20f;
    [Range(0f, 1f)] public float pulseMaxAlpha = 0.75f;
    public float pulseSpeed = 2.0f;

    [Header("UI - Control Buttons (Optional)")]
    [SerializeField] private Button upButton;
    [SerializeField] private Button downButton;
    [SerializeField] private Button leftButton;
    [SerializeField] private Button rightButton;
    // [SerializeField] private Button rotateButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    // Internals
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;

    private readonly List<GameObject> spawned = new List<GameObject>();

    // Grid flags detected from loaded map
    private bool[,] roadCells;
    private bool[,] spawnerCells;

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

    // CAR SPAWNER PLACEMENT: runtime state
    private bool placingSpawner = false;
    private GameObject spawnerGhost;
    private List<Renderer> ghostRs = new List<Renderer>();
    private List<Material[]> ghostValidMats = new List<Material[]>();
    private List<Material[]> ghostInvalidMats = new List<Material[]>();
    private bool usingInvalidLook = false;
    private bool lastValid = false;

    private int gx, gz;
    private float yaw;

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

        surfaceBounds = surfaceRenderer.bounds;
        surfaceY = surfaceBounds.center.y;

        roadCells = new bool[gridWidth, gridHeight];
        spawnerCells = new bool[gridWidth, gridHeight];

        LoadFromJson(SceneParameters.GetSavedJSON());
        WireButtons(false);

        // --- UI VISIBILITY CONTROL ---
        SetControlButtonsVisible(false); // Hide controls by default
    }

    void Update()
    {
        if (placingSpawner && spawnerGhost && lastValid && !usingInvalidLook)
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);
            SetGhostAlpha(a);
        }
    }

    public void ClearAll()
    {
        foreach (var go in spawned)
        {
            if (go) Destroy(go);
        }
        spawned.Clear();

        roadCells = new bool[gridWidth, gridHeight];
        spawnerCells = new bool[gridWidth, gridHeight];
    }

    public void LoadFromJson(string json)
    {
        Debug.Log($"Placing JSON{json}");
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
            Debug.LogWarning($"[TrafficSimulator] Save grid {loaded.gridWidth}x{loaded.gridHeight} != current {gridWidth}x{gridHeight}. Using current grid for placement.");
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

        if (IsRoadPrefab(prefab))
        {
            for (int dx = 0; dx < w; dx++)
                for (int dz = 0; dz < h; dz++)
                    roadCells[Mathf.Clamp(x0 + dx, 0, gridWidth - 1),
                              Mathf.Clamp(z0 + dz, 0, gridHeight - 1)] = true;
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

    bool IsRoadPrefab(GameObject prefab)
    {
        return prefab && prefab.GetComponent<RequiresCenterConnection>() != null;
    }

    // CAR SPAWNER PLACEMENT UI

    public void StartCarSpawnerPlacement()
    {
        if (carSpawnerPrefab == null)
        {
            Debug.LogError("[TrafficSimulator] CarSpawner prefab not assigned.");
            return;
        }

        placingSpawner = true;
        gx = Mathf.Clamp(gridWidth / 2, 0, gridWidth - 1);
        gz = Mathf.Clamp(gridHeight / 2, 0, gridHeight - 1);
        yaw = 0f;

        CreateGhost(carSpawnerPrefab);
        UpdateGhostTransform();
        ValidateGhost();
        WireButtons(true);

        // --- Show UI controls when placement starts ---
        SetControlButtonsVisible(true);
    }

    void CancelCarSpawnerPlacement()
    {
        WireButtons(false);
        DestroyGhost();
        placingSpawner = false;

        // --- Hide UI controls when done ---
        SetControlButtonsVisible(false);
    }

    void ConfirmCarSpawnerPlacement()
    {
        if (!placingSpawner) return;
        if (!ValidateGhost()) return;

        Vector3 pos = FootprintCenterWorld(gx, gz, 1, 1);
        var go = Instantiate(carSpawnerPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
        spawned.Add(go);

        spawnerCells[gx, gz] = true;

        CancelCarSpawnerPlacement();
    }

    void Nudge(int dx, int dz)
    {
        if (!placingSpawner) return;

        gx = Mathf.Clamp(gx + dx, 0, gridWidth - 1);
        gz = Mathf.Clamp(gz + dz, 0, gridHeight - 1);

        UpdateGhostTransform();
        ValidateGhost();
    }

    // void RotateGhost()
    // {
    //     if (!placingSpawner) return;
    //     yaw = (yaw + 90f) % 360f;
    //     UpdateGhostTransform();
    //     ValidateGhost();
    // }

    // Ghost visuals
    void CreateGhost(GameObject prefab)
    {
        DestroyGhost();
        spawnerGhost = Instantiate(prefab);
        spawnerGhost.name = "[GHOST] CarSpawner";

        foreach (var c in spawnerGhost.GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        ghostRs.Clear();
        spawnerGhost.GetComponentsInChildren(true, ghostRs);

        ghostValidMats.Clear();
        ghostInvalidMats.Clear();
        foreach (var r in ghostRs)
        {
            int n = r.sharedMaterials.Length;
            var v = new Material[n];
            var iv = new Material[n];
            for (int i = 0; i < n; i++)
            {
                v[i] = new Material(ghostMaterial);
                iv[i] = new Material(invalidMaterial);
            }
            ghostValidMats.Add(v);
            ghostInvalidMats.Add(iv);
            r.materials = v;
        }

        usingInvalidLook = false;
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    void DestroyGhost()
    {
        if (spawnerGhost) Destroy(spawnerGhost);
        spawnerGhost = null;

        ghostRs.Clear();
        ghostValidMats.Clear();
        ghostInvalidMats.Clear();
        usingInvalidLook = false;
        lastValid = false;
    }

    void UpdateGhostTransform()
    {
        if (!spawnerGhost) return;
        spawnerGhost.transform.position = FootprintCenterWorld(gx, gz, 1, 1);
        spawnerGhost.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void ApplyValidLook()
    {
        if (!spawnerGhost) return;
        for (int i = 0; i < ghostRs.Count; i++)
            ghostRs[i].materials = ghostValidMats[i];

        usingInvalidLook = false;
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    void ApplyInvalidLook()
    {
        if (!spawnerGhost) return;
        for (int i = 0; i < ghostRs.Count; i++)
            ghostRs[i].materials = ghostInvalidMats[i];

        usingInvalidLook = true;
    }

    void SetGhostAlpha(float a)
    {
        if (!spawnerGhost || usingInvalidLook) return;
        foreach (var r in ghostRs)
        {
            foreach (var m in r.materials)
            {
                if (!m) continue;
                if (m.HasProperty("_BaseColor"))
                {
                    var c = m.GetColor("_BaseColor"); c.a = a; m.SetColor("_BaseColor", c);
                }
                else if (m.HasProperty("_Color"))
                {
                    var c = m.GetColor("_Color"); c.a = a; m.SetColor("_Color", c);
                }
            }
        }
    }

    bool ValidateGhost()
    {
        bool valid = true;
        valid &= gx >= 0 && gz >= 0 && gx < gridWidth && gz < gridHeight;

        if (valid && !roadCells[gx, gz]) valid = false;
        if (valid && spawnerCells[gx, gz]) valid = false;

        if (valid != lastValid)
        {
            if (valid) ApplyValidLook();
            else ApplyInvalidLook();
            lastValid = valid;
        }

        if (confirmButton) confirmButton.interactable = valid;
        return valid;
    }

    void WireButtons(bool enable)
    {
        upButton?.onClick.RemoveAllListeners();
        downButton?.onClick.RemoveAllListeners();
        leftButton?.onClick.RemoveAllListeners();
        rightButton?.onClick.RemoveAllListeners();
        // rotateButton?.onClick.RemoveAllListeners();
        confirmButton?.onClick.RemoveAllListeners();
        cancelButton?.onClick.RemoveAllListeners();

        if (!enable) return;

        upButton?.onClick.AddListener(() => Nudge(0, +1));
        downButton?.onClick.AddListener(() => Nudge(0, -1));
        leftButton?.onClick.AddListener(() => Nudge(-1, 0));
        rightButton?.onClick.AddListener(() => Nudge(+1, 0));
        // rotateButton?.onClick.AddListener(RotateGhost);
        confirmButton?.onClick.AddListener(ConfirmCarSpawnerPlacement);
        cancelButton?.onClick.AddListener(CancelCarSpawnerPlacement);
    }

    // --- UI VISIBILITY CONTROL ---
    void SetControlButtonsVisible(bool visible)
    {
        if (upButton) upButton.gameObject.SetActive(visible);
        if (downButton) downButton.gameObject.SetActive(visible);
        if (leftButton) leftButton.gameObject.SetActive(visible);
        if (rightButton) rightButton.gameObject.SetActive(visible);
        // if (rotateButton) rotateButton.gameObject.SetActive(visible);
        if (confirmButton) confirmButton.gameObject.SetActive(visible);
        if (cancelButton) cancelButton.gameObject.SetActive(visible);
    }
}
