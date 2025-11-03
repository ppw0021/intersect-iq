using UnityEngine;
using UnityEngine.UI;

public static class SceneParameters
{
    private static string savedJson = "";
    private static double currentLat = -36.918296;
    private static double currentLong = 174.935087;
    private static Renderer currentMapRenderer;
    private static RawImage currentMapImage;

    public static string GetSavedJSON() => savedJson;
    public static void SetSavedJSON(string jsonIN) => savedJson = jsonIN;

    public static double GetCurrentLat() => currentLat;
    public static void SetCurrentLat(double value) => currentLat = value;

    public static double GetCurrentLong() => currentLong;
    public static void SetCurrentLong(double value) => currentLong = value;

    public static void SetCurrentCoords(double lat, double lng)
    {
        currentLat = lat;
        currentLong = lng;
    }

    public static Renderer GetCurrentMapRenderer() => currentMapRenderer;
    public static void SetCurrentMapRenderer(Renderer renderer) => currentMapRenderer = renderer;

    public static RawImage GetCurrentMapImage() => currentMapImage;
    public static void SetCurrentMapImage(RawImage image) => currentMapImage = image;

    public static void ClearAll()
    {
        currentLat = 0;
        currentLong = 0;
        currentMapRenderer = null;
        currentMapImage = null;
    }

    // Only destroy CarAgents on the "Cars" layer, ignore others (like blockers)
    public static void StartSimulation()
    {
        int carLayer = LayerMask.NameToLayer("Cars");
        var agents = Object.FindObjectsOfType<CarAgent>();

        foreach (var a in agents)
        {
            if (a.gameObject.layer == carLayer)
                Object.Destroy(a.gameObject);
        }

        var spawners = Object.FindObjectsOfType<CarSpawnerNode>();
        foreach (var s in spawners)
            s.StartSpawning();
    }
}
