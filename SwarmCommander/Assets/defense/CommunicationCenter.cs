using UnityEngine;

// 通訊中心。被摧毀時會降低全局通訊完整度 (CommandNetwork.commsIntegrity)，
// 進而讓所有 SAM/AAA 的反應變慢、瞄準變不准。
[RequireComponent(typeof(BuildingHealth))]
public class CommunicationCenter : MonoBehaviour
{
    [Header("被摧毀時降低多少通訊完整度")]
    [Tooltip("例如場景裡有 2 座通訊中心，各設 0.5，兩座都毀時 commsIntegrity 會降到 0\n" +
             "只有 1 座的話可以設 1.0 (全毀直接歸零)")]
    [Range(0f, 1f)]
    public float integrityContribution = 0.5f;

    private BuildingHealth health;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
        health.onDestroyed.AddListener(HandleDestroyed);
    }

    private void HandleDestroyed()
    {
        if (CommandNetwork.Instance != null)
        {
            CommandNetwork.Instance.ReduceIntegrity(integrityContribution);
        }
        else
        {
            Debug.LogWarning("場景中找不到 CommandNetwork，請確認已建立並掛上 CommandNetwork.cs");
        }
    }
}
