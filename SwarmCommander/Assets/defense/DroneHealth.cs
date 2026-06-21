using System;
using UnityEngine;

public class DroneHealth : MonoBehaviour
{
    public int maxHP = 50;
    public bool disableCollidersOnDeath = true;
    public bool disableThisObjectOnDeath = false;
    public bool destroyRootOnDeath = false;
    private int currentHP;

    public int CurrentHP => currentHP;
    public float NormalizedHealth => maxHP > 0 ? Mathf.Clamp01((float)currentHP / maxHP) : 0f;

    public event Action<DroneHealth> OnHealthChanged;
    public event Action<DroneHealth> OnDied;

    private void Awake()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(int amount)
    {
        currentHP = Mathf.Max(0, currentHP - amount);
        OnHealthChanged?.Invoke(this);

        Debug.Log($"{gameObject.name} took {amount} damage, HP: {currentHP}");

        if (currentHP > 0) return;

        Debug.Log($"{gameObject.name} destroyed.");
        NotifySelectableDroneDied();
        OnDied?.Invoke(this);
        ApplyDeathObjectState();
    }

    private void NotifySelectableDroneDied()
    {
        ISelectableDrone selectableDrone = GetComponentInParent<ISelectableDrone>();
        if (selectableDrone != null && GameManager.Instance != null)
            GameManager.Instance.RemoveDroneFromAllGroups(selectableDrone);
    }

    private void ApplyDeathObjectState()
    {
        ISelectableDrone selectableDrone = GetComponentInParent<ISelectableDrone>();
        GameObject root = selectableDrone?.GameObject != null ? selectableDrone.GameObject : gameObject;

        if (destroyRootOnDeath)
        {
            Destroy(root);
            return;
        }

        if (disableCollidersOnDeath)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>();
            foreach (Collider collider in colliders)
                collider.enabled = false;
        }

        if (disableThisObjectOnDeath)
            gameObject.SetActive(false);
    }
}
