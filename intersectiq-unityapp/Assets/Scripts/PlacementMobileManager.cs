using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
// Checks for legacy input system
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif
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

    private Vector3 ghostBaseScale = Vector3.one;

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

    // Begin end cancel
    void BeginPlacement()
    {
        isPlacing = true;
        currentIsCenter = false;
        footprintW = footprintH = 1;

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

        Vector3 spawnPos = FootprintCenterWorld(gx, gz, footprintW, footprintH);
        var real = Instantiate(currentPrefab, spawnPos, Quaternion.Euler(0f, yaw, 0f));

        if (currentIsCenter)
        {
            var s = real.transform.localScale;
            real.transform.localScale = new Vector3(s.x * footprintW, s.y, s.z * footprintH);

            centerPlaced = true;
            if (placeCenterButton) placeCenterButton.interactable = false;
        }

        // Mark cells
        for (int x = 0; x < footprintW; x++)
            for (int z = 0; z < footprintH; z++)
                occupied[gx + x, gz + z] = true;

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

        valid &= gx >= 0 && gz >= 0 &&
                 gx + footprintW <= gridWidth &&
                 gz + footprintH <= gridHeight;

        if (valid)
        {
            for (int x = 0; x < footprintW && valid; x++)
                for (int z = 0; z < footprintH && valid; z++)
                    if (occupied[gx + x, gz + z]) valid = false;
        }

        if (valid && currentIsCenter && centerPlaced)
            valid = false;

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

        if (valid != lastValid)
        {
            if (valid) ApplyValidLook();
            else ApplyInvalidLook();
            lastValid = valid;
        }

        if (confirmButton) confirmButton.interactable = valid;
        return valid;
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
}
