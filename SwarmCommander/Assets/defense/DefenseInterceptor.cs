using UnityEngine;
using System.Collections.Generic;

// SAM 和 AAA 共用這一份腳本，差異只來自 weaponData (WeaponData.cs) 裡的數值
// FSM 流程：Idle -> Detect -> Track -> (反應延遲) -> Fire -> Cooldown
//
// 通訊完整度 (CommandNetwork.commsIntegrity) 會影響：
// - 反應延遲：通訊越差，從鎖定到開火要等更久
// - 瞄準誤差：通訊越差，子彈/飛彈會偏離目標越多
[RequireComponent(typeof(BuildingHealth))]
public class DefenseInterceptor : MonoBehaviour
{
    public enum InterceptorState { Idle, Detect, Track, Fire, Cooldown }

    [Header("武器參數 (把 SAM 或 AAA 的 WeaponData 拖到這裡)")]
    public WeaponData weaponData;

    [Header("發射點 (可留空，留空則從本體位置發射)")]
    public Transform firePoint;

    [Header("狀態 (FSM，唯讀，Play 時可即時觀察)")]
    public InterceptorState currentState = InterceptorState.Idle;

    private BuildingHealth health;
    private float cooldownTimer = 0f;
    private float reactionTimer = -1f; // -1 表示尚未開始倒數
    private List<Transform> targetsInRange = new List<Transform>();
    private Transform currentTarget;

    void Awake()
    {
        health = GetComponent<BuildingHealth>();

        // 自動加上偵測範圍 (Trigger)
        SphereCollider detectionTrigger = gameObject.AddComponent<SphereCollider>();
        detectionTrigger.isTrigger = true;
        detectionTrigger.radius = weaponData != null ? weaponData.detectionRange : 30f;
    }

    void Update()
    {
        if (weaponData == null) return;

        // 被摧毀後直接停止運作
        if (health.IsDestroyed)
        {
            currentState = InterceptorState.Idle;
            return;
        }

        // 清除已經消失/被擊落的目標
        targetsInRange.RemoveAll(t => t == null || !t.gameObject.activeInHierarchy);

        switch (currentState)
        {
            case InterceptorState.Idle:
                // 範圍內有目標 -> 進入偵測
                if (targetsInRange.Count > 0)
                    currentState = InterceptorState.Detect;
                break;

            case InterceptorState.Detect:
                // 從範圍內的目標中找最近的一個
                currentTarget = FindClosestTarget();
                if (currentTarget != null)
                {
                    currentState = InterceptorState.Track;
                    reactionTimer = -1f; // 重置反應計時
                }
                else
                {
                    currentState = InterceptorState.Idle;
                }
                break;

            case InterceptorState.Track:
                if (currentTarget == null)
                {
                    currentState = InterceptorState.Idle;
                    reactionTimer = -1f;
                    break;
                }

                // 直接用距離判斷目標是否還在偵測範圍內（不依賴 OnTriggerExit）
                if (Vector3.Distance(transform.position, currentTarget.position) > weaponData.detectionRange)
                {
                    currentTarget = null;
                    reactionTimer = -1f;
                    currentState = InterceptorState.Idle;
                    break;
                }

                float dist = Vector3.Distance(transform.position, currentTarget.position);
                if (dist <= weaponData.fireRange)
                {
                    // 進入射程才開始朝目標轉向 (未進入射程時砲台保持原本方向)
                    RotateTowards(currentTarget.position);
                    // 第一次進入射程：開始反應延遲倒數
                    if (reactionTimer < 0f)
                    {
                        reactionTimer = GetReactionDelay();
                    }

                    reactionTimer -= Time.deltaTime;
                    if (reactionTimer <= 0f)
                    {
                        currentState = InterceptorState.Fire;
                        reactionTimer = -1f;
                    }
                }
                else
                {
                    // 目標離開射程，反應延遲重新計算
                    reactionTimer = -1f;
                }
                break;

            case InterceptorState.Fire:
                FireAtTarget();
                cooldownTimer = weaponData.fireRate;
                currentState = InterceptorState.Cooldown;
                break;

            case InterceptorState.Cooldown:
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    // 冷卻結束，如果還有目標就重新偵測，否則回到 Idle
                    currentState = (targetsInRange.Count > 0)
                        ? InterceptorState.Detect
                        : InterceptorState.Idle;
                }
                break;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("PlayerDrone"))
        {
            if (!targetsInRange.Contains(other.transform))
                targetsInRange.Add(other.transform);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("PlayerDrone"))
        {
            targetsInRange.Remove(other.transform);
        }
    }

    private Transform FindClosestTarget()
    {
        Transform closest = null;
        float closestDist = Mathf.Infinity;

        foreach (Transform t in targetsInRange)
        {
            if (t == null) continue;
            float dist = Vector3.Distance(transform.position, t.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = t;
            }
        }
        return closest;
    }

    private void RotateTowards(Vector3 targetPos)
    {
        Vector3 direction = targetPos - transform.position;
        if (direction.sqrMagnitude < 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction);
        // 200 = 轉向速度，數字越大轉得越快
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, 200f * Time.deltaTime);
    }

    // 通訊完整度越低，反應延遲越長 (最多放大到約3倍)
    private float GetReactionDelay()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float multiplier = Mathf.Lerp(3f, 1f, comms); // comms=0 -> x3, comms=1 -> x1
        return weaponData.reactionDelayBase * multiplier;
    }

    // 通訊完整度越低，瞄準誤差越大 (最多放大到約5倍)
    private float GetAimError()
    {
        float comms = CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f;
        float worstCase = weaponData.baseAimError * 5f;
        return Mathf.Lerp(worstCase, weaponData.baseAimError, comms); // comms=0 -> worstCase, comms=1 -> base
    }

    private void FireAtTarget()
    {
        if (currentTarget == null) return;

        Debug.Log($"{gameObject.name} ({weaponData.weaponName}) 對 {currentTarget.name} 開火！" +
                  $" [通訊完整度: {(CommandNetwork.Instance != null ? CommandNetwork.Instance.commsIntegrity : 1f):F2}]");

        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position;

        if (weaponData.projectilePrefab != null)
        {
            GameObject proj = Instantiate(weaponData.projectilePrefab, spawnPos, Quaternion.identity);
            SimpleProjectile sp = proj.GetComponent<SimpleProjectile>();
            if (sp != null)
            {
                sp.target = currentTarget;
                sp.speed = weaponData.projectileSpeed;
                sp.damage = weaponData.damage;
                sp.isGuided = weaponData.isGuided;
                sp.aimError = GetAimError();
            }
        }
    }

    // 在 Scene 視窗畫出兩個範圍圈：黃色=偵測範圍，紅色=攻擊範圍
    void OnDrawGizmos()
    {
        if (weaponData == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, weaponData.detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, weaponData.fireRange);
    }
}