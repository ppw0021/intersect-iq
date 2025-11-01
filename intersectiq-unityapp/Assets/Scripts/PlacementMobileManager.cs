using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
// Checks for legacy input system
// #if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
// using UnityEngine.InputSystem;
// #endif

public class PlacementMobileManager : MonoBehaviour
{
    [Header("Surface")]
    public Transform gridSurface;

    [Header("Placement Height")]
    [SerializeField] private float heightOffset = 0f;

    [Header("Grid")]
    public int gridWidth = 6;
    public int gridHeight = 6;

    [Header("Prefabs & Visuals")]
    public List<GameObject> placeablePrefabs;

    [Tooltip("Material used for the valid ghost look (we'll pulse its alpha).")]
    public Material ghostMaterial;

    [Tooltip("Material used when placement is invalid (e.g., red).")]
    public Material invalidMaterial;

    [Header("Ghost Pulse")]
    public float pulseMinAlpha = 0.20f;
    public float pulseMaxAlpha = 0.75f;
    public float pulseSpeed = 2.0f;

    [Header("Physics / Blocking")]
    [Tooltip("Objects already placed (used to block overlaps).")]
    public LayerMask placeableMask;

    [Header("UI - Control Buttons")]
    public Button upButton;
    public Button downButton;
    public Button leftButton;
    public Button rightButton;
    public Button rotateButton;
    public Button confirmButton;
    public Button cancelButton;

    [Header("Center Piece (N x N)")]
    [SerializeField] Button placeCenterButton;
    [SerializeField] GameObject centerSizePromptPanel;
    [SerializeField] InputField centerSizeInput;
    [SerializeField] Button centerSizeOkButton;
    [SerializeField] Button centerSizeCancelButton;
    [SerializeField] GameObject centerPrefab;

    // Runtime State    
    private Camera cam;
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;

    private GameObject ghostInstance;
    private GameObject currentPrefab;
    private int currentIndex = -1;

    private bool isPlacing = false;
    private bool[,] occupied; // gridWidth x gridHeight

    // Tracks the connected network grown from the center (intersection)
    private bool[,] network; // gridWidth x gridHeight

    // current ghost state (grid coords + rotation)
    private int gx, gz;        // top-left cell for current footprint
    private float yaw;         // 0/90/180/270

    // Ghost visuals/material bookkeeping
    private List<Renderer> ghostRenderers = new List<Renderer>();
    private List<Material[]> ghostValidMats = new List<Material[]>();
    private List<Material[]> ghostInvalidMats = new List<Material[]>();
    private bool lastValid = false;
    private bool usingInvalidLook = false;

    // Footprint (in cells) for the currently selected ghost
    private int footprintW = 1;
    private int footprintH = 1;

    // Center piece state
    private bool centerPlaced = false;
    private bool currentIsCenter = false;

    // Connectivity requirement for current prefab (true for roads)
    private bool currentRequiresConn = false;

    private Vector3 ghostBaseScale = Vector3.one;

    // ================================
    // == Placement Recording (NEW) ==
    // ================================
    [Serializable]
    public class PlacedItem
    {
        public string prefabName;
        public bool isCenter;
        public int x, z;         // top-left grid cell
        public int w = 1, h = 1; // footprint in cells
        public float yaw;        // rotation around Y (0/90/180/270)
        [NonSerialized] public GameObject instance; // runtime only
    }

    // Save file root
    [Serializable]
    public class PlacementSave
    {
        public int gridWidth;
        public int gridHeight;
        public float heightOffset;
        public List<PlacedItem> items = new List<PlacedItem>();
    }

    // Stored placements (in order of placement)
    private readonly List<PlacedItem> placedItems = new List<PlacedItem>();

    // Fast lookup of which item occupies a cell (null if none)
    private PlacedItem[,] cellOwner;

