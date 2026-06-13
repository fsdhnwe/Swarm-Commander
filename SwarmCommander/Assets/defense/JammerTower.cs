using UnityEngine;
using System.Collections.Generic;

// 干擾塔：進入範圍的玩家無人機，移動腳本會被強制停用，改由干擾塔控制隨機偏移
// 離開範圍或干擾塔被摧毀後，移動腳本自動恢復
//
// ============================================================
// ⚠️  整合時需要修改的地方（搜尋 TEAMMATE_SCRIPT_NAME）：
//     把下面兩處的 "TEAMMATE_SCRIPT_NAME" 換成隊友無人機移動腳本的實際類別名稱
//     目前測試用 PlayerDroneMarker，整合時換成隊友的腳本名稱
// ============================================================
[RequireComponent(typeof(BuildingHealth))]
public class JammerTower : MonoBehaviour
{
    [Header("干擾範圍")]
    public float jamRadius = 25f;

    [Header("干擾強度（每秒偏移距離，建議 3~8）")]
    public float jamForce = 5f;

    [Header("干擾更新頻率（秒）：每隔多久換一次亂飛方向，越小越抖")]
    public float directionChangeInterval = 0.3f;

    private BuildingHealth health;
    private List<Transform> targetsInRange = new List<Transform>();
    private Dictionary<Transform, Vector3> jamDirections = new Dictionary<Transform, Vector3>();
    private Dictionary<Transform, float> jamTimers = new Dictionary<Transform, float>();

    // 記錄每架被干擾的無人機的移動腳本，離開範圍時恢復
    private Dictionary<Transform, MonoBehaviour> disabledScripts = new Dictionary<Transform, MonoBehaviour>();

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
        // ⚠️ SphereCollider 請在 Inspector 手動加，勾選 Is Trigger，設好半徑
        // Awake 這裡不自動加，避免跟手動加的衝突
    }

    void Update()
    {
        if (health.IsDestroyed)
        {
            RestoreAll();
            return;
        }

        // 清除已消失/被擊落的目標（離開範圍由 OnTriggerExit 處理）
        List<Transform> toRemove = new List<Transform>();
        foreach (Transform t in targetsInRange)
        {
            if (t == null || !t.gameObject.activeInHierarchy)
            {
                RestoreTarget(t);
                toRemove.Add(t);
            }
        }
        foreach (Transform t in toRemove)
            RemoveTarget(t);

        // 對範圍內的目標施加干擾偏移
        foreach (Transform t in targetsInRange)
        {
            if (t == null) continue;

            if (!jamTimers.ContainsKey(t) || jamTimers[t] <= 0f)
            {
                jamDirections[t] = Random.insideUnitSphere.normalized;
                jamTimers[t] = directionChangeInterval;
            }
            else
            {
                jamTimers[t] -= Time.deltaTime;
            }

            t.position += jamDirections[t] * jamForce * Time.deltaTime;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerDrone")) return;

        Transform t = other.transform;
        if (targetsInRange.Contains(t)) return;

        // ============================================================
        // ⚠️  整合時：把 PlayerDroneMarker 換成隊友移動腳本的類別名稱
        // ============================================================
        MonoBehaviour moveScript = other.GetComponentInParent<PlayerDroneMarker>();

        if (moveScript != null)
        {
            moveScript.enabled = false;
            disabledScripts[t] = moveScript;
            Debug.Log($"[干擾塔] {other.name} 移動腳本已停用");
        }
        else
        {
            Debug.LogWarning($"[干擾塔] 找不到 {other.name} 的移動腳本");
        }

        targetsInRange.Add(t);
        jamDirections[t] = Random.insideUnitSphere.normalized;
        jamTimers[t] = directionChangeInterval;

        Debug.Log($"[干擾塔] {other.name} 進入干擾範圍");
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("PlayerDrone")) return;

        Transform t = other.transform;
        RestoreTarget(t);
        RemoveTarget(t);
    }

    private void RestoreTarget(Transform t)
    {
        if (t == null) return;
        if (disabledScripts.TryGetValue(t, out MonoBehaviour script))
        {
            if (script != null) script.enabled = true;
            Debug.Log($"[干擾塔] {t.name} 移動腳本已恢復");
        }
    }

    private void RemoveTarget(Transform t)
    {
        targetsInRange.Remove(t);
        jamDirections.Remove(t);
        jamTimers.Remove(t);
        disabledScripts.Remove(t);
    }

    private void RestoreAll()
    {
        foreach (Transform t in new List<Transform>(targetsInRange))
            RestoreTarget(t);

        targetsInRange.Clear();
        jamDirections.Clear();
        jamTimers.Clear();
        disabledScripts.Clear();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, jamRadius);
    }
}