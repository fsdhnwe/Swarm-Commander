using UnityEngine;

// 測試用：掛在玩家無人機(Tag = PlayerDrone)上
// 讓 SAM/AAA 發射的 SimpleProjectile 命中時可以扣血、擊落
// 之後進攻方的正式無人機腳本完成後，這個可以整合過去或互相參考
public class DroneHealth : MonoBehaviour
{
    public int maxHP = 50;
    private int currentHP;

    void Awake()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(int amount)
    {
        currentHP -= amount;
        Debug.Log($"{gameObject.name} 受到 {amount} 傷害，剩餘 HP: {currentHP}");

        if (currentHP <= 0)
        {
            Debug.Log($"{gameObject.name} 被擊落！");
            gameObject.SetActive(false);
        }
    }
}
