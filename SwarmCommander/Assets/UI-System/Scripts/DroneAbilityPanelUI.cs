using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DroneAbilityPanelUI : MonoBehaviour
{
    [Serializable]
    public class SpawnFrameConfig
    {
        public string displayName;
        public DronePlacementManager.PlacementType placementType;
        public Sprite icon;
        public int cost;
    }

    [Header("References")]
    [SerializeField] private SelectionManager selectionManager;
    [SerializeField] private DronePlacementManager placementManager;

    [Header("Frame Template")]
    [SerializeField] private DroneActionFrame framePrefab;
    [SerializeField] private Transform spawnFrameContainer;
    [SerializeField] private Transform abilityFrameContainer;

    [Header("Spawn Frames")]
    public int money = 300;
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private SpawnFrameConfig[] spawnEntries =
    {
        new SpawnFrameConfig { displayName = "Fighting", placementType = DronePlacementManager.PlacementType.FightingDrone, cost = 100 },
        new SpawnFrameConfig { displayName = "Recon", placementType = DronePlacementManager.PlacementType.ReconDrone, cost = 80 },
        new SpawnFrameConfig { displayName = "Decoy", placementType = DronePlacementManager.PlacementType.DecoyDrone, cost = 60 },
        new SpawnFrameConfig { displayName = "Shahed", placementType = DronePlacementManager.PlacementType.ShahedDrone, cost = 120 },
    };

    [Header("Ability Icons")]
    [SerializeField] private Sprite reconAbilityIcon;
    [SerializeField] private Sprite decoyAbilityIcon;

    [Header("Visual")]
    [SerializeField, Range(0f, 1f)] private float activeAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float availableAlpha = 0.65f;
    [SerializeField, Range(0f, 1f)] private float unavailableAlpha = 0.25f;
    [SerializeField] private Color affordableCostColor = Color.white;
    [SerializeField] private Color unaffordableCostColor = Color.red;

    private readonly List<DroneActionFrame> spawnFrames = new();
    private readonly List<DroneActionFrame> abilityFrames = new();
    private readonly List<SelectionManager.AbilityDroneType> abilityTypes = new();

    private void Awake()
    {
        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (placementManager == null)
            placementManager = FindAnyObjectByType<DronePlacementManager>();

        if (abilityFrameContainer == null)
            abilityFrameContainer = spawnFrameContainer;

        RebuildSpawnFrames();
    }

    private void Update()
    {
        RefreshSpawnFrames();
        RefreshAbilityFrames();
    }

    private void RebuildSpawnFrames()
    {
        if (framePrefab == null || spawnFrameContainer == null || spawnEntries == null)
            return;

        while (spawnFrames.Count < spawnEntries.Length)
            spawnFrames.Add(Instantiate(framePrefab, spawnFrameContainer));

        RefreshSpawnFrames();
    }

    private void RefreshSpawnFrames()
    {
        if (spawnEntries == null)
            return;

        RefreshMoneyText();

        if (placementManager == null)
            placementManager = FindAnyObjectByType<DronePlacementManager>();

        for (int i = 0; i < spawnFrames.Count; i++)
        {
            if (i >= spawnEntries.Length)
            {
                spawnFrames[i].gameObject.SetActive(false);
                continue;
            }

            SpawnFrameConfig entry = spawnEntries[i];
            bool canAfford = money >= entry.cost;
            bool canSpawn = placementManager != null && placementManager.CanPlaceDrone(entry.placementType) && canAfford;

            spawnFrames[i].gameObject.SetActive(true);
            spawnFrames[i].Bind(
                entry.icon,
                $"${entry.cost}",
                () => SpawnDrone(entry),
                canSpawn,
                false,
                canSpawn ? activeAlpha : unavailableAlpha);
            spawnFrames[i].SetLabelColor(canAfford ? affordableCostColor : unaffordableCostColor);
        }
    }

    private void RefreshAbilityFrames()
    {
        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (framePrefab == null || abilityFrameContainer == null || selectionManager == null)
        {
            HideAbilityFrames();
            return;
        }

        selectionManager.GetAvailableAbilityTypesInSelectionOrder(abilityTypes);

        while (abilityFrames.Count < abilityTypes.Count)
            abilityFrames.Add(Instantiate(framePrefab, abilityFrameContainer));

        for (int i = 0; i < abilityFrames.Count; i++)
        {
            if (i >= abilityTypes.Count)
            {
                abilityFrames[i].gameObject.SetActive(false);
                continue;
            }

            SelectionManager.AbilityDroneType type = abilityTypes[i];
            bool active = selectionManager.ActiveAbilityType == type;

            abilityFrames[i].gameObject.SetActive(true);
            abilityFrames[i].Bind(
                GetAbilityIcon(type),
                "E",
                () => UseAbility(type),
                true,
                active,
                active ? activeAlpha : availableAlpha);
        }
    }

    private void HideAbilityFrames()
    {
        foreach (DroneActionFrame frame in abilityFrames)
        {
            if (frame != null)
                frame.gameObject.SetActive(false);
        }
    }

    public void AddMoney(int amount)
    {
        money = Mathf.Max(0, money + amount);
        RefreshSpawnFrames();
    }

    public bool SpendMoney(int amount)
    {
        if (amount < 0) return false;
        if (money < amount) return false;

        money -= amount;
        RefreshSpawnFrames();
        return true;
    }

    private void RefreshMoneyText()
    {
        if (moneyText != null)
            moneyText.text = $"${money}";
    }

    private void SpawnDrone(SpawnFrameConfig entry)
    {
        if (entry == null) return;

        if (placementManager == null)
            placementManager = FindAnyObjectByType<DronePlacementManager>();

        if (placementManager == null) return;
        if (!SpendMoney(entry.cost)) return;

        placementManager.BeginPlaceDrone(entry.placementType);
    }

    private void UseAbility(SelectionManager.AbilityDroneType type)
    {
        if (selectionManager == null) return;

        selectionManager.SetActiveAbilityTypeFromUI(type);

        switch (type)
        {
            case SelectionManager.AbilityDroneType.Recon:
                selectionManager.BeginReconAbilityTargeting();
                break;
            case SelectionManager.AbilityDroneType.Decoy:
                selectionManager.BeginDecoyAbility();
                break;
        }
    }

    private Sprite GetAbilityIcon(SelectionManager.AbilityDroneType type)
    {
        switch (type)
        {
            case SelectionManager.AbilityDroneType.Recon:
                return reconAbilityIcon;
            case SelectionManager.AbilityDroneType.Decoy:
                return decoyAbilityIcon;
            default:
                return null;
        }
    }
}
