using System.Collections.Generic;
using UnityEngine;

public class MALDPathFollower : MonoBehaviour
{
    public float speed = 25f;
    public float waypointReachDistance = 1f;
    public Vector3 modelRotationOffset = Vector3.zero;
    public float rotationSpeed = 720f;
    public float fallSpeed = 20f;
    public float fallDuration = 2f;
    public float fallRotationSpeed = 240f;

    private readonly List<Vector3> _waypoints = new();
    private int _waypointIndex;
    private bool _isFalling;
    private float _fallEndTime;

    public void SetPath(IReadOnlyList<Vector3> waypoints)
    {
        _waypoints.Clear();

        if (waypoints != null)
            _waypoints.AddRange(waypoints);

        _waypointIndex = 0;
        _isFalling = false;

        if (_waypoints.Count > 0)
            SnapFaceTarget(_waypoints[0]);
    }

    void Update()
    {
        if (_isFalling)
        {
            UpdateFalling();
            return;
        }

        if (_waypointIndex >= _waypoints.Count) return;

        Vector3 target = _waypoints[_waypointIndex];
        Vector3 toTarget = target - transform.position;

        if (toTarget.magnitude <= waypointReachDistance)
        {
            _waypointIndex++;

            if (_waypointIndex >= _waypoints.Count)
            {
                BeginFalling();
                return;
            }

            target = _waypoints[_waypointIndex];
            toTarget = target - transform.position;
        }

        Vector3 moveDir = toTarget.normalized;
        transform.position += moveDir * speed * Time.deltaTime;
        FaceDirection(moveDir, Vector3.up, rotationSpeed);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 flatDir = target - transform.position;
        flatDir.y = 0f;

        if (flatDir.sqrMagnitude > 0.001f)
            FaceDirection(flatDir.normalized, Vector3.up, rotationSpeed);
    }

    void SnapFaceTarget(Vector3 target)
    {
        Vector3 flatDir = target - transform.position;
        flatDir.y = 0f;

        if (flatDir.sqrMagnitude <= 0.001f) return;

        transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up)
                            * Quaternion.Euler(modelRotationOffset);
    }

    void BeginFalling()
    {
        _isFalling = true;
        _fallEndTime = Time.time + fallDuration;
    }

    void UpdateFalling()
    {
        transform.position += Vector3.down * fallSpeed * Time.deltaTime;
        FaceDirection(Vector3.down, Vector3.forward, fallRotationSpeed);

        if (Time.time >= _fallEndTime)
            Destroy(gameObject);
    }

    void FaceDirection(Vector3 direction, Vector3 up, float turnSpeed)
    {
        if (direction.sqrMagnitude <= 0.001f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, up)
                                    * Quaternion.Euler(modelRotationOffset);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeed * Time.deltaTime);
    }
}
