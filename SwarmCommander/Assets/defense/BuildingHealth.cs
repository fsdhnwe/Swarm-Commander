using UnityEngine;
using UnityEngine.Events;


// 這個腳本可以掛在「任何」防守建築上 (雷達站、SAM、AAA、指揮中心都通用)
// 它只負責「生命值」和「被摧毀後要做什麼」，跟外觀完全無關
public class BuildingHealth : MonoBehaviour
{
   // void Start()
    //{
      //  Invoke("TestDamage", 3f); // 3秒後測試扣血
    //}
    [Header("生命值設定")]
    public int maxHP = 100;
    private int currentHP;

    [Header("摧毀特效 (拖 Prefab 到這裡)")]
    public GameObject destroyEffectPrefab;
    [Header("外觀模型 (現在先留空，之後美術模型拖到這裡)")]
    public Transform visualModel;

    [Header("被摧毀時觸發的事件 (可在 Inspector 裡接其他效果)")]
    public UnityEvent onDestroyed;

    public bool IsDestroyed { get; private set; } = false;

    void Awake()
    {
        currentHP = maxHP;
    }

    // 給其他腳本呼叫：造成傷害
    public void TakeDamage(int amount)
    {
        if (IsDestroyed) return;

        currentHP -= amount;
        Debug.Log($"{gameObject.name} 受到 {amount} 傷害，剩餘 HP: {currentHP}");

        if (currentHP <= 0)
        {
            Destroyed();
        }
    }

    private void Destroyed()
    {
        IsDestroyed = true;
        Debug.Log($"{gameObject.name} 已被摧毀！");

        // 如果之後美術模型有掛 Animator，這裡會自動播放「被摧毀」動畫
        // 現在沒有 Animator 也不會報錯，會直接跳過
        if (destroyEffectPrefab != null)
        {
            GameObject effect = Instantiate(destroyEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 6f); // 6秒後清除特效物件 (爆炸1秒+冒煙5秒)
        }

        if (visualModel != null)
        {
            Animator anim = visualModel.GetComponent<Animator>();
            if (anim != null)
            {
                anim.SetTrigger("Destroyed");
            }
        }

        onDestroyed?.Invoke();
        gameObject.SetActive(false);
    }
    void TestDamage()
    {
      TakeDamage(120); // 超過 maxHP，會直接觸發摧毀
    }
}