    void Awake()
    {
        cam = Camera.main;

        if (!gridSurface)
        {
            Debug.LogError("Assign gridSurface.");
            enabled = false; return;
        }
        surfaceRenderer = gridSurface.GetComponentInChildren<Renderer>();
        if (!surfaceRenderer)
        {
            Debug.LogError("gridSurface needs a Renderer.");
            enabled = false; return;
        }

        surfaceBounds = surfaceRenderer.bounds;
        surfaceY = surfaceBounds.center.y;

        occupied = new bool[gridWidth, gridHeight];
        network  = new bool[gridWidth, gridHeight];
        cellOwner = new PlacedItem[gridWidth, gridHeight];

        WireButtons(false);

        // Center UI
        if (placeCenterButton) placeCenterButton.onClick.AddListener(OnPlaceCenterClicked);
        if (centerSizeOkButton) centerSizeOkButton.onClick.AddListener(OnCenterSizeOk);
        if (centerSizeCancelButton) centerSizeCancelButton.onClick.AddListener(() =>
        {
            if (centerSizePromptPanel) centerSizePromptPanel.SetActive(false);
        });
        if (centerSizePromptPanel) centerSizePromptPanel.SetActive(false);
        if (placeCenterButton) placeCenterButton.interactable = !centerPlaced;

        // Load from static class
        LoadPlacementsFromJson(SceneParameters.GetSavedJSON());
    }

