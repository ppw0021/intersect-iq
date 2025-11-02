using UnityEngine;

[DisallowMultipleComponent]
public class CarAgent : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("Forward movement speed (m/s).")]
    public float speed = 3.0f;

    [Tooltip("Acceleration/Deceleration smoothing (s). 0 = instant.")]
    public float moveSmoothTime = 0.08f;

    [Header("Grounding")]
    [Tooltip("Raycast downward to keep the car glued to road height.")]
    public float groundRayDistance = 3f;

    [Tooltip("Layers considered 'ground/road' for vertical snapping.")]
    public LayerMask groundMask;

    private Vector3 travelDir = Vector3.forward; // set by spawner
    private float currSpeed;                      // smoothed speed
    private float speedVel;                       // ref velocity for SmoothDamp

    // Call this once after spawn
    public void SetTravelDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            travelDir = dir.normalized;
            transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);
        }
    }

    void Update()
    {
        // Always move at target speed (no stopping/interaction logic)
        float targetSpeed = speed;
        currSpeed = Mathf.SmoothDamp(currSpeed, targetSpeed, ref speedVel, moveSmoothTime);

        // Move along travelDir
        Vector3 delta = travelDir * currSpeed * Time.deltaTime;
        transform.position += delta;

        GroundSnap();
    }

    private void GroundSnap()
    {
        if (groundMask.value == 0) return;

        Vector3 rayStart = transform.position + Vector3.up * 1.0f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 p = transform.position;
            p.y = hit.point.y + 0.02f; // small float to prevent z-fighting
            transform.position = p;
        }
    }
}
