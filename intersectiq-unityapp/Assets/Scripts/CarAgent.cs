using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CarAgent : MonoBehaviour
{
    [Header("Do not record statistics")]
    public bool noStatistics = false;

    [Header("Movement Settings")]
    [Tooltip("Cruise speed when path is clear (m/s).")]
    public float cruiseSpeed = 6f;

    [Tooltip("Acceleration up to cruise (m/s²).")]
    public float acceleration = 2.5f;

    [Tooltip("Braking when stopping (m/s²).")]
    public float braking = 8f;

    [Tooltip("How fast to rotate steering (deg/s).")]
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

    [Header("Intersection Detection")]
    [Tooltip("Layer(s) used by the Center (intersection) objects.")]
    public LayerMask centerLayerMask;

    [Tooltip("Downward ray length for intersection detection (m).")]
    public float centerCheckRayLength = 5f;

    private TrafficSimulator trafficSimulator;

    [Tooltip("Distance at which we consider the car to have reached the chosen exit (m).")]
    public float exitArrivalDistance = 0.75f;

    [Header("Post-Exit Alignment")]
    [Tooltip("Nudge forward along the exit before alignment to avoid jitter at the seam (m).")]
    public float alignmentForwardOffset = 0.5f;

    [Tooltip("How strongly to snap laterally to the road centerline on arrival (1 = full snap).")]
    [Range(0f, 1f)] public float lateralSnapStrength = 1f;

    [Tooltip("If true, snap final heading to 0/90/180/270 to match grid roads.")]
    public bool snapYawToCardinals = true;

    [Header("Statistics")]
    [Tooltip("Speed (m/s) below which the car is considered stationary for timing.")]
    public float stationarySpeedThreshold = 0.05f;

    // Internal state
    private float currentSpeed = 0f;
    private Vector3 travelDir = Vector3.forward;
    private bool isBlocked = false;
    private float detectionTimer = 0f;

    // Exit navigation
    private bool hasExitTarget = false;
    private bool hasChosenExit = false; // single direction change latch
    private TrafficSimulator.ExitNode exitTarget;

    // Cached list of all cars for efficiency
    private static List<CarAgent> allCars = new List<CarAgent>();

    // Stats state
    private float totalStationaryTime = 0f;
    private float lifetimeTime = 0f;
    private float totalDistance = 0f;
    private Vector3 lastPosition;
    private Vector3 startPosition;

    public void SetTravelDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            travelDir = dir.normalized;
            transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);
        }
    }

    // Call if recycling the car and allowing it to pick an exit again
    public void ResetRouting()
    {
        hasExitTarget = false;
        hasChosenExit = false;
    }

    // Reset statistics counters (does not affect routing)
    public void ResetStatistics()
    {
        totalStationaryTime = 0f;
        lifetimeTime = 0f;
        totalDistance = 0f;
        startPosition = transform.position;
        lastPosition = transform.position;
    }

    // Accessors for statistics
    public float GetTotalStationaryTimeSeconds() => totalStationaryTime;
    public float GetAverageSpeedMetersPerSecond() => lifetimeTime > 0f ? totalDistance / lifetimeTime : 0f;
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
        // Auto-find TrafficSimulator if not assigned
        if (!trafficSimulator)
        {
            trafficSimulator = FindFirstObjectByType<TrafficSimulator>();
            if (!trafficSimulator)
                Debug.LogWarning($"{name}: No TrafficSimulator found in scene.");
        }

        if (travelDir.sqrMagnitude < 0.0001f)
            travelDir = transform.forward;

        startPosition = transform.position;
        lastPosition = transform.position;
    }

    void Update()
    {
        detectionTimer -= Time.deltaTime;
        if (detectionTimer <= 0f)
        {
            detectionTimer = detectionInterval;
            DetectCarsAhead();
            DetectCenterBelow();
        }

        // Navigation direction
        Vector3 desiredDir = travelDir;
        if (hasExitTarget)
        {
            Vector3 toExit = exitTarget.world - transform.position;
            toExit.y = 0f;

            if (toExit.magnitude <= exitArrivalDistance)
            {
                hasExitTarget = false;   // reached the exit point; realign to the road
                desiredDir = exitTarget.forward;
                AlignToExitRoad();
            }
            else
            {
                desiredDir = toExit.normalized;
            }
        }

        // Smooth steering
        travelDir = Vector3.RotateTowards(
            travelDir,
            desiredDir,
            Mathf.Deg2Rad * turnRate * Time.deltaTime,
            1f
        ).normalized;

        // Speed logic
        float targetSpeed = isBlocked ? 0f : cruiseSpeed;
        float rate = (targetSpeed > currentSpeed) ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, rate * Time.deltaTime);

        // Move + orient
        Vector3 prePos = transform.position;
        transform.position += travelDir * currentSpeed * Time.deltaTime;
        if (travelDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(travelDir, Vector3.up);

        // Statistics update
        if (!noStatistics)
        {
            float dt = Time.deltaTime;
            lifetimeTime += dt;

            // Distance traveled this frame (actual path length)
            float frameDist = Vector3.Distance(transform.position, lastPosition);
            totalDistance += frameDist;
            lastPosition = transform.position;

            // Stationary time accumulation (use both speed and displacement to be robust)
            bool effectivelyStopped = (currentSpeed <= stationarySpeedThreshold) &&
                                      (Vector3.Distance(transform.position, prePos) <= stationarySpeedThreshold * dt * 0.5f);
            if (effectivelyStopped)
                totalStationaryTime += dt;
        }
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

            float forwardDot = Vector3.Dot(transform.forward, toOther.normalized);
            if (forwardDot <= 0.3f) continue;

            float lateralOffset = Vector3.Dot(transform.right, toOther.normalized) * toOther.magnitude;
            if (Mathf.Abs(lateralOffset) > laneWidth * 0.5f) continue;

            float distance = toOther.magnitude;
            if (distance < detectionRange && distance < nearestDist)
            {
                nearestDist = distance;
                found = true;
            }
        }

        if (found && !isBlocked)
        {
            isBlocked = true;
            if (nearestDist < stopDistance)
                currentSpeed = Mathf.Min(currentSpeed, 0.1f);
        }
        else if (!found && isBlocked)
        {
            isBlocked = false;
        }
    }

    private void DetectCenterBelow()
    {
        if (hasChosenExit) return;

        Ray ray = new Ray(transform.position + Vector3.up * 0.5f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, centerCheckRayLength, centerLayerMask))
        {
            if (!hasExitTarget && trafficSimulator && trafficSimulator.IntersectionExits.Count > 0)
            {
                if (trafficSimulator.TryGetRandomExit(out var exit))
                {
                    exitTarget = exit;
                    hasExitTarget = true;
                    hasChosenExit = true;

                    Vector3 toExit = exitTarget.world - transform.position;
                    toExit.y = 0f;
                    if (toExit.sqrMagnitude > 0.0001f)
                        travelDir = toExit.normalized;
                }
            }
        }
    }

    // Alignment helpers
    private void AlignToExitRoad()
    {
        if (!hasChosenExit) return;

        Vector3 f = exitTarget.forward;
        if (f.sqrMagnitude < 1e-6f) return;
        f.Normalize();

        Vector3 p0 = exitTarget.world + f * alignmentForwardOffset;
        p0.y = transform.position.y;

        Vector3 toP = transform.position - p0;
        toP.y = 0f;
        float t = Vector3.Dot(toP, f);
        Vector3 centerlinePoint = p0 + f * t;

        Vector3 newPos = Vector3.Lerp(transform.position, centerlinePoint, Mathf.Clamp01(lateralSnapStrength));
        transform.position = newPos;

        Vector3 finalForward = f;
        if (snapYawToCardinals)
        {
            float yaw = Mathf.Atan2(finalForward.x, finalForward.z) * Mathf.Rad2Deg;
            float snapped = SnapYawToCardinal(yaw);
            finalForward = YawToForward(snapped);
        }

        travelDir = finalForward;
        transform.rotation = Quaternion.LookRotation(finalForward, Vector3.up);
    }

    private static Vector3 YawToForward(float yawDeg)
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)).normalized;
    }

    private static float SnapYawToCardinal(float yawDeg)
    {
        yawDeg = Mathf.Repeat(yawDeg, 360f);
        float[] card = { 0f, 90f, 180f, 270f };
        float best = 0f, bestDist = float.MaxValue;
        for (int i = 0; i < card.Length; i++)
        {
            float dist = Mathf.Abs(Mathf.DeltaAngle(yawDeg, card[i]));
            if (dist < bestDist) { bestDist = dist; best = card[i]; }
        }
        return best;
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

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position + Vector3.up * 0.5f, transform.position + Vector3.up * 0.5f + Vector3.down * centerCheckRayLength);

        if (hasExitTarget)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, exitTarget.world);
            Gizmos.DrawSphere(exitTarget.world, 0.15f);
            Gizmos.color = Color.white;
            Gizmos.DrawLine(exitTarget.world, exitTarget.world + exitTarget.forward * 1.0f);

            Vector3 f = exitTarget.forward.normalized;
            Vector3 p0 = exitTarget.world + f * alignmentForwardOffset;
            p0.y = transform.position.y;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p0 - f * 3f, p0 + f * 3f);
        }
    }
#endif
}
