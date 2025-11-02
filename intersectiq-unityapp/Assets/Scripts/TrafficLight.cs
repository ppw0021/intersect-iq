using UnityEngine;

public enum LightState { Red, Amber, Green }

public class TrafficLight : MonoBehaviour
{
    [Header("Renderer & Materials")]
    public Renderer lightRenderer;
    public Material redMat;
    public Material amberMat;
    public Material greenMat;

    public void SetState(LightState state)
    {
        if (!lightRenderer) return;

        switch (state)
        {
            case LightState.Red:
                lightRenderer.material = redMat;
                break;
            case LightState.Amber:
                lightRenderer.material = amberMat;
                break;
            case LightState.Green:
                lightRenderer.material = greenMat;
                break;
        }
    }
}
