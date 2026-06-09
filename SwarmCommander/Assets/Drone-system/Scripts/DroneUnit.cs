using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Attach to each drone (Capsule). Handles selection highlight and movement via NavMeshAgent.
/// If you don't want NavMesh, set useNavMesh = false and the drone will lerp toward the target.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DroneUnit : MonoBehaviour
{
    [Header("Selection Visual")]
    [Tooltip("Assign a child GameObject with a Projector or a circle mesh under the drone")]
    public GameObject selectionIndicator;

    [Tooltip("Highlight color when selected")]
    public Color selectedColor = new Color(0f, 1f, 0.5f, 1f);

    [Tooltip("Normal (unselected) color")]
    public Color normalColor = Color.white;

    [Header("Movement")]
    [Tooltip("Use NavMeshAgent for pathfinding (recommended). Disable if your scene has no NavMesh.")]
    public bool useNavMesh = true;
    public float moveSpeed = 8f;

    // ── internal state ─────────────────────────────────────────────
    private bool _isSelected;
    private Renderer[] _renderers;
    private NavMeshAgent _agent;
    private Vector3 _targetPosition;
    private bool _hasTarget;

    public bool IsSelected => _isSelected;

    // ───────────────────────────────────────────────────────────────
    void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>();
        _agent = GetComponent<NavMeshAgent>();

        if (useNavMesh && _agent == null)
        {
            _agent = gameObject.AddComponent<NavMeshAgent>();
        }

        if (_agent != null)
        {
            _agent.speed = moveSpeed;
            _agent.acceleration = 20f;
            _agent.stoppingDistance = 0.5f;
        }

        SetSelectionIndicator(false);
        ApplyColor(normalColor);
    }

    void Update()
    {
        if (!useNavMesh && _hasTarget)
        {
            transform.position = Vector3.MoveTowards(
                transform.position, _targetPosition, moveSpeed * Time.deltaTime);
            if (Vector3.Distance(transform.position, _targetPosition) < 0.1f)
                _hasTarget = false;
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>Set whether this drone is currently selected.</summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        ApplyColor(selected ? selectedColor : normalColor);
        SetSelectionIndicator(selected);
    }

    /// <summary>Command the drone to move to worldPosition.</summary>
    public void MoveTo(Vector3 worldPosition)
    {
        if (useNavMesh && _agent != null && _agent.isOnNavMesh)
        {
            _agent.SetDestination(worldPosition);
        }
        else
        {
            // Fallback: keep same Y so the drone doesn't dive into the ground
            _targetPosition = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
            _hasTarget = true;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    void ApplyColor(Color color)
    {
        foreach (var r in _renderers)
        {
            // Works with URP/HDRP (use "_BaseColor") or legacy ("_Color")
            if (r.material.HasProperty("_BaseColor"))
                r.material.color = color;          // URP
            else if (r.material.HasProperty("_Color"))
                r.material.color = color;          // Legacy / Standard
        }
    }

    void SetSelectionIndicator(bool show)
    {
        if (selectionIndicator != null)
            selectionIndicator.SetActive(show);
    }
}