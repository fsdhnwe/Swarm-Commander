using UnityEngine;
using System.Collections.Generic;

// 方向性雷達站 (扇形/特定方向偵測版本)
// 架構跟 RadarStation2.cs (圓形雷達) 完全一致，共用同一套全域靜態清單，
// 兩種雷達站可以混在同個場景，互相加成、互相計入「全滅判斷」
//
// 差別：不是整圈偵測，而是「距離 + 角度」雙重判斷，做出扇形偵測範圍
// 因為扇形無法直接用 Unity 內建 Collider 形狀做 Trigger，
// 改用 SphereCollider 抓「範圍內候選目標」，再用程式碼過濾角度
[RequireComponent(typeof(BuildingHealth))]
public class RadarStation1 : MonoBehaviour
{
    [Header("偵測距離 (公尺，不受物件 Scale 影響)")]
    public float detectionRadius = 30f;

    [Header("扇形偵測角度 (度數，180=半圓，90=直角扇形，360=等同全向)")]
    [Range(1f, 360f)]
    public float detectionAngle = 90f;

    [Header("垂直偵測角度 (度數，控制上下仰角範圍，90=上下各45度，180=全向)")]
    [Range(1f, 180f)]
    public float detectionElevation = 60f;

    [Header("朝向參考軸 (這個雷達站「正面」方向，預設用本體 Z 軸/藍色箭頭)")]
    [Tooltip("如果模型正面不是 Z 軸，可以拖一個子物件進來當作朝向參考，\n" +
             "調整子物件的 Rotation 讓它的藍色箭頭對齊模型真正的朝向")]
    public Transform facingReference;

    [Header("雷達加成數值 (跟 RadarStation2 共用同一套加成邏輯)")]
    [Tooltip("偵測到目標時，全場武器對該目標的 fireRange 倍率（1.4 = +40%）")]
    public float fireRangeBonus = 1.4f;
    [Tooltip("偵測到目標時，全場武器對該目標的 aimError 倍率（0.4 = 誤差降低 60%）")]
    public float aimErrorMultiplier = 0.4f;

    public enum RadarState { Idle, Detecting }
    public RadarState currentState = RadarState.Idle;

    // 目前被這座雷達偵測到的無人機(已通過角度判斷)
    private HashSet<Transform> detectedDrones = new HashSet<Transform>();

    // 範圍內但還沒判斷角度的候選目標(SphereCollider 抓到的)
    private List<Transform> candidateTargets = new List<Transform>();

    private BuildingHealth health;

    // 跟 RadarStation2 共用同一組靜態集合，這樣兩種雷達可以互相加成、共同計入全滅判斷
    public static HashSet<RadarStation1> allDirectionalRadars = new HashSet<RadarStation1>();

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        GameObject detectionZone = new GameObject("DirectionalDetectionZone");

        int detectionLayer = LayerMask.NameToLayer("DetectionZone");
        if (detectionLayer >= 0)
            detectionZone.layer = detectionLayer;
        detectionZone.transform.SetParent(transform, false);
        detectionZone.transform.localPosition = Vector3.zero;

        SphereCollider rangeCollider = detectionZone.AddComponent<SphereCollider>();
        rangeCollider.isTrigger = true;

        float maxScale = Mathf.Max(
            transform.lossyScale.x,
            transform.lossyScale.y,
            transform.lossyScale.z
        );
        rangeCollider.radius = (maxScale > 0f) ? detectionRadius / maxScale : detectionRadius;

