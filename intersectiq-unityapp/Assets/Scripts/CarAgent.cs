using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CarAgent : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Cruise speed when path is clear (m/s).")]
    public float cruiseSpeed = 6f;

    [Tooltip("Acceleration up to cruise (m/s²).")]
    public float acceleration = 2.5f;

    [Tooltip("Braking when stopping (m/s²).")]
    public float braking = 8f;

    [Tooltip("How fast to rotate towards travelDir (deg/s).")]
    public float turnRate = 360f;

    [Header("Detection Settings")]
    [Tooltip("How far ahead to look for other cars (m).")]
    public float detectionRange = 8f;

    [Tooltip("Width tolerance when checking for cars in front (m).")]
    public float laneWidth = 2.0f;

    [Tooltip("Distance threshold where the car fully stops (m).")]
    public float stopDistance = 2.0f;

    [Tooltip("How often to update the car detection logic (s).")]
    public float detectionInterval = 0.1f;

    [Header("Grounding (optional)")]
    public LayerMask groundMask;
    public float groundRayDistance = 3f;
    public float groundOffset = 0.02f;

    // Internal
    private float currentSpeed = 0f;
    private Vector3 travelDir = Vector3.forward;
    private bool isBlocked = false;
    private float detectionTimer = 0f;

    // Cached list of all cars for efficiency
    private static List<CarAgent> allCars = new List<CarAgent>();

    public void SetTravelDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            travelDir = dir.normalized;
            transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);
        }
    }

    void OnEnable()
    {
        allCars.Add(this);
    }

    void OnDisable()
    {
        allCars.Remove(this);
    }

    void Start()
    {
        if (travelDir.sqrMagnitude < 0.0001f)
            travelDir = transform.forward;
    }

    void Update()
    {
        // Detect other cars every detectionInterval seconds
        detectionTimer -= Time.deltaTime;
        if (detectionTimer <= 0f)
        {
            detectionTimer = detectionInterval;
            DetectCarsAhead();
        }

        // Choose target speed
        float targetSpeed = isBlocked ? 0f : cruiseSpeed;

        // Smoothly adjust speed
        float rate = (targetSpeed > currentSpeed) ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

        // Move and orient
        transform.position += travelDir * currentSpeed * Time.deltaTime;
        Vector3 fwd = Vector3.RotateTowards(transform.forward, travelDir, Mathf.Deg2Rad * turnRate * Time.deltaTime, 1f);
        transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        // ground snapping
        if (groundMask.value != 0)
            GroundSnap();
    }

    private void DetectCarsAhead()
    {
        bool found = false;
        float nearestDist = Mathf.Infinity;

        foreach (var other in allCars)
        {
            if (other == this) continue;

            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0f;

            // Check if the other car is in front
            float forwardDot = Vector3.Dot(transform.forward, toOther.normalized);
            if (forwardDot <= 0.3f) continue; // behind or sideways

            float lateralOffset = Vector3.Dot(transform.right, toOther.normalized) * toOther.magnitude;
            if (Mathf.Abs(lateralOffset) > laneWidth * 0.5f) continue; // too far to the side

            float distance = toOther.magnitude;
            if (distance < detectionRange && distance < nearestDist)
            {
                nearestDist = distance;
                found = true;
            }
        }

        // Update blocking state
        if (found && !isBlocked)
        {
            isBlocked = true;
            Debug.Log($"{name}: Car detected ahead within {nearestDist:F2} m");
        }
        else if (!found && isBlocked)
        {
            isBlocked = false;
            Debug.Log($"{name}: Path clear");
        }
    }

    private void GroundSnap()
    {
        Vector3 rayStart = transform.position + Vector3.up * 1f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundRayDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 p = transform.position;
            p.y = hit.point.y + groundOffset;
            transform.position = p;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = isBlocked ? Color.red : Color.green;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * detectionRange);

        Gizmos.color = new Color(0, 1, 1, 0.25f);
        Vector3 center = transform.position + transform.forward * (detectionRange * 0.5f);
        Vector3 size = new Vector3(laneWidth, 0.1f, detectionRange);
        Matrix4x4 prev = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = prev;
    }
#endif
}
