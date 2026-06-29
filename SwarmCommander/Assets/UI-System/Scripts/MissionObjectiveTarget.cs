using System;
using UnityEngine;

public enum ObjectiveTargetCategory
{
    CommandCenter,
    AirDefense,
    CommunicationCenter,
    General,
    Radar,
    Jammer,
    Other
}

public class MissionObjectiveTarget : MonoBehaviour
{
    [Header("Identity")]
    public string targetId;
    public string displayName;
    public ObjectiveTargetCategory category = ObjectiveTargetCategory.Other;

    private BuildingHealth buildingHealth;
    private TargetableObject targetableObject;
    private bool isCompleted;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public bool IsCompleted => isCompleted || IsDestroyed();

    public event Action<MissionObjectiveTarget> Completed;

    private void Awake()
    {
        buildingHealth = GetComponentInParent<BuildingHealth>();
        targetableObject = GetComponentInParent<TargetableObject>();

        if (string.IsNullOrWhiteSpace(targetId))
            targetId = gameObject.name;
    }

    private void OnEnable()
    {
        if (buildingHealth != null)
            buildingHealth.onDestroyed.AddListener(HandleDestroyed);


        if (targetableObject != null)
            targetableObject.Destroyed += HandleTargetableDestroyed;

        if (IsDestroyed())
            MarkCompleted();
    }

    private void OnDisable()
    {
        if (buildingHealth != null)
            buildingHealth.onDestroyed.RemoveListener(HandleDestroyed);


        if (targetableObject != null)
            targetableObject.Destroyed -= HandleTargetableDestroyed;
    }

    private void HandleDestroyed()
    {
        MarkCompleted();
    }

    private void HandleTargetableDestroyed(TargetableObject target)
    {
        MarkCompleted();
    }

    private void MarkCompleted()
    {
        if (isCompleted) return;

        isCompleted = true;
        Completed?.Invoke(this);
    }

    private bool IsDestroyed()
    {
        if (buildingHealth != null) return buildingHealth.IsDestroyed;
        if (targetableObject != null) return !targetableObject.IsAlive;
        return !gameObject.activeInHierarchy;
    }
}
