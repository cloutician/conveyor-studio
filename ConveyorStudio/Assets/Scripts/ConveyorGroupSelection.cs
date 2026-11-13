using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class ConveyorPlacementController
{
    [SerializeField, HideInInspector] private List<PlacedConveyor> selection = new List<PlacedConveyor>();
    [System.Serializable] private struct GroupPose
    { public PlacedConveyor machine; public Vector3 position; public Quaternion rotation; }
    [SerializeField, HideInInspector] private List<GroupPose> groupMove = new List<GroupPose>();
    private Pose groupAnchor;
    private bool additivePress;
    public IReadOnlyList<PlacedConveyor> SelectedMachines => selection;
    public bool IsGroupMove => groupMove.Count > 1;
    public bool IsSelected(PlacedConveyor machine) => machine != null && selection.Contains(machine);
    public string SelectionIds
    {
        get
        {
            var ids = new List<string>();
            foreach (var machine in machines) if (IsSelected(machine)) ids.Add(machine.UnitId);
            return string.Join(", ", ids);
        }
    }
    private void RestoreSelection()
    {
        selection.RemoveAll(machine => machine == null || !machines.Contains(machine));
        if (selection.Count == 0 && Selected != null && machines.Contains(Selected)) selection.Add(Selected);
        if (Selected == null || !selection.Contains(Selected)) Selected = selection.Count > 0 ? selection[selection.Count - 1] : null;
    }
    private void SelectionChanged()
    {
        status = selection.Count > 1 ? selection.Count + " machines selected · drag any selected machine to move the group · Delete removes all"
            : Selected != null ? Selected.DisplayName + " selected · drag to move · Ctrl-click to add machines"
            : "Choose a module or select a machine.";
        UpdateGuides(); Changed?.Invoke(); ClearUiFocus();
    }
    public void MakePrimary(PlacedConveyor machine)
    {
        if (!IsSelected(machine)) { Select(machine); return; }
        Selected = machine; SelectionChanged();
    }
    public void CompleteSelectionGesture(PlacedConveyor machine, bool additive, bool moved)
    {
        if (!additive && !moved && selection.Count > 1 && IsSelected(machine)) Select(machine);
    }
    public void FocusSelection(float reservedRightPixels = 0f)
    {
        if (selection.Count > 0) orbitCamera.FocusMachines(selection, reservedRightPixels);
    }
    private bool IsGroupMember(PlacedConveyor machine)
    { foreach (var member in groupMove) if (member.machine == machine) return true; return false; }
    private bool BeginGroupMove(PlacedConveyor anchor)
    {
        foreach (var machine in selection) if (machine == null || machine.IsReturning || machine.IsSettling || machine.IsEditing) return false;
        CancelPreview();
        groupMove.Clear(); groupAnchor = new Pose(anchor.transform.position, anchor.transform.rotation);
        foreach (var machine in selection)
        {
            var pose = new GroupPose { machine = machine, position = machine.transform.position, rotation = machine.transform.rotation };
            groupMove.Add(pose); machine.LastValidPose = new Pose(pose.position, pose.rotation);
        }
        preview = anchor; Selected = anchor; adding = false; createdFrame = Time.frameCount; ResetMagnet();
        var keep = new HashSet<ConveyorSection>(); foreach (var member in groupMove) keep.Add(member.machine.Section);
        CaptureGroupDetach(keep);
        foreach (var member in groupMove) { member.machine.Section.DisconnectExcept(keep); member.machine.BeginEditing(); }
        MoveGroupPreview(groupAnchor, false, true); Changed?.Invoke(); return true;
    }
    private void MoveGroupPreview(Pose requested, bool allowSnap, bool immediate = false, float deltaTime = -1f)
    {
        float dt = Mathf.Clamp(deltaTime < 0f ? Time.unscaledDeltaTime : deltaTime, 0.0001f, 0.05f);
        Quaternion rotation = Quaternion.Euler(0f, Mathf.Round(requested.rotation.eulerAngles.y / 90f) * 90f, 0f);
        float lowest = GroupLowest(new Pose(requested.position, rotation));
        bool belowFloor = lowest < -0.0001f;
        Vector3 anchor = requested.position + Vector3.up * Mathf.Max(0f, -lowest);
        requestedPose = new Pose(anchor, rotation);
        float speed = hasRawPosition ? Vector3.Distance(anchor, lastRawPosition) / dt : 0f;
        lastRawPosition = anchor; hasRawPosition = true; immediatePreview = immediate;
        bool previouslyMagnetic = wasMagnetic;
        UpdateGroupDetach(requestedPose);
        if (allowSnap && !belowFloor) FindSnap(requestedPose, speed, dt, immediate, ref belowFloor);
        else { snapPartner = guidePartner = intentPartner = null; snapEnd = intentEnd = -1; intentTime = 0f; }
        Vector3 position;
        if (snapPartner != null && !belowFloor)
        {
            position = immediate ? snapPose.position : Vector3.SmoothDamp(preview.transform.position, snapPose.position, ref magneticVelocity, snapSmoothTime, Mathf.Infinity, dt);
            if (Vector3.Distance(position, snapPose.position) < 0.00005f) { position = snapPose.position; magneticVelocity = Vector3.zero; }
            releaseOffset = position - anchor;
        }
        else
        {
            if (previouslyMagnetic) { releaseOffset = preview.transform.position - anchor; releaseVelocity = Vector3.zero; }
            if (immediate || !allowSnap) releaseOffset = releaseVelocity = Vector3.zero;
            else releaseOffset = Vector3.SmoothDamp(releaseOffset, Vector3.zero, ref releaseVelocity, 0.065f, Mathf.Infinity, dt);
            position = anchor + releaseOffset; magneticVelocity = Vector3.zero;
        }
        wasMagnetic = snapPartner != null;
        var visual = new Pose(position, rotation); visual.position.y += Mathf.Max(0f, -GroupLowest(visual));
        ApplyGroupPose(groupMove, groupAnchor, visual); RefreshTransfers();
        valid = !belowFloor && ValidatePreviewPose(snapPartner != null ? snapPose : requestedPose, out status);
        if (belowFloor) status = "Keep the whole group above the floor.";
        else if (valid) status = snapPartner != null ? "Group connection aligned · release to join" : guidePartner != null
            ? "Group connection nearby · bring the ends closer" : selection.Count + " machines · release to place the group";
        UpdateGuides();
    }
    private bool CommitGroupMove()
    {
        if (snapPartner == null && guidePartner != null && Candidate(requestedPose, guidePartner, guideEnd, snapCaptureDistance * 0.55f, out var drop)
            && !PreviewBelowFloor(drop.pose) && ValidatePreviewPose(drop.pose, out _)) { snapPartner = drop.partner; snapPose = drop.pose; }
        Pose destination = snapPartner != null ? snapPose : requestedPose;
        if (!ValidatePreviewPose(destination, out status)) return false;
        var members = new List<GroupPose>(groupMove); Pose originalAnchor = groupAnchor;
        Pose from = new Pose(preview.transform.position, preview.transform.rotation);
        bool settle = !immediatePreview && Vector3.Distance(from.position, destination.position) > 0.00005f;
        foreach (var member in members)
        {
            Pose pose = GroupMemberPose(member, originalAnchor, destination); member.machine.LastValidPose = pose;
            if (settle) member.machine.IsSettling = true;
        }
        Vector3 velocity = snapPartner != null ? magneticVelocity : releaseVelocity;
        groupMove.Clear(); preview = null; adding = dragging = followPointer = false; ResetMagnet();
        if (settle) StartCoroutine(SettleGroup(members, originalAnchor, from, destination, velocity));
        else { ApplyGroupPose(members, originalAnchor, destination); foreach (var member in members) member.machine.FinishEditing(); Reconnect(); }
        SelectionChanged(); return true;
    }
    private IEnumerator SettleGroup(List<GroupPose> members, Pose original, Pose from, Pose destination, Vector3 velocity)
    {
        float elapsed = 0f;
        while (elapsed < 0.36f && Vector3.Distance(from.position, destination.position) > 0.00005f)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f); elapsed += dt;
            from.position = Vector3.SmoothDamp(from.position, destination.position, ref velocity, 0.065f, Mathf.Infinity, dt);
            ApplyGroupPose(members, original, from); RefreshTransfers(); yield return null;
        }
        ApplyGroupPose(members, original, destination);
        foreach (var member in members) if (member.machine != null) member.machine.FinishEditing();
        Reconnect(); SelectionChanged();
    }
    private void CancelGroupMove()
    {
        Pose fromAnchor = new Pose(preview.transform.position, preview.transform.rotation), destinationAnchor = groupAnchor;
        var destination = new List<GroupPose>(groupMove); groupMove.Clear();
        preview = null; adding = dragging = followPointer = false; ResetMagnet();
        foreach (var member in destination) { member.machine.IsReturning = true; member.machine.ShowOutline(true, new Color(1f, 0.28f, 0.27f)); }
        StartCoroutine(ReturnGroup(destination, fromAnchor, destinationAnchor));
        status = "Returning the group to its last valid position."; UpdateGuides(); Changed?.Invoke();
    }
    private IEnumerator ReturnGroup(List<GroupPose> destination, Pose fromAnchor, Pose destinationAnchor)
    {
        float elapsed = 0f;
        while (elapsed < 0.26f)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.26f));
            Vector3 anchorPosition = Vector3.Lerp(fromAnchor.position, destinationAnchor.position, t);
            Quaternion delta = Quaternion.Slerp(fromAnchor.rotation, destinationAnchor.rotation, t) * Quaternion.Inverse(destinationAnchor.rotation);
            foreach (var member in destination)
            {
                if (member.machine == null) continue;
                member.machine.transform.SetPositionAndRotation(anchorPosition + delta * (member.position - destinationAnchor.position), delta * member.rotation);
                member.machine.RefreshSupports();
            }
            RefreshTransfers();
            yield return null;
        }
        foreach (var member in destination)
        {
            if (member.machine == null) continue;
            member.machine.transform.SetPositionAndRotation(member.position, member.rotation); member.machine.RefreshSupports(); member.machine.FinishEditing();
        }
        Reconnect(); SelectionChanged();
    }
}
