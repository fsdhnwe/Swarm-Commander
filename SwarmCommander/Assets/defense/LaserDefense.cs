using UnityEngine;
using System.Collections.Generic;

// 雷射防禦系統 — 現實對應 HELIOS（美）/ Iron Beam（以色列）
// 特性：
// - 無射彈，直接對目標持續造成傷害（光速命中，不需要提前量）
// - 超短距離（比 AAA 還近），但 DPS 極高
// - 過熱機制：連續照射超過 overheatTime 秒後進入冷卻，無法開火
// - 冷卻結束後自動恢復
// - 雷達全滅不影響雷射（光學追蹤，不依賴雷達）
//
// 現實數據參考：
// HELIOS 有效距離約 1~2km，功率 60kW+，對小型無人機秒殺
// Iron Beam 有效距離約 7km，但遊戲縮小比例後設短距離更符合關卡設計
//
// 威脅評估：第三關使用，第二關也包含（比固定 SAM 更需要精準選目標，因為射程短）
[RequireComponent(typeof(BuildingHealth))]
public class LaserDefense : MonoBehaviour
{
    public enum LaserState { Idle, Tracking, Firing, Overheat, Cooldown }

    [Header("偵測與射程")]
    [Tooltip("偵測範圍（比 AAA 稍短，最後一道防線，建議 10~15）")]
    public float detectionRange = 12f;
    [Tooltip("實際開火射程（等於或小於偵測範圍）")]
    public float fireRange = 10f;

    [Header("雷射傷害")]
    [Tooltip("每秒對目標造成的傷害（DPS），現實極高，遊戲建議 150~300/s）")]
    public float damagePerSecond = 200f;

    [Header("過熱機制")]
    [Tooltip("最多連續照射幾秒後過熱（現實 HELIOS 約 10~30 秒連續輸出）")]
    public float overheatTime = 8f;
    [Tooltip("過熱後需要冷卻幾秒才能再次開火")]
    public float cooldownTime = 5f;

    [Header("砲塔旋轉")]
    public Transform turretPivot;
    public float rotationOffsetY = 0f;
    [Tooltip("雷射轉向速度（度/秒），比飛彈砲管快很多）")]
    public float turretRotationSpeed = 360f;

    [Header("雷射視覺效果 (可留空)")]
    [Tooltip("LineRenderer 元件，用來顯示雷射光束，拖入即可自動控制")]
    public LineRenderer laserBeam;
    [Tooltip("雷射命中點特效 Prefab，命中時生成")]
    public GameObject impactEffectPrefab;

    [Header("威脅評估權重")]
    public ThreatEvaluator.ThreatWeights threatWeights = new ThreatEvaluator.ThreatWeights();

    [Header("狀態 (唯讀)")]
    public LaserState currentState = LaserState.Idle;

    private BuildingHealth health;
    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    private float heatTimer = 0f;       // 目前累積熱量（秒）
    private float cooldownTimer = 0f;   // 冷卻剩餘時間

    private GameObject currentImpactEffect;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        GameObject detectionZone = new GameObject("DetectionZone");
        detectionZone.transform.SetParent(transform, false);
        detectionZone.transform.localPosition = Vector3.zero;

        SphereCollider col = detectionZone.AddComponent<SphereCollider>();
        col.isTrigger = true;
        float maxScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        col.radius = maxScale > 0f ? detectionRange / maxScale : detectionRange;

        Rigidbody rb = detectionZone.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        LaserDetectionZone zone = detectionZone.AddComponent<LaserDetectionZone>();
        zone.Init(this);

