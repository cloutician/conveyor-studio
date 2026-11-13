using System.Collections.Generic;
using UnityEngine;

public enum ConveyorKind { Generic, Short, Incline }

// Attach this component to the clean conveyor parent, alongside Visual, Entry and Exit.
[DisallowMultipleComponent]
[AddComponentMenu("Conveyor Studio/Conveyor Section")]
public sealed class ConveyorSection : MonoBehaviour
{
    private void Awake()
    {
        if (GetComponent<ConveyorCollision>() == null) gameObject.AddComponent<ConveyorCollision>();
    }
    [SerializeField] private ConveyorKind moduleType;
    // SerializeField exposes these private references in the Unity Inspector.
    [SerializeField] private Transform entry;
    [SerializeField] private Transform exit;

    [Tooltip("The upstream conveyor connected at Entry.")]
    [SerializeField] private ConveyorSection previousSection;

    [Tooltip("The downstream conveyor connected at Exit.")]
    [SerializeField] private ConveyorSection nextSection;
    private ConveyorBeltSurface beltSurface;

    // Other components can read the endpoints without replacing the references.
    public Transform Entry => entry;
    public ConveyorKind ModuleType => moduleType;
    public Transform Exit => exit;
    public ConveyorSection Previous => previousSection;
    public ConveyorSection Next => nextSection;
    public ConveyorBeltSurface BeltSurface
    {
        get
        {
            if (beltSurface == null) beltSurface = GetComponentInChildren<ConveyorBeltSurface>(true);
            return beltSurface;
        }
    }
    public bool HasEndpoints => entry != null && exit != null;
    public float Length => HasEndpoints
        ? Vector3.Distance(entry.position, exit.position)
        : 0f;

    // Calculate a placement pose without changing the conveyor or its connections.
    public bool TryGetSnapPose(ConveyorSection upstream, out Pose pose)
    {
        pose = new Pose(transform.position, transform.rotation);
        if (upstream == null || upstream == this || !HasEndpoints || !upstream.HasEndpoints)
        {
            return false;
        }

        // Connectors face outwards, so the two touching faces must point oppositely.
        Quaternion desiredEntryRotation = Quaternion.LookRotation(-upstream.Exit.forward, upstream.Exit.up);
        Quaternion rotationDelta = desiredEntryRotation * Quaternion.Inverse(entry.rotation);
        Quaternion rootRotation = rotationDelta * transform.rotation;
        Vector3 rootPosition = upstream.Exit.position
            - rotationDelta * (entry.position - transform.position);

        pose = new Pose(rootPosition, rootRotation);
        return true;
    }

    // Move this section after upstream, then record both sides of the connection.
    public bool TrySnapAfter(ConveyorSection upstream, out string reason)
    {
        if (!TryGetSnapPose(upstream, out Pose pose) || Length < 0.0001f || upstream.Length < 0.0001f)
        {
            reason = "Choose two different conveyors with valid Entry and Exit markers.";
            return false;
        }

        if (transform.IsChildOf(upstream.transform) || upstream.transform.IsChildOf(transform))
        {
            reason = "Connected conveyors must be separate objects in the hierarchy.";
            return false;
        }

        if (upstream.nextSection != null && upstream.nextSection != this)
        {
            reason = "That conveyor's Exit is already occupied.";
            return false;
        }

        if (previousSection != null && previousSection != upstream)
        {
            reason = "This conveyor's Entry is already connected.";
            return false;
        }

        HashSet<ConveyorSection> visited = new HashSet<ConveyorSection>();
        for (ConveyorSection cursor = upstream; cursor != null; cursor = cursor.previousSection)
        {
            if (cursor == this || !visited.Add(cursor))
            {
                reason = "This connection would create a circular conveyor chain.";
                return false;
            }
        }

        if (nextSection != null)
        {
            reason = "Disconnect this conveyor's output before moving it.";
            return false;
        }

        transform.SetPositionAndRotation(pose.position, pose.rotation);
        upstream.nextSection = this;
        previousSection = upstream;
        reason = string.Empty;
        return true;
    }

    // Runtime placement already positions the machines. Linking must not move a neighbour.
    public bool TryConnectTo(ConveyorSection downstream)
    {
        if (downstream == null || downstream == this || !HasEndpoints || !downstream.HasEndpoints
            || (nextSection != null && nextSection != downstream)
            || (downstream.previousSection != null && downstream.previousSection != this)
            || Vector3.Distance(exit.position, downstream.entry.position) > 0.001f
            || Vector3.Dot(exit.forward, -downstream.entry.forward) < 0.9999f) return false;
        var visited = new HashSet<ConveyorSection>();
        for (var cursor = downstream; cursor != null; cursor = cursor.nextSection)
            if (cursor == this || !visited.Add(cursor)) return false;
        nextSection = downstream;
        downstream.previousSection = this;
        return true;
    }

    // A product may transfer only while the stored connection is still aligned.
    public bool TryGetNext(out ConveyorSection section)
    {
        section = nextSection;
        return HasEndpoints && section != null && section.HasEndpoints
            && section.previousSection == this && section.Length >= 0.0001f
            && (exit.position - section.entry.position).sqrMagnitude <= 0.000001f
            && Vector3.Dot(exit.forward, -section.entry.forward) >= 0.9999f;
    }

    public void Disconnect()
    { DisconnectExcept(null); }

    public void DisconnectExcept(ISet<ConveyorSection> keep)
    {
        if (previousSection != null && (keep == null || !keep.Contains(previousSection)))
        {
            if (previousSection.nextSection == this) previousSection.nextSection = null;
            previousSection = null;
        }

        if (nextSection != null && (keep == null || !keep.Contains(nextSection)))
        {
            if (nextSection.previousSection == this) nextSection.previousSection = null;
            nextSection = null;
        }
    }

    // Unity calls Reset when this component is first added in the editor.
    // The names match the two direct children we created in the prefab.
    private void Reset()
    {
        entry = transform.Find("Entry");
        exit = transform.Find("Exit");
    }

    // Unity redraws these editing guides in the Scene view.
    private void OnDrawGizmos()
    {
        if (!HasEndpoints)
        {
            return;
        }

        // Lift only the guide slightly above the belt so it remains visible.
        Vector3 guideOffset = transform.up * 0.025f;
        Vector3 start = entry.position + guideOffset;
        Vector3 end = exit.position + guideOffset;

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(start, 0.045f);

        if (TryGetNext(out _))
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(end, 0.065f);
        }
        else
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(end, 0.045f);
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(start, end);

        Vector3 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        direction.Normalize();
        Vector3 sideways = Vector3.Cross(transform.up, direction).normalized;
        Vector3 arrowTip = Vector3.Lerp(start, end, 0.65f);
        Vector3 arrowBase = arrowTip - direction * 0.15f;

        Gizmos.DrawLine(arrowTip, arrowBase + sideways * 0.075f);
        Gizmos.DrawLine(arrowTip, arrowBase - sideways * 0.075f);
    }
}
