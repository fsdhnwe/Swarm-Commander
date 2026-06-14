using UnityEngine;

// 雷達站專用腳本
// 功能：偵測敵方無人機進入範圍，並切換狀態 (Idle / Detecting)
[RequireComponent(typeof(BuildingHealth))]
public class RadarStation2 : MonoBehaviour
{
    [Header("偵測範圍 (公尺，不受物件 Scale 影響)")]
    public float detectionRadius = 15f;

    public enum RadarState { Idle, Detecting }
    public RadarState currentState = RadarState.Idle;

    private BuildingHealth health;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        SphereCollider detectionTrigger = gameObject.AddComponent<SphereCollider>();
        detectionTrigger.isTrigger = true;

        // 除以 lossy scale 的最大軸，讓 collider 的實際世界範圍等於 detectionRadius
        // 不管物件 Scale 是多少，偵測圓圈永遠是妳填的數字
        float maxScale = Mathf.Max(
            transform.lossyScale.x,
            transform.lossyScale.y,
            transform.lossyScale.z
        );
        detectionTrigger.radius = (maxScale > 0f) ? detectionRadius / maxScale : detectionRadius;
    }

    void OnTriggerEnter(Collider other)
    {
        if (health.IsDestroyed) return;
        if (IsPlayerDrone(other))
        {
            currentState = RadarState.Detecting;
            Debug.Log("雷達站偵測到敵方無人機: " + other.name);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayerDrone(other))
        {
            currentState = RadarState.Idle;
            Debug.Log("敵方無人機離開偵測範圍");
        }
    }

    // Gizmos 直接用世界座標畫圓圈，永遠顯示正確的實際偵測範圍
    void OnDrawGizmos()
    {
        Gizmos.color = (currentState == RadarState.Idle) ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }

    bool IsPlayerDrone(Collider other)
    {
        return other.CompareTag("PlayerDrone") ||
               (other.transform.root != null && other.transform.root.CompareTag("PlayerDrone")) ||
               other.GetComponentInParent<DroneHealth>() != null;
    }
}
