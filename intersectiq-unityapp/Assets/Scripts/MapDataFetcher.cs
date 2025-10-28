using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using UnityEngine.UI;

public class MapDataFetcher : MonoBehaviour
{
    [Header("Target Mesh")]
    [SerializeField] Renderer mapRenderer;
    [SerializeField] RawImage mapImage;
    [Header("Google")]
    [SerializeField] string apiKey;
    [Range(0, 21)] public int zoom = 18;
    // width and height for renderer
    public int width = 512, height = 512;
    // width and height for raw image
    public int rawImageWidth = 512, rawImageHeight = 512;

    void Awake()
    {
        apiKey = EnvConfig.Get("GOOGLE_MAPS_API_KEY");
        if (string.IsNullOrEmpty(apiKey)) Debug.LogError("Google Maps API key missing from .env");
    }

    public void SetMapToRenderer(double lat, double lon, int mapStyle)
    {
        StartCoroutine(LoadMapToRenderer(lat, lon, mapStyle));
    }

    public void SetMapToRawImage(double lat, double lon, int mapStyle)
    {
        StartCoroutine(LoadMapToRawImage(lat, lon, mapStyle));
    }

    public void ResetRawImage()
    {
        mapImage.texture = null;
    }

    public IEnumerator LoadMapToRawImage(double lat, double lon, int mapStyle)
    {
        var url = BuildStaticMapUrl(mapStyle, lat, lon, zoom, rawImageWidth, rawImageHeight, apiKey);
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Map fetch failed: " + req.error);
            yield break;
        }
        var tex = DownloadHandlerTexture.GetContent(req);
        mapImage.texture = tex;
        // mapImage.SetNativeSize(); // optional
    }

    IEnumerator LoadMapToRenderer(double lat, double lon, int mapStyle)
    {
        var url = BuildStaticMapUrl(mapStyle, lat, lon, zoom, width, height, apiKey);
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Map fetch failed: " + req.error);
            yield break;
        }

        // Get the downloaded texture
        var tex = DownloadHandlerTexture.GetContent(req);

        // Convert black pixels to transparent
        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            // Adjust threshold if needed (0.05 is good for near-black)
            if (pixels[i].r < 0.05f && pixels[i].g < 0.05f && pixels[i].b < 0.05f)
                pixels[i].a = 0f;
        }
        tex.SetPixels(pixels);
        tex.Apply();

        // Ensure we have a material instance and an unlit transparent shader
        var mat = mapRenderer.material; // instanced at runtime
        var transparentShader = Shader.Find("Unlit/Transparent");
        if (transparentShader != null)
            mat.shader = transparentShader;
        else
            mat.shader = Shader.Find("Unlit/Texture"); // fallback

        // Apply texture to the material
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

    string BuildStaticMapUrl(int mapStyle, double lat, double lon, int zoom, int width, int height, string apiKey)
    {
        string style;

        if (mapStyle == 0)
        {
            style =
            "&style=feature:all|element:labels.text|visibility:off" +
            "&style=feature:poi|visibility:off" +
            "&style=feature:transit|visibility:off";
        }
        else if (mapStyle == 1)
        {
            // transparent and white
            style =
            "&style=feature:all|element:labels|visibility:off" +
            // "&style=feature:administrative|visibility:off" +
            "&style=feature:poi|visibility:off" +
            "&style=feature:landscape|visibility:off" +
            // "&style=feature:transit|visibility:off" +
            "&style=feature:water|visibility:off" +
            "&style=feature:road|visibility:on|color:0xffffff";
        }
        else
        {
            // default
            style =
            "&style=feature:all|element:labels.text|visibility:off" +
            "&style=feature:poi|visibility:off" +
            "&style=feature:transit|visibility:off";
        }
        return $"https://maps.googleapis.com/maps/api/staticmap" +
               $"?center={lat},{lon}&zoom={zoom}&size={width}x{height}&scale=2&maptype=roadmap" +
               $"{style}&key={apiKey}";
    }

}
