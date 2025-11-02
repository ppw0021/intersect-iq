using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class CarSpawnerButton : MonoBehaviour
{
    [Tooltip("Reference to the TrafficSimulator in your scene.")]
    public TrafficSimulator simulator;

    void Awake()
    {
        // Automatically hook up this button's OnClick to start spawner placement
        GetComponent<Button>().onClick.AddListener(() =>
        {
            if (simulator != null)
            {
                simulator.StartCarSpawnerPlacement();
            }
            else
            {
                Debug.LogWarning("[CarSpawnerButton] No TrafficSimulator assigned.");
            }
        });
    }
}
