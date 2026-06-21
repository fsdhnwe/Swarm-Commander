using System;
using System.Collections.Generic;
using UnityEngine;

public partial class GameManager : MonoBehaviour
{
    [Header("Squad Selection")]
    public readonly List<ISelectableDrone> selectedDrones = new();

    [Header("Squad Camera")]
    public bool focusCameraOnSquad = true;
    public float squadFocusDoubleTapTime = 0.35f;
    public Vector3 squadCameraFocusOffset = Vector3.zero;

    public event Action OnSelectionChanged;
    public event Action OnSquadsChanged;

    private readonly List<ISelectableDrone>[] squads =
    {
        new(),
        new(),
        new(),
        new()
    };

    private SelectionManager selectionManager;
    private TopDownCameraController cameraController;
    private int lastSelectedSquadIndex = -1;
    private float lastSquadSelectTime = -999f;

    private void Start()
    {
        selectionManager = FindAnyObjectByType<SelectionManager>();
        cameraController = FindAnyObjectByType<TopDownCameraController>();
    }

    private void Update()
    {
        HandleSquadHotkeys();
    }

    private void HandleSquadHotkeys()
    {
        bool ShiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        for (int i = 0; i < squads.Length; i++)
        {
            KeyCode key = KeyCode.Alpha1 + i;
            if (!Input.GetKeyDown(key)) continue;

            if (ShiftHeld)
            {
                AssignSquad(i);
            }
            else
            {
                bool shouldFocus = lastSelectedSquadIndex == i &&
                    Time.unscaledTime - lastSquadSelectTime <= squadFocusDoubleTapTime;

                SelectSquad(i, shouldFocus);

                lastSelectedSquadIndex = i;
                lastSquadSelectTime = Time.unscaledTime;
            }
        }
    }

    public void AssignSquad(int squadIndex)
    {
        if (!IsValidSquadIndex(squadIndex)) return;

        RemoveDestroyedReferences(selectedDrones);
        if (selectedDrones.Count == 0)
        {
            Debug.LogWarning($"Squad {squadIndex + 1} was not assigned because no drones are selected.");
            return;
        }

        squads[squadIndex].Clear();
        squads[squadIndex].AddRange(selectedDrones);

        Debug.Log($"Squad {squadIndex + 1} assigned: {squads[squadIndex].Count} drones.");
        OnSquadsChanged?.Invoke();
    }

    public void SelectSquad(int squadIndex)
    {
        SelectSquad(squadIndex, false);
    }

    public void SelectSquad(int squadIndex, bool focusCamera)
    {
        if (!IsValidSquadIndex(squadIndex)) return;

        List<ISelectableDrone> squad = squads[squadIndex];
        RemoveDestroyedReferences(squad);
        if (squad.Count == 0) return;

        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (selectionManager != null)
            selectionManager.SelectDronesFromSquad(squad);
        else
            ReplaceSelection(squad);

        if (focusCamera)
            FocusCameraOnSquad(squad);
    }

    public int GetSquadCount(int squadIndex)
    {
        if (!IsValidSquadIndex(squadIndex)) return 0;

        RemoveDestroyedReferences(squads[squadIndex]);
        return squads[squadIndex].Count;
    }

    public bool HasSquad(int squadIndex)
    {
        return GetSquadCount(squadIndex) > 0;
    }

    public string GetSquadLabel(ISelectableDrone drone)
    {
        if (drone == null) return "-";

        List<int> squadNumbers = new();
        for (int i = 0; i < squads.Length; i++)
        {
            RemoveDestroyedReferences(squads[i]);
            if (squads[i].Contains(drone))
                squadNumbers.Add(i + 1);
        }

        if (squadNumbers.Count == 0)
            return "No Squad";

        if (squadNumbers.Count == 1)
            return $"Squad {squadNumbers[0]}";

        return $"Squads {string.Join(", ", squadNumbers)}";
    }

    public void SelectDrone(ISelectableDrone drone, bool addToSelection = false)
    {
        if (drone == null) return;

        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (selectionManager != null)
            selectionManager.SelectDroneFromUI(drone, addToSelection);
        else if (addToSelection)
            AddSelectedDrone(drone);
        else
            ReplaceSelection(new[] { drone });
    }

    public void DeselectDrone(ISelectableDrone drone)
    {
        if (drone == null) return;

        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (selectionManager != null)
            selectionManager.DeselectDroneFromUI(drone);
        else
            RemoveSelectedDrone(drone);
    }

    public void ClearSelection()
    {
        if (selectionManager == null)
            selectionManager = FindAnyObjectByType<SelectionManager>();

        if (selectionManager != null)
            selectionManager.ClearDroneSelectionFromUI();
        else
            ReplaceSelection(Array.Empty<ISelectableDrone>());
    }

    public void ReplaceSelection(IEnumerable<ISelectableDrone> drones)
    {
        foreach (ISelectableDrone selected in selectedDrones)
        {
            if (IsAlive(selected))
                selected.SetSelected(false);
        }

        selectedDrones.Clear();

        foreach (ISelectableDrone drone in drones)
        {
            if (!IsAlive(drone) || selectedDrones.Contains(drone)) continue;

            drone.SetSelected(true);
            selectedDrones.Add(drone);
        }

        OnSelectionChanged?.Invoke();
    }

    public void AddSelectedDrone(ISelectableDrone drone)
    {
        if (!IsAlive(drone) || selectedDrones.Contains(drone)) return;

        drone.SetSelected(true);
        selectedDrones.Add(drone);
        OnSelectionChanged?.Invoke();
    }

    public void RemoveSelectedDrone(ISelectableDrone drone)
    {
        if (drone == null) return;

        if (IsAlive(drone))
            drone.SetSelected(false);

        selectedDrones.Remove(drone);
        OnSelectionChanged?.Invoke();
    }

    public void NotifySelectionChanged()
    {
        RemoveDestroyedReferences(selectedDrones);
        OnSelectionChanged?.Invoke();
    }

    public void RemoveDroneFromAllGroups(ISelectableDrone drone)
    {
        if (drone == null) return;

        selectedDrones.Remove(drone);
        foreach (List<ISelectableDrone> squad in squads)
            squad.Remove(drone);

        OnSelectionChanged?.Invoke();
        OnSquadsChanged?.Invoke();
    }

    private static void RemoveDestroyedReferences(List<ISelectableDrone> drones)
    {
        drones.RemoveAll(d => !IsAlive(d));
    }

    private static bool IsAlive(ISelectableDrone drone)
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

    private bool IsValidSquadIndex(int squadIndex)
    {
        return squadIndex >= 0 && squadIndex < squads.Length;
    }

    private void FocusCameraOnSquad(List<ISelectableDrone> squad)
    {
        if (!focusCameraOnSquad || squad == null || squad.Count == 0) return;

        Vector3 center = Vector3.zero;
        int count = 0;

        foreach (ISelectableDrone drone in squad)
        {
            if (!IsAlive(drone)) continue;

            center += drone.GameObject.transform.position;
            count++;
        }

        if (count == 0) return;

        center = center / count + squadCameraFocusOffset;

        if (cameraController == null)
            cameraController = FindAnyObjectByType<TopDownCameraController>();

        if (cameraController != null)
        {
            cameraController.FocusOnWorldPosition(center);
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return;

        Vector3 cameraPosition = mainCamera.transform.position;
        cameraPosition.x = center.x;
        cameraPosition.z = center.z;
        mainCamera.transform.position = cameraPosition;
    }
}
