using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class DronePlacementManager : MonoBehaviour
{
    enum PlacementType { None, FightingDrone, DecoyDrone, ShahedDrone }

    [Header("References")]
    public Camera gameCamera;
    public SelectionManager selectionManager;
    public LayerMask groundLayer;
    public Transform placementOrigin;

    [Header("Prefabs")]
    public GameObject fightingDronePrefab;
    public GameObject decoyDronePrefab;
    public GameObject shahedDronePrefab;

    [Header("Placement")]
    public float placementRange = 60f;
    public float spawnHeightAboveGround = 8f;
    public bool horizontalRangeOnly = true;

    private PlacementType _pendingType = PlacementType.None;
    private bool _waitingForUiMouseRelease;

    public bool IsPlacing => _pendingType != PlacementType.None;

    void Awake()
    {
        if (gameCamera == null) gameCamera = Camera.main;
        if (selectionManager == null) selectionManager = FindFirstObjectByType<SelectionManager>();
    }

    void Update()
    {
        if (selectionManager != null)
            selectionManager.InputBlocked = IsPlacing;

        if (!IsPlacing) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        if (Mouse.current == null) return;

        if (_waitingForUiMouseRelease)
        {
            if (!Mouse.current.leftButton.isPressed)
                _waitingForUiMouseRelease = false;

            return;
        }

        if (!Mouse.current.leftButton.wasPressedThisFrame) return;
        if (IsPointerOverUi()) return;

        TryPlacePendingDrone();
    }

    void OnDisable()
    {
        if (selectionManager != null)
            selectionManager.InputBlocked = false;
    }

    public void BeginPlaceFightingDrone()
    {
        BeginPlacement(PlacementType.FightingDrone);
    }

    public void BeginPlaceDecoyDrone()
    {
        BeginPlacement(PlacementType.DecoyDrone);
    }

    public void BeginPlaceShahedDrone()
    {
        BeginPlacement(PlacementType.ShahedDrone);
    }

    public void CancelPlacement()
    {
        _pendingType = PlacementType.None;
        _waitingForUiMouseRelease = false;

        if (selectionManager != null)
            selectionManager.InputBlocked = false;
    }

    void BeginPlacement(PlacementType type)
    {
        _pendingType = type;
        _waitingForUiMouseRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (selectionManager != null)
            selectionManager.InputBlocked = true;
    }

    void TryPlacePendingDrone()
    {
        if (!TryGetPlacementPoint(out Vector3 position))
            return;

        if (!IsWithinPlacementRange(position))
        {
            Debug.LogWarning($"Placement point is outside deployment range ({placementRange:0.#}).");
            return;
        }

        GameObject prefab = GetPendingPrefab();
        if (prefab == null)
        {
            Debug.LogWarning($"{_pendingType} prefab is not assigned.");
            return;
        }

        GameObject spawned = Instantiate(prefab, position, prefab.transform.rotation);
        RegisterSpawnedDrone(spawned);
        CancelPlacement();
    }

    bool TryGetPlacementPoint(out Vector3 position)
    {
        position = Vector3.zero;

        if (gameCamera == null || Mouse.current == null)
            return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = gameCamera.ScreenPointToRay(mousePos);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
            return false;

        position = hit.point + Vector3.up * spawnHeightAboveGround;
        return true;
    }

    bool IsWithinPlacementRange(Vector3 position)
    {
        Vector3 origin = placementOrigin != null ? placementOrigin.position : transform.position;
        Vector3 delta = position - origin;

        if (horizontalRangeOnly)
            delta.y = 0f;

        return delta.magnitude <= placementRange;
    }

    GameObject GetPendingPrefab()
    {
        switch (_pendingType)
        {
            case PlacementType.FightingDrone: return fightingDronePrefab;
            case PlacementType.DecoyDrone: return decoyDronePrefab;
            case PlacementType.ShahedDrone: return shahedDronePrefab;
            default: return null;
        }
    }

    void RegisterSpawnedDrone(GameObject spawned)
    {
        if (spawned == null || selectionManager == null) return;

        DroneUnit drone = spawned.GetComponentInChildren<DroneUnit>();
        if (drone != null)
        {
            selectionManager.RegisterDrone(drone);
            return;
        }

        DecoyDroneUnit decoy = spawned.GetComponentInChildren<DecoyDroneUnit>();
        if (decoy != null)
        {
            selectionManager.RegisterDecoy(decoy);
            return;
        }

        ShahedDroneUnit shahed = spawned.GetComponentInChildren<ShahedDroneUnit>();
        if (shahed != null)
            selectionManager.RegisterShahed(shahed);
    }

    bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = placementOrigin != null ? placementOrigin.position : transform.position;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(origin, placementRange);
    }
}
