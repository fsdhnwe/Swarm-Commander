using UnityEngine;
using UnityEngine.Events;

public class BuildingHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHP = 100;

    [Header("Destroy Effect")]
    public GameObject destroyEffectPrefab;

    [Header("Visual Model")]
    public Transform visualModel;

    [Header("Events")]
    public UnityEvent onDestroyed;

    public bool IsDestroyed { get; private set; }
    public int CurrentHP { get; private set; }

    void Awake()
    {
        CurrentHP = maxHP;
    }

    public void TakeDamage(int amount)
    {
        if (IsDestroyed) return;

        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        Debug.Log($"{gameObject.name} took {amount} damage. HP: {CurrentHP} / {maxHP}");

        if (CurrentHP <= 0)
            Destroyed();
    }

    private void Destroyed()
    {
        IsDestroyed = true;
        Debug.Log($"{gameObject.name} destroyed.");

        if (destroyEffectPrefab != null)
        {
            GameObject effect = Instantiate(destroyEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 6f);
        }

        if (visualModel != null)
        {
            Animator anim = visualModel.GetComponent<Animator>();
            if (anim != null)
                anim.SetTrigger("Destroyed");
        }

        onDestroyed?.Invoke();
        gameObject.SetActive(false);
    }

    void TestDamage()
    {
        TakeDamage(120);
    }
}