    void Update()
    {
        // Pulse only while placing, ghost exists, and valid
        if (isPlacing && ghostInstance && lastValid && !usingInvalidLook)
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
            float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);
            SetGhostAlpha(a);
        }
    }

    // Buttons
    void WireButtons(bool enable)
    {
        upButton?.onClick.RemoveAllListeners();
        downButton?.onClick.RemoveAllListeners();
        leftButton?.onClick.RemoveAllListeners();
        rightButton?.onClick.RemoveAllListeners();
        rotateButton?.onClick.RemoveAllListeners();
        confirmButton?.onClick.RemoveAllListeners();
        cancelButton?.onClick.RemoveAllListeners();

        if (!enable) { SetControlsVisible(false); return; }

        upButton?.onClick.AddListener(() => Nudge(0, +1));
        downButton?.onClick.AddListener(() => Nudge(0, -1));
        leftButton?.onClick.AddListener(() => Nudge(-1, 0));
        rightButton?.onClick.AddListener(() => Nudge(+1, 0));
        rotateButton?.onClick.AddListener(RotateGhost);
        confirmButton?.onClick.AddListener(ConfirmPlacement);
        cancelButton?.onClick.AddListener(CancelPlacement);

        SetControlsVisible(true);
    }

    // Placements
    public void SelectPrefabByIndex(int index)
    {
        if (index < 0 || index >= placeablePrefabs.Count)
        {
            CancelPlacement();
            return;
        }

        currentIndex = index;
        currentPrefab = placeablePrefabs[index];
        BeginPlacement(); // 1x1
    }

    // Flow
    void OnPlaceCenterClicked()
    {
        if (centerPlaced) { Debug.Log("Center already placed."); return; }
        if (centerSizePromptPanel) centerSizePromptPanel.SetActive(true);
        if (centerSizeInput) centerSizeInput.text = "2";
    }

    void OnCenterSizeOk()
    {
        if (centerSizeInput == null) return;
        if (!int.TryParse(centerSizeInput.text, out int n)) n = 1;
        n = Mathf.Clamp(n, 1, Mathf.Min(gridWidth, gridHeight));
        centerSizePromptPanel?.SetActive(false);

        if (centerPrefab == null)
        {
            Debug.LogError("Center prefab not assigned.");
            return;
        }
        BeginPlacementCenter(n);
    }

    // Begin / end / cancel
    void BeginPlacement()
    {
        isPlacing = true;
        currentIsCenter = false;
        footprintW = footprintH = 1;

        // Determine if this prefab requires connection to the network (i.e., is a road)
        currentRequiresConn = currentPrefab && currentPrefab.GetComponent<RequiresCenterConnection>() != null;

        CreateGhost(currentPrefab);

        gx = Mathf.Clamp(gridWidth / 2, 0, gridWidth - 1);
        gz = Mathf.Clamp(gridHeight / 2, 0, gridHeight - 1);
        yaw = 0f;

        UpdateGhostTransform();
        ValidateGhost();
        WireButtons(true);
    }

    void BeginPlacementCenter(int n)
    {
        if (centerPlaced) return;

        isPlacing = true;
        currentIsCenter = true;
        footprintW = footprintH = n;

        currentPrefab = centerPrefab;

        // Center never requires a connection (it is the seed)
        currentRequiresConn = false;

        CreateGhost(currentPrefab);

        gx = Mathf.Clamp(gridWidth / 2 - n / 2, 0, Mathf.Max(0, gridWidth - n));
        gz = Mathf.Clamp(gridHeight / 2 - n / 2, 0, Mathf.Max(0, gridHeight - n));
        yaw = 0f;

        UpdateGhostTransform();
        ValidateGhost();
        WireButtons(true);
    }

    void EndPlacement()
    {
        isPlacing = false;
        currentPrefab = null;
        currentIndex = -1;

        currentIsCenter = false;
        currentRequiresConn = false;
        footprintW = footprintH = 1;

        DestroyGhost();
        WireButtons(false);
    }

    void CancelPlacement()
    {
        centerSizePromptPanel?.SetActive(false);
        EndPlacement();
    }

    // Confirm
    void ConfirmPlacement()
    {
        if (!isPlacing || ghostInstance == null) return;
        if (!ValidateGhost()) return;

        // Spawn
        Vector3 spawnPos = FootprintCenterWorld(gx, gz, footprintW, footprintH);
        var real = Instantiate(currentPrefab, spawnPos, Quaternion.Euler(0f, yaw, 0f));

        // Scale center footprint
        bool isCenter = currentIsCenter;
        if (isCenter)
        {
            var s = real.transform.localScale;
            real.transform.localScale = new Vector3(s.x * footprintW, s.y, s.z * footprintH);

            centerPlaced = true;
            if (placeCenterButton) placeCenterButton.interactable = false;
        }

        // Mark cells occupied
        for (int x = 0; x < footprintW; x++)
            for (int z = 0; z < footprintH; z++)
                occupied[gx + x, gz + z] = true;

        // Grow the connected network
        bool addToNetwork = isCenter || currentRequiresConn;
        if (addToNetwork)
        {
            for (int x = 0; x < footprintW; x++)
                for (int z = 0; z < footprintH; z++)
                    network[gx + x, gz + z] = true;
        }

        // Record placement (NEW)
        var rec = new PlacedItem
        {
            prefabName = currentPrefab ? currentPrefab.name : "Unknown",
            isCenter = isCenter,
            x = gx, z = gz,
            w = footprintW, h = footprintH,
            yaw = yaw,
            instance = real
        };
        placedItems.Add(rec);
        for (int x = 0; x < footprintW; x++)
            for (int z = 0; z < footprintH; z++)
                cellOwner[gx + x, gz + z] = rec;

        EndPlacement();
    }

    // Ghost
    void CreateGhost(GameObject src)
    {
        DestroyGhost();
        if (src == null) return;

        ghostInstance = Instantiate(src);
        ghostInstance.name = "[GHOST] " + src.name;

        ghostBaseScale = ghostInstance.transform.localScale;

        // Collect renderers
        ghostRenderers.Clear();
        ghostRenderers.AddRange(ghostInstance.GetComponentsInChildren<Renderer>(true));

        // Disable colliders on the ghost
        foreach (var col in ghostInstance.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Build per-renderer valid/invalid mats
        ghostValidMats.Clear();
        ghostInvalidMats.Clear();
        foreach (var r in ghostRenderers)
        {
            var srcCount = r.sharedMaterials.Length;
            var validArr = new Material[srcCount];
            var invalidArr = new Material[srcCount];

            for (int i = 0; i < srcCount; i++)
            {
                validArr[i] = new Material(ghostMaterial);
                invalidArr[i] = new Material(invalidMaterial);
            }
            ghostValidMats.Add(validArr);
            ghostInvalidMats.Add(invalidArr);
            r.materials = validArr;
        }

        usingInvalidLook = false;
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    void DestroyGhost()
    {
        if (ghostInstance) Destroy(ghostInstance);
        ghostInstance = null;
        ghostRenderers.Clear();
        ghostValidMats.Clear();
        ghostInvalidMats.Clear();
        usingInvalidLook = false;
        lastValid = false;
    }

    void UpdateGhostTransform()
    {
        if (!ghostInstance) return;

        Vector3 centerPos = FootprintCenterWorld(gx, gz, footprintW, footprintH);
        ghostInstance.transform.position = centerPos;
        ghostInstance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        if (currentIsCenter)
        {
            var s = ghostBaseScale;
            ghostInstance.transform.localScale = new Vector3(s.x * footprintW, s.y, s.z * footprintH);
        }
        else
        {
            ghostInstance.transform.localScale = ghostBaseScale;
        }
    }

    void RotateGhost()
    {
        yaw = (yaw + 90f) % 360f;
        UpdateGhostTransform();
        ValidateGhost();
    }

    void Nudge(int dx, int dz)
    {
        if (!isPlacing) return;

        int maxX = Mathf.Max(0, gridWidth - footprintW);
        int maxZ = Mathf.Max(0, gridHeight - footprintH);

        gx = Mathf.Clamp(gx + dx, 0, maxX);
        gz = Mathf.Clamp(gz + dz, 0, maxZ);

        UpdateGhostTransform();
        ValidateGhost();
    }

    bool ValidateGhost()
    {
        bool valid = true;

        // Bounds
        valid &= gx >= 0 && gz >= 0 &&
                 gx + footprintW <= gridWidth &&
                 gz + footprintH <= gridHeight;

        // Not overlapping occupied
        if (valid)
        {
            for (int x = 0; x < footprintW && valid; x++)
                for (int z = 0; z < footprintH && valid; z++)
                    if (occupied[gx + x, gz + z]) valid = false;
        }

        // Only one center placement
        if (valid && currentIsCenter && centerPlaced)
            valid = false;

        // Physics overlap (already placed objects using layer)
        if (valid)
        {
            Vector3 center = FootprintCenterWorld(gx, gz, footprintW, footprintH);
            float halfX = (surfaceBounds.size.x / gridWidth) * footprintW * 0.49f;
            float halfZ = (surfaceBounds.size.z / gridHeight) * footprintH * 0.49f;

            Vector3 half = new Vector3(halfX, 0.25f, halfZ);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            Collider[] buf = new Collider[8];
            int hitCount = Physics.OverlapBoxNonAlloc(center, half, buf, rot, placeableMask);
            if (hitCount > 0) valid = false;
        }

        // Roads must connect to the intersection-grown network
        if (valid && currentRequiresConn)
        {
            // Cannot place roads before the center exists
            if (!centerPlaced) valid = false;
            // Must be edge-adjacent to any network cell (no diagonals)
            else if (!IsAdjacentToNetwork(gx, gz, footprintW, footprintH)) valid = false;
        }

        if (valid != lastValid)
        {
            if (valid) ApplyValidLook();
            else ApplyInvalidLook();
            lastValid = valid;
        }

        if (confirmButton) confirmButton.interactable = valid;
        return valid;
    }

    // 4-neighbour adjacency check (no diagonals) around the footprint
    bool IsAdjacentToNetwork(int x0, int z0, int w, int h)
    {
        for (int x = x0; x < x0 + w; x++)
        {
            for (int z = z0; z < z0 + h; z++)
            {
                // Up
                if (z + 1 < gridHeight && network[x, z + 1]) return true;
                // Down
                if (z - 1 >= 0 && network[x, z - 1]) return true;
                // Right
                if (x + 1 < gridWidth && network[x + 1, z]) return true;
                // Left
                if (x - 1 >= 0 && network[x - 1, z]) return true;
            }
        }
        return false;
    }

    void ApplyValidLook()
    {
        if (!ghostInstance) return;
        for (int i = 0; i < ghostRenderers.Count; i++)
            ghostRenderers[i].materials = ghostValidMats[i];

        usingInvalidLook = false;
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    void ApplyInvalidLook()
    {
        if (!ghostInstance) return;
        for (int i = 0; i < ghostRenderers.Count; i++)
            ghostRenderers[i].materials = ghostInvalidMats[i];

        usingInvalidLook = true;
    }

    void SetGhostAlpha(float a)
    {
        if (!ghostInstance || usingInvalidLook) return;
        foreach (var r in ghostRenderers)
        {
            foreach (var m in r.materials)
            {
                if (m.HasProperty("_BaseColor"))
                {
                    var c = m.GetColor("_BaseColor");
                    c.a = a; m.SetColor("_BaseColor", c);
                }
                else if (m.HasProperty("_Color"))
                {
                    var c = m.GetColor("_Color");
                    c.a = a; m.SetColor("_Color", c);
                }
            }
        }
    }

    // Utility
    Vector3 FootprintCenterWorld(int cx, int cz, int w, int h)
    {
        var min = surfaceBounds.min;
        float sizeX = surfaceBounds.size.x;
        float sizeZ = surfaceBounds.size.z;
        float cellX = sizeX / gridWidth;
        float cellZ = sizeZ / gridHeight;

        float worldX = min.x + (cx + w * 0.5f) * cellX;
        float worldZ = min.z + (cz + h * 0.5f) * cellZ;

        // Apply placement height offset relative to surfaceY
        return new Vector3(worldX, surfaceY + heightOffset, worldZ);
    }

    void SetControlsVisible(bool visible)
    {
        if (upButton) upButton.gameObject.SetActive(visible);
        if (downButton) downButton.gameObject.SetActive(visible);
        if (leftButton) leftButton.gameObject.SetActive(visible);
        if (rightButton) rightButton.gameObject.SetActive(visible);
        if (rotateButton) rotateButton.gameObject.SetActive(visible);
        if (confirmButton) confirmButton.gameObject.SetActive(visible);
        if (cancelButton) cancelButton.gameObject.SetActive(visible);
    }

    // JSON Save / Load – API
    /// Saves current placements to a pretty-printed JSON string.
    /// Includes grid size and heightOffset for reference
    public string SavePlacementsToJson()
    {
        var save = new PlacementSave
        {
            gridWidth = this.gridWidth,
            gridHeight = this.gridHeight,
            heightOffset = this.heightOffset,
            items = new List<PlacedItem>(placedItems.Count)
        };

        // copy items without runtime-only references
        foreach (var it in placedItems)
        {
            save.items.Add(new PlacedItem
            {
                prefabName = it.prefabName,
                isCenter = it.isCenter,
                x = it.x, z = it.z,
                w = it.w, h = it.h,
                yaw = it.yaw
            });
        }

        return JsonUtility.ToJson(save, /*prettyPrint=*/true);
    }

    /// Destroys current placed instances and rebuilds from the given JSON.
    /// JSON must be created by SavePlacementsToJson().
    public void LoadPlacementsFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("LoadPlacementsFromJson: empty JSON. Loading empty");
            return;
        }

        PlacementSave loaded;
        try
        {
            loaded = JsonUtility.FromJson<PlacementSave>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("LoadPlacementsFromJson: parse failed: " + e.Message);
            return;
        }

        if (loaded == null || loaded.items == null)
        {
            Debug.LogWarning("LoadPlacementsFromJson: no items.");
            return;
        }

        // log warning if grid settings differ
        if (loaded.gridWidth != gridWidth || loaded.gridHeight != gridHeight)
        {
            Debug.LogWarning($"Loaded grid size {loaded.gridWidth}x{loaded.gridHeight} does not match current {gridWidth}x{gridHeight}. Items will be placed using current grid.");
        }

        // Rebuild
        ClearAllPlacements();

        // Apply placements without validation (assume the save is authoritative)
        foreach (var data in loaded.items)
        {
            TryPlaceFromData(data);
        }

        // Post: update centerPlaced & UI
        centerPlaced = HasAnyCenter();
        if (placeCenterButton) placeCenterButton.interactable = !centerPlaced;
    }

    // Save Load Functionality

    // Destroys all instantiated items and resets grids/network.
    void ClearAllPlacements()
    {
        // Destroy existing instances
        foreach (var it in placedItems)
        {
            if (it.instance) Destroy(it.instance);
        }
        placedItems.Clear();

        // Reset occupancy/network/owners
        occupied = new bool[gridWidth, gridHeight];
        network  = new bool[gridWidth, gridHeight];
        cellOwner = new PlacedItem[gridWidth, gridHeight];

        // Reset center state
        centerPlaced = false;
    }

    // Place a single item from save data (no validation UI).
    void TryPlaceFromData(PlacedItem data)
    {
        var prefab = ResolvePrefab(data);
        if (!prefab)
        {
            Debug.LogWarning($"Load: Prefab '{data.prefabName}' not found. Skipping.");
            return;
        }

        int w = Mathf.Max(1, data.w);
        int h = Mathf.Max(1, data.h);
        int x0 = Mathf.Clamp(data.x, 0, Mathf.Max(0, gridWidth - w));
        int z0 = Mathf.Clamp(data.z, 0, Mathf.Max(0, gridHeight - h));

        // Compute world position and spawn
        var pos = FootprintCenterWorld(x0, z0, w, h);
        var rot = Quaternion.Euler(0f, data.yaw, 0f);
        var real = Instantiate(prefab, pos, rot);

        // Scale center piece across its footprint
        if (data.isCenter)
        {
            var s = real.transform.localScale;
            real.transform.localScale = new Vector3(s.x * w, s.y, s.z * h);
        }

        // Occupancy
        for (int dx = 0; dx < w; dx++)
            for (int dz = 0; dz < h; dz++)
                occupied[x0 + dx, z0 + dz] = true;

        // Network growth: center OR roads (RequireCenterConnection marker)
        bool requiresConn = (!data.isCenter) && prefab.GetComponent<RequiresCenterConnection>() != null;
        if (data.isCenter || requiresConn)
        {
            for (int dx = 0; dx < w; dx++)
                for (int dz = 0; dz < h; dz++)
                    network[x0 + dx, z0 + dz] = true;
        }

        // Record + ownership grid
        var rec = new PlacedItem
        {
            prefabName = data.prefabName,
            isCenter = data.isCenter,
            x = x0, z = z0,
            w = w, h = h,
            yaw = data.yaw,
            instance = real
        };
        placedItems.Add(rec);

        for (int dx = 0; dx < w; dx++)
            for (int dz = 0; dz < h; dz++)
                cellOwner[x0 + dx, z0 + dz] = rec;
    }

    // Resolve prefab by saved name (checks center first, then list)
    GameObject ResolvePrefab(PlacedItem data)
    {
        if (data.isCenter)
        {
            if (centerPrefab) return centerPrefab;
            return null;
        }

        // Match by name in placeablePrefabs list
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

    bool HasAnyCenter()
    {
        foreach (var it in placedItems) if (it.isCenter) return true;
        return false;
    }
}