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


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        fetcher = GetComponent<MapDataFetcher>();
        mapRenderer = mapQuad.GetComponent<MeshRenderer>();
        // Update quad
        fetcher.SetMapToRenderer(SceneParameters.GetCurrentLat(), SceneParameters.GetCurrentLong(), 1);

        // Home panel
        homePanel_mainMenuButton.onClick.AddListener(homePanel_onHomeClick);
        Debug.Log($"LAT: {SceneParameters.GetCurrentLat()}, LONG: {SceneParameters.GetCurrentLong()}");

        homePanel_toggleOverLayButton.onClick.AddListener(homePanel_onOverLayClick);
        homePanel_rotateTexClockwise.onClick.AddListener(homePanel_onRotateClockwiseClick);
        homePanel_rotateTexCounterClockwise.onClick.AddListener(homePanel_onRotateCounterClockwiseClick);
        homePanel_trafficSimButton.onClick.AddListener(homePanel_onTrafficSimButtonClick);
    }
    void homePanel_onHomeClick()
    {
        SceneParameters.SetSavedJSON(placementMobileManager.SavePlacementsToJson());
        Debug.Log(SceneParameters.GetSavedJSON());
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
