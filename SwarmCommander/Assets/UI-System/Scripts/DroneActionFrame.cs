using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class DroneActionFrame : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private TMP_Text labelText;
    [SerializeField] private Text legacyLabelText;
    [SerializeField] private GameObject selectedHighlight;

    private Button button;
    private UnityAction clickAction;
    private CanvasGroup canvasGroup;

    private void Awake()
    {
        button = GetComponent<Button>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Bind(Sprite icon, string label, UnityAction onClick, bool interactable, bool highlighted, float alpha)
    {
        if (button == null)
            button = GetComponent<Button>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (portraitImage != null)
            portraitImage.sprite = icon;

        if (labelText != null)
            labelText.text = label;

        if (legacyLabelText != null)
            legacyLabelText.text = label;

        if (clickAction != null)
            button.onClick.RemoveListener(clickAction);

        clickAction = onClick;
        if (clickAction != null)
            button.onClick.AddListener(clickAction);

        button.interactable = interactable;

        if (selectedHighlight != null)
            selectedHighlight.SetActive(highlighted);

        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }
    }

    public void SetLabelColor(Color color)
    {
        if (labelText != null)
            labelText.color = color;

        if (legacyLabelText != null)
            legacyLabelText.color = color;
    }

    private void OnDestroy()
    {
        if (button != null && clickAction != null)
            button.onClick.RemoveListener(clickAction);
    }
}
