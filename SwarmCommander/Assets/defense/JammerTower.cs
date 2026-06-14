using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BuildingHealth))]
public class JammerTower : MonoBehaviour
{
    [Header("干擾範圍 (公尺，不受物件 Scale 影響)")]
    public float jamRadius = 25f;

    [Header("干擾強度（每秒偏移距離，建議 3~8）")]
    public float jamForce = 5f;

    [Header("干擾更新頻率（秒）：每隔多久換一次亂飛方向，越小越抖")]
    public float directionChangeInterval = 0.3f;

    private BuildingHealth health;
    private SphereCollider jamCollider;
    private readonly List<Transform> targetsInRange = new List<Transform>();
    private readonly Dictionary<Transform, Vector3> jamDirections = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, float> jamTimers = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, MonoBehaviour> disabledScripts = new Dictionary<Transform, MonoBehaviour>();

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
        jamCollider = GetComponent<SphereCollider>();
    }

    void Start()
    {
        if (jamCollider != null)
        {
            jamCollider.isTrigger = true;

            float maxScale = Mathf.Max(
                transform.lossyScale.x,
                transform.lossyScale.y,
                transform.lossyScale.z
            );

            jamCollider.radius = maxScale > 0f ? jamRadius / maxScale : jamRadius;
        }
        else
        {
            Debug.LogWarning("[干擾塔] 找不到 SphereCollider，請在 Inspector 手動加上並勾選 Is Trigger");
        }
    }

    void Update()
    {
        if (health.IsDestroyed)
        {
            RestoreAll();
            return;
        }

        List<Transform> toRemove = new List<Transform>();
        foreach (Transform target in targetsInRange)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                RestoreTarget(target);
                toRemove.Add(target);
            }
        }

        foreach (Transform target in toRemove)
            RemoveTarget(target);

        foreach (Transform target in targetsInRange)
        {
            if (target == null) continue;

            if (!jamTimers.ContainsKey(target) || jamTimers[target] <= 0f)
            {
                jamDirections[target] = Random.insideUnitSphere.normalized;
                jamTimers[target] = directionChangeInterval;
            }
            else
            {
                jamTimers[target] -= Time.deltaTime;
            }

            target.position += jamDirections[target] * jamForce * Time.deltaTime;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsPlayerDrone(other)) return;

        Transform target = GetDroneTargetTransform(other);
        if (target == null || targetsInRange.Contains(target)) return;

        MonoBehaviour moveScript = other.GetComponentInParent<PlayerDroneMarker>();
        if (moveScript != null)
        {
            moveScript.enabled = false;
            disabledScripts[target] = moveScript;
            Debug.Log($"[JammerTower] {target.name} movement disabled.");
        }
        else
        {
            Debug.LogWarning($"[JammerTower] {target.name} has no PlayerDroneMarker to disable.");
        }

        targetsInRange.Add(target);
        jamDirections[target] = Random.insideUnitSphere.normalized;
        jamTimers[target] = directionChangeInterval;
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsPlayerDrone(other)) return;

        Transform target = GetDroneTargetTransform(other);
        RestoreTarget(target);
        RemoveTarget(target);
    }

    private Transform GetDroneTargetTransform(Collider other)
    {
        DroneHealth droneHealth = other.GetComponentInParent<DroneHealth>();
        return droneHealth != null ? droneHealth.transform : other.transform.root;
    }

    private bool IsPlayerDrone(Collider other)
    {
        return other.CompareTag("PlayerDrone") ||
               (other.transform.root != null && other.transform.root.CompareTag("PlayerDrone")) ||
               other.GetComponentInParent<DroneHealth>() != null;
    }

    private void RestoreTarget(Transform target)
    {
        if (target == null) return;

        if (disabledScripts.TryGetValue(target, out MonoBehaviour script) && script != null)
        {
            script.enabled = true;
            Debug.Log($"[JammerTower] {target.name} movement restored.");
        }
    }

    private void RemoveTarget(Transform target)
    {
        if (target == null) return;

        targetsInRange.Remove(target);
        jamDirections.Remove(target);
        jamTimers.Remove(target);
        disabledScripts.Remove(target);
    }

    private void RestoreAll()
    {
        foreach (Transform target in new List<Transform>(targetsInRange))
            RestoreTarget(target);

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
