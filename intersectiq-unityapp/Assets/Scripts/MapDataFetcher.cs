using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class MapDataFetcher : MonoBehaviour
{
    [Header("Target Mesh")]
    [SerializeField] Renderer mapRenderer;
    [Header("Google")]
    [SerializeField] string apiKey;
    [Range(0, 21)] public int zoom = 18;
    public int width = 512, height = 512;

    void Awake()
    {
        apiKey = EnvConfig.Get("GOOGLE_MAPS_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) Debug.LogError("Google Maps API key missing from .env");
        StartCoroutine(LoadMap(-36.9198, 174.91297));
    }

    IEnumerator LoadMap(double lat, double lon)
    {
        var url = BuildStaticMapUrl(lat, lon, zoom, width, height, apiKey);
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Map fetch failed: " + req.error);
            yield break;
        }

        var tex = DownloadHandlerTexture.GetContent(req);

        // Ensure we have a material instance and an unlit shader so lighting doesn't darken the map
        var mat = mapRenderer.material; // instanced at runtime
        if (mat.shader == null || mat.shader.name != "Unlit/Texture")
            mat.shader = Shader.Find("Unlit/Texture");

        mat.mainTexture = tex;
        mat.mainTextureScale = Vector2.one;
        mat.mainTextureOffset = Vector2.zero;

        FitMeshToTextureAspect(tex);
    }

    void FitMeshToTextureAspect(Texture2D tex)
    {
        if (mapRenderer == null || tex == null) return;

        float aspect = (float)tex.width / tex.height;

        var mf = mapRenderer.GetComponent<MeshFilter>();
        string meshName = mf && mf.sharedMesh ? mf.sharedMesh.name.ToLower() : "";

        // Quad is 1x1 in X–Y; Plane is 10x10 in X–Z
        if (meshName.Contains("plane"))
        {
            // scale.x affects X, scale.z affects Z. Base plane is 10 units, but aspect only needs ratio
            mapRenderer.transform.localScale = new Vector3(aspect, 1f, 1f);
        }
        else
        {
            // Assume Quad (or any mesh where X–Y are the visible axes)
            mapRenderer.transform.localScale = new Vector3(aspect, 1f, 1f);
        }
    }

    string BuildStaticMapUrl(double lat, double lon, int zoom, int width, int height, string apiKey)
    {
        string style =
            "&style=feature:all|element:labels|visibility:off" +
            "&style=feature:administrative|visibility:off" +
            "&style=feature:poi|visibility:off" +
            "&style=feature:landscape|visibility:off" +
            "&style=feature:transit|visibility:off" +
            "&style=feature:water|visibility:off" +
            "&style=feature:road|visibility:on|color:0xffffff";

        return $"https://maps.googleapis.com/maps/api/staticmap" +
               $"?center={lat},{lon}&zoom={zoom}&size={width}x{height}&scale=2&maptype=roadmap" +
               $"{style}&key={apiKey}";
    }
}
