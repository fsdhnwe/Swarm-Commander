using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 綁定手動建立在 Canvas 上的按鈕到 DronePlacementManager 的對應方法。
///
/// 設定方式：
///   1. 在 Canvas 下手動建立 4 個 Button（Fighting / Decoy / Shahed / Cancel）。
///   2. 把這個腳本掛在任意一個物件上（例如 Canvas 本身或 GameManager）。
///   3. 在 Inspector 把 4 個 Button 拖進對應欄位。
///   4. 把 DronePlacementManager 拖進 Placement Manager 欄位（或留空自動尋找）。
/// </summary>
public class DroneSpawnPanelUI : MonoBehaviour
{
    [Header("References")]
    public DronePlacementManager placementManager;

    [Header("Buttons (拖入手動建立好的按鈕)")]
    public Button fightingDroneButton;
    public Button decoyDroneButton;
    public Button shahedDroneButton;
    public Button cancelButton;

    void Awake()
    {
        if (placementManager == null)
            placementManager = FindFirstObjectByType<DronePlacementManager>();
    }

    void Start()
    {
        if (placementManager == null)
        {
            Debug.LogWarning("DroneSpawnPanelUI: PlacementManager not found.");
            return;
        }

        if (fightingDroneButton != null)
            fightingDroneButton.onClick.AddListener(() => placementManager.BeginPlaceFightingDrone());

        if (decoyDroneButton != null)
            decoyDroneButton.onClick.AddListener(() => placementManager.BeginPlaceDecoyDrone());

        if (shahedDroneButton != null)
            shahedDroneButton.onClick.AddListener(() => placementManager.BeginPlaceShahedDrone());

        if (cancelButton != null)
            cancelButton.onClick.AddListener(() => placementManager.CancelPlacement());
    }
}