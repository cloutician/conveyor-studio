using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ConveyorLineController : MonoBehaviour
{
    public ConveyorPlacementController placement;
    [SerializeField] private bool running, emergency, synchronised;
    [SerializeField] private int synchronisedLevel = 4;
    [SerializeField] private PlacedConveyor selected;
    [SerializeField] private List<PlacedConveyor> registered = new List<PlacedConveyor>();
    public bool Running => running;
    public bool Emergency => emergency;
    public bool Synchronised => synchronised;
    public PlacedConveyor Selected => selected;
    public int MachineCount => registered.Count;
    public int Level => selected != null ? selected.SpeedLevel : synchronised ? synchronisedLevel : 4;
    public event Action Changed;
    public event Action<ConveyorLineFeedback> Feedback;
    private void OnEnable()
    {
        if (placement == null) placement = GetComponent<ConveyorPlacementController>();
        if (placement == null) return;
        if (synchronised && selected != null) synchronisedLevel = selected.SpeedLevel;
        placement.Changed -= RefreshMachines; placement.Changed += RefreshMachines;
        RefreshMachines();
    }
    private void OnDisable() { if (placement != null) placement.Changed -= RefreshMachines; }
    private void RefreshMachines()
    {
        int previousCount = registered.Count;
        registered.RemoveAll(machine => machine == null || !Contains(machine));
        foreach (var machine in placement.Machines)
        {
            if (machine == null) continue;
            if (!registered.Contains(machine))
            {
                registered.Add(machine);
                machine.ApplyLineState(running, synchronised ? synchronisedLevel : machine.SpeedLevel);
            }
        }
        selected = placement.Selected;
        if (MachineCount == 0) running = false;
        if (MachineCount < 2) synchronised = false;
        Changed?.Invoke();
        if (previousCount < 8 && MachineCount >= 8) Feedback?.Invoke(ConveyorLineFeedback.EightMachines);
    }
    private bool Contains(PlacedConveyor machine)
    { foreach (var other in placement.Machines) if (machine == other) return true; return false; }
    public void SetLevel(int level)
    {
        if (selected == null) return;
        level = Mathf.Clamp(level, 0, 9);
        if (synchronised) { synchronisedLevel = level; ApplyAll(level); }
        else foreach (var machine in placement.SelectedMachines) if (machine != null) machine.ApplyLineState(running, level);
        Changed?.Invoke();
    }
    public bool SetSynchronised(bool value)
    {
        if (value && MachineCount < 2)
        { Feedback?.Invoke(ConveyorLineFeedback.NeedTwoMachinesToSync); return false; }
        synchronised = value;
        if (value) { synchronisedLevel = Level; ApplyAll(synchronisedLevel); }
        Changed?.Invoke();
        return true;
    }
    public bool StartLine()
    {
        if (MachineCount == 0) { Feedback?.Invoke(ConveyorLineFeedback.AddMachinesToStart); return false; }
        running = true; emergency = false; ApplyAll(); Changed?.Invoke(); return true;
    }
    public void EmergencyStop() { running = false; emergency = true; ApplyAll(); Changed?.Invoke(); }
    private void ApplyAll(int level = -1)
    { foreach (var machine in registered) if (machine != null) machine.ApplyLineState(running, level < 0 ? machine.SpeedLevel : level); }
}

public enum ConveyorLineFeedback { AddMachinesToStart, NeedTwoMachinesToSync, EightMachines }
