using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class IntersectionEditorNavigator : MonoBehaviour
{
    private MapDataFetcher fetcher;
    [SerializeField] GameObject homePanel;
    [SerializeField] Button homePanel_mainMenuButton;

    // Show hide Overlay
    [SerializeField] Button homePanel_toggleOverLayButton;
    [SerializeField] GameObject mapQuad;
    private MeshRenderer mapRenderer;
    [SerializeField] Button homePanel_rotateTexClockwise;
    [SerializeField] Button homePanel_rotateTexCounterClockwise;

    [SerializeField] Button homePanel_trafficSimButton;
    [SerializeField] PlacementMobileManager placementMobileManager;


    void Start()
    {
        fetcher = GetComponent<MapDataFetcher>();
        mapRenderer = mapQuad.GetComponent<MeshRenderer>();

        // Update quad
        fetcher.SetMapToRenderer(SceneParameters.GetCurrentLat(), SceneParameters.GetCurrentLong(), 1);

        // Disable sim button initially
        homePanel_trafficSimButton.interactable = false;

        // Home panel listeners
        homePanel_mainMenuButton.onClick.AddListener(homePanel_onHomeClick);
        homePanel_toggleOverLayButton.onClick.AddListener(homePanel_onOverLayClick);
        homePanel_rotateTexClockwise.onClick.AddListener(homePanel_onRotateClockwiseClick);
        homePanel_rotateTexCounterClockwise.onClick.AddListener(homePanel_onRotateCounterClockwiseClick);
        homePanel_trafficSimButton.onClick.AddListener(homePanel_onTrafficSimButtonClick);

        // Check initial road state
        UpdateTrafficSimButton();
    }

    void Update()
    {
        UpdateTrafficSimButton();
    }

    private void UpdateTrafficSimButton()
    {
        if (placementMobileManager == null) return;

        bool canSimulate = placementMobileManager.HasValidRoadConfiguration();
        homePanel_trafficSimButton.interactable = canSimulate;
    }

    private void homePanel_onHomeClick()
    {
        try
        {
            if (placementMobileManager != null)
            {
                string json = placementMobileManager.SavePlacementsToJson();

                // Build full save path
                string path = System.IO.Path.Combine(Application.persistentDataPath, "placements.json");

                // Ensure folder exists and write file
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, json);

                Debug.Log($"[IntersectionEditorNavigator] Saved placements JSON to disk: {path}");
            }
            else
            {
                Debug.LogWarning("[IntersectionEditorNavigator] placementMobileManager is null; skipping save.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[IntersectionEditorNavigator] Failed to save placements: {ex}");
        }

        // Now go back to the Start screen or home scene
        SceneManager.LoadScene("StartScreen");
    }


    void homePanel_onOverLayClick()
    {
        // Swap enabled state
        mapRenderer.enabled = !mapRenderer.enabled;
        if (!mapRenderer.enabled)
        {
            homePanel_rotateTexClockwise.gameObject.SetActive(false);
            homePanel_rotateTexCounterClockwise.gameObject.SetActive(false);
        }
        else
        {
            homePanel_rotateTexClockwise.gameObject.SetActive(true);
            homePanel_rotateTexCounterClockwise.gameObject.SetActive(true);
        }
    }

    void homePanel_onRotateClockwiseClick()
    {
        // Rotate map clockwise by 15 degrees
        mapQuad.transform.Rotate(0f, 0f, 5f, Space.Self);
    }

    void homePanel_onRotateCounterClockwiseClick()
    {
        // Rotate map counter-clockwise by 15 degrees
        mapQuad.transform.Rotate(0f, 0f, -5f, Space.Self);
    }

    void homePanel_onTrafficSimButtonClick()
    {
        SceneParameters.SetSavedJSON(placementMobileManager.SavePlacementsToJson());
        Debug.Log(SceneParameters.GetSavedJSON());
        SceneManager.LoadScene("TrafficSim");
    }
}