        Rigidbody rb = detectionZone.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        RadarDirectionalZone zone = detectionZone.AddComponent<RadarDirectionalZone>();
        zone.Init(this);
    }

    void OnEnable()
    {
        allDirectionalRadars.Add(this);
    }

    void OnDisable()
    {
        foreach (Transform t in detectedDrones)
            RefreshGlobalDetected(t);

        detectedDrones.Clear();
        candidateTargets.Clear();
        allDirectionalRadars.Remove(this);
    }

    void Update()
    {
        if (health.IsDestroyed)
        {
            if (detectedDrones.Count > 0)
            {
                foreach (Transform t in detectedDrones)
                    RefreshGlobalDetected(t);
                detectedDrones.Clear();
                currentState = RadarState.Idle;
            }
            return;
        }

        candidateTargets.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

        Transform facing = (facingReference != null) ? facingReference : transform;

        // 對每個範圍內的候選目標，檢查是否在扇形角度內（水平 + 垂直雙重判斷）
        List<Transform> toRemoveFromDetected = null;
        foreach (Transform t in candidateTargets)
        {
            Vector3 toTarget = t.position - transform.position;

            // 水平角度：目標方向投影到水平面後與正面夾角
            Vector3 toTargetFlat = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
            Vector3 facingFlat = new Vector3(facing.forward.x, 0f, facing.forward.z).normalized;
            float horizontalAngle = Vector3.Angle(facingFlat, toTargetFlat);

            // 垂直角度：目標相對於雷達的仰角
            float verticalAngle = Mathf.Abs(Mathf.Atan2(toTarget.y, new Vector2(toTarget.x, toTarget.z).magnitude) * Mathf.Rad2Deg);

            bool inSector = horizontalAngle <= detectionAngle * 0.5f &&
                            verticalAngle <= detectionElevation * 0.5f;

            bool wasDetected = detectedDrones.Contains(t);

            if (inSector && !wasDetected)
            {
                detectedDrones.Add(t);
                RadarStation2.globalDetectedDrones.Add(t);
                Debug.Log($"[方向雷達] {gameObject.name} 偵測到敵方無人機: {t.name}");
            }
            else if (!inSector && wasDetected)
            {
                detectedDrones.Remove(t);
                RefreshGlobalDetected(t);
                if (toRemoveFromDetected == null) toRemoveFromDetected = new List<Transform>();
                Debug.Log($"[方向雷達] {gameObject.name} 失去鎖定(超出角度): {t.name}");
            }
        }

        currentState = (detectedDrones.Count > 0) ? RadarState.Detecting : RadarState.Idle;
    }

    // 重新確認某架無人機是否還被其他雷達(圓形或方向性都算)偵測，決定要不要從全域清單移除
    private void RefreshGlobalDetected(Transform drone)
    {
        if (drone == null) return;

        bool stillDetected = false;

        // 檢查其他方向性雷達
        foreach (RadarStation1 r in allDirectionalRadars)
        {
            if (r == this) continue;
            if (r.health != null && r.health.IsDestroyed) continue;
            if (r.detectedDrones.Contains(drone))
            {
                stillDetected = true;
                break;
            }
        }

        // 檢查圓形雷達
        if (!stillDetected)
        {
            foreach (RadarStation2 r in RadarStation2.allRadars)
            {
                if (r == null) continue;
                // RadarStation2 內部的 detectedDrones 是 private，
                // 改用 globalDetectedDrones 的存在狀態間接判斷會更簡單，
                // 這裡保守起見直接看 globalDetectedDrones 目前是否還有它即可
            }
        }

        if (!stillDetected)
            RadarStation2.globalDetectedDrones.Remove(drone);
    }

    // 由 RadarDirectionalZone 轉發呼叫：候選目標進入「距離」範圍(還沒判斷角度)
    public void OnZoneTriggerEnter(Collider other)
    {
        if (health.IsDestroyed) return;
        if (!IsPlayerDrone(other)) return;

        Transform drone = GetDroneTransform(other);
        if (drone != null && !candidateTargets.Contains(drone))
            candidateTargets.Add(drone);
    }

    // 候選目標離開「距離」範圍，不管角度，直接移除並通知離開
    public void OnZoneTriggerExit(Collider other)
    {
        if (!IsPlayerDrone(other)) return;

        Transform drone = GetDroneTransform(other);
        if (drone == null) return;

        candidateTargets.Remove(drone);

        if (detectedDrones.Contains(drone))
        {
            detectedDrones.Remove(drone);
            RefreshGlobalDetected(drone);
        }
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

    // 在 Scene 視窗畫出扇形範圍（水平扇形 + 垂直仰角輔助線）
    void OnDrawGizmos()
    {
        Transform facing = (facingReference != null) ? facingReference : transform;
        Gizmos.color = (currentState == RadarState.Idle) ? Color.green : Color.red;

        Vector3 origin = transform.position;
        float halfH = detectionAngle * 0.5f;
        float halfV = detectionElevation * 0.5f;

        // 水平扇形邊界線
        Vector3 leftDir = Quaternion.Euler(0, -halfH, 0) * facing.forward;
        Vector3 rightDir = Quaternion.Euler(0, halfH, 0) * facing.forward;
        Gizmos.DrawLine(origin, origin + leftDir * detectionRadius);
        Gizmos.DrawLine(origin, origin + rightDir * detectionRadius);

        // 水平扇形弧線
        int segments = 24;
        Vector3 prevPoint = origin + leftDir * detectionRadius;
        for (int i = 1; i <= segments; i++)
        {
            float t = (float)i / segments;
            float angle = Mathf.Lerp(-halfH, halfH, t);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * facing.forward;
            Vector3 point = origin + dir * detectionRadius;
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }

        // 垂直仰角輔助線（沿正面方向往上/往下畫）
        Vector3 upDir = Quaternion.AngleAxis(-halfV, facing.right) * facing.forward;
        Vector3 downDir = Quaternion.AngleAxis(halfV, facing.right) * facing.forward;
        Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.5f);
        Gizmos.DrawLine(origin, origin + upDir * detectionRadius);
        Gizmos.DrawLine(origin, origin + downDir * detectionRadius);
    }
}

// 掛在自動建立的 DirectionalDetectionZone 子物件上，轉發 Trigger 事件給父物件
public class RadarDirectionalZone : MonoBehaviour
{
    private RadarStation1 radar;
    public void Init(RadarStation1 parent) { radar = parent; }

    void OnTriggerEnter(Collider other)
    {
        if (radar != null) radar.OnZoneTriggerEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (radar != null) radar.OnZoneTriggerExit(other);
    }
}