using System.Collections.Generic;
using UnityEngine;

public sealed partial class ConveyorPlacementController
{
    private sealed class DetachedGroupPort { public PlacedConveyor moving, partner; public int end; public float peak; public bool released; }
    private readonly List<DetachedGroupPort> groupDetached = new List<DetachedGroupPort>();
    private readonly List<PlacedConveyor> groupReadyPartners = new List<PlacedConveyor>();
    private readonly List<Transform> groupReadyPorts = new List<Transform>();
    private static Pose GroupMemberPose(GroupPose member, Pose originalAnchor, Pose anchor)
    {
        Quaternion delta = anchor.rotation * Quaternion.Inverse(originalAnchor.rotation);
        return new Pose(anchor.position + delta * (member.position - originalAnchor.position), delta * member.rotation);
    }
    private static void ApplyGroupPose(List<GroupPose> members, Pose original, Pose anchor)
    {
        foreach (var member in members)
        {
            if (member.machine == null) continue;
            Pose pose = GroupMemberPose(member, original, anchor);
            member.machine.transform.SetPositionAndRotation(pose.position, pose.rotation); member.machine.RefreshSupports();
        }
    }
    private float GroupLowest(Pose pose)
    { float low = float.PositiveInfinity; foreach (var member in groupMove) low = Mathf.Min(low, GroupMemberPose(member, groupAnchor, pose).position.y); return low; }
    private bool PreviewBelowFloor(Pose pose) => IsGroupMove ? GroupLowest(pose) < -0.0001f : pose.position.y < -0.0001f;
    private bool ValidatePreviewPose(Pose pose, out string reason)
    {
        if (!IsGroupMove) return ValidatePlacement(preview, pose, out reason);
        foreach (var member in groupMove) if (!ValidatePlacement(member.machine, GroupMemberPose(member, groupAnchor, pose), out reason)) return false;
        reason = ""; return true;
    }
    private int PlacementConnectionCount(Pose pose)
    {
        int count = 0;
        if (!IsGroupMove) { MatchingPartners(preview, pose, out var a, out var b); return (a != null ? 1 : 0) + (b != null ? 1 : 0); }
        foreach (var member in groupMove)
        { MatchingPartners(member.machine, GroupMemberPose(member, groupAnchor, pose), out var a, out var b); count += (a != null ? 1 : 0) + (b != null ? 1 : 0); }
        return count;
    }
    private bool GroupCandidate(Pose raw, PlacedConveyor other, int end, float radius, out SnapCandidate candidate)
    {
        candidate = default;
        if (other == null || IsGroupMember(other) || other.IsReturning || other.IsEditing
            || (end == 0 ? other.Section.Next != null : other.Section.Previous != null)) return false;
        Transform target = end == 0 ? other.Section.Exit : other.Section.Entry;
        float best = radius;
        foreach (var member in groupMove)
        {
            var machine = member.machine;
            if ((end == 0 ? machine.Section.Previous : machine.Section.Next) != null) continue;
            bool detached = false;
            foreach (var port in groupDetached) if (!port.released && port.moving == machine && port.partner == other && port.end == end) { detached = true; break; }
            if (detached) continue;
            Pose pose = GroupMemberPose(member, groupAnchor, raw); Transform from = end == 0 ? machine.Section.Entry : machine.Section.Exit;
            if (Vector3.Dot(FacingAtPose(machine, from, pose), -target.forward) < 0.999f) continue;
            Vector3 point = AtPose(machine, from, pose);
            float distance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(target.position.x, target.position.z));
            if (distance > best) continue;
            best = distance; candidate = new SnapCandidate { partner = other, end = end, distance = distance,
                pose = new Pose(raw.position + (target.position - point), raw.rotation) };
        }
        return candidate.partner != null;
    }
    private void CaptureGroupDetach(ISet<ConveyorSection> keep)
    {
        foreach (var member in groupMove)
        {
            var section = member.machine.Section;
            if (section.Previous != null && !keep.Contains(section.Previous)) groupDetached.Add(new DetachedGroupPort { moving = member.machine, partner = section.Previous.GetComponent<PlacedConveyor>(), end = 0 });
            if (section.Next != null && !keep.Contains(section.Next)) groupDetached.Add(new DetachedGroupPort { moving = member.machine, partner = section.Next.GetComponent<PlacedConveyor>(), end = 1 });
        }
    }
    private void UpdateGroupDetach(Pose raw)
    {
        float release = Mathf.Max(snapCaptureDistance + 0.02f, snapReleaseDistance);
        foreach (var port in groupDetached)
        {
            if (port.released || port.partner == null) { port.released = true; continue; }
            foreach (var member in groupMove)
            {
                if (member.machine != port.moving) continue;
                Pose pose = GroupMemberPose(member, groupAnchor, raw);
                Transform from = port.end == 0 ? member.machine.Section.Entry : member.machine.Section.Exit;
                Transform target = port.end == 0 ? port.partner.Section.Exit : port.partner.Section.Entry;
                Vector3 point = AtPose(member.machine, from, pose);
                float distance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(target.position.x, target.position.z));
                port.peak = Mathf.Max(port.peak, distance);
                port.released |= distance > release || (port.peak > 0.02f && port.peak - distance > 0.01f)
                    || Vector3.Dot(FacingAtPose(member.machine, from, pose), -target.forward) < 0.999f;
            }
        }
    }
    private void GroupMatchingPartners(Pose anchor)
    {
        foreach (var member in groupMove)
        {
            MatchingPartners(member.machine, GroupMemberPose(member, groupAnchor, anchor), out var a, out var b);
            if (a != null) { entryPartner = a; if (!groupReadyPartners.Contains(a)) { groupReadyPartners.Add(a); groupReadyPorts.Add(a.Section.Exit); } }
            if (b != null) { exitPartner = b; if (!groupReadyPartners.Contains(b)) { groupReadyPartners.Add(b); groupReadyPorts.Add(b.Section.Entry); } }
        }
    }
    private void RefreshTransfers()
    { foreach (var machine in machines) if (machine != null) machine.GetComponent<ConveyorCollision>()?.RefreshTransfer(); }
}
