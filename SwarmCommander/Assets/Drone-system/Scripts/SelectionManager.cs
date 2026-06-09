using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to a persistent GameObject (e.g. "GameManager").
/// Handles: left-click single select, drag-box multi-select, right-click move order.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Layer mask for drone colliders")]
    public LayerMask droneLayer;

    [Tooltip("Layer mask for ground (right-click move target)")]
    public LayerMask groundLayer;

    [Tooltip("The Camera used for raycasting (leave empty → Camera.main)")]
    public Camera gameCamera;

    [Header("Drag Settings")]
    [Tooltip("Minimum drag distance in pixels before box-select activates")]
    public float dragThreshold = 5f;

    [Header("Move Formation")]
    [Tooltip("Spread drones out by this distance when issuing a group move order")]
    public float formationSpacing = 2f;

    // ── internal state ──────────────────────────────────────────────
    private readonly List<DroneUnit> _selected = new();
    private readonly List<DroneUnit> _allDrones = new();

    private Vector2 _dragStart;   // screen space
    private bool    _isDragging;

    private float _clickTime;
    private const float ClickMaxDuration = 0.2f;

    // UI box (driven by SelectionBoxUI)
    private SelectionBoxUI _boxUI;

    // ───────────────────────────────────────────────────────────────
    void Awake()
    {
        if (gameCamera == null) gameCamera = Camera.main;
        _boxUI = GetComponent<SelectionBoxUI>();
    }

    void Start()
    {
        // Cache all drones in the scene.
        // If you spawn drones at runtime, call RegisterDrone() instead.
        foreach (var d in FindObjectsByType<DroneUnit>(FindObjectsSortMode.None))
            _allDrones.Add(d);
    }

    /// <summary>Call this after spawning a new drone at runtime.</summary>
    public void RegisterDrone(DroneUnit drone) => _allDrones.Add(drone);

    /// <summary>Call before destroying a drone.</summary>
    public void UnregisterDrone(DroneUnit drone)
    {
        _allDrones.Remove(drone);
        _selected.Remove(drone);
    }

    // ───────────────────────────────────────────────────────────────
    void Update()
    {
        HandleLeftClick();
        HandleRightClick();
    }

    // ── Left Click / Drag ───────────────────────────────────────────

    void HandleLeftClick()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _dragStart  = mousePos;
            _isDragging = false;
            _clickTime  = Time.unscaledTime;
        }

        if (Mouse.current.leftButton.isPressed)
        {
            float dist = Vector2.Distance(mousePos, _dragStart);
            if (!_isDragging && dist >= dragThreshold)
                _isDragging = true;

            if (_isDragging)
                _boxUI?.UpdateBox(_dragStart, mousePos);
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            _boxUI?.Hide();

            float duration = Time.unscaledTime - _clickTime;
            bool isClick   = !_isDragging && duration < ClickMaxDuration;

            if (isClick)
            {
                bool additive = Keyboard.current != null &&
                                (Keyboard.current.leftShiftKey.isPressed ||
                                Keyboard.current.rightShiftKey.isPressed);
                ClickSelect(mousePos, additive);
            }
            else if (_isDragging)
            {
                SelectByRect(_dragStart, mousePos);
            }

            _isDragging = false;
        }
    }

    void ClickSelect(Vector2 screenPos, bool additive)
    {
        Ray ray = gameCamera.ScreenPointToRay(screenPos);

        
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, droneLayer))
        {
            var drone = hit.collider.GetComponentInParent<DroneUnit>();
            if (drone == null) return;

            if (!additive)
            {
                // Clicking an already-selected drone while not holding shift = deselect all others
                DeselectAll();
            }

            if (!_selected.Contains(drone))
                Select(drone);
            else if (additive)
                Deselect(drone);   // Shift-click selected drone = toggle off
        }
        else
        {
            // Clicked empty ground → clear selection (unless shift held)
            if (!additive) DeselectAll();
        }
    }

    void SelectByRect(Vector2 screenA, Vector2 screenB)
    {
        // Build a screen-space Rect
        Rect screenRect = GetScreenRect(screenA, screenB);

        bool additive = Keyboard.current != null &&
                        (Keyboard.current.leftShiftKey.isPressed ||
                         Keyboard.current.rightShiftKey.isPressed);

        if (!additive) DeselectAll();

        foreach (var drone in _allDrones)
        {
            Vector3 screenPoint = gameCamera.WorldToScreenPoint(drone.transform.position);
            if (screenRect.Contains(screenPoint, true))
            {
                if (!_selected.Contains(drone))
                    Select(drone);
            }
        }
    }

    // ── Right Click – Move Order ─────────────────────────────────────

    void HandleRightClick()
    {
        if (Mouse.current == null) return;
        if (!Mouse.current.rightButton.wasPressedThisFrame) return;
        if (_selected.Count == 0) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = gameCamera.ScreenPointToRay(mousePos);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer)) return;

        Vector3 destination = hit.point;

        // Spread drones in a grid/circle formation so they don't pile up
        List<Vector3> positions = GetFormationPositions(destination, _selected.Count);

        for (int i = 0; i < _selected.Count; i++)
            _selected[i].MoveTo(positions[i]);
    }

    // ── Selection Helpers ───────────────────────────────────────────

    void Select(DroneUnit drone)
    {
        _selected.Add(drone);
        drone.SetSelected(true);
    }

    void Deselect(DroneUnit drone)
    {
        _selected.Remove(drone);
        drone.SetSelected(false);
    }

    void DeselectAll()
    {
        foreach (var d in _selected) d.SetSelected(false);
        _selected.Clear();
    }

    // ── Formation ───────────────────────────────────────────────────

    List<Vector3> GetFormationPositions(Vector3 center, int count)
    {
        var positions = new List<Vector3>();

        if (count == 1)
        {
            positions.Add(center);
            return positions;
        }

        // Simple grid formation
        int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
        int rows = Mathf.CeilToInt((float)count / cols);

        float totalW = (cols - 1) * formationSpacing;
        float totalD = (rows - 1) * formationSpacing;

        int index = 0;
        for (int r = 0; r < rows && index < count; r++)
        {
            for (int c = 0; c < cols && index < count; c++, index++)
            {
                float x = center.x - totalW * 0.5f + c * formationSpacing;
                float z = center.z - totalD * 0.5f + r * formationSpacing;
                positions.Add(new Vector3(x, center.y, z));
            }
        }

        return positions;
    }

    // ── Utility ─────────────────────────────────────────────────────

    static Rect GetScreenRect(Vector2 a, Vector2 b)
    {
        return new Rect(
            Mathf.Min(a.x, b.x),
            Mathf.Min(a.y, b.y),
            Mathf.Abs(a.x - b.x),
            Mathf.Abs(a.y - b.y));
    }
}