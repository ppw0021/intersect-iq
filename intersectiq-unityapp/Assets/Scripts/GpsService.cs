using UnityEngine;
using System.Collections;

public class GpsService : MonoBehaviour
{
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public bool Ready { get; private set; }

    IEnumerator Start()
    {
        // Ask permission (Android)
#if UNITY_ANDROID
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation)) {
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
            yield return new WaitForSeconds(0.5f);
        }
#endif
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("Location disabled by user.");
            yield break;
        }

        Input.location.Start(desiredAccuracyInMeters: 10f, updateDistanceInMeters: 5f);

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }
        if (maxWait <= 0 || Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning("Location failed or timed out.");
            Input.location.Stop();
            yield break;
        }

        var last = Input.location.lastData;
        Latitude = last.latitude;
        Longitude = last.longitude;
        Ready = true;
    }
}
