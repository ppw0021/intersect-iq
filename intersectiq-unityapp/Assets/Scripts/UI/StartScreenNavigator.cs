using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.IO;
using System;

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

    // Select Save Menu (kept for now, but Open flows directly to scene)
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

    private double mapSetLatitude;
    private double mapSetLongitude;

    [Serializable]
    private class PlacementSave
    {
        public int gridWidth;
        public int gridHeight;
        public float heightOffset;

        // Optional but supported: coordinates
        public double latitude;
        public double longitude;

        // We don't need the full shape of items to set coords,
        // but include it so JsonUtility is happy if present.
        [Serializable]
        public class PlacedItem
        {
            public string prefabName;
            public bool isCenter;
            public int x, z, w, h;
            public float yaw;
        }
        public PlacedItem[] items;
    }

    private string GetSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "placements.json");
    }

    void Start()
    {
        fetcher = GetComponent<MapDataFetcher>();
        if (fetcher == null)
            Debug.LogWarning("MapDataFetcher not found on the same GameObject.");

        // Start Menu Buttons
        startMenu_openSaveButton.onClick.AddListener(startMenu_OpenSaveButtonClicked);
        startMenu_newSaveButton.onClick.AddListener(startMenu_NewSaveButtonClicked);

        // Select Save Buttons (legacy flow; not used by Open now)
        selectSaveMenu_returnButton.onClick.AddListener(selectSaveMenu_ReturnButtonClicked);
        selectSaveMenu_continueButton.onClick.AddListener(selectSaveMenu_ContinueButtonClicked);

        // New Save Menu Buttons
        newSaveMenu_searchButton.onClick.AddListener(newSaveMenu_SearchButtonClicked);
        newSaveMenu_returnButton.onClick.AddListener(newSaveMenu_ReturnButtonClicked);
        newSaveMenu_continueButton.onClick.AddListener(newSaveMenu_ContinueButtonClicked);

        // Enable "Open Saved Intersection" only if the save file exists
        string path = GetSavePath();
        bool saveExists = File.Exists(path);
        startMenu_openSaveButton.interactable = saveExists;

        if (!saveExists)
        {
            Debug.Log($"[StartScreenNavigator] No save found at: {path}. 'Open Saved Intersection' disabled.");
        }
    }

    // Start Menu Buttons
    void startMenu_OpenSaveButtonClicked()
    {
        // Directly load the JSON from disk and jump to the editor scene
        string path = GetSavePath();
        if (!File.Exists(path))
        {
            Debug.LogWarning($"[StartScreenNavigator] Save not found at: {path}");
            startMenu_openSaveButton.interactable = false;
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogWarning("[StartScreenNavigator] Saved JSON is empty. Aborting open.");
                return;
            }

            SceneParameters.SetSavedJSON(json);

            var parsed = JsonUtility.FromJson<PlacementSave>(json);
            if (parsed != null)
            {
                // if (!double.IsNaN(parsed.latitude) &&
                //     !double.IsNaN(parsed.longitude) &&
                //     (parsed.latitude != 0.0 || parsed.longitude != 0.0))
                // {
                SceneParameters.SetCurrentCoords(parsed.latitude, parsed.longitude);
                // }
            }

            // Go to editor
            SceneManager.LoadScene("IntersectionEditorScene");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[StartScreenNavigator] Failed to open saved intersection: {ex}");
        }
    }

    void startMenu_NewSaveButtonClicked()
    {
        startMenu.SetActive(false);
        newSaveMenu.SetActive(true);
    }

    void selectSaveMenu_ReturnButtonClicked()
    {
        startMenu.SetActive(true);
        selectSaveMenu.SetActive(false);

        // Reset select save menu
        newSaveMenu_errorMessage.text = "";
        newSaveMenu_latEntry.text = "";
        newSaveMenu_longEntry.text = "";
        newSaveMenu_continueButton.interactable = false;
        if (fetcher) fetcher.ResetRawImage();
    }

    void selectSaveMenu_ContinueButtonClicked()
    {
        Debug.Log(SceneParameters.GetSavedJSON());
        SceneManager.LoadScene("IntersectionEditorScene");
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
            if (fetcher) fetcher.ResetRawImage();
            newSaveMenu_continueButton.interactable = false;
            return;
        }

        // Validate longitude
        if (!validLon || longitude < -180.0 || longitude > 180.0)
        {
            newSaveMenu_errorMessage.text = "Invalid Coordinates";
            Debug.LogWarning($"Invalid longitude: {lonText}. Must be a number between -180 and 180.");
            if (fetcher) fetcher.ResetRawImage();
            newSaveMenu_continueButton.interactable = false;
            return;
        }

        // Passed validation
        Debug.Log($"Validated coordinates: LAT {latitude}, LON {longitude}");

        // Trigger map fetch if available
        if (fetcher != null)
        {
            mapSetLatitude = latitude;
            mapSetLongitude = longitude;
            fetcher.SetMapToRawImage(latitude, longitude, 0);
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
        if (fetcher) fetcher.ResetRawImage();

        newSaveMenu.SetActive(false);
    }

    void newSaveMenu_ContinueButtonClicked()
    {
        // Load next scene
        SceneParameters.SetCurrentCoords(mapSetLatitude, mapSetLongitude);
        SceneManager.LoadScene("IntersectionEditorScene");
    }
}
