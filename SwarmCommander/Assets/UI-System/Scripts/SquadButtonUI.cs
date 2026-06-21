using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class SquadButtonUI : MonoBehaviour
{
    [Header("Squad")]
    [SerializeField, Range(1, 4)] private int squadNumber = 1;

    [Header("Visual")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField, Range(0f, 1f)] private float assignedAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float emptyAlpha = 0.35f;

    private Button button;
    private bool isSubscribed;
    private int lastSquadCount = -1;

    private int SquadIndex => squadNumber - 1;

    private void Awake()
    {
        button = GetComponent<Button>();

        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        button.onClick.AddListener(HandleClick);
    }

    private void OnEnable()
    {
        TrySubscribe();
        RefreshVisual();
    }

    private void OnDisable()
    {
        if (isSubscribed && GameManager.Instance != null)
        {
            GameManager.Instance.OnSquadsChanged -= RefreshVisual;
            GameManager.Instance.OnSelectionChanged -= RefreshVisual;
        }

        isSubscribed = false;
    }

    private void Update()
    {
        TrySubscribe();

        if (GameManager.Instance == null) return;

        int squadCount = GameManager.Instance.GetSquadCount(SquadIndex);
        if (squadCount == lastSquadCount) return;

        RefreshVisual();
    }

    private void HandleClick()
    {
        if (GameManager.Instance == null) return;

        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (shiftHeld)
            GameManager.Instance.AssignSquad(SquadIndex);
        else
            GameManager.Instance.SelectSquad(SquadIndex);

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (canvasGroup == null) return;

        int squadCount = GameManager.Instance != null ? GameManager.Instance.GetSquadCount(SquadIndex) : 0;
        lastSquadCount = squadCount;

        bool hasSquad = squadCount > 0;
        canvasGroup.alpha = hasSquad ? assignedAlpha : emptyAlpha;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void TrySubscribe()
    {
        if (isSubscribed || GameManager.Instance == null) return;

        GameManager.Instance.OnSquadsChanged += RefreshVisual;
        GameManager.Instance.OnSelectionChanged += RefreshVisual;
        isSubscribed = true;
    }
}
