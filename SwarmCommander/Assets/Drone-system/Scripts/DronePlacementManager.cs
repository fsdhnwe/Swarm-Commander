using UnityEngine;

public class DronePlacementManager : MonoBehaviour
{
    public enum PlacementType { None, FightingDrone, ReconDrone, DecoyDrone, ShahedDrone }

    [Header("References")]
    public Camera gameCamera;
    public SelectionManager selectionManager;
    public LayerMask groundLayer;
    public Transform placementOrigin;
    public Transform fixedSpawnPoint;

    [Header("Prefabs")]
    public GameObject fightingDronePrefab;
    public GameObject reconDronePrefab;
    public GameObject decoyDronePrefab;
    public GameObject shahedDronePrefab;

    [Header("Fixed Spawn")]
    public float spawnY = 8f;

    [Header("Idle Search")]
    public Transform idleSearchCenter;
    public float idleSearchRadius = 20f;
    public float idleMinDistanceFromSpawn = 4f;
    public float idleClearanceRadius = 2f;
    public int idleSearchAttempts = 24;
    public LayerMask idleBlockedLayers;

    public bool IsPlacing => false;

    void Awake()
    {
        if (gameCamera == null) gameCamera = Camera.main;
        if (selectionManager == null) selectionManager = FindAnyObjectByType<SelectionManager>();
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

    public void BeginPlaceReconDrone()
    {
        BeginPlacement(PlacementType.ReconDrone);
    }

    public void BeginPlaceDecoyDrone()
    {
        BeginPlacement(PlacementType.DecoyDrone);
    }

    public void BeginPlaceShahedDrone()
    {
        BeginPlacement(PlacementType.ShahedDrone);
    }

    public void BeginPlaceDrone(PlacementType type)
    {
        BeginPlacement(type);
    }

    public bool CanPlaceDrone(PlacementType type)
    {
        return GetPrefab(type) != null;
    }

    public void CancelPlacement()
    {
        if (selectionManager != null)
            selectionManager.InputBlocked = false;
    }

    void BeginPlacement(PlacementType type)
    {
        SpawnDrone(type);
    }

    void SpawnDrone(PlacementType type)
    {
        GameObject prefab = GetPrefab(type);
        if (prefab == null)
        {
            Debug.LogWarning($"{type} prefab is not assigned.");
            return;
        }

        Vector3 spawnPosition = GetSpawnPosition();
        GameObject spawned = Instantiate(prefab, spawnPosition, prefab.transform.rotation);
        RegisterSpawnedDrone(spawned);
        MoveSpawnedDroneToIdle(spawned, spawnPosition);
    }

    Vector3 GetSpawnPosition()
    {
        Vector3 basePosition = fixedSpawnPoint != null ? fixedSpawnPoint.position : transform.position;
        basePosition.y = spawnY;
        return basePosition;
    }

    bool TryFindIdlePoint(Vector3 spawnPosition, out Vector3 idlePoint)
    {
        Vector3 center = idleSearchCenter != null
            ? idleSearchCenter.position
            : (placementOrigin != null ? placementOrigin.position : spawnPosition);

        for (int i = 0; i < idleSearchAttempts; i++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * idleSearchRadius;
            if (randomOffset.magnitude < idleMinDistanceFromSpawn)
                randomOffset = randomOffset.normalized * idleMinDistanceFromSpawn;

            Vector3 candidate = new Vector3(
                center.x + randomOffset.x,
                spawnY,
                center.z + randomOffset.y);

            if (Vector2.Distance(
                    new Vector2(candidate.x, candidate.z),
                    new Vector2(spawnPosition.x, spawnPosition.z)) < idleMinDistanceFromSpawn)
            {
                continue;
            }

            if (idleBlockedLayers.value != 0 &&
                Physics.CheckSphere(candidate, idleClearanceRadius, idleBlockedLayers))
            {
                continue;
            }

            idlePoint = candidate;
            return true;
        }

        idlePoint = spawnPosition;
        return false;
    }

    GameObject GetPrefab(PlacementType type)
    {
        switch (type)
        {
            case PlacementType.FightingDrone: return fightingDronePrefab;
            case PlacementType.ReconDrone: return reconDronePrefab;
            case PlacementType.DecoyDrone: return decoyDronePrefab;
            case PlacementType.ShahedDrone: return shahedDronePrefab;
            default: return null;
        }
    }

    void MoveSpawnedDroneToIdle(GameObject spawned, Vector3 spawnPosition)
    {
        if (spawned == null) return;

        if (!TryFindIdlePoint(spawnPosition, out Vector3 idlePoint))
            Debug.LogWarning($"{spawned.name}: no clear idle point found, staying at spawn point.");

        DroneUnit drone = spawned.GetComponentInChildren<DroneUnit>();
        if (drone != null)
        {
            drone.MoveTo(idlePoint);
            return;
        }

        ShahedDroneUnit shahed = spawned.GetComponentInChildren<ShahedDroneUnit>();
        if (shahed != null)
        {
            shahed.MoveTo(idlePoint);
            return;
        }

        ReconDroneUnit recon = spawned.GetComponentInChildren<ReconDroneUnit>();
        if (recon != null)
        {
            recon.MoveTo(idlePoint);
            return;
        }

        DecoyDroneUnit decoy = spawned.GetComponentInChildren<DecoyDroneUnit>();
        if (decoy != null)
            decoy.MoveTo(idlePoint);
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

        ReconDroneUnit recon = spawned.GetComponentInChildren<ReconDroneUnit>();
        if (recon != null)
        {
            selectionManager.RegisterRecon(recon);
            return;
        }

        ShahedDroneUnit shahed = spawned.GetComponentInChildren<ShahedDroneUnit>();
        if (shahed != null)
            selectionManager.RegisterShahed(shahed);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = idleSearchCenter != null
            ? idleSearchCenter.position
            : (placementOrigin != null ? placementOrigin.position : transform.position);

        Vector3 spawnPosition = fixedSpawnPoint != null ? fixedSpawnPoint.position : transform.position;
        spawnPosition.y = spawnY;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(spawnPosition, 0.75f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(origin, idleSearchRadius);
    }
}
