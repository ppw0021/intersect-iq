using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlacementMobileManager : MonoBehaviour
{
    [Header("Surface")]
    public Transform gridSurface;

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
    [Tooltip("Min alpha when pulsing (0..1).")]
    public float pulseMinAlpha = 0.20f;
    [Tooltip("Max alpha when pulsing (0..1).")]
    public float pulseMaxAlpha = 0.75f; // ~75% opaque
    [Tooltip("How fast the sine pulse runs.")]
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
    [SerializeField] Button placeCenterButton;          // UI button to start center placement
    [SerializeField] GameObject centerSizePromptPanel;  // Panel with InputField + OK/Cancel
    [SerializeField] InputField centerSizeInput;        // Unity UI InputField (or TMP_InputField with small change)
    [SerializeField] Button centerSizeOkButton;
    [SerializeField] Button centerSizeCancelButton;
    [SerializeField] GameObject centerPrefab;           // Prefab for the intersection center

    // Variables
    private Camera cam;
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;

    private GameObject ghostInstance;
    private GameObject currentPrefab;
    private int currentIndex = -1;

    private bool isPlacing = false;
    private bool[,] occupied; // gridWidth x gridHeight, 1x1 occupancy baseline

    // current ghost state (grid coords + rotation)
    private int gx, gz;        // top-left grid cell of the footprint
    private float yaw;         // 0,90,180,270

    // Ghost visuals/material bookkeeping
    private List<Renderer> ghostRenderers = new List<Renderer>();
    private List<Material[]> ghostValidMats = new List<Material[]>();    // per-renderer material arrays (instances of ghostMaterial)
    private List<Material[]> ghostInvalidMats = new List<Material[]>();  // per-renderer material arrays (instances of invalidMaterial)
    private bool lastValid = false;
    private bool usingInvalidLook = false;

    // Footprint (in cells) for the currently selected ghost
    private int footprintW = 1;
    private int footprintH = 1;

    // Center piece state
    private bool centerPlaced = false;
    private bool currentIsCenter = false;

    // For scaling ghosts relative to their authored size
    private Vector3 ghostBaseScale = Vector3.one;

    void Awake()
    {
        cam = Camera.main;

        if (!gridSurface)
        {
            Debug.LogError("Assign gridSurface.");
            enabled = false;
            return;
        }
        surfaceRenderer = gridSurface.GetComponentInChildren<Renderer>();
        if (!surfaceRenderer)
        {
            Debug.LogError("gridSurface needs a Renderer.");
            enabled = false;
            return;
        }

        surfaceBounds = surfaceRenderer.bounds;
        surfaceY = surfaceBounds.center.y;

        occupied = new bool[gridWidth, gridHeight];

        WireButtons(false);

        // Wire Center UI
        if (placeCenterButton)
            placeCenterButton.onClick.AddListener(OnPlaceCenterClicked);

        if (centerSizeOkButton)
            centerSizeOkButton.onClick.AddListener(OnCenterSizeOk);

        if (centerSizeCancelButton)
            centerSizeCancelButton.onClick.AddListener(() =>
            {
                if (centerSizePromptPanel) centerSizePromptPanel.SetActive(false);
            });

        if (centerSizePromptPanel) centerSizePromptPanel.SetActive(false);
        if (placeCenterButton) placeCenterButton.interactable = !centerPlaced;
    }

    void Update()
    {
        // Pulse only while placing, ghost exists, and the current state is valid
        if (isPlacing && ghostInstance && lastValid && !usingInvalidLook)
        {
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
            float a = Mathf.Lerp(pulseMinAlpha, pulseMaxAlpha, t);
            SetGhostAlpha(a);
        }
    }

    void WireButtons(bool enable)
    {
        // Clear previous listeners
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

    // === PUBLIC ===
    // Select Prefab Function (normal 1x1 items)
    public void SelectPrefabByIndex(int index)
    {
        if (index < 0 || index >= placeablePrefabs.Count)
        {
            CancelPlacement();
            return;
        }

        currentIndex = index;
        currentPrefab = placeablePrefabs[index];
        BeginPlacement(); // normal 1x1
    }

    // === CENTER PIECE FLOW ===
    void OnPlaceCenterClicked()
    {
        if (centerPlaced)
        {
            Debug.Log("Center already placed.");
            return;
        }

        if (centerSizePromptPanel) centerSizePromptPanel.SetActive(true);
        if (centerSizeInput) centerSizeInput.text = "2"; // default
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

    // Begin Placement Function (normal 1x1)
    void BeginPlacement()
    {
        isPlacing = true;

        currentIsCenter = false;
        footprintW = 1;
        footprintH = 1;

        CreateGhost(currentPrefab);

        // Start centered on the grid
        gx = Mathf.Clamp(gridWidth / 2, 0, gridWidth - 1);
        gz = Mathf.Clamp(gridHeight / 2, 0, gridHeight - 1);
        yaw = 0f;

        UpdateGhostTransform();
        ValidateGhost();

        WireButtons(true);
    }

    // Begin placement for center (N x N)
    void BeginPlacementCenter(int n)
    {
        if (centerPlaced) return;

        isPlacing = true;
        currentIsCenter = true;

        footprintW = n;
        footprintH = n;

        currentPrefab = centerPrefab;
        CreateGhost(currentPrefab);

        // Start near middle, clamp so footprint fits
        gx = Mathf.Clamp(gridWidth / 2 - n / 2, 0, Mathf.Max(0, gridWidth - n));
        gz = Mathf.Clamp(gridHeight / 2 - n / 2, 0, Mathf.Max(0, gridHeight - n));
        yaw = 0f;

        UpdateGhostTransform();
        ValidateGhost();

        WireButtons(true);
    }

    // End placement function
    void EndPlacement()
    {
        isPlacing = false;
        currentPrefab = null;
        currentIndex = -1;

        currentIsCenter = false; // reset center mode
        footprintW = 1;
        footprintH = 1;

        DestroyGhost();
        WireButtons(false);
    }

    // Confirm placement function
    void ConfirmPlacement()
    {
        if (!isPlacing || ghostInstance == null) return;
        if (!ValidateGhost()) return;

        // Spawn the real object at the footprint center
        Vector3 spawnPos = FootprintCenterWorld(gx, gz, footprintW, footprintH);
        var real = Instantiate(currentPrefab, spawnPos, Quaternion.Euler(0f, yaw, 0f));

        // Scale the spawned object if it's the center (match ghost logic)
        if (currentIsCenter)
        {
            var s = real.transform.localScale;
            real.transform.localScale = new Vector3(s.x * footprintW, s.y, s.z * footprintH);

            centerPlaced = true;
            if (placeCenterButton) placeCenterButton.interactable = false;
        }

        // Mark all covered cells occupied
        for (int x = 0; x < footprintW; x++)
            for (int z = 0; z < footprintH; z++)
                occupied[gx + x, gz + z] = true;

        EndPlacement();
    }

    void CancelPlacement()
    {
        centerSizePromptPanel?.SetActive(false);
        EndPlacement();
    }

    // Create Ghost function
    void CreateGhost(GameObject src)
    {
        DestroyGhost();
        if (src == null) return;

        ghostInstance = Instantiate(src);
        ghostInstance.name = "[GHOST] " + src.name;

        // Remember base scale
        ghostBaseScale = ghostInstance.transform.localScale;

        // Collect renderers
        ghostRenderers.Clear();
        ghostRenderers.AddRange(ghostInstance.GetComponentsInChildren<Renderer>(true));

        // Disable colliders on the ghost
        foreach (var col in ghostInstance.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        // Build per-renderer valid/invalid material arrays (instances so we can tweak alpha safely)
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
        // Set initial alpha to mid of the pulse to avoid popping
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    // Destroy ghost function
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

    // Update Ghost
    void UpdateGhostTransform()
    {
        if (!ghostInstance) return;

        // Position at center of footprint
        Vector3 centerPos = FootprintCenterWorld(gx, gz, footprintW, footprintH);
        ghostInstance.transform.position = centerPos;
        ghostInstance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Scale: for center piece, stretch to N×N cells; for normal, keep base scale
        if (currentIsCenter)
        {
            // If your prefab is authored to be "1 cell" wide in X/Z already, this simple scale works:
            var s = ghostBaseScale;
            ghostInstance.transform.localScale = new Vector3(s.x * footprintW, s.y, s.z * footprintH);

            // If your authored size isn't "1 cell", you could instead compute world cell size and match exactly:
            // float cellX = surfaceBounds.size.x / gridWidth;
            // float cellZ = surfaceBounds.size.z / gridHeight;
            // ghostInstance.transform.localScale = new Vector3(cellX * footprintW, s.y, cellZ * footprintH);
        }
        else
        {
            ghostInstance.transform.localScale = ghostBaseScale;
        }
    }

    // Rotate Ghost before placement
    void RotateGhost()
    {
        yaw = (yaw + 90f) % 360f;
        UpdateGhostTransform();
        ValidateGhost();
    }

    // Move ghost with button
    void Nudge(int dx, int dz)
    {
        if (!isPlacing) return;

        int maxX = Mathf.Max(0, gridWidth - footprintW);
        int maxZ = Mathf.Max(0, gridHeight - footprintH);

        int nx = Mathf.Clamp(gx + dx, 0, maxX);
        int nz = Mathf.Clamp(gz + dz, 0, maxZ);

        gx = nx; gz = nz;

        UpdateGhostTransform();
        ValidateGhost();
    }

    bool ValidateGhost()
    {
        bool valid = true;

        // In-bounds for top-left + footprint
        valid &= gx >= 0 && gz >= 0 &&
                 gx + footprintW <= gridWidth &&
                 gz + footprintH <= gridHeight;

        // Ensure every covered cell is free
        if (valid)
        {
            for (int x = 0; x < footprintW && valid; x++)
                for (int z = 0; z < footprintH && valid; z++)
                    if (occupied[gx + x, gz + z]) valid = false;
        }

        // Only one center allowed
        if (valid && currentIsCenter && centerPlaced)
            valid = false;

        // Physics overlap (match footprint bounds)
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

        // If state changed, swap look
        if (valid != lastValid)
        {
            if (valid) ApplyValidLook();
            else       ApplyInvalidLook();
            lastValid = valid;
        }

        // Confirm button state
        if (confirmButton) confirmButton.interactable = valid;

        return valid;
    }

    void ApplyValidLook()
    {
        if (!ghostInstance) return;
        for (int i = 0; i < ghostRenderers.Count; i++)
            ghostRenderers[i].materials = ghostValidMats[i];

        usingInvalidLook = false;

        // set to a safe alpha right away; Update() will pulse it
        float a0 = (pulseMinAlpha + pulseMaxAlpha) * 0.5f;
        SetGhostAlpha(a0);
    }

    void ApplyInvalidLook()
    {
        if (!ghostInstance) return;
        for (int i = 0; i < ghostRenderers.Count; i++)
            ghostRenderers[i].materials = ghostInvalidMats[i];

        usingInvalidLook = true;
        // No pulsing while invalid; leave invalid mats’ color as authored
    }

    void SetGhostAlpha(float a)
    {
        if (!ghostInstance || usingInvalidLook) return; // don't change alpha on the invalid/red look
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

    Vector3 CellCenter(int cx, int cz)
    {
        var min = surfaceBounds.min;
        float sizeX = surfaceBounds.size.x;
        float sizeZ = surfaceBounds.size.z;
        float cellX = sizeX / gridWidth;
        float cellZ = sizeZ / gridHeight;

        return new Vector3(
            min.x + cx * cellX + cellX * 0.5f,
            surfaceY,
            min.z + cz * cellZ + cellZ * 0.5f
        );
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
        return new Vector3(worldX, surfaceY, worldZ);
    }

    Vector3 CellHalfExtents()
    {
        // ~1 cell footprint, leave a small margin
        float hx = surfaceBounds.size.x / gridWidth * 0.49f;
        float hz = surfaceBounds.size.z / gridHeight * 0.49f;
        return new Vector3(hx, 0.25f, hz);
    }

    void SetControlsVisible(bool visible)
    {
        if (upButton)       upButton.gameObject.SetActive(visible);
        if (downButton)     downButton.gameObject.SetActive(visible);
        if (leftButton)     leftButton.gameObject.SetActive(visible);
        if (rightButton)    rightButton.gameObject.SetActive(visible);
        if (rotateButton)   rotateButton.gameObject.SetActive(visible);
        if (confirmButton)  confirmButton.gameObject.SetActive(visible);
        if (cancelButton)   cancelButton.gameObject.SetActive(visible);
    }
}
