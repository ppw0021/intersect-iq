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

    // Variables
    private Camera cam;
    private Renderer surfaceRenderer;
    private Bounds surfaceBounds;
    private float surfaceY;

    private GameObject ghostInstance;
    private GameObject currentPrefab;
    private int currentIndex = -1;

    private bool isPlacing = false;
    private bool[,] occupied; // 1x1 occupancy

    // current ghost state (grid coords + rotation)
    private int gx, gz;
    private float yaw; // 0,90,180,270

    // Ghost visuals/material bookkeeping
    private List<Renderer> ghostRenderers = new List<Renderer>();
    private List<Material[]> ghostValidMats = new List<Material[]>();    // per-renderer material arrays (instances of ghostMaterial)
    private List<Material[]> ghostInvalidMats = new List<Material[]>();  // per-renderer material arrays (instances of invalidMaterial)
    private bool lastValid = false;
    private bool usingInvalidLook = false;

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

    // Select Prefab Function
    public void SelectPrefabByIndex(int index)
    {
        if (index < 0 || index >= placeablePrefabs.Count)
        {
            CancelPlacement();
            return;
        }

        currentIndex = index;
        currentPrefab = placeablePrefabs[index];
        BeginPlacement();
    }

    // Begin Placement Function
    void BeginPlacement()
    {
        isPlacing = true;
        CreateGhost(currentPrefab);

        // Start centered on the grid
        gx = Mathf.Clamp(gridWidth  / 2, 0, gridWidth  - 1);
        gz = Mathf.Clamp(gridHeight / 2, 0, gridHeight - 1);
        yaw = 0f;

        UpdateGhostTransform();
        ValidateGhost();     // sets look + confirm state

        WireButtons(true);
    }

    // End placement function
    void EndPlacement()
    {
        isPlacing = false;
        currentPrefab = null;
        currentIndex = -1;

        DestroyGhost();
        WireButtons(false);
    }

    // Confirm placement function
    void ConfirmPlacement()
    {
        if (!isPlacing || ghostInstance == null) return;
        if (!ValidateGhost()) return;

        Instantiate(currentPrefab, CellCenter(gx, gz), Quaternion.Euler(0f, yaw, 0f));
        occupied[gx, gz] = true;
        EndPlacement();
    }

    void CancelPlacement() => EndPlacement();

    // Create Ghost function
    void CreateGhost(GameObject src)
    {
        DestroyGhost();
        if (src == null) return;

        ghostInstance = Instantiate(src);
        ghostInstance.name = "[GHOST] " + src.name;

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

    // Destory ghost function
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
        ghostInstance.transform.position = CellCenter(gx, gz);
        ghostInstance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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
        int nx = Mathf.Clamp(gx + dx, 0, gridWidth  - 1);
        int nz = Mathf.Clamp(gz + dz, 0, gridHeight - 1);
        gx = nx; gz = nz;

        UpdateGhostTransform();
        ValidateGhost();
    }

    bool ValidateGhost()
    {
        bool valid = true;

        // Ensure in bounds
        valid &= gx >= 0 && gx < gridWidth && gz >= 0 && gz < gridHeight;

        // Ensure spot not taken
        if (valid && occupied[gx, gz]) valid = false;

        // Physics overlap with other placeables (covers off-grid/manual cases)
        if (valid)
        {
            Vector3 pos = CellCenter(gx, gz);
            Vector3 half = CellHalfExtents();          // approximate footprint 1x1 cell
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            Collider[] buf = new Collider[4];
            int hitCount = Physics.OverlapBoxNonAlloc(pos, half, buf, rot, placeableMask);
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

    Vector3 CellHalfExtents()
    {
        // ~1 cell footprint, leave a small margin
        float hx = surfaceBounds.size.x / gridWidth  * 0.49f;
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
