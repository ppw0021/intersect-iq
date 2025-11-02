using UnityEngine;

[DisallowMultipleComponent]
public class CarSpawnerNode : MonoBehaviour
{
    [Header("Car Spawning")]
    [Tooltip("The car prefab to spawn.")]
    public GameObject carPrefab;

    [Tooltip("Vertical offset when spawning the car (to avoid clipping).")]
    public float spawnHeightOffset = 0.1f;

    [Tooltip("Raycast distance downward to find the road under this spawner.")]
    public float downRayDistance = 5f;

    [Tooltip("Layers considered 'road' for reading direction.")]
    public LayerMask roadMask;

    [Tooltip("If true, quantize road forward to world-cardinal axes (+X,-X,+Z,-Z).")]
    public bool snapToCardinals = true;

    [Header("Respawn")]
    [Tooltip("Time interval between each car spawn.")]
    [SerializeField] private float respawnDelay = 5f;

    private float respawnTimer;

    void Start()
    {
        respawnTimer = respawnDelay;
    }

    void Update()
    {
        respawnTimer -= Time.deltaTime;
        if (respawnTimer <= 0f)
        {
            TrySpawn();
            respawnTimer = respawnDelay; // Reset timer for next spawn
        }
    }

    public void ForceSpawnNow()
    {
        TrySpawn();
    }

    private void TrySpawn()
    {
        if (carPrefab == null)
        {
            Debug.LogWarning($"[CarSpawnerNode] No car prefab assigned on {name}.");
            return;
        }

        // Find road under us
        if (!FindRoadBelow(out Vector3 roadForward))
        {
            Debug.LogWarning($"[CarSpawnerNode] No road detected under {name}. Cannot spawn.");
            return;
        }

        // Optional cardinal snap
        if (snapToCardinals)
            roadForward = SnapToCardinal(roadForward);

        if (roadForward.sqrMagnitude < 0.5f)
        {
            Debug.LogWarning($"[CarSpawnerNode] Road forward invalid at {name}.");
            return;
        }

        // Spawn and align
        Vector3 pos = transform.position + Vector3.up * spawnHeightOffset;
        Quaternion rot = Quaternion.LookRotation(new Vector3(roadForward.x, 0f, roadForward.z), Vector3.up);

        GameObject newCar = Instantiate(carPrefab, pos, rot);

        // Initialize agent with travel direction if present
        var agent = newCar.GetComponent<CarAgent>();
        if (agent != null)
        {
            agent.SetTravelDirection(roadForward.normalized);
        }
        else
        {
            Debug.LogWarning("[CarSpawnerNode] Spawned car has no CarAgent component.");
        }
    }

    private bool FindRoadBelow(out Vector3 roadForward)
    {
        roadForward = Vector3.zero;
        Ray ray = new Ray(transform.position + Vector3.up * 1f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, downRayDistance, roadMask, QueryTriggerInteraction.Ignore))
        {
            roadForward = hit.collider.transform.forward;
            roadForward.y = 0f;
            return true;
        }
        return false;
    }

    private Vector3 SnapToCardinal(Vector3 v)
    {
        v.y = 0f;
        if (v == Vector3.zero) return Vector3.zero;

        Vector3 n = v.normalized;
        // Pick axis with largest magnitude
        if (Mathf.Abs(n.x) > Mathf.Abs(n.z))
            return new Vector3(Mathf.Sign(n.x), 0f, 0f);
        else
            return new Vector3(0f, 0f, Mathf.Sign(n.z));
    }
}
