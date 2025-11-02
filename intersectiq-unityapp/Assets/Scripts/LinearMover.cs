using UnityEngine;

public class LinearMover : MonoBehaviour
{
    public Transform pointA;
    public Transform pointB;
    public float speed = 2f;
    private bool toB = true;

    void Update()
    {
        transform.position = Vector3.MoveTowards(transform.position, toB ? pointB.position : pointA.position, speed * Time.deltaTime);
        if (Vector3.Distance(transform.position, toB ? pointB.position : pointA.position) < 0.01f)
        {
            if (toB) transform.position = pointA.position;
            toB = !toB;
        }
    }
}
