using UnityEngine;

public class Headquarters : MonoBehaviour
{
    [Header("Health")]
    public int maxHP = 1000;

    [Header("Warning")]
    [Tooltip("Triggers a warning when HP falls below this percentage.")]
    [Range(0f, 1f)]
    public float warningThreshold = 0.3f;

    [Header("Destroy Effect")]
    public GameObject destroyEffectPrefab;

    [Header("Visual Model")]
    public Transform visualModel;

    [Header("Test")]
    [Tooltip("Automatically destroys this headquarters after 3 seconds for testing.")]
    public bool autoDestroyForTest = false;

    private bool warningTriggered;

    public bool IsDestroyed { get; private set; }
    public int CurrentHP { get; private set; }

    void Awake()
    {
        CurrentHP = maxHP;
    }

    void Start()
    {
        if (autoDestroyForTest)
        {
            Debug.LogWarning("[Headquarters] Auto destroy test is enabled.");
            Invoke(nameof(TestDestroy), 3f);
        }
    }

    private void TestDestroy()
    {
        TakeDamage(maxHP + 1);
    }

    public void TakeDamage(int amount)
    {
        if (IsDestroyed) return;

        CurrentHP = Mathf.Max(0, CurrentHP - amount);
        Debug.Log($"[Headquarters] Took {amount} damage. HP: {CurrentHP} / {maxHP}");

        if (!warningTriggered && (float)CurrentHP / maxHP <= warningThreshold)
        {
            warningTriggered = true;
            Debug.LogWarning($"[Headquarters] Critical HP: {CurrentHP} / {maxHP}");
        }

        if (CurrentHP <= 0)
            Destroyed();
    }

    private void Destroyed()
    {
        IsDestroyed = true;
        Debug.Log("[Headquarters] Destroyed. Game over.");

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

        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerGameOver();
        }
        else
        {
            Debug.LogWarning("[Headquarters] GameManager not found. Pausing game.");
            Time.timeScale = 0f;
        }

        gameObject.SetActive(false);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.8f, 0f, 0.8f, 0.5f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * 3f);
    }
}
