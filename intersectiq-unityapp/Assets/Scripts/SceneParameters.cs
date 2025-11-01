using UnityEngine;
using UnityEngine.UI;

public static class SceneParameters
{
    // Default params
    private static string savedJson = "";
    private static double currentLat = -36.918296;
    private static double currentLong = 174.935087;
    private static Renderer currentMapRenderer;
    private static RawImage currentMapImage;

    // --- JSON ---
    public static string GetSavedJSON()
    {
        return savedJson;
    }

    public static void SetSavedJSON(string jsonIN)
    {
        savedJson = jsonIN;
    }
    
    // --- Latitude ---
    public static double GetCurrentLat()
    {
        return currentLat;
    }

    public static void SetCurrentLat(double value)
    {
        currentLat = value;
    }

    // --- Longitude ---
    public static double GetCurrentLong()
    {
        return currentLong;
    }

    public static void SetCurrentLong(double value)
    {
        currentLong = value;
    }

    // --- Both coordinates together ---
    public static void SetCurrentCoords(double lat, double lng)
    {
        currentLat = lat;
        currentLong = lng;
    }

    // --- Map Renderer ---
    public static Renderer GetCurrentMapRenderer()
    {
        return currentMapRenderer;
    }

    public static void SetCurrentMapRenderer(Renderer renderer)
    {
        currentMapRenderer = renderer;
    }

    // --- Map RawImage ---
    public static RawImage GetCurrentMapImage()
    {
        return currentMapImage;
    }

    public static void SetCurrentMapImage(RawImage image)
    {
        currentMapImage = image;
    }

    // --- Optional convenience reset ---
    public static void ClearAll()
    {
        currentLat = 0;
        currentLong = 0;
        currentMapRenderer = null;
        currentMapImage = null;
    }
}