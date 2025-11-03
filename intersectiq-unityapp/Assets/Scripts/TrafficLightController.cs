using UnityEngine;
using System.Collections.Generic;

public class TrafficLightController : MonoBehaviour
{
    [Header("Cycle Timing")]
    public float greenDuration = 6f;
    public float amberDuration = 1.5f;
    public float allRedDuration = 1.0f;

    [Header("Signal Lights (Auto-Detect)")]
    public TrafficLight northSignal;
    public TrafficLight eastSignal;
    public TrafficLight southSignal;
    public TrafficLight westSignal;

    [Header("Run Control")]
    [Tooltip("If true, the controller starts cycling on Start(). Otherwise remains all-red until StartCycle() is called.")]
    public bool autoStart = false;

    private TrafficSimulator trafficSimulator;
    private float timer;
    private int currentIndex = 0;   // index in the activePhases list
    private bool amberPhase;
    private bool allRedPhase;
    private bool running;

    private readonly List<int> activePhases = new(); // 0=N, 1=E, 2=S, 3=W

    void Start()
    {
        trafficSimulator = FindFirstObjectByType<TrafficSimulator>();
        if (!trafficSimulator)
        {
            Debug.LogError("[TrafficLightController] No TrafficSimulator found!");
            enabled = false;
            return;
        }

        if (!northSignal || !eastSignal || !southSignal || !westSignal)
            AutoBindSignalsByPosition();

        if (northSignal) activePhases.Add(0);
        if (eastSignal)  activePhases.Add(1);
        if (southSignal) activePhases.Add(2);
        if (westSignal)  activePhases.Add(3);

        if (activePhases.Count == 0)
        {
            Debug.LogWarning("[TrafficLightController] No valid signals found — controller disabled.");
            enabled = false;
            return;
        }

        currentIndex = 0;
        amberPhase = false;
        allRedPhase = false;

        if (autoStart)
            StartCycle();
        else
        {
            running = false;
            SetAllRed();
        }
    }

    void Update()
    {
        if (!running) return;

        timer -= Time.deltaTime;
        if (timer > 0f) return;

        if (amberPhase)
        {
            SetAllRed();
            amberPhase = false;
            allRedPhase = true;
            timer = allRedDuration;
        }
        else if (allRedPhase)
        {
            currentIndex = (currentIndex + 1) % activePhases.Count;
            SetPhase(activePhases[currentIndex]);
            allRedPhase = false;
            timer = greenDuration;
        }
        else
        {
            SetAmber();
            amberPhase = true;
            timer = amberDuration;
        }
    }

    // Public controls
    public void StartCycle()
    {
        if (activePhases.Count == 0) return;

        running = true;
        amberPhase = false;
        allRedPhase = false;

        // If we're already mid-cycle, keep currentIndex; otherwise ensure valid
        currentIndex = Mathf.Clamp(currentIndex, 0, Mathf.Max(0, activePhases.Count - 1));

        SetPhase(activePhases[currentIndex]);
        timer = greenDuration;

        Debug.Log("[TrafficLightController] Cycle started");
    }

    public void StopCycle()
    {
        running = false;
        amberPhase = false;
        allRedPhase = false;

        SetAllRed();
        Debug.Log("[TrafficLightController] Cycle stopped (all red)");
    }

    public void ToggleCycle()
    {
        if (running) StopCycle(); else StartCycle();
    }

    public bool IsRunning() => running;

    // Internals
    private void SetPhase(int phase)
    {
        bool north = true, east = true, south = true, west = true;

        switch (phase)
        {
            case 0: north = false; break;
            case 1: east  = false; break;
            case 2: south = false; break;
            case 3: west  = false; break;
        }

        if (trafficSimulator)
        {
            trafficSimulator.NorthBlocker(north);
            trafficSimulator.EastBlocker(east);
            trafficSimulator.SouthBlocker(south);
            trafficSimulator.WestBlocker(west);
        }

        if (northSignal) northSignal.SetState(north ? LightState.Red : LightState.Green);
        if (eastSignal)  eastSignal.SetState(east  ? LightState.Red : LightState.Green);
        if (southSignal) southSignal.SetState(south ? LightState.Red : LightState.Green);
        if (westSignal)  westSignal.SetState(west  ? LightState.Red : LightState.Green);

        Debug.Log($"[TrafficLightController] Green: {(phase == 0 ? "North" : phase == 1 ? "East" : phase == 2 ? "South" : "West")}");
    }

    private void SetAmber()
    {
        if (northSignal) northSignal.SetState(activePhases.Contains(0) && activePhases[currentIndex] == 0 ? LightState.Amber : LightState.Red);
        if (eastSignal)  eastSignal.SetState(activePhases.Contains(1) && activePhases[currentIndex] == 1 ? LightState.Amber : LightState.Red);
        if (southSignal) southSignal.SetState(activePhases.Contains(2) && activePhases[currentIndex] == 2 ? LightState.Amber : LightState.Red);
        if (westSignal)  westSignal.SetState(activePhases.Contains(3) && activePhases[currentIndex] == 3 ? LightState.Amber : LightState.Red);

        Debug.Log("[TrafficLightController] Amber phase");
    }

    private void SetAllRed()
    {
        if (trafficSimulator)
        {
            trafficSimulator.NorthBlocker(true);
            trafficSimulator.EastBlocker(true);
            trafficSimulator.SouthBlocker(true);
            trafficSimulator.WestBlocker(true);
        }

        if (northSignal) northSignal.SetState(LightState.Red);
        if (eastSignal)  eastSignal.SetState(LightState.Red);
        if (southSignal) southSignal.SetState(LightState.Red);
        if (westSignal)  westSignal.SetState(LightState.Red);

        Debug.Log("[TrafficLightController] All red phase");
    }

    private void AutoBindSignalsByPosition()
    {
        var center = trafficSimulator ? trafficSimulator.transform.position : Vector3.zero;
        var lamps = FindObjectsByType<TrafficLight>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (lamps == null || lamps.Length == 0) return;

        TrafficLight bestNorth = null, bestEast = null, bestSouth = null, bestWest = null;
        float bestNorthSqr = float.MaxValue, bestEastSqr = float.MaxValue, bestSouthSqr = float.MaxValue, bestWestSqr = float.MaxValue;

        foreach (var lamp in lamps)
        {
            var p = lamp.transform.position;
            Vector3 delta = p - center; delta.y = 0f;
            if (delta == Vector3.zero) continue;
            float sqr = delta.sqrMagnitude;

            if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z))
            {
                if (delta.x < 0f) { if (sqr < bestWestSqr) { bestWestSqr = sqr; bestWest = lamp; } }
                else { if (sqr < bestEastSqr) { bestEastSqr = sqr; bestEast = lamp; } }
            }
            else
            {
                if (delta.z < 0f) { if (sqr < bestSouthSqr) { bestSouthSqr = sqr; bestSouth = lamp; } }
                else { if (sqr < bestNorthSqr) { bestNorthSqr = sqr; bestNorth = lamp; } }
            }
        }

        if (!northSignal) northSignal = bestNorth;
        if (!eastSignal)  eastSignal  = bestEast;
        if (!southSignal) southSignal = bestSouth;
        if (!westSignal)  westSignal  = bestWest;

        Debug.Log($"[TrafficLightController] Auto-bound lamps N:{northSignal} E:{eastSignal} S:{southSignal} W:{westSignal}");
    }
}
