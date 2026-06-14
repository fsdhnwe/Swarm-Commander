using UnityEngine;

/// <summary>
/// Simple selectable target for drones.
/// Can be highlighted, selected, and damaged by drone attacks.
/// </summary>
public class TargetableObject : MonoBehaviour
{
    [Header("Selection Visual")]
    public GameObject selectionIndicator;
    public Color outlineColor = new Color(1f, 0.35f, 0.2f, 1f);
    public Color hoverColor = Color.white;

    [Header("Health")]
    public float maxHealth = 100f;

    [Tooltip("Optional point that missiles and drones should aim at. Useful for large buildings.")]
    public Transform aimPoint;

    [Tooltip("If enabled, the object is destroyed when health reaches zero.")]
    public bool destroyOnDeath = true;

    private Outline _outline;
    private BuildingHealth _buildingHealth;
    private Headquarters _headquarters;
    private bool _isSelected;
    private bool _isHovered;
    private float _currentHealth;

    public bool IsAlive
    {
        get
        {
            if (_buildingHealth != null) return !_buildingHealth.IsDestroyed;
            if (_headquarters != null) return !_headquarters.IsDestroyed;
            return _currentHealth > 0f;
        }
    }

    public float CurrentHealth
    {
        get
        {
            if (_buildingHealth != null) return _buildingHealth.CurrentHP;
            if (_headquarters != null) return _headquarters.CurrentHP;
            return _currentHealth;
        }
    }

    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;

    void Awake()
    {
        _outline = GetComponentInChildren<Outline>();
        _buildingHealth = GetComponentInParent<BuildingHealth>();
        _headquarters = GetComponentInParent<Headquarters>();
        _currentHealth = Mathf.Max(1f, maxHealth);

        RefreshOutline();

        SetSelectionIndicator(false);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        RefreshOutline();

        SetSelectionIndicator(selected);
    }

    public void SetHovered(bool hovered)
    {
        _isHovered = hovered;
        RefreshOutline();
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;

        int roundedDamage = Mathf.CeilToInt(amount);
        if (_buildingHealth != null)
        {
            _buildingHealth.TakeDamage(roundedDamage);
            return;
        }

        if (_headquarters != null)
        {
            _headquarters.TakeDamage(roundedDamage);
            return;
        }

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);

        if (_currentHealth <= 0f)
        {
            if (destroyOnDeath)
            {
                Destroy(gameObject);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }

    void SetSelectionIndicator(bool show)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(show);
    }

    void RefreshOutline()
    {
        if (_outline == null) return;

        bool showOutline = _isHovered || _isSelected;
        _outline.enabled = showOutline;

        if (showOutline)
            _outline.OutlineColor = _isHovered ? hoverColor : outlineColor;
    }
}