        if (laserBeam != null)
            laserBeam.enabled = false;
    }

    void Update()
    {
        if (health.IsDestroyed)
        {
            SetLaserBeam(false);
            return;
        }

        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

        switch (currentState)
        {
            case LaserState.Idle:
                if (targetsInRange.Count > 0)
                {
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget != null)
                        currentState = LaserState.Tracking;
                }
                break;

            case LaserState.Tracking:
                if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
                {
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    if (currentTarget == null) { currentState = LaserState.Idle; break; }
                }

                RotateTurretTowards(currentTarget.position);

                float dist = Vector3.Distance(transform.position, currentTarget.position);
                if (dist <= fireRange)
                    currentState = LaserState.Firing;
                break;

            case LaserState.Firing:
                if (currentTarget == null || !currentTarget.gameObject.activeInHierarchy)
                {
                    SetLaserBeam(false);
                    currentTarget = ThreatEvaluator.FindHighestThreat(
                        transform.position, targetsInRange, threatWeights);
                    currentState = currentTarget != null ? LaserState.Tracking : LaserState.Idle;
                    break;
                }

                float fireDist = Vector3.Distance(transform.position, currentTarget.position);
                if (fireDist > fireRange)
                {
                    // 目標飛出射程，退回追蹤
                    SetLaserBeam(false);
                    currentState = LaserState.Tracking;
                    break;
                }

                RotateTurretTowards(currentTarget.position);
                SetLaserBeam(true, currentTarget.position);

                // 每幀持續扣血（DPS 換算）
                DroneHealth droneHealth = currentTarget.GetComponent<DroneHealth>();
                if (droneHealth != null)
                    droneHealth.TakeDamage(Mathf.CeilToInt(damagePerSecond * Time.deltaTime));

                // 累積熱量
                heatTimer += Time.deltaTime;
                if (heatTimer >= overheatTime)
                {
                    heatTimer = overheatTime;
                    SetLaserBeam(false);
                    cooldownTimer = cooldownTime;
                    currentState = LaserState.Overheat;
                    Debug.Log($"[雷射防禦] {gameObject.name} 過熱！冷卻 {cooldownTime} 秒");
                }

                // 每秒重新評估目標
                currentTarget = ThreatEvaluator.FindHighestThreat(
                    transform.position, targetsInRange, threatWeights) ?? currentTarget;
                break;

            case LaserState.Overheat:
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    heatTimer = 0f;
                    currentState = targetsInRange.Count > 0 ? LaserState.Tracking : LaserState.Idle;
                    Debug.Log($"[雷射防禦] {gameObject.name} 冷卻完成，恢復作戰");
                }
                break;

            case LaserState.Cooldown:
                // 保留給之後擴充使用
                break;
        }
    }

    private void SetLaserBeam(bool active, Vector3 targetPos = default)
    {
        if (laserBeam == null) return;

        laserBeam.enabled = active;

        if (active)
        {
            Vector3 origin = turretPivot != null ? turretPivot.position : transform.position;
            laserBeam.SetPosition(0, origin);
            laserBeam.SetPosition(1, targetPos);

            // 命中特效
            if (impactEffectPrefab != null && currentImpactEffect == null)
                currentImpactEffect = Instantiate(impactEffectPrefab, targetPos, Quaternion.identity);

            if (currentImpactEffect != null)
                currentImpactEffect.transform.position = targetPos;
        }
        else
        {
            if (currentImpactEffect != null)
            {
                Destroy(currentImpactEffect);
                currentImpactEffect = null;
            }
        }
    }

    private void RotateTurretTowards(Vector3 targetPos)
    {
        Transform pivot = turretPivot != null ? turretPivot : transform;
        Vector3 direction = targetPos - pivot.position;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(direction);
        targetRot *= Quaternion.Euler(0, rotationOffsetY, 0);
        pivot.rotation = Quaternion.RotateTowards(
            pivot.rotation, targetRot, turretRotationSpeed * Time.deltaTime);
    }

    public void OnDetectionTriggerEnter(Collider other)
    {
        if (!IsPlayerDrone(other)) return;
        Transform t = GetDroneTransform(other);
        if (t != null && !targetsInRange.Contains(t))
            targetsInRange.Add(t);
    }

    public void OnDetectionTriggerExit(Collider other)
    {
        if (!IsPlayerDrone(other)) return;
        Transform t = GetDroneTransform(other);
        if (t != null) targetsInRange.Remove(t);
    }

    private bool IsPlayerDrone(Collider other)
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

    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0f, 0.5f); // 洋紅色區分雷射射程
        Gizmos.DrawWireSphere(transform.position, fireRange);
    }
}

// 子物件轉發腳本
public class LaserDetectionZone : MonoBehaviour
{
    private LaserDefense laser;
    public void Init(LaserDefense parent) { laser = parent; }
    void OnTriggerEnter(Collider other) { if (laser != null) laser.OnDetectionTriggerEnter(other); }
    void OnTriggerExit(Collider other) { if (laser != null) laser.OnDetectionTriggerExit(other); }
}
