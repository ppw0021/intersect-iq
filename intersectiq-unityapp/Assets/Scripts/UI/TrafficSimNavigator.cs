using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Globalization;
using System.Collections.Generic;

public class TrafficSimNavigator : MonoBehaviour
{
    [Header("Home Panel")]
    [SerializeField] GameObject homePanel;
    [SerializeField] Button homePanel_mainMenuButton;
    [SerializeField] Button homePanel_startSimulation;
    [SerializeField] Button homePanel_stopSimulation;
    [SerializeField] Button homePanel_editParams;

    [Header("Edit Params Panel")]
    [SerializeField] GameObject editParamsPanel;
    [SerializeField] InputField input_northRate;
    [SerializeField] InputField input_eastRate;
    [SerializeField] InputField input_southRate;
    [SerializeField] InputField input_westRate;
    [SerializeField] InputField input_redSeconds;
    [SerializeField] InputField input_amberSeconds;
    [SerializeField] InputField input_greenSeconds;
    [SerializeField] Button edit_cancelButton;
    [SerializeField] Button edit_confirmButton;

    [Header("Results Panel")]
    [SerializeField] GameObject resultsPanel;
    [SerializeField] Text results_averageStationaryTimeText;
    [SerializeField] Text results_averageVelocityText;
    [SerializeField] Button results_closeButton;

    private TrafficLightController lightController;
    private TrafficSimulator simulator;
    private bool simulationRunning = false;
    private readonly CultureInfo ci = CultureInfo.InvariantCulture;

    private readonly List<CarSpawnerNode> northSpawners = new List<CarSpawnerNode>();
    private readonly List<CarSpawnerNode> eastSpawners  = new List<CarSpawnerNode>();
    private readonly List<CarSpawnerNode> southSpawners = new List<CarSpawnerNode>();
    private readonly List<CarSpawnerNode> westSpawners  = new List<CarSpawnerNode>();

    void Start()
    {
        homePanel_mainMenuButton.onClick.AddListener(homePanel_onHomeClick);
        homePanel_startSimulation.onClick.AddListener(homePanel_onStartSimulationClick);
        homePanel_stopSimulation.onClick.AddListener(homePanel_onStopSimulationClick);
        homePanel_editParams.onClick.AddListener(homePanel_onEditParamsClick);

        edit_cancelButton.onClick.AddListener(edit_onCancelClick);
        edit_confirmButton.onClick.AddListener(edit_onConfirmClick);

        if (results_closeButton)
            results_closeButton.onClick.AddListener(() => { if (resultsPanel) resultsPanel.SetActive(false); });

        lightController = FindFirstObjectByType<TrafficLightController>();
        simulator       = FindFirstObjectByType<TrafficSimulator>();

        RebuildSpawnerBuckets();
        if (editParamsPanel) editParamsPanel.SetActive(false);
        if (resultsPanel) resultsPanel.SetActive(false);
        UpdateButtonStates();
    }

    void homePanel_onHomeClick()
    {
        SceneManager.LoadScene("StartScreen");
    }

    void homePanel_onStartSimulationClick()
    {
        if (simulationRunning) return;
        
        SceneParameters.StartSimulation();
        if (!lightController) lightController = FindFirstObjectByType<TrafficLightController>();
        if (lightController) lightController.StartCycle();

        simulationRunning = true;
        UpdateButtonStates();
    }

    void homePanel_onStopSimulationClick()
    {
        if (!simulationRunning) return;

        var spawners = FindObjectsByType<CarSpawnerNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in spawners) s.StopSpawning();

        if (!lightController) lightController = FindFirstObjectByType<TrafficLightController>();
        if (lightController) lightController.StopCycle();

        ShowSimulationResults();
        simulationRunning = false;
        UpdateButtonStates();

