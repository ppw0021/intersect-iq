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
    [SerializeField] InputField newSaveMenu_latEntry;
    [SerializeField] InputField newSaveMenu_longEntry;
    [SerializeField] Button newSaveMenu_searchButton;
    [SerializeField] Text newSaveMenu_errorMessage;
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
        newSaveMenu_errorMessage.text = "";
        newSaveMenu_latEntry.text = "";
        newSaveMenu_longEntry.text = "";
        newSaveMenu_continueButton.interactable = false;
        fetcher.ResetRawImage();
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
            newSaveMenu_errorMessage.text = "Invalid Coordinates";
            Debug.LogWarning($"Invalid latitude: {latText}. Must be a number between -90 and 90.");
            fetcher.ResetRawImage();
            newSaveMenu_continueButton.interactable = false;
            return;
        }

        // Validate longitude
        if (!validLon || longitude < -180.0 || longitude > 180.0)
        {
            newSaveMenu_errorMessage.text = "Invalid Coordinates";
            Debug.LogWarning($"Invalid longitude: {lonText}. Must be a number between -180 and 180.");
            fetcher.ResetRawImage();
            newSaveMenu_continueButton.interactable = false;
            return;
        }

        // Passed validation
        Debug.Log($"Validated coordinates: LAT {latitude}, LON {longitude}");

        // Trigger map fetch if available
        if (fetcher != null)
        {
            fetcher.SetMapToRawImage(latitude, longitude);
            newSaveMenu_continueButton.interactable = true;
        }
        else
        {
            Debug.LogWarning("MapDataFetcher not assigned, cannot fetch map.");
        }
    }

    void newSaveMenu_ReturnButtonClicked()
    {
        startMenu.SetActive(true);
        // reset map and entry field here
        newSaveMenu_errorMessage.text = "";
        newSaveMenu_latEntry.text = "";
        newSaveMenu_longEntry.text = "";
        newSaveMenu_continueButton.interactable = false;
        fetcher.ResetRawImage();
        newSaveMenu.SetActive(false);
    }

    void newSaveMenu_ContinueButtonClicked()
    {
        // Load next scene
    }
}
