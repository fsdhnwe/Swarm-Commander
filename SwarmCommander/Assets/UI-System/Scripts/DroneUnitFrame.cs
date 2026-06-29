using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 掛在底部 UI 單位格子 Prefab 上。
/// Prefab 結構建議：
/// DroneUnitFrame (此腳本 + Button)
///   ├── Portrait (Image)          → portraitImage
///   ├── HealthBarBackground
///   │     └── HealthBarFill (Image, Type=Filled, Horizontal) → healthBarFill
///   └── SelectedHighlight (Image，外框高亮，預設關閉)        → selectedHighlight
/// </summary>
[RequireComponent(typeof(Button))]
public class DroneUnitFrame : MonoBehaviour
{
    [Header("UI 元件參照")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image healthBarFill;
    [SerializeField] private GameObject selectedHighlight; // 可選，用來標示「目前單獨高亮」的單位

    private ISelectableDrone boundDrone;
    private DroneHealth boundHealth;
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClicked);
    }

    /// <summary>
    /// 綁定一台無人機到這個格子，並訂閱其血量變化事件
    /// </summary>
    public void Bind(ISelectableDrone drone)
    {
        // 先解除舊綁定，避免重複訂閱事件造成洩漏
        Unbind();

        boundDrone = drone;
        if (boundDrone == null) return;

        if (portraitImage != null)
            portraitImage.sprite = boundDrone.PortraitIcon;

        boundDrone.OnHealthChanged += HandleHealthChanged;
        boundDrone.OnDied += HandleDied;
        boundHealth = boundDrone.GameObject != null
            ? boundDrone.GameObject.GetComponent<DroneHealth>()
            : null;

        UpdateHealthBar();
        UpdateSelectedHighlight();
    }

    /// <summary>
    /// 解除目前綁定，取消事件訂閱
    /// </summary>
    public void Unbind()
    {
        if (boundDrone != null)
        {
            boundDrone.OnHealthChanged -= HandleHealthChanged;
            boundDrone.OnDied -= HandleDied;
        }
        boundDrone = null;
        boundHealth = null;
    }

    private void HandleHealthChanged(ISelectableDrone drone)
    {
        UpdateHealthBar();
    }

    private void HandleDied(ISelectableDrone drone)
    {
        // GameManager 那邊死亡時會觸發 OnSelectionChanged，
        // DroneSelectionUI 會重新 RefreshUI 把這格收回池子裡，這裡不用額外處理
    }

    private void UpdateHealthBar()
    {
        if (boundDrone == null || healthBarFill == null) return;

        healthBarFill.fillAmount = boundHealth != null ? boundHealth.NormalizedHealth : 1f;
    }

    private void UpdateSelectedHighlight()
    {
        if (selectedHighlight != null)
            selectedHighlight.SetActive(boundDrone != null && boundDrone.IsSelected);
    }

    /// <summary>
    /// 點擊這個格子：
    /// - 一般點擊：單獨選取這台無人機（取消其他選取）
    /// - 之後若要加「再點一次取消選取」或「Ctrl+點擊多選」可以在這裡擴充
    /// </summary>
    private void OnClicked()
    {
        if (boundDrone == null || GameManager.Instance == null) return;

        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (ctrlHeld)
        {
            // Ctrl+點擊：把這台從選取中移除（取消選取單一單位）
            GameManager.Instance.DeselectDrone(boundDrone);
        }
        else
        {
            // 一般點擊：單獨選取這台（會清空其他選取）
            GameManager.Instance.SelectDrone(boundDrone, addToSelection: false);
        }
    }

    private void OnDestroy()
    {
        Unbind();
    }
}
