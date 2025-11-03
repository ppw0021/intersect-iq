using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class TrafficSimNavigator : MonoBehaviour
{
    [SerializeField] GameObject homePanel;
    [SerializeField] Button homePanel_mainMenuButton;
    [SerializeField] Button homePanel_startSimulation;


    void Start()
    {
        // Home panel listeners
        homePanel_mainMenuButton.onClick.AddListener(homePanel_onHomeClick);
    }

    void Update()
    {
    
    }

    void homePanel_onHomeClick()
    {
        SceneManager.LoadScene("StartScreen");
    }

}
