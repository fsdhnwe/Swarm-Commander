using UnityEngine;

public class UnitInfoProfile : MonoBehaviour
{
    [Header("Identity")]
    public string displayName;
    public Sprite icon;

    [Header("Description")]
    [TextArea(2, 4)] public string functionSummary;
    [TextArea(3, 8)] public string skillDescription;

    [Header("Fallback")]
    public string fallbackSquadLabel = "-";

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
}