        // Remove all cars after displaying results
        ClearAllCars();
    }

    void homePanel_onEditParamsClick()
    {
        RebuildSpawnerBuckets();
        if (!lightController) lightController = FindFirstObjectByType<TrafficLightController>();

        SetInputText(input_northRate, AverageRate(northSpawners));
        SetInputText(input_eastRate,  AverageRate(eastSpawners));
        SetInputText(input_southRate, AverageRate(southSpawners));
        SetInputText(input_westRate,  AverageRate(westSpawners));

        if (lightController)
        {
            SetInputText(input_redSeconds,   lightController.allRedDuration);
            SetInputText(input_amberSeconds, lightController.amberDuration);
            SetInputText(input_greenSeconds, lightController.greenDuration);
        }

        if (editParamsPanel) editParamsPanel.SetActive(true);
    }

    void edit_onCancelClick()
    {
        if (editParamsPanel) editParamsPanel.SetActive(false);
    }

    void edit_onConfirmClick()
    {
        float northRate = ParsePositive(input_northRate);
        float eastRate  = ParsePositive(input_eastRate);
        float southRate = ParsePositive(input_southRate);
        float westRate  = ParsePositive(input_westRate);

        if (northRate > 0f) ApplyRateToSpawners(northSpawners, northRate);
        if (eastRate  > 0f) ApplyRateToSpawners(eastSpawners,  eastRate);
        if (southRate > 0f) ApplyRateToSpawners(southSpawners, southRate);
        if (westRate  > 0f) ApplyRateToSpawners(westSpawners,  westRate);

        if (lightController)
        {
            float red   = ParsePositive(input_redSeconds);
            float amber = ParsePositive(input_amberSeconds);
            float green = ParsePositive(input_greenSeconds);

            if (red   > 0f) lightController.allRedDuration = red;
            if (amber > 0f) lightController.amberDuration  = amber;
            if (green > 0f) lightController.greenDuration  = green;
        }

        if (editParamsPanel) editParamsPanel.SetActive(false);
    }

    private void UpdateButtonStates()
    {
        homePanel_startSimulation.interactable = !simulationRunning;
        homePanel_stopSimulation.interactable = simulationRunning;
        homePanel_mainMenuButton.interactable = true;
        homePanel_editParams.interactable = true;
    }

    // Results
    private void ShowSimulationResults()
    {
        int carsLayer = LayerMask.NameToLayer("Cars");
        var agents = FindObjectsByType<CarAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        int count = 0;
        double stationarySum = 0.0;
        double avgSpeedSum = 0.0;

        foreach (var a in agents)
        {
            if (a.gameObject.layer != carsLayer) continue;
            count++;

            stationarySum += a.GetTotalStationaryTimeSeconds();
            avgSpeedSum   += a.GetAverageSpeedMetersPerSecond();
        }

        double avgStationary = (count > 0) ? (stationarySum / count) : 0.0;
        double avgSpeed      = (count > 0) ? (avgSpeedSum / count) : 0.0;

        if (results_averageStationaryTimeText)
            results_averageStationaryTimeText.text = $"{avgStationary:0.00} s";

        if (results_averageVelocityText)
            results_averageVelocityText.text = $"{avgSpeed:0.00} m/s";

        if (resultsPanel) resultsPanel.SetActive(true);
    }

    // Cleanup
    private void ClearAllCars()
    {
        int carsLayer = LayerMask.NameToLayer("Cars");
        var cars = FindObjectsByType<CarAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var car in cars)
        {
            if (car.gameObject.layer == carsLayer)
                Destroy(car.gameObject);
        }
    }

    // Helpers
    private void RebuildSpawnerBuckets()
    {
        northSpawners.Clear(); eastSpawners.Clear(); southSpawners.Clear(); westSpawners.Clear();
        Vector3 center = simulator ? simulator.transform.position : Vector3.zero;

        var all = FindObjectsByType<CarSpawnerNode>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var sp in all)
        {
            Vector3 d = sp.transform.position - center; d.y = 0f;

            if (Mathf.Abs(d.x) > Mathf.Abs(d.z))
            {
                if (d.x < 0f) westSpawners.Add(sp);
                else          eastSpawners.Add(sp);
            }
            else
            {
                if (d.z < 0f) southSpawners.Add(sp);
                else          northSpawners.Add(sp);
            }
        }
    }

    private void SetInputText(InputField field, float value)
    {
        if (!field) return;
        field.text = value > 0f ? value.ToString("0.##", ci) : "";
    }

    private float ParsePositive(InputField field)
    {
        if (!field) return -1f;
        if (float.TryParse(field.text, NumberStyles.Float, ci, out float v) && v > 0f) return v;
        return -1f;
    }

    private float RateFromSpawner(CarSpawnerNode spawner)
    {
        if (!spawner) return -1f;
        var f = typeof(CarSpawnerNode).GetField("respawnDelay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f == null) return -1f;
        float delay = (float)f.GetValue(spawner);
        return delay > 0f ? 60f / delay : -1f;
    }

    private float AverageRate(List<CarSpawnerNode> spawners)
    {
        if (spawners == null || spawners.Count == 0) return -1f;
        float sum = 0f; int n = 0;
        foreach (var s in spawners)
        {
            float r = RateFromSpawner(s);
            if (r > 0f) { sum += r; n++; }
        }
        return n > 0 ? sum / n : -1f;
    }

    private void ApplyRateToSpawners(List<CarSpawnerNode> spawners, float carsPerMinute)
    {
        if (spawners == null || spawners.Count == 0 || carsPerMinute <= 0f) return;
        float newDelay = 60f / carsPerMinute;
        var f = typeof(CarSpawnerNode).GetField("respawnDelay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f == null) return;

        foreach (var s in spawners) f.SetValue(s, newDelay);
    }
}
