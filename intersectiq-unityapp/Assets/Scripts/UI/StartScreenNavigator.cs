using UnityEngine;
using UnityEngine.UI;

public class StartScreenNavigator : MonoBehaviour
{
    private MapDataFetcher fetcher;
    
    // Panels
    [Header("Panels")]
    [SerializeField] GameObject startMenu;
    [SerializeField] GameObject selectSaveMenu;
    [SerializeField] GameObject newSaveMenu;

    // Start Menu 
    [Header("Start Menu")]
    [SerializeField] Button startMenu_newSaveButton;
    [SerializeField] Button startMenu_openSaveButton;
    
    // Select Save Menu
    [Header("Select Save Menu")]
    [SerializeField] Button selectSaveMenu_returnButton;
    [SerializeField] Button selectSaveMenu_continueButton;

    // New Save Menu
    [Header("New Save Menu")]
    [SerializeField] Text newSaveMenu_latEntry;
    [SerializeField] Text newSaveMenu_longEntry;
    [SerializeField] Button newSaveMenu_searchButton;
    [SerializeField] Button newSaveMenu_returnButton;
    [SerializeField] Button newSaveMenu_continueButton;

    void Start()
    {
        fetcher = GetComponent<MapDataFetcher>();
        if (fetcher == null)
            Debug.LogWarning("MapDataFetcher not found on the same GameObject.");

        // Start Menu Buttons
        startMenu_openSaveButton.onClick.AddListener(startMenu_OpenSaveButtonClicked);
        startMenu_newSaveButton.onClick.AddListener(startMenu_NewSaveButtonClicked);

        // Select Save Buttons
        selectSaveMenu_returnButton.onClick.AddListener(selectSaveMenu_ReturnButtonClicked);
        selectSaveMenu_continueButton.onClick.AddListener(selectSaveMenu_ContinueButtonClicked);

        // New Save Menu Buttons
        newSaveMenu_searchButton.onClick.AddListener(newSaveMenu_SearchButtonClicked);
        newSaveMenu_returnButton.onClick.AddListener(newSaveMenu_ReturnButtonClicked);
        newSaveMenu_continueButton.onClick.AddListener(newSaveMenu_ContinueButtonClicked);

    }

    // Start Menu Buttons
    void startMenu_OpenSaveButtonClicked()
    {
        startMenu.SetActive(false);
        selectSaveMenu.SetActive(true);
    }

    void startMenu_NewSaveButtonClicked()
    {
        startMenu.SetActive(false);
        newSaveMenu.SetActive(true);
    }

    // Select Save Buttons
    void selectSaveMenu_ReturnButtonClicked()
    {
        startMenu.SetActive(true);
        selectSaveMenu.SetActive(false);
        // Reset select save menu
    }
    
    void selectSaveMenu_ContinueButtonClicked()
    {
        // Load next scene
    }

    // New Save Menu Buttons
    void newSaveMenu_SearchButtonClicked()
    {
        string latText = newSaveMenu_latEntry.text.Trim();
        string lonText = newSaveMenu_longEntry.text.Trim();

        // Try parsing as doubles
        bool validLat = double.TryParse(latText, out double latitude);
        bool validLon = double.TryParse(lonText, out double longitude);

        // Validate latitude
        if (!validLat || latitude < -90.0 || latitude > 90.0)
        {
            Debug.LogWarning($"Invalid latitude: {latText}. Must be a number between -90 and 90.");
            return;
        }

        // Validate longitude
        if (!validLon || longitude < -180.0 || longitude > 180.0)
        {
            Debug.LogWarning($"Invalid longitude: {lonText}. Must be a number between -180 and 180.");
            return;
        }

        // Passed validation
        Debug.Log($"Validated coordinates: LAT {latitude}, LON {longitude}");

        // Trigger map fetch if available
        if (fetcher != null)
        {
            fetcher.SetMapToRawImage(latitude, longitude);
        }
        else
        {
            Debug.LogWarning("MapDataFetcher not assigned, cannot fetch map.");
        }
    }

    void newSaveMenu_ReturnButtonClicked()
    {
        startMenu.SetActive(true);
        newSaveMenu.SetActive(false);
        // reset map and entry field here
    }
    
    void newSaveMenu_ContinueButtonClicked()
    {
        // Load next scene
    }
}
