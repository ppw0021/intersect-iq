using UnityEngine;

[DisallowMultipleComponent]
public class CarSpawnerNode : MonoBehaviour
{
    [Header("Car Spawning")]
    public GameObject carPrefab;
    public float spawnHeightOffset = 0.1f;
    public float downRayDistance = 5f;
    public LayerMask roadMask;
    public bool snapToCardinals = true;

    [Header("Respawn")]
    [SerializeField] private float respawnDelay = 5f;

    private float respawnTimer;
    private bool spawning = false;

    void Start()
    {
        respawnTimer = respawnDelay;
    }

    void Update()
    {
        if (!spawning) return;

        respawnTimer -= Time.deltaTime;
        if (respawnTimer <= 0f)
        {
            TrySpawn();
            respawnTimer = respawnDelay;
        }
    }

    public void StartSpawning()
    {
        spawning = true;
        TrySpawn(); // spawn immediately when starting
        respawnTimer = respawnDelay; // reset for next cycle
    }

    public void StopSpawning()
    {
        spawning = false;
    }

    public void ForceSpawnNow()
    {
        TrySpawn();
        respawnTimer = respawnDelay;
    }

    private void TrySpawn()
    {
        if (carPrefab == null)
        {
            Debug.LogWarning($"[CarSpawnerNode] No car prefab assigned on {name}.");
            return;
        }

        if (!FindRoadBelow(out Vector3 roadForward))
        {
            Debug.LogWarning($"[CarSpawnerNode] No road detected under {name}. Cannot spawn.");
            return;
        }

        if (snapToCardinals)
            roadForward = SnapToCardinal(roadForward);

        if (roadForward.sqrMagnitude < 0.5f)
        {
            Debug.LogWarning($"[CarSpawnerNode] Road forward invalid at {name}.");
            return;
        }

        Vector3 pos = transform.position + Vector3.up * spawnHeightOffset;
        Quaternion rot = Quaternion.LookRotation(new Vector3(roadForward.x, 0f, roadForward.z), Vector3.up);

        GameObject newCar = Object.Instantiate(carPrefab, pos, rot);

        var agent = newCar.GetComponent<CarAgent>();
        if (agent != null)
            agent.SetTravelDirection(roadForward.normalized);
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
        return Mathf.Abs(n.x) > Mathf.Abs(n.z)
            ? new Vector3(Mathf.Sign(n.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(n.z));
    }
}
