using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ToolButtonMobile : MonoBehaviour
{
    public PlacementMobileManager manager;
    public int prefabIndex;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(() =>
        {
            manager.SelectPrefabByIndex(prefabIndex);
        });
    }
}
