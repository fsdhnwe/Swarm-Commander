using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MissionObjectiveUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MissionObjectiveManager objectiveManager;
    [SerializeField] private Transform objectiveContainer;
    [SerializeField] private TMP_Text objectiveTextPrefab;

    [Header("Style")]
    [SerializeField] private Color incompleteColor = Color.white;
    [SerializeField] private Color completeColor = new(0.45f, 1f, 0.55f, 1f);
    [SerializeField] private bool hideWhenNoObjectives = true;

    private readonly List<TMP_Text> objectiveRows = new();

    private void Awake()
    {
        if (objectiveManager == null)
            objectiveManager = MissionObjectiveManager.Instance;

        if (objectiveContainer == null)
            objectiveContainer = transform;
    }

    private void OnEnable()
    {
        TrySubscribe();
        Refresh();
    }

    private void OnDisable()
    {
        if (objectiveManager != null)
            objectiveManager.ObjectivesChanged -= Refresh;
    }

    private void TrySubscribe()
    {
        if (objectiveManager == null)
            objectiveManager = MissionObjectiveManager.Instance;

        if (objectiveManager != null)
        {
            objectiveManager.ObjectivesChanged -= Refresh;
            objectiveManager.ObjectivesChanged += Refresh;
        }
    }

    public void Refresh()
    {
        if (objectiveManager == null)
        {
            TrySubscribe();
            if (objectiveManager == null) return;
        }

        IReadOnlyList<MissionObjective> objectives = objectiveManager.Objectives;
        EnsureRowCount(objectives.Count);

        for (int i = 0; i < objectiveRows.Count; i++)
        {
            bool active = i < objectives.Count;
            TMP_Text row = objectiveRows[i];
            row.gameObject.SetActive(active);

            if (!active) continue;

            MissionObjective objective = objectives[i];
            row.text = BuildObjectiveText(objective);
            row.color = objective.isCompleted ? completeColor : incompleteColor;
        }

        if (hideWhenNoObjectives)
            gameObject.SetActive(objectives.Count > 0);
    }

    private void EnsureRowCount(int count)
    {
        while (objectiveRows.Count < count)
        {
            TMP_Text row = Instantiate(objectiveTextPrefab, objectiveContainer);
            objectiveRows.Add(row);
        }
    }

    private static string BuildObjectiveText(MissionObjective objective)
    {
        string status = objective.isCompleted ? "[x]" : "[ ]";
        string title = string.IsNullOrWhiteSpace(objective.title)
            ? GetFallbackTitle(objective)
            : objective.title;

        string progress = objective.totalCount > 1 || objective.objectiveType == MissionObjectiveType.DestroyCountByCategory
            ? $" {objective.completedCount}/{objective.totalCount}"
            : string.Empty;

        return $"{status} {title}{progress}";
    }

    private static string GetFallbackTitle(MissionObjective objective)
    {
        return objective.objectiveType switch
        {
            MissionObjectiveType.DestroyAllByCategory => $"Destroy all {objective.category}",
            MissionObjectiveType.DestroyCountByCategory => $"Destroy {objective.requiredCount} {objective.category}",
            _ => "Destroy target"
        };
    }
}
