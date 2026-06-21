using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Attach to a persistent GameObject (e.g. "GameManager").
/// Handles: left-click single select, drag-box multi-select, right-click move order.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    public enum AbilityDroneType { None, Recon, Decoy }

    [Header("References")]
    [Tooltip("Layer mask for drone colliders")]
    public LayerMask droneLayer;

    [Tooltip("Layer mask for selectable attack targets")]
    public LayerMask targetLayer;

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

    [Header("Move Destination Cursor")]
    public MoveDestinationCursor moveDestinationCursorPrefab;
    public bool showFormationDestinationCursors = false;

    [Header("MALD Path Planning")]
    public Material maldPathMaterial;
    public Color maldPathColor = Color.cyan;
    public Color maldPathInvalidColor = Color.red;
    public float maldPathWidth = 0.15f;
    public float maldDashLength = 1.5f;
    public float maldGapLength = 0.8f;
    public GameObject maldWaypointMarkerPrefab;

    [Header("Recon Targeting")]
    public Material reconRangeMaterial;
    public Color reconRangeColor = new Color(0f, 0.9f, 1f, 0.9f);
    public float reconRangeLineWidth = 0.15f;
    public int reconRangeSegments = 96;

    // ── internal state ──────────────────────────────────────────────
    private readonly List<DroneUnit> _selected = new();
    private readonly List<ShahedDroneUnit> _selectedShaheds = new();
    private readonly List<ReconDroneUnit> _selectedRecons = new();
    private readonly List<DecoyDroneUnit> _selectedDecoys = new();
    private readonly List<TargetableObject> _selectedTargets = new();
    private readonly List<DroneUnit> _allDrones = new();
    private readonly List<ShahedDroneUnit> _allShaheds = new();
    private readonly List<ReconDroneUnit> _allRecons = new();
    private readonly List<DecoyDroneUnit> _allDecoys = new();

    private Vector2 _dragStart;   // screen space
    private bool    _isDragging;
    private DroneUnit _hoveredDrone;
    private ShahedDroneUnit _hoveredShahed;
    private ReconDroneUnit _hoveredRecon;
    private DecoyDroneUnit _hoveredDecoy;
    private TargetableObject _hoveredTarget;
    private DecoyDroneUnit _activePathDecoy;
    private ReconDroneSkill _activeReconSkill;
    private readonly List<Vector3> _maldPathPoints = new();
    private readonly List<LineRenderer> _maldPreviewSegments = new();
    private readonly List<GameObject> _maldWaypointMarkers = new();
    private bool _isPlanningMaldPath;
    private bool _isTargetingReconScan;
    private bool _lastPathWasValid = true;
    private Material _defaultMaldPathMaterial;
    private Material _defaultReconRangeMaterial;
    private LineRenderer _reconRangePreview;
    private bool _pointerDownStartedOverUI;
    private readonly List<RaycastResult> _uiRaycastResults = new();
    private AbilityDroneType _activeAbilityType = AbilityDroneType.None;
    private readonly List<AbilityDroneType> _availableAbilityTypes = new();
    private readonly List<MoveDestinationCursor> _activeMoveDestinationCursors = new();
    private readonly List<DroneUnit> _moveCursorTrackedDrones = new();
    private readonly List<ShahedDroneUnit> _moveCursorTrackedShaheds = new();
    private readonly List<ReconDroneUnit> _moveCursorTrackedRecons = new();
    private readonly List<DecoyDroneUnit> _moveCursorTrackedDecoys = new();

    private float _clickTime;
    private const float ClickMaxDuration = 0.2f;

    // UI box (driven by SelectionBoxUI)
    private SelectionBoxUI _boxUI;

    public bool InputBlocked { get; set; }
    public AbilityDroneType ActiveAbilityType => _activeAbilityType;
    public bool IsReconAbilityAvailable => _selectedRecons.Count > 0;
    public bool IsDecoyAbilityAvailable => _selectedDecoys.Count > 0;

    public event Action<GameObject> OnInfoTargetChanged;

    public void SetActiveAbilityTypeFromUI(AbilityDroneType abilityType)
    {
        if (abilityType == AbilityDroneType.Recon && !IsReconAbilityAvailable) return;
        if (abilityType == AbilityDroneType.Decoy && !IsDecoyAbilityAvailable) return;

        SetActiveAbilityType(abilityType);
    }

    public void GetAvailableAbilityTypesInSelectionOrder(List<AbilityDroneType> result)
    {
        result.Clear();

        if (GameManager.Instance == null)
        {
            if (IsReconAbilityAvailable)
                result.Add(AbilityDroneType.Recon);
            if (IsDecoyAbilityAvailable)
                result.Add(AbilityDroneType.Decoy);
            return;
        }

        foreach (ISelectableDrone drone in GameManager.Instance.selectedDrones)
        {
            AbilityDroneType type = GetAbilityTypeForDrone(drone);
            if (type != AbilityDroneType.None && !result.Contains(type))
                result.Add(type);
        }
    }

    // ───────────────────────────────────────────────────────────────
    void Awake()
    {
        if (gameCamera == null) gameCamera = Camera.main;
        _boxUI = GetComponent<SelectionBoxUI>();
    }

    void OnDisable()
    {
        ClearMoveDestinationCursors();
    }

    void Start()
    {
        // Cache all drones in the scene.
        // If you spawn drones at runtime, call RegisterDrone() instead.
        foreach (var d in FindObjectsByType<DroneUnit>())
            _allDrones.Add(d);

        foreach (var d in FindObjectsByType<ShahedDroneUnit>())
            _allShaheds.Add(d);

        foreach (var d in FindObjectsByType<ReconDroneUnit>())
            _allRecons.Add(d);

        foreach (var d in FindObjectsByType<DecoyDroneUnit>())
            _allDecoys.Add(d);
    }

    /// <summary>Call this after spawning a new drone at runtime.</summary>
    public void RegisterDrone(DroneUnit drone) => _allDrones.Add(drone);
    public void RegisterShahed(ShahedDroneUnit shahed) => _allShaheds.Add(shahed);
    public void RegisterRecon(ReconDroneUnit recon) => _allRecons.Add(recon);
    public void RegisterDecoy(DecoyDroneUnit decoy) => _allDecoys.Add(decoy);

    public void SelectDronesFromSquad(IReadOnlyList<ISelectableDrone> squad)
    {
        DeselectAll();
        if (squad == null) return;

        foreach (ISelectableDrone drone in squad)
            SelectSelectableDrone(drone);
    }

    public void SelectDroneFromUI(ISelectableDrone drone, bool addToSelection)
    {
        if (drone == null) return;

        if (!addToSelection)
            DeselectAll();

        SelectSelectableDrone(drone);
    }

    public void DeselectDroneFromUI(ISelectableDrone drone)
    {
        DeselectSelectableDrone(drone);
    }

    public void ClearDroneSelectionFromUI()
    {
        DeselectAll();
    }

    /// <summary>Call before destroying a drone.</summary>
    public void UnregisterDrone(DroneUnit drone)
    {
        _allDrones.Remove(drone);
        _selected.Remove(drone);
    }

    public void UnregisterShahed(ShahedDroneUnit shahed)
    {
        _allShaheds.Remove(shahed);
        _selectedShaheds.Remove(shahed);
    }

    public void UnregisterRecon(ReconDroneUnit recon)
    {
        _allRecons.Remove(recon);
        _selectedRecons.Remove(recon);
    }

    public void UnregisterDecoy(DecoyDroneUnit decoy)
    {
        _allDecoys.Remove(decoy);
        _selectedDecoys.Remove(decoy);
    }

    // ───────────────────────────────────────────────────────────────
    void Update()
    {
        CleanupDestroyedUnits();
        RefreshActiveAbilityType();
        HandleHover();

        if (InputBlocked) return;

        HandleAbilityHotkeys();

        if (_isPlanningMaldPath)
        {
            HandleMaldPathPlanning();
            return;
        }

        if (_isTargetingReconScan)
        {
            HandleReconScanTargeting();
            return;
        }

        HandleLeftClick();
        HandleRightClick();
    }

    void HandleHover()
    {
        if (Mouse.current == null)
        {
            SetHoveredDrone(null);
            SetHoveredShahed(null);
            SetHoveredRecon(null);
            SetHoveredDecoy(null);
            SetHoveredTarget(null);
            return;
        }

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = gameCamera.ScreenPointToRay(mousePos);

        DroneUnit hoveredDrone = null;
        ShahedDroneUnit hoveredShahed = null;
        ReconDroneUnit hoveredRecon = null;
        DecoyDroneUnit hoveredDecoy = null;
        TargetableObject hoveredTarget = null;

        if (Physics.Raycast(ray, out RaycastHit droneHit, 1000f, droneLayer))
        {
            hoveredDrone = droneHit.collider.GetComponentInParent<DroneUnit>();
            if (hoveredDrone == null)
                hoveredShahed = droneHit.collider.GetComponentInParent<ShahedDroneUnit>();
            if (hoveredDrone == null && hoveredShahed == null)
                hoveredRecon = droneHit.collider.GetComponentInParent<ReconDroneUnit>();
            if (hoveredDrone == null && hoveredShahed == null && hoveredRecon == null)
                hoveredDecoy = droneHit.collider.GetComponentInParent<DecoyDroneUnit>();
        }

        if (hoveredDrone == null && hoveredShahed == null && hoveredRecon == null && hoveredDecoy == null)
            hoveredShahed = FindShahedAtScreenPoint(mousePos);

        if (hoveredDrone == null && hoveredShahed == null && hoveredRecon == null && hoveredDecoy == null)
            hoveredRecon = FindReconAtScreenPoint(mousePos);

        if (hoveredDrone == null && hoveredShahed == null && hoveredRecon == null && hoveredDecoy == null)
            hoveredDecoy = FindDecoyAtScreenPoint(mousePos);

        if (hoveredDrone == null && hoveredShahed == null && hoveredRecon == null && hoveredDecoy == null &&
            Physics.Raycast(ray, out RaycastHit targetHit, 1000f, targetLayer))
        {
            hoveredTarget = targetHit.collider.GetComponentInParent<TargetableObject>();
            if (hoveredTarget != null && (!hoveredTarget.IsAlive || !hoveredTarget.HasActionableIntel))
                hoveredTarget = null;
        }

        SetHoveredDrone(hoveredDrone);
        SetHoveredShahed(hoveredShahed);
        SetHoveredRecon(hoveredRecon);
        SetHoveredDecoy(hoveredDecoy);
        SetHoveredTarget(hoveredTarget);
    }

    void HandleAbilityHotkeys()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.tabKey.wasPressedThisFrame)
            CycleActiveAbilityType();

        if (Keyboard.current.eKey.wasPressedThisFrame)
            UseActiveAbility();
    }

    public void CycleActiveAbilityType()
    {
        GetAvailableAbilityTypesInSelectionOrder(_availableAbilityTypes);

        if (_availableAbilityTypes.Count == 0)
        {
            SetActiveAbilityType(AbilityDroneType.None);
            return;
        }

        int currentIndex = _availableAbilityTypes.IndexOf(_activeAbilityType);
        if (currentIndex < 0)
        {
            SetActiveAbilityType(_availableAbilityTypes[0]);
            return;
        }

        int nextIndex = (currentIndex + 1) % _availableAbilityTypes.Count;
        SetActiveAbilityType(_availableAbilityTypes[nextIndex]);
    }

    public void UseActiveAbility()
    {
        RefreshActiveAbilityType();

        switch (_activeAbilityType)
        {
            case AbilityDroneType.Recon:
                QuickReleaseReconScan();
                break;
            case AbilityDroneType.Decoy:
                ToggleMaldPathPlanningForSelectedDecoy();
                break;
            default:
                Debug.LogWarning("No selected drone ability is available.");
                break;
        }
    }

    public void BeginReconAbilityTargeting()
    {
        if (!TryGetActiveReconSkill(out ReconDroneSkill skill))
        {
            Debug.LogWarning("Select a ReconDrone before using recon scan.");
            return;
        }

        EndMaldPathPlanning();
        _activeReconSkill = skill;
        _isTargetingReconScan = true;
        SetActiveAbilityType(AbilityDroneType.Recon);
        UpdateReconRangePreview();
    }

    public void BeginDecoyAbility()
    {
        CancelReconScanTargeting();
        SetActiveAbilityType(AbilityDroneType.Decoy);
        ToggleMaldPathPlanningForSelectedDecoy();
    }

    void QuickReleaseReconScan()
    {
        if (!TryGetActiveReconSkill(out ReconDroneSkill skill))
        {
            Debug.LogWarning("Select a ReconDrone before using recon scan.");
            return;
        }

        CancelReconScanTargeting();
        skill.StartScan();
    }

    public void ToggleMaldPathPlanningForSelectedDecoy()
    {
        if (_isPlanningMaldPath)
        {
            DeployMaldPath();
            return;
        }

        if (_selectedDecoys.Count != 1)
        {
            Debug.LogWarning("Select exactly one DecoyDrone before entering MALD path planning.");
            return;
        }

        BeginMaldPathPlanning(_selectedDecoys[0]);
    }

    void BeginMaldPathPlanning(DecoyDroneUnit decoy)
    {
        _activePathDecoy = decoy;
        _maldPathPoints.Clear();
        ClearMaldWaypointMarkers();
        _isPlanningMaldPath = true;
        _lastPathWasValid = true;
        UpdateMaldPathPreview();
    }

    void HandleMaldPathPlanning()
    {
        if (_activePathDecoy == null)
        {
            EndMaldPathPlanning();
            return;
        }

        UpdateMaldPathPreview();

        if (Mouse.current == null || !Mouse.current.rightButton.wasPressedThisFrame || IsPointerOverUI())
            return;

        if (!TryGetMouseFlightPoint(_activePathDecoy, out Vector3 point))
            return;

        _maldPathPoints.Add(point);

        if (_activePathDecoy.GetPathLength(_maldPathPoints) > _activePathDecoy.launchRange)
        {
            _maldPathPoints.RemoveAt(_maldPathPoints.Count - 1);
            Debug.LogWarning($"{_activePathDecoy.name}: MALD path exceeds launch range ({_activePathDecoy.launchRange:0.#}).");
            return;
        }

        CreateMaldWaypointMarker(point);
        UpdateMaldPathPreview();
    }

    void HandleReconScanTargeting()
    {
        if (_activeReconSkill == null)
        {
            CancelReconScanTargeting();
            return;
        }

        UpdateReconRangePreview();

        if (Mouse.current == null) return;

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CancelReconScanTargeting();
            return;
        }

        if (!Mouse.current.leftButton.wasPressedThisFrame || IsPointerOverUI())
            return;

        _activeReconSkill.StartScan();
        CancelReconScanTargeting();
    }

    void DeployMaldPath()
    {
        if (_activePathDecoy == null)
        {
            EndMaldPathPlanning();
            return;
        }

        if (_maldPathPoints.Count == 0)
        {
            Debug.LogWarning("Place at least one MALD waypoint before deployment.");
            return;
        }

        if (!_activePathDecoy.CanDeployPath(_maldPathPoints))
        {
            Debug.LogWarning($"{_activePathDecoy.name}: MALD path exceeds launch range ({_activePathDecoy.launchRange:0.#}).");
            return;
        }

        _activePathDecoy.DeployMald(_maldPathPoints);
        EndMaldPathPlanning();
    }

    void EndMaldPathPlanning()
    {
        _isPlanningMaldPath = false;
        _activePathDecoy = null;
        _maldPathPoints.Clear();
        ClearMaldPathPreview();
        ClearMaldWaypointMarkers();
    }

    void CancelReconScanTargeting()
    {
        _isTargetingReconScan = false;
        _activeReconSkill = null;
        ClearReconRangePreview();
    }

    // ── Left Click / Drag ───────────────────────────────────────────

    void HandleLeftClick()
    {
        if (Mouse.current == null) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _pointerDownStartedOverUI = IsPointerOverUI();
            if (_pointerDownStartedOverUI)
            {
                _boxUI?.Hide();
                return;
            }

            _dragStart  = mousePos;
            _isDragging = false;
            _clickTime  = Time.unscaledTime;
        }

        if (_pointerDownStartedOverUI)
        {
            if (Mouse.current.leftButton.wasReleasedThisFrame)
                _pointerDownStartedOverUI = false;

            return;
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
        DroneUnit clickedDrone = null;
        ShahedDroneUnit clickedShahed = null;
        ReconDroneUnit clickedRecon = null;
        DecoyDroneUnit clickedDecoy = null;

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, droneLayer))
        {
            clickedDrone = hit.collider.GetComponentInParent<DroneUnit>();
            clickedShahed = hit.collider.GetComponentInParent<ShahedDroneUnit>();
            if (clickedShahed == null)
                clickedRecon = hit.collider.GetComponentInParent<ReconDroneUnit>();
            if (clickedShahed == null && clickedRecon == null)
                clickedDecoy = hit.collider.GetComponentInParent<DecoyDroneUnit>();
        }

        if (clickedDrone == null && clickedShahed == null && clickedRecon == null && clickedDecoy == null)
            clickedShahed = FindShahedAtScreenPoint(screenPos);

        if (clickedDrone == null && clickedShahed == null && clickedRecon == null && clickedDecoy == null)
            clickedRecon = FindReconAtScreenPoint(screenPos);

        if (clickedDrone == null && clickedShahed == null && clickedRecon == null && clickedDecoy == null)
            clickedDecoy = FindDecoyAtScreenPoint(screenPos);

        if (!IsSelectableDroneAlive(clickedDrone)) clickedDrone = null;
        if (!IsSelectableDroneAlive(clickedShahed)) clickedShahed = null;
        if (!IsSelectableDroneAlive(clickedRecon)) clickedRecon = null;
        if (!IsSelectableDroneAlive(clickedDecoy)) clickedDecoy = null;

        if (clickedDrone != null || clickedShahed != null || clickedRecon != null || clickedDecoy != null)
        {

            if (!additive)
            {
                // Clicking an already-selected drone while not holding shift = deselect all others
                DeselectAll();
            }

            if (clickedDrone != null && !_selected.Contains(clickedDrone))
                Select(clickedDrone);
            else if (clickedDrone != null && additive)
                Deselect(clickedDrone);   // Shift-click selected drone = toggle off
            else if (clickedShahed != null && !_selectedShaheds.Contains(clickedShahed))
                Select(clickedShahed);
            else if (clickedShahed != null && additive)
                Deselect(clickedShahed);
            else if (clickedRecon != null && !_selectedRecons.Contains(clickedRecon))
                Select(clickedRecon);
            else if (clickedRecon != null && additive)
                Deselect(clickedRecon);
            else if (clickedDecoy != null && !_selectedDecoys.Contains(clickedDecoy))
                Select(clickedDecoy);
            else if (clickedDecoy != null && additive)
                Deselect(clickedDecoy);
        }
        else if (Physics.Raycast(ray, out hit, 1000f, targetLayer))
        {
            var target = hit.collider.GetComponentInParent<TargetableObject>();
            if (target == null || !target.IsAlive || !target.HasActionableIntel) return;

            if (!additive)
            {
                DeselectAllTargets();
                DeselectAll();
            }

            if (!_selectedTargets.Contains(target))
                Select(target);
            else if (additive)
                Deselect(target);
        }
        else
        {
            // Clicked empty ground → clear selection (unless shift held)
            if (!additive)
            {
                DeselectAll();
                DeselectAllTargets();
                ClearInfoTarget();
            }
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
            if (!IsSelectableDroneAlive(drone)) continue;

            Vector3 screenPoint = gameCamera.WorldToScreenPoint(drone.transform.position);
            if (screenRect.Contains(screenPoint, true))
            {
                if (!_selected.Contains(drone))
                    Select(drone);
            }
        }

        foreach (var shahed in _allShaheds)
        {
            if (!IsSelectableDroneAlive(shahed)) continue;

            Vector3 screenPoint = gameCamera.WorldToScreenPoint(shahed.transform.position);
            if (screenRect.Contains(screenPoint, true))
            {
                if (!_selectedShaheds.Contains(shahed))
                    Select(shahed);
            }
        }

        foreach (var recon in _allRecons)
        {
            if (!IsSelectableDroneAlive(recon)) continue;

            Vector3 screenPoint = gameCamera.WorldToScreenPoint(recon.transform.position);
            if (screenRect.Contains(screenPoint, true))
            {
                if (!_selectedRecons.Contains(recon))
                    Select(recon);
            }
        }

        foreach (var decoy in _allDecoys)
        {
            if (!IsSelectableDroneAlive(decoy)) continue;

            Vector3 screenPoint = gameCamera.WorldToScreenPoint(decoy.transform.position);
            if (screenRect.Contains(screenPoint, true))
            {
                if (!_selectedDecoys.Contains(decoy))
                    Select(decoy);
            }
        }
    }

    // ── Right Click – Move Order ─────────────────────────────────────

    void HandleRightClick()
    {
        if (Mouse.current == null) return;
        if (!Mouse.current.rightButton.wasPressedThisFrame) return;
        if (IsPointerOverUI()) return;

        int movableCount = _selected.Count + _selectedShaheds.Count + _selectedRecons.Count + _selectedDecoys.Count;
        if (movableCount == 0) return;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = gameCamera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out RaycastHit targetHit, 1000f, targetLayer))
        {
            var target = targetHit.collider.GetComponentInParent<TargetableObject>();
            if (target != null && target.IsAlive && target.HasActionableIntel)
            {
                ClearMoveDestinationCursors();
                DeselectAllTargets();
                Select(target);

                foreach (var drone in _selected)
                    drone.AttackTarget(target);

                foreach (var shahed in _selectedShaheds)
                {
                    if (shahed != null)
                        shahed.AttackTarget(target);
                }

                return;
            }
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer)) return;

        Vector3 destination = hit.point;
        ClearMoveDestinationCursors();
        DeselectAllTargets();

        // Spread drones in a grid/circle formation so they don't pile up
        List<Vector3> positions = GetFormationPositions(destination, movableCount);

        int index = 0;
        for (int i = 0; i < _selected.Count; i++, index++)
            _selected[i].MoveTo(positions[index]);

        for (int i = 0; i < _selectedShaheds.Count; i++, index++)
        {
            if (_selectedShaheds[i] != null)
                _selectedShaheds[i].MoveTo(positions[index]);
        }

        for (int i = 0; i < _selectedRecons.Count; i++, index++)
        {
            if (_selectedRecons[i] != null)
                _selectedRecons[i].MoveTo(positions[index]);
        }

        for (int i = 0; i < _selectedDecoys.Count; i++, index++)
        {
            if (_selectedDecoys[i] != null)
                _selectedDecoys[i].MoveTo(positions[index]);
        }

        ShowMoveDestinationCursors(destination, hit.normal, positions);
        TrackMoveDestinationArrivals();
    }

    void ShowMoveDestinationCursors(Vector3 destination, Vector3 surfaceNormal, List<Vector3> formationPositions)
    {
        if (moveDestinationCursorPrefab == null) return;

        if (!showFormationDestinationCursors)
        {
            SpawnMoveDestinationCursor(destination, surfaceNormal);
            return;
        }

        foreach (Vector3 position in formationPositions)
            SpawnMoveDestinationCursor(position, surfaceNormal);
    }

    void SpawnMoveDestinationCursor(Vector3 position, Vector3 surfaceNormal)
    {
        MoveDestinationCursor cursor = Instantiate(moveDestinationCursorPrefab);
        cursor.Show(position, surfaceNormal);
        _activeMoveDestinationCursors.Add(cursor);
    }

    void TrackMoveDestinationArrivals()
    {
        foreach (DroneUnit drone in _selected)
        {
            if (drone == null) continue;
            drone.MoveArrived += HandleMoveDestinationArrived;
            _moveCursorTrackedDrones.Add(drone);
        }

        foreach (ShahedDroneUnit shahed in _selectedShaheds)
        {
            if (shahed == null) continue;
            shahed.MoveArrived += HandleMoveDestinationArrived;
            _moveCursorTrackedShaheds.Add(shahed);
        }

        foreach (ReconDroneUnit recon in _selectedRecons)
        {
            if (recon == null) continue;
            recon.MoveArrived += HandleMoveDestinationArrived;
            _moveCursorTrackedRecons.Add(recon);
        }

        foreach (DecoyDroneUnit decoy in _selectedDecoys)
        {
            if (decoy == null) continue;
            decoy.MoveArrived += HandleMoveDestinationArrived;
            _moveCursorTrackedDecoys.Add(decoy);
        }
    }

    void HandleMoveDestinationArrived(DroneUnit drone) => ClearMoveDestinationCursors();
    void HandleMoveDestinationArrived(ShahedDroneUnit shahed) => ClearMoveDestinationCursors();
    void HandleMoveDestinationArrived(ReconDroneUnit recon) => ClearMoveDestinationCursors();
    void HandleMoveDestinationArrived(DecoyDroneUnit decoy) => ClearMoveDestinationCursors();

    void ClearMoveDestinationCursors()
    {
        UntrackMoveDestinationArrivals();

        foreach (MoveDestinationCursor cursor in _activeMoveDestinationCursors)
        {
            if (cursor != null)
                cursor.Dismiss();
        }

        _activeMoveDestinationCursors.Clear();
    }

    void UntrackMoveDestinationArrivals()
    {
        foreach (DroneUnit drone in _moveCursorTrackedDrones)
            if (drone != null)
                drone.MoveArrived -= HandleMoveDestinationArrived;

        foreach (ShahedDroneUnit shahed in _moveCursorTrackedShaheds)
            if (shahed != null)
                shahed.MoveArrived -= HandleMoveDestinationArrived;

        foreach (ReconDroneUnit recon in _moveCursorTrackedRecons)
            if (recon != null)
                recon.MoveArrived -= HandleMoveDestinationArrived;

        foreach (DecoyDroneUnit decoy in _moveCursorTrackedDecoys)
            if (decoy != null)
                decoy.MoveArrived -= HandleMoveDestinationArrived;

        _moveCursorTrackedDrones.Clear();
        _moveCursorTrackedShaheds.Clear();
        _moveCursorTrackedRecons.Clear();
        _moveCursorTrackedDecoys.Clear();
    }

    // ── Selection Helpers ───────────────────────────────────────────

    void Select(DroneUnit drone)
    {
        _selected.Add(drone);
        drone.SetSelected(true);
        GameManager.Instance?.AddSelectedDrone(drone);
        NotifyInfoTarget(drone.gameObject);
    }

    void Select(ShahedDroneUnit shahed)
    {
        _selectedShaheds.Add(shahed);
        shahed.SetSelected(true);
        GameManager.Instance?.AddSelectedDrone(shahed);
        NotifyInfoTarget(shahed.gameObject);
    }

    void Select(ReconDroneUnit recon)
    {
        _selectedRecons.Add(recon);
        recon.SetSelected(true);
        GameManager.Instance?.AddSelectedDrone(recon);
        NotifyInfoTarget(recon.gameObject);
    }

    void Select(DecoyDroneUnit decoy)
    {
        _selectedDecoys.Add(decoy);
        decoy.SetSelected(true);
        GameManager.Instance?.AddSelectedDrone(decoy);
        NotifyInfoTarget(decoy.gameObject);
    }

    void Select(TargetableObject target)
    {
        _selectedTargets.Add(target);
        target.SetSelected(true);
        NotifyInfoTarget(target.gameObject);
    }

    void Deselect(DroneUnit drone)
    {
        _selected.Remove(drone);
        drone.SetSelected(false);
        GameManager.Instance?.RemoveSelectedDrone(drone);
    }

    void Deselect(ShahedDroneUnit shahed)
    {
        _selectedShaheds.Remove(shahed);
        shahed.SetSelected(false);
        GameManager.Instance?.RemoveSelectedDrone(shahed);
    }

    void Deselect(ReconDroneUnit recon)
    {
        _selectedRecons.Remove(recon);
        recon.SetSelected(false);
        GameManager.Instance?.RemoveSelectedDrone(recon);
    }

    void Deselect(DecoyDroneUnit decoy)
    {
        _selectedDecoys.Remove(decoy);
        decoy.SetSelected(false);
        GameManager.Instance?.RemoveSelectedDrone(decoy);
    }

    void Deselect(TargetableObject target)
    {
        _selectedTargets.Remove(target);
        if (target != null)
            target.SetSelected(false);
    }

    void DeselectAll()
    {
        foreach (var d in _selected) if (d != null) d.SetSelected(false);
        foreach (var d in _selectedShaheds) if (d != null) d.SetSelected(false);
        foreach (var d in _selectedRecons) if (d != null) d.SetSelected(false);
        foreach (var d in _selectedDecoys) if (d != null) d.SetSelected(false);
        _selected.Clear();
        _selectedShaheds.Clear();
        _selectedRecons.Clear();
        _selectedDecoys.Clear();
        GameManager.Instance?.ReplaceSelection(System.Array.Empty<ISelectableDrone>());
    }

    void NotifyInfoTarget(GameObject target)
    {
        OnInfoTargetChanged?.Invoke(target);
    }

    void ClearInfoTarget()
    {
        OnInfoTargetChanged?.Invoke(null);
    }

    void SelectSelectableDrone(ISelectableDrone drone)
    {
        switch (drone)
        {
            case DroneUnit standard when !_selected.Contains(standard):
                Select(standard);
                break;
            case ShahedDroneUnit shahed when !_selectedShaheds.Contains(shahed):
                Select(shahed);
                break;
            case ReconDroneUnit recon when !_selectedRecons.Contains(recon):
                Select(recon);
                break;
            case DecoyDroneUnit decoy when !_selectedDecoys.Contains(decoy):
                Select(decoy);
                break;
        }
    }

    void DeselectSelectableDrone(ISelectableDrone drone)
    {
        switch (drone)
        {
            case DroneUnit standard:
                Deselect(standard);
                break;
            case ShahedDroneUnit shahed:
                Deselect(shahed);
                break;
            case ReconDroneUnit recon:
                Deselect(recon);
                break;
            case DecoyDroneUnit decoy:
                Deselect(decoy);
                break;
        }
    }

    void DeselectAllTargets()
    {
        foreach (var target in _selectedTargets)
        {
            if (target != null)
                target.SetSelected(false);
        }

        _selectedTargets.Clear();
    }

    void SetHoveredDrone(DroneUnit drone)
    {
        if (_hoveredDrone == drone) return;

        if (_hoveredDrone != null)
            _hoveredDrone.SetHovered(false);

        _hoveredDrone = drone;

        if (_hoveredDrone != null)
            _hoveredDrone.SetHovered(true);
    }

    void SetHoveredShahed(ShahedDroneUnit shahed)
    {
        if (_hoveredShahed == shahed) return;

        if (_hoveredShahed != null)
            _hoveredShahed.SetHovered(false);

        _hoveredShahed = shahed;

        if (_hoveredShahed != null)
            _hoveredShahed.SetHovered(true);
    }

    void SetHoveredRecon(ReconDroneUnit recon)
    {
        if (_hoveredRecon == recon) return;

        if (_hoveredRecon != null)
            _hoveredRecon.SetHovered(false);

        _hoveredRecon = recon;

        if (_hoveredRecon != null)
            _hoveredRecon.SetHovered(true);
    }

    void SetHoveredDecoy(DecoyDroneUnit decoy)
    {
        if (_hoveredDecoy == decoy) return;

        if (_hoveredDecoy != null)
            _hoveredDecoy.SetHovered(false);

        _hoveredDecoy = decoy;

        if (_hoveredDecoy != null)
            _hoveredDecoy.SetHovered(true);
    }

    void SetHoveredTarget(TargetableObject target)
    {
        if (_hoveredTarget == target) return;

        if (_hoveredTarget != null)
            _hoveredTarget.SetHovered(false);

        _hoveredTarget = target;

        if (_hoveredTarget != null)
            _hoveredTarget.SetHovered(true);
    }

    void CleanupDestroyedUnits()
    {
        _selected.RemoveAll(d => !IsSelectableDroneAlive(d));
        _selectedShaheds.RemoveAll(d => !IsSelectableDroneAlive(d));
        _selectedRecons.RemoveAll(d => !IsSelectableDroneAlive(d));
        _selectedDecoys.RemoveAll(d => !IsSelectableDroneAlive(d));
        _allDrones.RemoveAll(d => d == null);
        _allShaheds.RemoveAll(d => !IsSelectableDroneAlive(d));
        _allRecons.RemoveAll(d => d == null);
        _allDecoys.RemoveAll(d => d == null);
    }

    static bool IsSelectableDroneAlive(ISelectableDrone drone)
    {
        if (drone == null)
            return false;

        if (drone is UnityEngine.Object unityObject && unityObject == null)
            return false;

        GameObject droneObject = drone.GameObject;
        if (droneObject == null || !droneObject.activeInHierarchy)
            return false;

        DroneHealth health = droneObject.GetComponentInChildren<DroneHealth>();
        return health == null || health.CurrentHP > 0;
    }

    void RefreshActiveAbilityType()
    {
        bool hasRecon = IsReconAbilityAvailable;
        bool hasDecoy = IsDecoyAbilityAvailable;

        if (!hasRecon && !hasDecoy)
        {
            SetActiveAbilityType(AbilityDroneType.None);
            CancelReconScanTargeting();
            EndMaldPathPlanning();
            return;
        }

        if (_activeAbilityType == AbilityDroneType.Recon && hasRecon) return;
        if (_activeAbilityType == AbilityDroneType.Decoy && hasDecoy) return;

        SetActiveAbilityType(GetFirstAbilityTypeInSelectionOrder());
    }

    void SetActiveAbilityType(AbilityDroneType abilityType)
    {
        _activeAbilityType = abilityType;
    }

    bool TryGetActiveReconSkill(out ReconDroneSkill skill)
    {
        skill = null;

        foreach (ReconDroneUnit recon in _selectedRecons)
        {
            if (recon == null) continue;

            skill = recon.GetComponent<ReconDroneSkill>();
            if (skill != null)
                return true;
        }

        return false;
    }

    AbilityDroneType GetFirstAbilityTypeInSelectionOrder()
    {
        if (GameManager.Instance != null)
        {
            foreach (ISelectableDrone drone in GameManager.Instance.selectedDrones)
            {
                AbilityDroneType type = GetAbilityTypeForDrone(drone);
                if (type != AbilityDroneType.None)
                    return type;
            }
        }

        if (IsReconAbilityAvailable) return AbilityDroneType.Recon;
        if (IsDecoyAbilityAvailable) return AbilityDroneType.Decoy;
        return AbilityDroneType.None;
    }

    static AbilityDroneType GetAbilityTypeForDrone(ISelectableDrone drone)
    {
        switch (drone)
        {
            case ReconDroneUnit:
                return AbilityDroneType.Recon;
            case DecoyDroneUnit:
                return AbilityDroneType.Decoy;
            default:
                return AbilityDroneType.None;
        }
    }

    bool IsPointerOverUI()
    {
        if (EventSystem.current == null || Mouse.current == null)
            return false;

        PointerEventData pointerData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        _uiRaycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _uiRaycastResults);

        foreach (RaycastResult result in _uiRaycastResults)
        {
            GameObject hitObject = result.gameObject;
            if (hitObject == null) continue;

            if (_boxUI != null &&
                _boxUI.selectionBoxRect != null &&
                hitObject.transform.IsChildOf(_boxUI.selectionBoxRect))
            {
                continue;
            }

            if (hitObject.GetComponentInParent<Selectable>() != null)
                return true;
        }

        return false;
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

    DecoyDroneUnit FindDecoyAtScreenPoint(Vector2 screenPoint)
    {
        DecoyDroneUnit bestDecoy = null;
        float bestDistance = float.PositiveInfinity;

        foreach (var decoy in _allDecoys)
        {
            if (decoy == null) continue;
            if (!decoy.ContainsScreenPoint(gameCamera, screenPoint)) continue;

            Vector3 decoyScreenPoint = gameCamera.WorldToScreenPoint(decoy.transform.position);
            if (decoyScreenPoint.z <= 0f) continue;

            float distance = Vector2.Distance(screenPoint, decoyScreenPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestDecoy = decoy;
            }
        }

        return bestDecoy;
    }

    ShahedDroneUnit FindShahedAtScreenPoint(Vector2 screenPoint)
    {
        ShahedDroneUnit bestShahed = null;
        float bestDistance = float.PositiveInfinity;

        foreach (var shahed in _allShaheds)
        {
            if (shahed == null) continue;
            if (!shahed.ContainsScreenPoint(gameCamera, screenPoint)) continue;

            Vector3 shahedScreenPoint = gameCamera.WorldToScreenPoint(shahed.transform.position);
            if (shahedScreenPoint.z <= 0f) continue;

            float distance = Vector2.Distance(screenPoint, shahedScreenPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestShahed = shahed;
            }
        }

        return bestShahed;
    }

    ReconDroneUnit FindReconAtScreenPoint(Vector2 screenPoint)
    {
        ReconDroneUnit bestRecon = null;
        float bestDistance = float.PositiveInfinity;

        foreach (var recon in _allRecons)
        {
            if (recon == null) continue;
            if (!recon.ContainsScreenPoint(gameCamera, screenPoint)) continue;

            Vector3 reconScreenPoint = gameCamera.WorldToScreenPoint(recon.transform.position);
            if (reconScreenPoint.z <= 0f) continue;

            float distance = Vector2.Distance(screenPoint, reconScreenPoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestRecon = recon;
            }
        }

        return bestRecon;
    }

    bool TryGetMouseFlightPoint(DecoyDroneUnit decoy, out Vector3 point)
    {
        point = Vector3.zero;

        if (Mouse.current == null || decoy == null)
            return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = gameCamera.ScreenPointToRay(mousePos);
        Plane flightPlane = new Plane(Vector3.up, decoy.LaunchPosition);

        if (!flightPlane.Raycast(ray, out float enter))
            return false;

        point = ray.GetPoint(enter);
        return true;
    }

    void UpdateMaldPathPreview()
    {
        if (_activePathDecoy == null)
        {
            ClearMaldPathPreview();
            return;
        }

        List<Vector3> previewPoints = new() { _activePathDecoy.LaunchPosition };
        previewPoints.AddRange(_maldPathPoints);

        List<Vector3> pathWithMouse = new(_maldPathPoints);
        if (TryGetMouseFlightPoint(_activePathDecoy, out Vector3 mousePoint))
        {
            previewPoints.Add(mousePoint);
            pathWithMouse.Add(mousePoint);
        }

        bool isValid = _activePathDecoy.GetPathLength(pathWithMouse) <= _activePathDecoy.launchRange;
        if (!isValid && _lastPathWasValid)
            Debug.LogWarning($"{_activePathDecoy.name}: MALD preview exceeds launch range ({_activePathDecoy.launchRange:0.#}).");

        _lastPathWasValid = isValid;
        DrawDashedPath(previewPoints, isValid ? maldPathColor : maldPathInvalidColor);
    }

    void UpdateReconRangePreview()
    {
        if (_activeReconSkill == null)
        {
            ClearReconRangePreview();
            return;
        }

        if (_reconRangePreview == null)
            _reconRangePreview = CreateReconRangePreview();

        int segments = Mathf.Max(12, reconRangeSegments);
        float radius = Mathf.Max(0.1f, _activeReconSkill.scanRadius);
        Vector3 center = _activeReconSkill.transform.position;

        _reconRangePreview.gameObject.SetActive(true);
        _reconRangePreview.startColor = reconRangeColor;
        _reconRangePreview.endColor = reconRangeColor;
        _reconRangePreview.startWidth = reconRangeLineWidth;
        _reconRangePreview.endWidth = reconRangeLineWidth;
        _reconRangePreview.positionCount = segments + 1;

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.PI * 2f * i / segments;
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            _reconRangePreview.SetPosition(i, point);
        }
    }

    void DrawDashedPath(IReadOnlyList<Vector3> points, Color color)
    {
        int segmentIndex = 0;
        float dashLength = Mathf.Max(0.1f, maldDashLength);
        float gapLength = Mathf.Max(0.05f, maldGapLength);

        for (int i = 1; i < points.Count; i++)
        {
            Vector3 start = points[i - 1];
            Vector3 end = points[i];
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance <= 0.001f) continue;

            Vector3 dir = delta / distance;
            float travelled = 0f;

            while (travelled < distance)
            {
                float dashEnd = Mathf.Min(travelled + dashLength, distance);
                SetPreviewSegment(segmentIndex, start + dir * travelled, start + dir * dashEnd, color);
                segmentIndex++;
                travelled = dashEnd + gapLength;
            }
        }

        for (int i = segmentIndex; i < _maldPreviewSegments.Count; i++)
            _maldPreviewSegments[i].gameObject.SetActive(false);
    }

    void SetPreviewSegment(int index, Vector3 start, Vector3 end, Color color)
    {
        while (_maldPreviewSegments.Count <= index)
            _maldPreviewSegments.Add(CreatePreviewSegment());

        LineRenderer line = _maldPreviewSegments[index];
        line.gameObject.SetActive(true);
        line.startColor = color;
        line.endColor = color;
        line.startWidth = maldPathWidth;
        line.endWidth = maldPathWidth;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }

    LineRenderer CreatePreviewSegment()
    {
        GameObject segment = new GameObject("MALD Path Preview Segment");
        segment.transform.SetParent(transform);

        LineRenderer line = segment.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.material = GetMaldPathMaterial();
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    LineRenderer CreateReconRangePreview()
    {
        GameObject preview = new GameObject("Recon Range Preview");
        preview.transform.SetParent(transform);

        LineRenderer line = preview.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.material = GetReconRangeMaterial();
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    Material GetMaldPathMaterial()
    {
        if (maldPathMaterial != null)
            return maldPathMaterial;

        if (_defaultMaldPathMaterial == null)
            _defaultMaldPathMaterial = new Material(Shader.Find("Sprites/Default"));

        return _defaultMaldPathMaterial;
    }

    Material GetReconRangeMaterial()
    {
        if (reconRangeMaterial != null)
            return reconRangeMaterial;

        if (_defaultReconRangeMaterial == null)
            _defaultReconRangeMaterial = new Material(Shader.Find("Sprites/Default"));

        return _defaultReconRangeMaterial;
    }

    void ClearMaldPathPreview()
    {
        foreach (var segment in _maldPreviewSegments)
        {
            if (segment != null)
                segment.gameObject.SetActive(false);
        }
    }

    void ClearReconRangePreview()
    {
        if (_reconRangePreview != null)
            _reconRangePreview.gameObject.SetActive(false);
    }

    void CreateMaldWaypointMarker(Vector3 point)
    {
        GameObject marker = maldWaypointMarkerPrefab != null
            ? Instantiate(maldWaypointMarkerPrefab, point, Quaternion.identity)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        marker.name = "MALD Waypoint Marker";
        marker.transform.position = point;
        marker.transform.localScale = Vector3.one * 0.8f;
        _maldWaypointMarkers.Add(marker);
    }

    void ClearMaldWaypointMarkers()
    {
        foreach (var marker in _maldWaypointMarkers)
        {
            if (marker != null)
                Destroy(marker);
        }

        _maldWaypointMarkers.Clear();
    }
}
