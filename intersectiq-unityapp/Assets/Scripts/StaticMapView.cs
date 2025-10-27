using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using System.Collections;
using Unity.VisualScripting;

public class StaticMapView : MonoBehaviour {
    [SerializeField] RawImage mapImage;
    [SerializeField] string apiKey;
    void Awake()
    {
        apiKey = EnvConfig.Get("GOOGLE_MAPS_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("Google Maps API key missing from .env");
        }

        StartCoroutine(LoadMap(-36.9198, 174.91297));
    }
    [Range(0,21)] public int zoom = 18; // 18–20 shows intersections clearly
    public int width = 512, height = 512;

    public IEnumerator LoadMap(double lat, double lon) {
        var url = BuildStaticMapUrl(lat, lon, zoom, width, height, apiKey);
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success) {
            Debug.LogError("Map fetch failed: " + req.error);
            yield break;
        }
        var tex = DownloadHandlerTexture.GetContent(req);
        mapImage.texture = tex;
        mapImage.SetNativeSize(); // optional
    }

    string BuildStaticMapUrl(double lat, double lon, int zoom, int width, int height, string apiKey)
    {
        return $"https://maps.googleapis.com/maps/api/staticmap" +
               $"?center={lat},{lon}&zoom={zoom}&size={width}x{height}&scale=2&maptype=roadmap" +
               $"&markers=color:red|{lat},{lon}&key={apiKey}";
    }
}
