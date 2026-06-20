using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 掛在底部 UI 面板的容器物件上（例如一個有 Horizontal Layout Group 的 Panel）。
/// 負責監聽 GameManager 的選取變化，動態生成/回收 DroneUnitFrame。
/// </summary>
public class DroneSelectionUI : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private DroneUnitFrame unitFramePrefab;
    [SerializeField] private Transform frameContainer; // 掛 Horizontal Layout Group 的 Transform

    // 物件池：避免每次選取變化都 Instantiate/Destroy，造成 GC 壓力
    private List<DroneUnitFrame> framePool = new List<DroneUnitFrame>();

    private void OnEnable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnSelectionChanged += RefreshUI;
        }
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnSelectionChanged -= RefreshUI;
        }
    }

    private void Start()
    {
        RefreshUI();
    }

    /// <summary>
    /// 根據 GameManager.selectedDrones 重新整理底部 UI 顯示
    /// </summary>
    private void RefreshUI()
    {
        if (GameManager.Instance == null) return;

        var selected = GameManager.Instance.selectedDrones;

        // 確保物件池足夠
        while (framePool.Count < selected.Count)
        {
            DroneUnitFrame frame = Instantiate(unitFramePrefab, frameContainer);
            framePool.Add(frame);
        }

        // 設定每個 frame 對應的無人機，多餘的隱藏
        for (int i = 0; i < framePool.Count; i++)
        {
            if (i < selected.Count)
            {
                framePool[i].gameObject.SetActive(true);
                framePool[i].Bind(selected[i]);
            }
            else
            {
                framePool[i].Unbind();
                framePool[i].gameObject.SetActive(false);
            }
        }
    }
}
