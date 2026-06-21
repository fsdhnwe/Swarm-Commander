using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnitInfoPanelUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup panelCanvasGroup;

    [Header("UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image healthFillImage;
    [SerializeField] private TMP_Text functionText;
    [SerializeField] private TMP_Text squadText;
    [SerializeField] private TMP_Text skillText;

    private GameObject currentObject;
    private UnitInfoProfile currentProfile;
    private ISelectableDrone currentDrone;
    private TargetableObject currentTarget;
    private DroneHealth currentDroneHealth;
    private BuildingHealth currentBuildingHealth;
    private Headquarters currentHeadquarters;

    private void Awake()
    {
        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (panelRoot == null)
            panelRoot = gameObject;

        if (panelCanvasGroup == null)
        {
            panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (panelCanvasGroup == null)
                panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();
        }
    }

    private void OnEnable()
    {
        if (selectionManager != null)
            selectionManager.OnInfoTargetChanged += Show;

        SetVisible(false);
    }

    private void OnDisable()
    {
        if (selectionManager != null)
            selectionManager.OnInfoTargetChanged -= Show;
    }

    private void Update()
    {
        if (currentObject == null) return;
        RefreshHealth();
        RefreshSquad();
    }

    public void Show(GameObject target)
    {
        currentObject = target;

        if (currentObject == null)
        {
            Clear();
            return;
        }

        currentProfile = currentObject.GetComponentInParent<UnitInfoProfile>();
        currentDrone = currentObject.GetComponentInParent<ISelectableDrone>();
        currentTarget = currentObject.GetComponentInParent<TargetableObject>();
        currentDroneHealth = currentObject.GetComponentInParent<DroneHealth>();
        currentBuildingHealth = currentObject.GetComponentInParent<BuildingHealth>();
        currentHeadquarters = currentObject.GetComponentInParent<Headquarters>();

        SetVisible(true);
        RefreshStaticInfo();
        RefreshHealth();
        RefreshSquad();
    }

    public void Clear()
    {
        currentObject = null;
        currentProfile = null;
        currentDrone = null;
        currentTarget = null;
        currentDroneHealth = null;
        currentBuildingHealth = null;
        currentHeadquarters = null;
        SetVisible(false);
    }

    private void RefreshStaticInfo()
    {
        string displayName = currentProfile != null
            ? currentProfile.DisplayName
            : currentObject.name;

        if (nameText != null)
            nameText.text = displayName;

        if (iconImage != null)
        {
            Sprite icon = currentProfile != null ? currentProfile.icon : null;
            if (icon == null && currentDrone != null)
                icon = currentDrone.PortraitIcon;

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (functionText != null)
            functionText.text = currentProfile != null && !string.IsNullOrWhiteSpace(currentProfile.functionSummary)
                ? currentProfile.functionSummary
                : GuessFunctionSummary();

        if (skillText != null)
            skillText.text = currentProfile != null && !string.IsNullOrWhiteSpace(currentProfile.skillDescription)
                ? currentProfile.skillDescription
                : GuessSkillDescription();
    }

    private void RefreshHealth()
    {
        float current;
        float max;
        if (!TryGetHealth(out current, out max))
        {
            if (healthText != null)
                healthText.text = "-";
            if (healthFillImage != null)
                healthFillImage.fillAmount = 0f;
            return;
        }

        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";

        if (healthFillImage != null)
            healthFillImage.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    private void RefreshSquad()
    {
        if (squadText == null) return;

        if (currentDrone != null && GameManager.Instance != null)
        {
            squadText.text = GameManager.Instance.GetSquadLabel(currentDrone);
            return;
        }

        squadText.text = currentProfile != null ? currentProfile.fallbackSquadLabel : "-";
    }

    private bool TryGetHealth(out float current, out float max)
    {
        if (currentDroneHealth != null)
        {
            current = currentDroneHealth.CurrentHP;
            max = currentDroneHealth.maxHP;
            return true;
        }

        if (currentHeadquarters != null)
        {
            current = currentHeadquarters.CurrentHP;
            max = currentHeadquarters.maxHP;
            return true;
        }

        if (currentBuildingHealth != null)
        {
            current = currentBuildingHealth.CurrentHP;
            max = currentBuildingHealth.maxHP;
            return true;
        }

        if (currentTarget != null)
        {
            current = currentTarget.CurrentHealth;
            max = currentTarget.maxHealth;
            return true;
        }

        current = 0f;
        max = 0f;
        return false;
    }

    private string GuessFunctionSummary()
    {
        if (currentObject.GetComponentInParent<ReconDroneUnit>() != null) return "Recon / Detection";
        if (currentObject.GetComponentInParent<DecoyDroneUnit>() != null) return "Decoy / MALD deployment";
        if (currentObject.GetComponentInParent<ShahedDroneUnit>() != null) return "Attack / Strike";
        if (currentObject.GetComponentInParent<DroneUnit>() != null) return "Attack / Damage";
        if (currentObject.GetComponentInParent<RadarStation2>() != null) return "Detection / Tracking";
        if (currentObject.GetComponentInParent<JammerTower>() != null) return "Jamming / Disruption";
        if (currentObject.GetComponentInParent<DefenseInterceptor>() != null) return "Air Defense / Interception";
        if (currentObject.GetComponentInParent<CommunicationCenter>() != null) return "Command Network Support";
        if (currentObject.GetComponentInParent<Headquarters>() != null) return "Command Center";
        return "-";
    }

    private string GuessSkillDescription()
    {
        if (currentObject.GetComponentInParent<ReconDroneUnit>() != null) return "Scans the area around the drone and confirms enemy intel for a limited time.";
        if (currentObject.GetComponentInParent<DecoyDroneUnit>() != null) return "Deploys a MALD decoy along a planned path to draw defensive fire.";
        if (currentObject.GetComponentInParent<ShahedDroneUnit>() != null) return "Flies into a selected target and detonates on impact.";
        if (currentObject.GetComponentInParent<DroneUnit>() != null) return "Attacks visible targets with missiles from range.";
        if (currentObject.GetComponentInParent<RadarStation2>() != null) return "Detects player drones within its radar radius.";
        if (currentObject.GetComponentInParent<JammerTower>() != null) return "Disrupts drones inside its jamming radius.";
        if (currentObject.GetComponentInParent<DefenseInterceptor>() != null) return "Detects, tracks, and fires at hostile drones in range.";
        if (currentObject.GetComponentInParent<CommunicationCenter>() != null) return "Contributes to the defense command network.";
        if (currentObject.GetComponentInParent<Headquarters>() != null) return "Primary command objective. Destroying it ends the mission.";
        return "-";
    }

    private void SetVisible(bool visible)
    {
        if (panelRoot != null && panelRoot != gameObject)
        {
            panelRoot.SetActive(visible);
            return;
        }

        if (panelCanvasGroup == null) return;

        panelCanvasGroup.alpha = visible ? 1f : 0f;
        panelCanvasGroup.interactable = visible;
        panelCanvasGroup.blocksRaycasts = visible;
    }
}
