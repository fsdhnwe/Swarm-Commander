using UnityEngine;

public class Headquarters : MonoBehaviour
{
    [Header("生命值設定 (主堡血量設厚一點)")]
    public int maxHP = 1000;
    private int currentHP;

    [Header("血量警告閾值")]
    [Tooltip("血量低於這個百分比時，Console 會印出警告提醒，方便之後加 UI 血條用")]
    [Range(0f, 1f)]
    public float warningThreshold = 0.3f;
    private bool warningTriggered = false;

    [Header("摧毀特效 (拖入爆炸 Prefab，可留空)")]
    public GameObject destroyEffectPrefab;

    [Header("外觀模型 (可留空，之後美術模型來了拖進來)")]
    public Transform visualModel;

    [Header("測試用 — 自動自毀")]
    [Tooltip("勾選後遊戲開始 3 秒會自動摧毀主堡，測試完記得取消勾選")]
    public bool autoDestroyForTest = false;

    public bool IsDestroyed { get; private set; } = false;

    void Awake()
    {
        currentHP = maxHP;
    }

    void Start()
    {
        if (autoDestroyForTest)
        {
            Debug.LogWarning("[主堡] ⚠️ 測試模式：3 秒後自動摧毀，測試完記得在 Inspector 取消勾選 Auto Destroy For Test");
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

        currentHP -= amount;
        Debug.Log($"[主堡] 受到 {amount} 傷害，剩餘 HP: {currentHP} / {maxHP}");

        if (!warningTriggered && (float)currentHP / maxHP <= warningThreshold)
        {
            warningTriggered = true;
            Debug.LogWarning($"[主堡] ⚠️ 血量嚴重不足！剩餘 {currentHP} / {maxHP}");
        }

        if (currentHP <= 0)
        {
            currentHP = 0;
            Destroyed();
        }
    }

    private void Destroyed()
    {
        IsDestroyed = true;
        Debug.Log("[主堡] 已被摧毀！遊戲結束！");

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
            Debug.LogWarning("[主堡] 場景中找不到 GameManager，請確認已建立並掛上 GameManager.cs");
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
