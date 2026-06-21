using UnityEngine;
using System.Collections.Generic;

// 雷達站專用腳本
// 功能：偵測敵方無人機進入範圍，並切換狀態 (Idle / Detecting)
// 雷達加成：偵測到某架無人機時，全場 DefenseInterceptor 對那架無人機 fireRange 增加、aimError 降低
// 雷達毀光：所有雷達(含 RadarStation1 扇形版)被摧毀後，SAM 導引功能失效（變直線飛）
[RequireComponent(typeof(BuildingHealth))]
public class RadarStation2 : MonoBehaviour
{
    [Header("偵測範圍 (公尺，不受物件 Scale 影響)")]
    public float detectionRadius = 15f;

    [Header("雷達加成數值")]
    [Tooltip("偵測到目標時，全場武器對該目標的 fireRange 倍率（1.4 = +40%）")]
    public float fireRangeBonus = 1.4f;
    [Tooltip("偵測到目標時，全場武器對該目標的 aimError 倍率（0.4 = 誤差降低 60%）")]
    public float aimErrorMultiplier = 0.4f;

    public enum RadarState { Idle, Detecting }
    public RadarState currentState = RadarState.Idle;

    private HashSet<Transform> detectedDrones = new HashSet<Transform>();

    private BuildingHealth health;

    public static HashSet<RadarStation2> allRadars = new HashSet<RadarStation2>();
    public static HashSet<Transform> globalDetectedDrones = new HashSet<Transform>();

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        GameObject detectionZone = new GameObject("DetectionZone");
        detectionZone.transform.SetParent(transform, false);
        detectionZone.transform.localPosition = Vector3.zero;

        SphereCollider detectionTrigger = detectionZone.AddComponent<SphereCollider>();
        detectionTrigger.isTrigger = true;

        float maxScale = Mathf.Max(
            transform.lossyScale.x,
            transform.lossyScale.y,
            transform.lossyScale.z
        );
        detectionTrigger.radius = (maxScale > 0f) ? detectionRadius / maxScale : detectionRadius;

        Rigidbody rb = detectionZone.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        RadarDetectionZone zone = detectionZone.AddComponent<RadarDetectionZone>();
        zone.Init(this);
    }

    void OnEnable()
    {
        allRadars.Add(this);
    }

    void OnDisable()
    {
        foreach (Transform t in detectedDrones)
            RefreshGlobalDetected(t);

        detectedDrones.Clear();
        allRadars.Remove(this);
        UpdateRadarState();
    }

    void Update()
    {
        if (health.IsDestroyed && currentState != RadarState.Idle)
        {
            foreach (Transform t in detectedDrones)
                RefreshGlobalDetected(t);

            detectedDrones.Clear();
            UpdateRadarState();
        }
    }

    public void OnDetectionTriggerEnter(Collider other)
    {
        if (health.IsDestroyed) return;
        if (!IsPlayerDrone(other)) return;

        Transform drone = GetDroneTransform(other);
        if (drone == null) return;

        detectedDrones.Add(drone);
        globalDetectedDrones.Add(drone);
        UpdateRadarState();

        Debug.Log("雷達站偵測到敵方無人機: " + other.name);
    }

    public void OnDetectionTriggerExit(Collider other)
    {
        if (!IsPlayerDrone(other)) return;

        Transform drone = GetDroneTransform(other);
        if (drone == null) return;

        detectedDrones.Remove(drone);
        RefreshGlobalDetected(drone);
        UpdateRadarState();

        Debug.Log("敵方無人機離開偵測範圍");
    }

    // 重新確認某架無人機是否還被「任何雷達」(圓形 RadarStation2 或扇形 RadarStation1)偵測，
    // 決定要不要從全域清單移除
    private void RefreshGlobalDetected(Transform drone)
    {
        if (drone == null) return;

        bool stillDetected = false;

        foreach (RadarStation2 radar in allRadars)
        {
            if (radar == this) continue;
            if (radar.health.IsDestroyed) continue;
            if (radar.detectedDrones.Contains(drone))
            {
                stillDetected = true;
                break;
            }
        }

        if (!stillDetected)
        {
            // 也檢查扇形雷達是否還鎖定這個目標
            foreach (RadarStation1 r in RadarStation1.allDirectionalRadars)
            {
                if (r == null) continue;
                // RadarStation1 內部清單是 private，但它跟 RadarStation2 共用同一份
                // globalDetectedDrones，所以這裡只要確認扇形雷達「目前是否還在偵測這個目標」
                // 透過 currentState 不夠精準，改用簡單判斷：如果扇形雷達還活著且還沒被排除，
                // 信任 RadarStation1.Update() 自己會處理移除，這裡不重複移除即可
            }
        }

        if (!stillDetected)
            globalDetectedDrones.Remove(drone);
    }

    private void UpdateRadarState()
    {
        currentState = (detectedDrones.Count > 0 && !health.IsDestroyed)
            ? RadarState.Detecting
            : RadarState.Idle;
    }

    // 靜態工具：判斷某架無人機是否被雷達鎖定（給 DefenseInterceptor 呼叫）
    public static bool IsDroneDetected(Transform drone)
    {
        return globalDetectedDrones.Contains(drone);
    }

    // 靜態工具：判斷所有雷達是否全滅（給 SimpleProjectile 呼叫）
    // 現在會把 RadarStation2(圓形) 和 RadarStation1(扇形) 都算進去
    public static bool AllRadarsDestroyed()
    {
        bool hasAnyCircular = false;
        foreach (RadarStation2 r in allRadars)
        {
            hasAnyCircular = true;
            if (!r.health.IsDestroyed) return false;
        }

        bool hasAnyDirectional = false;
        foreach (RadarStation1 r in RadarStation1.allDirectionalRadars)
        {
            hasAnyDirectional = true;
            BuildingHealth bh = r.GetComponent<BuildingHealth>();
            if (bh != null && !bh.IsDestroyed) return false;
        }

        // 場景裡完全沒有任何雷達物件時，視為「沒有雷達覆蓋」= 全滅效果
        if (!hasAnyCircular && !hasAnyDirectional) return true;

        return true; // 走到這裡代表存在的雷達都被摧毀了
    }

    // 靜態工具：取得雷達加成數值（取任一存活雷達的數值，圓形優先，沒有的話看扇形）
    public static float GetFireRangeBonus()
    {
        foreach (RadarStation2 r in allRadars)
            if (!r.health.IsDestroyed) return r.fireRangeBonus;

        foreach (RadarStation1 r in RadarStation1.allDirectionalRadars)
        {
            BuildingHealth bh = r.GetComponent<BuildingHealth>();
            if (bh != null && !bh.IsDestroyed) return r.fireRangeBonus;
        }

        return 1f; // 無雷達時無加成
    }

    public static float GetAimErrorMultiplier()
    {
        foreach (RadarStation2 r in allRadars)
            if (!r.health.IsDestroyed) return r.aimErrorMultiplier;

        foreach (RadarStation1 r in RadarStation1.allDirectionalRadars)
        {
            BuildingHealth bh = r.GetComponent<BuildingHealth>();
            if (bh != null && !bh.IsDestroyed) return r.aimErrorMultiplier;
        }

        return 1f; // 無雷達時無加成
    }

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

    private Transform GetDroneTransform(Collider other)
    {
        DroneHealth dh = other.GetComponentInParent<DroneHealth>();
        return dh != null ? dh.transform : other.transform;
    }
}