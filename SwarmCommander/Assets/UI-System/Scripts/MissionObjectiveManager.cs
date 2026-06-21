using System;
using System.Collections.Generic;
using UnityEngine;

public enum MissionObjectiveType
{
    DestroySpecificTargets,
    DestroyAllByCategory,
    DestroyCountByCategory
}

[Serializable]
public class MissionObjective
{
    public string title;
    public MissionObjectiveType objectiveType = MissionObjectiveType.DestroySpecificTargets;
    public ObjectiveTargetCategory category = ObjectiveTargetCategory.Other;
    public int requiredCount = 1;
    public List<MissionObjectiveTarget> specificTargets = new();

    [NonSerialized] public int completedCount;
    [NonSerialized] public int totalCount;
    [NonSerialized] public bool isCompleted;
}

public class MissionObjectiveManager : MonoBehaviour
{
    public static MissionObjectiveManager Instance { get; private set; }

    [Header("Objectives")]
    public List<MissionObjective> objectives = new();

    [Header("Auto Discovery")]
    public bool autoFindTargetsInScene = true;

    private readonly List<MissionObjectiveTarget> allTargets = new();
    private bool allObjectivesCompletedRaised;

    public IReadOnlyList<MissionObjective> Objectives => objectives;
    public event Action ObjectivesChanged;
    public event Action AllObjectivesCompleted;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        RefreshTargets();
        SubscribeTargets();
        RecalculateAll();
    }

    private void OnDisable()
    {
        UnsubscribeTargets();
    }

    public void RefreshTargets()
    {
        allTargets.Clear();

        if (autoFindTargetsInScene)
            allTargets.AddRange(FindObjectsByType<MissionObjectiveTarget>());

        foreach (MissionObjective objective in objectives)
        {
            foreach (MissionObjectiveTarget target in objective.specificTargets)
            {
                if (target != null && !allTargets.Contains(target))
                    allTargets.Add(target);
            }
        }
    }

    private void SubscribeTargets()
    {
        foreach (MissionObjectiveTarget target in allTargets)
        {
            if (target != null)
                target.Completed += HandleTargetCompleted;
        }
    }

    private void UnsubscribeTargets()
    {
        foreach (MissionObjectiveTarget target in allTargets)
        {
            if (target != null)
                target.Completed -= HandleTargetCompleted;
        }
    }

    private void HandleTargetCompleted(MissionObjectiveTarget target)
    {
        RecalculateAll();
    }

    private void RecalculateAll()
    {
        bool allCompleted = objectives.Count > 0;

        foreach (MissionObjective objective in objectives)
        {
            RecalculateObjective(objective);
            allCompleted &= objective.isCompleted;
        }

        ObjectivesChanged?.Invoke();

        if (allCompleted && !allObjectivesCompletedRaised)
        {
            allObjectivesCompletedRaised = true;
            AllObjectivesCompleted?.Invoke();
        }
        else if (!allCompleted)
        {
            allObjectivesCompletedRaised = false;
        }
    }

    private void RecalculateObjective(MissionObjective objective)
    {
        switch (objective.objectiveType)
        {
            case MissionObjectiveType.DestroySpecificTargets:
                objective.totalCount = CountValidTargets(objective.specificTargets);
                objective.completedCount = CountCompletedTargets(objective.specificTargets);
                objective.isCompleted = objective.totalCount > 0 && objective.completedCount >= objective.totalCount;
                break;

            case MissionObjectiveType.DestroyAllByCategory:
                objective.totalCount = CountCategoryTargets(objective.category);
                objective.completedCount = CountCompletedCategoryTargets(objective.category);
                objective.isCompleted = objective.totalCount > 0 && objective.completedCount >= objective.totalCount;
                break;

            case MissionObjectiveType.DestroyCountByCategory:
                objective.totalCount = Mathf.Max(1, objective.requiredCount);
                objective.completedCount = Mathf.Min(CountCompletedCategoryTargets(objective.category), objective.totalCount);
                objective.isCompleted = objective.completedCount >= objective.totalCount;
                break;
        }
    }

    private static int CountValidTargets(List<MissionObjectiveTarget> targets)
    {
        int count = 0;
        foreach (MissionObjectiveTarget target in targets)
            if (target != null)
                count++;
        return count;
    }

    private static int CountCompletedTargets(List<MissionObjectiveTarget> targets)
    {
        int count = 0;
        foreach (MissionObjectiveTarget target in targets)
            if (target != null && target.IsCompleted)
                count++;
        return count;
    }

    private int CountCategoryTargets(ObjectiveTargetCategory category)
    {
        int count = 0;
        foreach (MissionObjectiveTarget target in allTargets)
            if (target != null && target.category == category)
                count++;
        return count;
    }

    private int CountCompletedCategoryTargets(ObjectiveTargetCategory category)
    {
        int count = 0;
        foreach (MissionObjectiveTarget target in allTargets)
            if (target != null && target.category == category && target.IsCompleted)
                count++;
        return count;
    }
}
