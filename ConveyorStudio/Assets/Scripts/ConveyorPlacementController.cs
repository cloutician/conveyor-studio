using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed partial class ConveyorPlacementController : MonoBehaviour
{
    public ConveyorCatalog catalog;
    public Camera sceneCamera;
    public ConveyorOrbitCamera orbitCamera;
    public ConveyorControlBox controlBox;
    public Transform machinesRoot;
    [Header("Magnetic placement")]
    [Min(0.02f)] public float snapCaptureDistance = 0.21f;
    [Min(0.02f)] public float snapReleaseDistance = 0.26f;
    [Min(0.02f)] public float snapGuideDistance = 0.31f;
    [Range(0.05f, 0.25f)] public float snapSmoothTime = 0.075f;
    [SerializeField, HideInInspector] private List<PlacedConveyor> machines = new List<PlacedConveyor>();
    private PlacedConveyor preview, snapPartner, pressedMachine;
    private bool adding, valid, dragging, followPointer;
    private Vector2 pressScreen;
    private PlacedConveyor lastTapped;
    private Vector2 lastTapScreen;
    private float lastTapTime = -10f, pressTime;
    private Vector3 dragOffset;
    private int nextId, createdFrame;
    private LineRenderer snapGuide, secondSnapGuide;
    private PlacedConveyor entryPartner, exitPartner;
    private Pose requestedPose, snapPose;
    private Vector3 magneticVelocity, releaseOffset, releaseVelocity, lastRawPosition;
    private bool hasRawPosition, immediatePreview, wasMagnetic;
    private PlacedConveyor intentPartner, guidePartner, detachEntry, detachExit;
    private int snapEnd = -1, intentEnd = -1, guideEnd = -1;
    private float intentTime, guideDistance, guideOpacity, secondGuideOpacity, detachEntryPeak, detachExitPeak;
    private Transform guidePort, secondGuidePort;
    private const float IntentDelay = 0.025f, MaximumCaptureSpeed = 2.5f;
    private struct SnapCandidate
    {
        public PlacedConveyor partner;
        public int end;
        public Pose pose;
        public float distance;
    }
    private string status = "Choose a module from the library to start your line.";
    public IReadOnlyList<PlacedConveyor> Machines => machines;
    public PlacedConveyor Selected { get; private set; }
    public PlacedConveyor PreviewMachine => preview;
    public PlacedConveyor SnapPartner => snapPartner;
    public PlacedConveyor EntrySnapPartner => entryPartner;
    public PlacedConveyor ExitSnapPartner => exitPartner;
    public bool SnapAligned => preview != null && snapPartner != null && Vector3.Distance(preview.transform.position, snapPose.position) < 0.002f;
    public bool PreviewIsValid => preview != null && valid;
    public bool IsAdding => preview != null && adding;
    public string Status => status;
    public string LastCollision { get; private set; }
    public event Action Changed;
    public bool LibraryOpen { get; set; } = true;
    public Rect WorldScreenRect { get; set; }
    private static readonly Color Green = new Color(0.26f, 1f, 0.63f);
    private static readonly Color Blue = new Color(0.23f, 0.77f, 1f);

    private void Awake()
    {
        ConveyorPhysicsWorld.Ensure(gameObject.scene);
        if (machinesRoot == null) { var go = new GameObject("Placed machines"); go.transform.SetParent(transform, false); machinesRoot = go.transform; }
        EnsureSnapGuides();
    }
    private void OnEnable()
    {
        if (!Application.isPlaying || catalog == null) return;
        EnsureSnapGuides();
        if (machinesRoot != null && machines.Count == 0)
            foreach (var machine in machinesRoot.GetComponentsInChildren<PlacedConveyor>(true))
            {
                if (string.IsNullOrEmpty(machine.UnitId)) continue;
                machines.Add(machine);
                int label = 0;
                foreach (char letter in machine.UnitId) label = label * 26 + letter - 'A' + 1;
                nextId = Mathf.Max(nextId, label);
            }
        RestoreSelection();
        Changed?.Invoke();
    }
    private void EnsureSnapGuides()
    {
        if (snapGuide == null) snapGuide = CreateSnapGuide("Magnetic connection guide");
        if (secondSnapGuide == null) secondSnapGuide = CreateSnapGuide("Second magnetic connection guide");
    }
    private LineRenderer CreateSnapGuide(string title)
    {
        var existing = transform.Find(title);
        if (existing != null && existing.TryGetComponent<LineRenderer>(out var line)) return line;
        var guide = new GameObject(title);
        guide.transform.SetParent(transform, false);
        line = guide.AddComponent<LineRenderer>();
        line.sharedMaterial = catalog.lineMaterial;
        line.widthMultiplier = 0.015f;
        line.startColor = line.endColor = Green;
        line.positionCount = 33;
        line.enabled = false;
        return line;
    }

    public PlacedConveyor BeginAdd(ConveyorKind kind)
    {
        CancelPreview();
        var module = catalog.Get(kind);
        GameObject instance = Instantiate(module.prefab, machinesRoot);
        preview = instance.AddComponent<PlacedConveyor>();
        preview.Initialize(module);
        preview.BeginEditing();
        ResetMagnet();
        adding = true; followPointer = true; dragging = false;
        dragOffset = Vector3.zero;
        createdFrame = Time.frameCount;
        selection.Clear(); Selected = null;
        MovePreview(new Pose(Vector3.zero, Quaternion.identity), true);
        Changed?.Invoke();
        ClearUiFocus();
        return preview;
    }
    public PlacedConveyor BeginAddShortcut(int number)
    { return number == 1 ? BeginAdd(ConveyorKind.Generic) : number == 2 ? BeginAdd(ConveyorKind.Short) : number == 3 ? BeginAdd(ConveyorKind.Incline) : null; }

    public bool BeginMove(PlacedConveyor machine)
    {
        if (machine == null || machine.IsReturning || machine.IsSettling || !machines.Contains(machine)) return false;
        if (selection.Count > 1 && IsSelected(machine)) return BeginGroupMove(machine);
        CancelPreview();
        preview = machine; selection.Clear(); selection.Add(machine); Selected = machine;
        adding = false; createdFrame = Time.frameCount;
        machine.LastValidPose = new Pose(machine.transform.position, machine.transform.rotation);
        ResetMagnet();
        // A connected unit must peel away freely, without being pulled straight back
        // onto the connection the user has just deliberately picked up.
        foreach (var other in machines)
        {
            if (other.Section == machine.Section.Previous) detachEntry = other;
            if (other.Section == machine.Section.Next) detachExit = other;
        }
        machine.Section.Disconnect();
        machine.BeginEditing();
        MovePreview(machine.LastValidPose, false);
        Changed?.Invoke();
        return true;
    }

    // Immediate placement is for known-pose scene setup. Pointer input always uses
    // the animated path; deltaTime also allows deterministic motion verification.
    public void MovePreview(Pose requested, bool allowSnap, bool immediate = false, float deltaTime = -1f)
    {
        if (preview == null) return;
        if (IsGroupMove) { MoveGroupPreview(requested, allowSnap, immediate, deltaTime); return; }
        float dt = Mathf.Clamp(deltaTime < 0f ? Time.unscaledDeltaTime : deltaTime, 0.0001f, 0.05f);
        float yaw = Mathf.Round(requested.rotation.eulerAngles.y / 90f) * 90f;
        Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
        bool belowFloor = requested.position.y < -0.0001f;
        Vector3 raw = requested.position; raw.y = Mathf.Max(0f, raw.y);
        requestedPose = new Pose(raw, rotation);
        float speed = hasRawPosition ? Vector3.Distance(raw, lastRawPosition) / dt : 0f;
        lastRawPosition = raw; hasRawPosition = true; immediatePreview = immediate;
        bool previouslyMagnetic = wasMagnetic;
        if (allowSnap && !belowFloor) FindSnap(requestedPose, speed, dt, immediate, ref belowFloor);
        else { snapPartner = guidePartner = intentPartner = null; snapEnd = intentEnd = -1; intentTime = 0; }
        Vector3 position;
        if (snapPartner != null && !belowFloor)
        {
            position = immediate ? snapPose.position : Vector3.SmoothDamp(preview.transform.position, snapPose.position,
                ref magneticVelocity, snapSmoothTime, Mathf.Infinity, dt);
            if (Vector3.Distance(position, snapPose.position) < 0.00005f) { position = snapPose.position; magneticVelocity = Vector3.zero; }
            releaseOffset = position - raw;
        }
        else
        {
            if (previouslyMagnetic) { releaseOffset = preview.transform.position - raw; releaseVelocity = Vector3.zero; }
            if (immediate || !allowSnap) releaseOffset = releaseVelocity = Vector3.zero;
            else releaseOffset = Vector3.SmoothDamp(releaseOffset, Vector3.zero, ref releaseVelocity, 0.065f, Mathf.Infinity, dt);
            position = raw + releaseOffset;
            magneticVelocity = Vector3.zero;
        }
        wasMagnetic = snapPartner != null;
        // Both rejected connectors and the animated path stay above the floor.
        position.y = Mathf.Max(0f, position.y);
        preview.transform.SetPositionAndRotation(position, rotation);
        preview.RefreshSupports();
        Pose placementPose = snapPartner != null ? snapPose : requestedPose;
        valid = !belowFloor && ValidatePlacement(preview, placementPose, out status);
        if (belowFloor) status = "This connection would put a machine below the floor.";
        else if (valid)
        {
            string action = adding ? "click to place" : "release to place";
            status = snapPartner != null ? (SnapAligned ? "Connection aligned · " : "Aligning gently · ") + action
                : guidePartner != null ? "Connection nearby · bring the ends closer" : "Clear placement · " + action;
        }
        UpdateGuides();
        if (valid && entryPartner != null && exitPartner != null)
            status = (SnapAligned ? "Both ends aligned · " : "Both connections ready · ") + (adding ? "click to place" : "release to reconnect");
    }

    private void ResetMagnet()
    {
        groupDetached.Clear();
        snapPartner = intentPartner = guidePartner = detachEntry = detachExit = null;
        entryPartner = exitPartner = null;
        snapEnd = intentEnd = guideEnd = -1; intentTime = 0;
        magneticVelocity = releaseOffset = releaseVelocity = Vector3.zero;
        hasRawPosition = wasMagnetic = immediatePreview = false;
        detachEntryPeak = detachExitPeak = 0f;
    }

    private bool Candidate(Pose raw, PlacedConveyor other, int end, float radius, out SnapCandidate candidate)
    {
        if (IsGroupMove) return GroupCandidate(raw, other, end, radius, out candidate);
        candidate = default;
        if (other == null || other == preview || other.IsReturning || other.IsEditing) return false;
        if (end == 0 ? other.Section.Next != null : other.Section.Previous != null) return false;
        Transform moving = end == 0 ? preview.Section.Entry : preview.Section.Exit;
        Transform target = end == 0 ? other.Section.Exit : other.Section.Entry;
        if (Vector3.Dot(FacingAtPose(preview, moving, raw), -target.forward) < 0.999f) return false;
        Vector3 from = AtPose(preview, moving, raw);
        float distance = Vector2.Distance(new Vector2(from.x, from.z), new Vector2(target.position.x, target.position.z));
        if (distance > radius) return false;
        candidate = new SnapCandidate { partner = other, end = end, distance = distance,
            pose = new Pose(target.position - raw.rotation * LocalPortPosition(preview, moving), raw.rotation) };
        return true;
    }

    private void FindSnap(Pose raw, float pointerSpeed, float dt, bool immediate, ref bool belowFloor)
    {
        float capture = snapCaptureDistance, release = Mathf.Max(capture + 0.02f, snapReleaseDistance);
        float guideRange = Mathf.Max(release, snapGuideDistance);
        detachEntry = UpdateDetachedPort(raw, detachEntry, 0, release, ref detachEntryPeak);
        detachExit = UpdateDetachedPort(raw, detachExit, 1, release, ref detachExitPeak);
        // Once acquired, keep this exact connector until the pointer leaves the wider
        // release area. A nearby competing port cannot steal it mid-gesture.
        if (snapPartner != null && Candidate(raw, snapPartner, snapEnd, release, out var held)
            && !PreviewBelowFloor(held.pose) && ValidatePreviewPose(held.pose, out _))
        {
            snapPose = held.pose; guidePartner = held.partner; guideEnd = held.end; guideDistance = held.distance;
            return;
        }
        snapPartner = null; snapEnd = -1; guidePartner = null;
        float best = guideRange, bestSnap = capture;
        int bestConnections = -1;
        SnapCandidate nearest = default, ready = default;
        foreach (var other in machines) for (int end = 0; end < 2; end++)
        {
            if (end == 0 ? other == detachEntry : other == detachExit) continue;
            if (!Candidate(raw, other, end, guideRange, out var candidate)) continue;
            if (nearest.partner == null || candidate.distance < best - 0.00001f)
            { best = candidate.distance; nearest = candidate; }
            if (candidate.distance > capture || PreviewBelowFloor(candidate.pose)
                || !ValidatePreviewPose(candidate.pose, out _)) continue;
            int connections = PlacementConnectionCount(candidate.pose);
            // Prefer a pose that fits the complete gap; keep ties stable while hovering.
            if (connections > bestConnections || (connections == bestConnections && candidate.distance < bestSnap - 0.00001f))
            { ready = candidate; bestSnap = candidate.distance; bestConnections = connections; }
        }
        if (ready.partner != null) nearest = ready;
        if (nearest.partner != null)
        {
            guidePartner = nearest.partner; guideEnd = nearest.end; guideDistance = nearest.distance;
            if (nearest.distance <= capture)
            {
                belowFloor = PreviewBelowFloor(nearest.pose);
                if (!belowFloor && ValidatePreviewPose(nearest.pose, out _) && (immediate || pointerSpeed <= MaximumCaptureSpeed))
                {
                    if (intentPartner != nearest.partner || intentEnd != nearest.end) intentTime = 0;
                    intentPartner = nearest.partner; intentEnd = nearest.end; intentTime += dt;
                    if (immediate || intentTime >= IntentDelay)
                    { snapPartner = nearest.partner; snapEnd = nearest.end; snapPose = nearest.pose; }
                    return;
                }
            }
        }
        intentPartner = null; intentEnd = -1; intentTime = 0;
    }

    private PlacedConveyor UpdateDetachedPort(Pose raw, PlacedConveyor partner, int end, float release, ref float peak)
    {
        if (partner == null || !Candidate(raw, partner, end, release, out var candidate)) return null;
        peak = Mathf.Max(peak, candidate.distance);
        // Moving back towards the former join is deliberate intent to reconnect.
        // Requiring 10 mm of reversal filters small hand jitter without forcing
        // the user to leave the whole magnetic field before trying that join again.
        return peak > 0.02f && peak - candidate.distance > 0.01f ? null : partner;
    }

    private void MatchingPartners(PlacedConveyor moving, Pose pose, out PlacedConveyor upstream, out PlacedConveyor downstream)
    {
        upstream = downstream = null;
        foreach (var other in machines)
        {
            if (other == moving || other.IsEditing || other.IsReturning) continue;
            Pose otherPose = new Pose(other.transform.position, other.transform.rotation);
            if ((other.Section.Next == null || other.Section.Next == moving.Section)
                && PortsMatch(moving, moving.Section.Entry, pose, other, other.Section.Exit, otherPose)) upstream = other;
            if ((other.Section.Previous == null || other.Section.Previous == moving.Section)
                && PortsMatch(moving, moving.Section.Exit, pose, other, other.Section.Entry, otherPose)) downstream = other;
        }
    }
    private static bool PortsMatch(PlacedConveyor a, Transform from, Pose ap, PlacedConveyor b, Transform to, Pose bp)
        => Vector3.Distance(AtPose(a, from, ap), AtPose(b, to, bp)) < 0.001f
            && Vector3.Dot(FacingAtPose(a, from, ap), -FacingAtPose(b, to, bp)) > 0.9999f;

    public bool ValidatePlacement(PlacedConveyor moving, Pose pose, out string reason)
    {
        if (pose.position.y < -0.0001f) { reason = "Keep the machine above the floor."; return false; }
        var aBoxes = moving.Boxes(pose);
        foreach (var other in machines)
        {
            if (other == moving || IsGroupMember(other)) continue;
            Pose otherPose = other.IsReturning || other.IsSettling ? other.LastValidPose : new Pose(other.transform.position, other.transform.rotation);
            bool joined = AlignedPort(moving, pose, other, otherPose, out Vector3 port, out Vector3 direction);
            foreach (var a in aBoxes)
            foreach (var b in other.Boxes(otherPose))
            {
                if (!ConveyorPlacementGeometry.Overlaps(a, b, out float depth, out Vector3 normal)) continue;
                if (joined && ConveyorPlacementGeometry.IsConnectorClearance(a, b, port, direction)) continue;
                reason = "Overlaps " + other.DisplayName + " · move into a clear space";
                LastCollision = a.name + " / " + b.name + "; depth=" + depth + "; joined=" + joined + "; port=" + port + "; a=" + a.centre + "; b=" + b.centre
                    + "; aHalf=" + a.half + "; bHalf=" + b.half + "; aRotation=" + a.rotation.eulerAngles + "; bRotation=" + b.rotation.eulerAngles;
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static Vector3 LocalPortPosition(PlacedConveyor machine, Transform port)
        => port.parent == machine.transform ? port.localPosition : machine.transform.InverseTransformPoint(port.position);
    private static Vector3 AtPose(PlacedConveyor machine, Transform port, Pose pose)
        => pose.position + pose.rotation * LocalPortPosition(machine, port);

    private static Vector3 FacingAtPose(PlacedConveyor machine, Transform port, Pose pose)
        => pose.rotation * machine.transform.InverseTransformDirection(port.forward);

    private bool AlignedPort(PlacedConveyor a, Pose ap, PlacedConveyor b, Pose bp, out Vector3 point, out Vector3 direction)
    {
        for (int end = 0; end < 2; end++)
        {
            Transform from = end == 0 ? a.Section.Entry : a.Section.Exit;
            Transform to = end == 0 ? b.Section.Exit : b.Section.Entry;
            bool free = end == 0 ? (b.Section.Next == null || b.Section.Next == a.Section)
                : (b.Section.Previous == null || b.Section.Previous == a.Section);
            point = AtPose(a, from, ap); direction = FacingAtPose(a, from, ap);
            if (free && Vector3.Distance(point, AtPose(b, to, bp)) < 0.001f
                && Vector3.Dot(direction, -FacingAtPose(b, to, bp)) > 0.999f) return true;
        }
        point = direction = Vector3.zero;
        return false;
    }

    public bool TryCommit()
    {
        if (preview == null || !valid) return false;
        if (IsGroupMove) return CommitGroupMove();
        var placed = preview;
        // A deliberate release extremely close to a port may finish the small final
        // adjustment, even if it arrived too recently to pass the hover intent delay.
        if (snapPartner == null && guidePartner != null
            && Candidate(requestedPose, guidePartner, guideEnd, snapCaptureDistance * 0.55f, out var drop)
            && drop.pose.position.y >= -0.0001f && ValidatePlacement(placed, drop.pose, out _))
        { snapPartner = drop.partner; snapPose = drop.pose; }
        bool connecting = snapPartner != null;
        Pose destination = connecting ? snapPose : requestedPose;
        if (!ValidatePlacement(placed, destination, out status)) return false;
        if (adding)
        {
            placed.UnitId = Label(nextId++);
            placed.name = placed.DisplayName;
            machines.Add(placed);
        }
        placed.LastValidPose = destination;
        bool settle = !immediatePreview && Vector3.Distance(placed.transform.position, destination.position) > 0.00005f;
        Vector3 velocity = connecting ? magneticVelocity : releaseVelocity;
        preview = null; adding = dragging = followPointer = false; ResetMagnet();
        selection.Clear(); selection.Add(placed); Selected = placed;
        if (settle)
        {
            placed.IsSettling = true;
            StartCoroutine(SettlePlacement(placed, destination, velocity));
            status = connecting ? "Finishing connection gently…" : "Settling into place…";
        }
        else { placed.FinishEditing(); Reconnect(); status = placed.DisplayName + " placed · drag to move · Q / E to rotate"; }
        UpdateGuides(); Changed?.Invoke();
        return true;
    }

    private IEnumerator SettlePlacement(PlacedConveyor machine, Pose destination, Vector3 velocity)
    {
        float elapsed = 0f;
        while (machine != null && elapsed < 0.36f && Vector3.Distance(machine.transform.position, destination.position) > 0.00005f)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.033f); elapsed += dt;
            Vector3 position = Vector3.SmoothDamp(machine.transform.position, destination.position, ref velocity, 0.065f, Mathf.Infinity, dt);
            position.y = Mathf.Max(0f, position.y);
            machine.transform.SetPositionAndRotation(position, destination.rotation); machine.RefreshSupports();
            machine.ShowOutline(true, Green);
            yield return null;
        }
        if (machine == null) yield break;
        machine.transform.SetPositionAndRotation(destination.position, destination.rotation);
        machine.RefreshSupports(); machine.FinishEditing(); Reconnect();
        if (preview == null && Selected == machine) status = machine.DisplayName + " placed · drag to move · Q / E to rotate";
        UpdateGuides(); Changed?.Invoke();
    }

    public void ReleasePreview()
    {
        if (preview == null) return;
        if (!TryCommit() && !adding) CancelPreview();
    }

    public void CancelPreview()
    {
        if (preview == null) return;
        if (IsGroupMove) { CancelGroupMove(); return; }
        var cancelled = preview;
        preview = null; ResetMagnet(); dragging = followPointer = false;
        if (adding) { Destroy(cancelled.gameObject); status = "Placement cancelled."; }
        else
        {
            cancelled.IsReturning = true;
            cancelled.ShowOutline(true, new Color(1f, 0.28f, 0.27f));
            StartCoroutine(ReturnSmoothly(cancelled));
            status = "Returning to the last valid position.";
        }
        adding = false;
        UpdateGuides(); Changed?.Invoke();
    }

    private IEnumerator ReturnSmoothly(PlacedConveyor machine)
    {
        var from = new Pose(machine.transform.position, machine.transform.rotation);
        float elapsed = 0f;
        const float duration = 0.26f;
        while (elapsed < duration && machine != null)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            Vector3 position = Vector3.Lerp(from.position, machine.LastValidPose.position, t);
            position.y = Mathf.Max(0f, position.y);
            machine.transform.SetPositionAndRotation(position, Quaternion.Slerp(from.rotation, machine.LastValidPose.rotation, t));
            machine.RefreshSupports();
            machine.ShowOutline(true, new Color(1f, 0.28f, 0.27f));
            yield return null;
        }
        if (machine == null) yield break;
        machine.transform.SetPositionAndRotation(machine.LastValidPose.position, machine.LastValidPose.rotation);
        machine.RefreshSupports(); machine.FinishEditing(); Reconnect();
        status = machine.DisplayName + " returned to its last valid position.";
        UpdateGuides(); Changed?.Invoke();
    }

    private void Reconnect()
    {
        foreach (var a in machines)
        foreach (var b in machines)
            if (a != b && !a.IsEditing && !b.IsEditing) a.Section.TryConnectTo(b.Section);
        RefreshTransfers();
    }

    public void Select(PlacedConveyor machine, bool additive = false)
    {
        if (preview != null) CancelPreview();
        if (!additive) selection.Clear();
        if (machine != null && machines.Contains(machine))
        {
            if (additive && selection.Contains(machine)) selection.Remove(machine);
            else if (!selection.Contains(machine)) selection.Add(machine);
        }
        Selected = machine != null && selection.Contains(machine) ? machine : selection.Count > 0 ? selection[selection.Count - 1] : null;
        SelectionChanged();
    }

    public void Rotate(int steps)
    {
        ClearUiFocus();
        if (preview != null)
        {
            var pose = new Pose(requestedPose.position, requestedPose.rotation * Quaternion.Euler(0, steps * 90, 0));
            MovePreview(pose, followPointer);
        }
        else if (Selected != null && BeginMove(Selected))
        {
            MovePreview(new Pose(preview.transform.position, preview.transform.rotation * Quaternion.Euler(0, steps * 90, 0)), false);
            ReleasePreview();
        }
    }

    public void RemoveSelected()
    {
        if (preview != null) CancelPreview();
        RestoreSelection();
        if (selection.Count == 0) return;
        foreach (var machine in selection) if (machine.IsReturning || machine.IsSettling) return;
        int removed = selection.Count;
        // Products are independent rigid bodies: removing their support lets them fall.
        foreach (var machine in selection)
        { machine.Section.Disconnect(); machines.Remove(machine); Destroy(machine.gameObject); }
        selection.Clear(); Selected = null;
        status = removed + (removed == 1 ? " machine removed." : " machines removed.") + " Other machine names stay the same.";
        UpdateGuides(); Changed?.Invoke(); ClearUiFocus();
    }

    public void AddParcel()
    {
        SpawnProduct(ProductKind.Box);
    }
    public void AddCan() => SpawnProduct(ProductKind.Can);
    public PhysicsProduct SpawnProduct(ProductKind kind)
    {
        var added = SpawnProducts(kind); return added.Count > 0 ? added[0] : null;
    }

    public static string Label(int index)
    {
        string text = string.Empty;
        do { text = (char)('A' + index % 26) + text; index = index / 26 - 1; } while (index >= 0);
        return text;
    }

    private void ClearUiFocus() { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
    private bool PointerInWorld(Vector2 position) => WorldScreenRect.Contains(position)
        && (controlBox == null || !controlBox.BlocksPointer(position))
        && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());

    private bool GroundPoint(Vector2 screen, out Vector3 point)
    {
        Ray ray = sceneCamera.ScreenPointToRay(screen);
        if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance))
        { point = ray.GetPoint(distance); return true; }
        point = Vector3.zero; return false;
    }

    private PlacedConveyor Pick(Vector2 screen)
    {
        Ray ray = sceneCamera.ScreenPointToRay(screen);
        var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            var machine = hit.collider.GetComponentInParent<PlacedConveyor>();
            if (machine != null && !machine.IsReturning && !machine.IsSettling) return machine;
        }
        return null;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.deleteKey.wasPressedThisFrame && !keyboard.altKey.isPressed
            && !keyboard.leftMetaKey.isPressed && !keyboard.rightMetaKey.isPressed) RemoveSelected();
        if (keyboard != null && !keyboard.ctrlKey.isPressed && !keyboard.altKey.isPressed && !keyboard.leftMetaKey.isPressed && !keyboard.rightMetaKey.isPressed)
        {
            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) BeginAddShortcut(1);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) BeginAddShortcut(2);
            else if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) BeginAddShortcut(3);
            if (keyboard.escapeKey.wasPressedThisFrame) CancelPreview();
            if (keyboard.qKey.wasPressedThisFrame) Rotate(-1);
            if (keyboard.eKey.wasPressedThisFrame || keyboard.rKey.wasPressedThisFrame) Rotate(1);
            if (keyboard.enterKey.wasPressedThisFrame && preview != null) ReleasePreview();
            if (keyboard.fKey.wasPressedThisFrame && Selected != null) FocusSelection(controlBox != null && !controlBox.Hidden ? controlBox.OpenWidthPixels : 0f);
            if (keyboard.homeKey.wasPressedThisFrame) orbitCamera.Reframe(machines);
        }
        var pointer = Pointer.current;
        var touch = Touchscreen.current?.primaryTouch;
        bool touching = touch != null && (touch.press.isPressed || touch.press.wasReleasedThisFrame);
        if ((!touching && pointer == null) || Time.frameCount <= createdFrame) return;
        Vector2 screen = touching ? touch.position.ReadValue() : pointer.position.ReadValue();
        bool pressed = touching ? touch.press.wasPressedThisFrame : pointer.press.wasPressedThisFrame;
        bool held = touching ? touch.press.isPressed : pointer.press.isPressed;
        bool released = touching ? touch.press.wasReleasedThisFrame : pointer.press.wasReleasedThisFrame;
        bool inWorld = PointerInWorld(screen);
        if (preview != null && followPointer && inWorld && GroundPoint(screen, out Vector3 ground))
        {
            Vector3 position = ground + dragOffset;
            position.y = IsGroupMove ? groupAnchor.position.y : 0f;
            MovePreview(new Pose(position, preview.transform.rotation), true);
        }
        if (pressed && inWorld)
        {
            if (preview != null && adding) { TryCommit(); return; }
            pressedMachine = Pick(screen); pressScreen = screen; pressTime = Time.unscaledTime;
            if (pressedMachine != null)
            {
                additivePress = !touching && keyboard != null && keyboard.ctrlKey.isPressed;
                if (additivePress) { Select(pressedMachine, true); lastTapped = null; }
                else if (selection.Count > 1 && IsSelected(pressedMachine)) MakePrimary(pressedMachine);
                else Select(pressedMachine);
                if (!IsSelected(pressedMachine)) { pressedMachine = null; return; }
                if (GroundPoint(screen, out Vector3 groundHit)) dragOffset = pressedMachine.transform.position - groundHit;
            }
            else Select(null);
        }
        if (held && pressedMachine != null && preview == null && Vector2.Distance(screen, pressScreen) > (touching ? 10f : 5f))
        {
            if (BeginMove(pressedMachine)) { followPointer = true; dragging = true; lastTapped = null; }
        }
        if (released)
        {
            if (pressedMachine != null && inWorld && preview == null) CompleteSelectionGesture(pressedMachine, additivePress, dragging);
            if (!additivePress && !dragging && preview == null && pressedMachine != null && inWorld && Time.unscaledTime - pressTime < 0.3f)
                RegisterTap(pressedMachine, screen, Time.unscaledTime);
            pressedMachine = null; additivePress = false;
            if (dragging) ReleasePreview();
        }
    }
    public void RegisterTap(PlacedConveyor machine, Vector2 screen, float time)
    {
        if (machine == null || machine.IsEditing || machine.IsReturning || machine.IsSettling) return;
        if (machine == lastTapped && time - lastTapTime <= 0.34f && (screen - lastTapScreen).sqrMagnitude <= 24f * 24f)
        {
            lastTapped = null; lastTapTime = -10f;
            if (controlBox != null) controlBox.OpenFor(machine); else orbitCamera.FocusMachine(machine);
            return;
        }
        lastTapped = machine; lastTapTime = time; lastTapScreen = screen;
    }

    private void UpdateGuides()
    {
        entryPartner = exitPartner = null;
        groupReadyPartners.Clear(); groupReadyPorts.Clear();
        if (IsGroupMove && valid) GroupMatchingPartners(snapPartner != null ? snapPose : requestedPose);
        else if (preview != null && valid)
            MatchingPartners(preview, snapPartner != null ? snapPose : requestedPose, out entryPartner, out exitPartner);
        foreach (var machine in machines)
            machine.ShowOutline(machine.IsReturning || machine.IsSettling || IsSelected(machine) || machine == entryPartner || machine == exitPartner || groupReadyPartners.Contains(machine) || machine == preview,
                machine.IsReturning ? new Color(1f, 0.28f, 0.27f) : machine.IsSettling ? Green
                    : machine == entryPartner || machine == exitPartner || groupReadyPartners.Contains(machine) ? Green : Blue);
        if (preview != null) preview.ShowOutline(true, valid ? Green : new Color(1f, 0.24f, 0.24f));
        if (IsGroupMove) foreach (var member in groupMove) member.machine.ShowOutline(true, valid ? Green : new Color(1f, 0.24f, 0.24f));
        if (guidePartner != null) guidePort = guideEnd == 0 ? guidePartner.Section.Exit : guidePartner.Section.Entry;
        if (entryPartner != null && exitPartner != null)
            secondGuidePort = guideEnd == 0 ? exitPartner.Section.Entry : entryPartner.Section.Exit;
        if (IsGroupMove) foreach (var port in groupReadyPorts) if (port != guidePort) { secondGuidePort = port; break; }
    }

    private void LateUpdate()
    {
        float desired = preview != null && guidePartner != null ? Mathf.Lerp(0.1f, 0.8f,
            1f - Mathf.InverseLerp(snapCaptureDistance, snapGuideDistance, guideDistance)) : 0f;
        guideOpacity = Mathf.Lerp(guideOpacity, desired, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.075f));
        float secondDesired = (entryPartner != null && exitPartner != null) || (IsGroupMove && groupReadyPorts.Count > 1) ? desired : 0f;
        secondGuideOpacity = Mathf.Lerp(secondGuideOpacity, secondDesired, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.075f));
        DrawGuide(snapGuide, guidePort, guideOpacity);
        DrawGuide(secondSnapGuide, secondGuidePort, secondGuideOpacity);
    }
    private void DrawGuide(LineRenderer line, Transform port, float opacity)
    {
        if (line == null) return;
        line.enabled = port != null && opacity > 0.01f;
        if (!line.enabled) return;
        Color colour = valid ? Green : new Color(1f, 0.24f, 0.24f); colour.a = opacity;
        line.startColor = line.endColor = colour;
        float radius = Mathf.Lerp(0.29f, 0.35f, opacity / 0.8f);
        for (int i = 0; i < 33; i++)
        {
            float angle = Mathf.PI * i / 32f;
            Vector3 offset = port.right * Mathf.Cos(angle) * radius + port.forward * Mathf.Sin(angle) * radius;
            line.SetPosition(i, port.position + Vector3.up * 0.04f + offset);
        }
    }
}
