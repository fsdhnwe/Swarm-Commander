using UnityEngine;

// 全局「通訊完整度」管理。
// 在場景裡建立一個空的 GameObject (例如叫 "CommandNetwork")，把這個腳本掛上去，
// 場景裡只需要一份。
//
// 防守方各單位 (DefenseInterceptor.cs) 在開火前會讀取這裡的 commsIntegrity 數值，
// 用來決定「反應延遲」和「瞄準誤差」要放大多少。
//
// commsIntegrity = 1   -> 通訊完全正常
// commsIntegrity = 0   -> 完全失聯 (所有通訊中心都被摧毀)
public class CommandNetwork : MonoBehaviour
{
    public static CommandNetwork Instance;

    [Header("通訊完整度 (1 = 正常，0 = 完全失聯)")]
    [Range(0f, 1f)]
    public float commsIntegrity = 1f;

    void Awake()
    {
        Instance = this;
    }

    // 給 CommunicationCenter.cs 呼叫：每摧毀一座通訊中心，降低一定比例的完整度
    public void ReduceIntegrity(float amount)
    {
        commsIntegrity = Mathf.Clamp01(commsIntegrity - amount);
        Debug.Log($"[CommandNetwork] 通訊完整度降為 {commsIntegrity:F2}");
    }
}
