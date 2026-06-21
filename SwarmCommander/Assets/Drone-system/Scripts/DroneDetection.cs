using System.Collections.Generic;
using UnityEngine;

public class DroneDetection : MonoBehaviour
{
    [Header("Detection")]
    public LayerMask targetLayer = ~0;
    public float detectionRadius = 18f;
    public float detectionInterval = 0.25f;

    [Header("Intel Timing")]
    public float confirmedIntelDuration = 5f;
    public float staleIntelDuration = 10f;

    private readonly Collider[] _detectionBuffer = new Collider[64];
    private readonly HashSet<EnemyIntelVisibility> _detectedThisTick = new HashSet<EnemyIntelVisibility>();
    private float _nextDetectionTime;

    void OnEnable()
    {
        _nextDetectionTime = 0f;
    }

    void Update()
    {
        if (Time.time < _nextDetectionTime) return;

        _nextDetectionTime = Time.time + Mathf.Max(0.02f, detectionInterval);
        DetectNearbyIntel();
    }

    void DetectNearbyIntel()
    {
        _detectedThisTick.Clear();

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRadius,
            _detectionBuffer,
            targetLayer,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _detectionBuffer[i];
            if (hit == null) continue;

            EnemyIntelVisibility intel = hit.GetComponentInParent<EnemyIntelVisibility>();
            if (intel == null) continue;
            if (!_detectedThisTick.Add(intel)) continue;

            intel.Confirm(confirmedIntelDuration, staleIntelDuration);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
