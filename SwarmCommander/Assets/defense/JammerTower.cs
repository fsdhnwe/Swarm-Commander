using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(BuildingHealth))]
public class JammerTower : MonoBehaviour
{
    [Header("Jamming")]
    public float jamRadius = 25f;
    public float jamForce = 5f;
    public float directionChangeInterval = 0.3f;
    public float scanInterval = 0.1f;

    private BuildingHealth health;
    private readonly List<Transform> targetsInRange = new List<Transform>();
    private readonly HashSet<Transform> detectedTargets = new HashSet<Transform>();
    private readonly Dictionary<Transform, Vector3> jamDirections = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, float> jamTimers = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, List<DisabledScriptState>> disabledScripts = new Dictionary<Transform, List<DisabledScriptState>>();
    private readonly Collider[] scanBuffer = new Collider[128];
    private float nextScanTime;

    private class DisabledScriptState
    {
        public MonoBehaviour script;
        public bool wasEnabled;

        public DisabledScriptState(MonoBehaviour script)
        {
            this.script = script;
            wasEnabled = script != null && script.enabled;
        }
    }

    void Awake()
    {
        health = GetComponent<BuildingHealth>();
    }

    void OnEnable()
    {
        if (health != null)
            health.onDestroyed.AddListener(HandleTowerDestroyed);
    }

    void OnDisable()
    {
        RestoreAll();

        if (health != null)
            health.onDestroyed.RemoveListener(HandleTowerDestroyed);
    }

    void Start()
    {
        DisableLegacyRangeCollider();
    }

    void Update()
    {
        if (health.IsDestroyed)
        {
            RestoreAll();
            return;
        }

        if (Time.time >= nextScanTime)
        {
            ScanTargetsInRange();
            nextScanTime = Time.time + Mathf.Max(0.02f, scanInterval);
        }

        foreach (Transform target in targetsInRange)
        {
            if (target == null) continue;

            if (!jamTimers.ContainsKey(target) || jamTimers[target] <= 0f)
            {
                jamDirections[target] = RandomFlatDirection();
                jamTimers[target] = directionChangeInterval;
            }
            else
            {
                jamTimers[target] -= Time.deltaTime;
            }

            target.position += jamDirections[target] * jamForce * Time.deltaTime;
        }
    }

    private void ScanTargetsInRange()
    {
        detectedTargets.Clear();

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            jamRadius,
            scanBuffer,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider other = scanBuffer[i];
            if (other == null || !IsPlayerDrone(other)) continue;

            Transform target = GetDroneTargetTransform(other);
            if (target == null || !target.gameObject.activeInHierarchy) continue;
            if (!IsInsideJamRange(target.position)) continue;

            detectedTargets.Add(target);

            if (!targetsInRange.Contains(target))
                JamTarget(target);
        }

        for (int i = targetsInRange.Count - 1; i >= 0; i--)
        {
            Transform target = targetsInRange[i];
            if (target == null || !target.gameObject.activeInHierarchy || !detectedTargets.Contains(target))
            {
                RestoreTarget(target);
                RemoveTarget(target);
            }
        }
    }

    private void JamTarget(Transform target)
    {
        if (target == null || targetsInRange.Contains(target)) return;

        List<MonoBehaviour> movementScripts = GetDroneMovementScripts(target);
        if (movementScripts.Count > 0)
        {
            disabledScripts[target] = DisableScripts(movementScripts);
            Debug.Log($"[JammerTower] {target.name} movement jammed.");
        }
        else
        {
            Debug.LogWarning($"[JammerTower] {target.name} has no supported movement script to jam.");
        }

        targetsInRange.Add(target);
        jamDirections[target] = RandomFlatDirection();
        jamTimers[target] = directionChangeInterval;
    }

    private bool IsInsideJamRange(Vector3 targetPosition)
    {
        return Vector3.Distance(transform.position, targetPosition) <= jamRadius;
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

    private void DisableLegacyRangeCollider()
    {
        SphereCollider legacyRangeCollider = GetComponent<SphereCollider>();
        if (legacyRangeCollider != null)
            legacyRangeCollider.enabled = false;
    }

    private List<DisabledScriptState> DisableScripts(List<MonoBehaviour> scripts)
    {
        List<DisabledScriptState> states = new List<DisabledScriptState>();

        foreach (MonoBehaviour script in scripts)
        {
            if (script == null) continue;

            states.Add(new DisabledScriptState(script));
            script.enabled = false;
        }

        return states;
    }

    private Vector3 RandomFlatDirection()
    {
        Vector2 random = Random.insideUnitCircle;
        if (random.sqrMagnitude <= 0.0001f)
            return Vector3.forward;

        return new Vector3(random.x, 0f, random.y).normalized;
    }

    private List<MonoBehaviour> GetDroneMovementScripts(Transform target)
    {
        List<MonoBehaviour> scripts = new List<MonoBehaviour>();

        AddMovementScript<DroneUnit>(target, scripts);
        AddMovementScript<ShahedDroneUnit>(target, scripts);
        AddMovementScript<ReconDroneUnit>(target, scripts);
        AddMovementScript<DecoyDroneUnit>(target, scripts);
        AddMovementScript<MALDPathFollower>(target, scripts);

        if (scripts.Count == 0)
            AddMovementScript<PlayerDroneMarker>(target, scripts);

        return scripts;
    }

    private void AddMovementScript<T>(Transform target, List<MonoBehaviour> scripts) where T : MonoBehaviour
    {
        if (target == null) return;

        T script = target.GetComponent<T>();

        if (script == null)
            script = target.GetComponentInParent<T>();

        if (script == null)
            script = target.GetComponentInChildren<T>();

        if (script != null && !scripts.Contains(script))
            scripts.Add(script);
    }

    private void RestoreTarget(Transform target)
    {
        if (target == null) return;

        if (disabledScripts.TryGetValue(target, out List<DisabledScriptState> scripts))
        {
            foreach (DisabledScriptState state in scripts)
            {
                if (state.script != null)
                    state.script.enabled = state.wasEnabled;
            }

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

    private void HandleTowerDestroyed()
    {
        RestoreAll();
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, jamRadius);
    }
}
