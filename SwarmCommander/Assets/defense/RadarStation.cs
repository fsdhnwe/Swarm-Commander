using UnityEngine;

// 雷達站專用腳本
// 功能：偵測敵方無人機進入範圍，並切換狀態 (Idle / Detecting)
[RequireComponent(typeof(BuildingHealth))]
public class RadarStation : MonoBehaviour
{
    [Header("偵測範圍 (公尺)")]
    public float detectionRadius = 15f;

    // 簡單的狀態機 (FSM)
    public enum RadarState { Idle, Detecting }
    public RadarState currentState = RadarState.Idle;

    private BuildingHealth health;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        // 自動加上一個「偵測範圍」用的觸發器 (Trigger)
        SphereCollider detectionTrigger = gameObject.AddComponent<SphereCollider>();
        detectionTrigger.isTrigger = true;
        detectionTrigger.radius = detectionRadius;
    }

    // 有東西進入偵測範圍時觸發
    void OnTriggerEnter(Collider other)
    {
        if (health.IsDestroyed) return;

        // 注意：玩家的無人機方塊要在 Inspector 裡把 Tag 設成 "PlayerDrone"
        if (other.CompareTag("PlayerDrone"))
        {
            currentState = RadarState.Detecting;
            Debug.Log("雷達站偵測到敵方無人機: " + other.name);
        }
    }

    // 東西離開偵測範圍時
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerDrone"))
        {
            currentState = RadarState.Idle;
            Debug.Log("敵方無人機離開偵測範圍");
        }
    }

    // 在 Scene 視窗畫出一個圓圈，方便妳看到偵測範圍多大
    // 綠色 = 沒偵測到東西，紅色 = 偵測到東西
    void OnDrawGizmos()
    {
        Gizmos.color = (currentState == RadarState.Idle) ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
