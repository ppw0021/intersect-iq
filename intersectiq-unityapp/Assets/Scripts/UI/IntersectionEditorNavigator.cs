using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class IntersectionEditorNavigator : MonoBehaviour
{
    private MapDataFetcher fetcher;
    [SerializeField] GameObject homePanel;
    [SerializeField] Button homePanel_MainMenuButton;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        fetcher = GetComponent<MapDataFetcher>();
        // Update quad
        fetcher.SetMapToRenderer(SceneParameters.GetCurrentLat(), SceneParameters.GetCurrentLong(), 1);

        // Home panel
        homePanel_MainMenuButton.onClick.AddListener(homePanel_onHomeClick);
        Debug.Log($"LAT: {SceneParameters.GetCurrentLat()}, LONG: {SceneParameters.GetCurrentLong()}");      
    }

    // Update is called once per frame
    void Update()
    {

    }
    
    void homePanel_onHomeClick()
    {
        SceneManager.LoadScene("StartScreen");
        
    }
}
