using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class ConveyorCollision : MonoBehaviour
{
    private ConveyorSection section;
    private ConveyorBeltSurface motor;
    private MeshCollider rubber;
    private Mesh beltShape;
    private Bounds beltBounds;
    [SerializeField, HideInInspector] private List<Collider> colliders = new List<Collider>();
    [System.Serializable] private sealed class SolidPart { public MeshFilter source; public MeshCollider collider; public float scale; public Mesh mesh; }
    [SerializeField, HideInInspector] private List<SolidPart> solids = new List<SolidPart>();
    private readonly Collider[] nearby = new Collider[128];
    private float previousSpeed = -1f;
    private GameObject bridge;
    private BoxCollider bridgeCollider;
    private Mesh bridgeRollerMesh;
    [SerializeField, HideInInspector] private List<Transform> bridgeRollers = new List<Transform>();
    private Vector3 bridgeDirection;
    private float bridgeSpeed, bridgeAngle;
    private Vector3 lastBridgeStart, lastBridgeEnd;
    private bool bridgeGeometryReady;
    public ConveyorSection Section => section;
    public Vector3 SurfaceNormal => motor.transform.TransformDirection(Vector3.forward).normalized;
    public bool Editing { get; private set; }

    private void Awake()
    {
        section = GetComponent<ConveyorSection>(); motor = section.BeltSurface;
        var body = GetComponent<Rigidbody>(); if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || HasRollerParent(filter.transform)) continue;
            if (filter.GetComponent<ConveyorBeltSurface>() != null)
            {
                beltBounds = filter.sharedMesh.bounds;
                float scale = Mathf.Abs(filter.transform.lossyScale.x);
                beltShape = ScaledMesh(CapsulePrism(beltBounds), scale, true);
                var proxy = new GameObject("Rubber collision surface"); proxy.transform.SetParent(filter.transform, false);
                proxy.transform.localScale = Vector3.one / scale;
                rubber = proxy.AddComponent<MeshCollider>(); rubber.sharedMesh = beltShape; rubber.convex = true;
                rubber.sharedMaterial = Material("Rubber"); rubber.hasModifiableContacts = true;
                rubber.contactOffset = 0.001f; colliders.Add(rubber);
            }
            else
            {
                float scale = Mathf.Abs(filter.transform.lossyScale.x);
                var proxy = new GameObject("Solid machine collision"); proxy.transform.SetParent(filter.transform, false);
                proxy.transform.localScale = Vector3.one / scale;
                var mesh = ScaledMesh(filter.sharedMesh, scale);
                var collider = proxy.AddComponent<MeshCollider>(); collider.sharedMesh = mesh;
                collider.sharedMaterial = Material("Steel"); collider.contactOffset = 0.001f; colliders.Add(collider);
                solids.Add(new SolidPart { source = filter, collider = collider, scale = scale, mesh = mesh });
            }
        }
    }
    private void OnEnable()
    {
        bridgeGeometryReady = false;
        if (rubber != null)
        {
            rubber.hasModifiableContacts = true;
            if (colliders.Count == 0)
                foreach (var collider in GetComponentsInChildren<Collider>(true)) if (!collider.isTrigger) colliders.Add(collider);
            if (solids.Count == 0)
                foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
                {
                    var proxy = filter.transform.Find("Solid machine collision");
                    if (proxy != null && proxy.TryGetComponent<MeshCollider>(out var collider))
                        solids.Add(new SolidPart { source = filter, collider = collider, scale = Mathf.Abs(filter.transform.lossyScale.x), mesh = collider.sharedMesh });
                }
        }
        if (bridgeCollider != null) bridgeCollider.hasModifiableContacts = true;
        if (bridge != null && bridgeRollers.Count == 0)
            foreach (Transform child in bridge.transform) bridgeRollers.Add(child);
        ConveyorPhysicsWorld.Register(this);
    }
    private void OnDisable()
    {
        WakeNearby();
        ConveyorPhysicsWorld.Unregister(this);
    }
    private void OnDestroy()
    {
        if (beltShape != null) Destroy(beltShape); if (bridgeRollerMesh != null) Destroy(bridgeRollerMesh);
        foreach (var part in solids) if (part.mesh != null) Destroy(part.mesh);
    }
    public static PhysicsMaterial Material(string name) => Resources.Load<PhysicsMaterial>("Physics/" + name);
    private bool HasRollerParent(Transform child)
    {
        for (var t = child; t != null && t != transform; t = t.parent) if (t.name == "MovingRollers") return true;
        return false;
    }
    public void SetEditing(bool editing)
    {
        // Wake products at the old position before the drag removes their support.
        if (editing && !Editing) WakeNearby();
        Editing = editing;
        if (!editing) foreach (var part in solids)
            if (part.source != null && part.collider != null && part.source.name.Contains("Legs"))
            {
                var old = part.mesh; part.mesh = ScaledMesh(part.source.sharedMesh, part.scale);
                part.collider.sharedMesh = part.mesh; if (old != null) Destroy(old);
            }
        foreach (var collider in colliders) if (collider != null) collider.enabled = !editing;
        if (!editing) WakeNearby();
    }
    public bool TrySurface(out ConveyorPhysicsWorld.Surface data)
    {
        data = default;
        if (rubber == null || !rubber.enabled || motor == null) return false;
        data = new ConveyorPhysicsWorld.Surface { collider = rubber.GetEntityId(), centre = beltBounds.center,
            halfStraight = beltBounds.extents.y - beltBounds.extents.z, speed = motor.EffectiveSpeed,
            reverse = motor.ReverseDirection, toLocal = motor.transform.worldToLocalMatrix, toWorld = motor.transform.localToWorldMatrix };
        return true;
    }
    public void AppendSurfaces(List<ConveyorPhysicsWorld.Surface> surfaces)
    {
        if (TrySurface(out var surface)) surfaces.Add(surface);
        if (bridgeCollider != null && bridgeCollider.enabled && bridge.activeSelf)
            surfaces.Add(new ConveyorPhysicsWorld.Surface { collider = bridgeCollider.GetEntityId(), flat = true, flatVelocity = bridgeDirection * bridgeSpeed });
    }
    private void FixedUpdate() => PreparePhysics();
    public void PreparePhysics()
    {
        RefreshTransfer();
        float speed = motor != null ? motor.EffectiveSpeed : 0;
        if (Mathf.Abs(speed - previousSpeed) > 0.00001f) { WakeNearby(); previousSpeed = speed; }
    }
    private void Update()
    {
        if (bridge == null || !bridge.activeSelf) return;
        bridgeAngle = (bridgeAngle + bridgeSpeed * Time.deltaTime / 0.012f * Mathf.Rad2Deg) % 360f;
        foreach (var roller in bridgeRollers) roller.localRotation = Quaternion.AngleAxis(bridgeAngle, Vector3.right);
    }
    public Vector3 TopEnd(bool output)
    {
        float a = beltBounds.extents.y - beltBounds.extents.z;
        float sign = (output == motor.ReverseDirection) ? 1f : -1f;
        return motor.transform.TransformPoint(beltBounds.center + new Vector3(0, sign * a, beltBounds.extents.z));
    }
    public void RefreshTransfer()
    {
        if (!section.TryGetNext(out var next)) { if (bridge != null) bridge.SetActive(false); return; }
        var other = next.GetComponent<ConveyorCollision>();
        if (other == null || (Editing != other.Editing)) { if (bridge != null) bridge.SetActive(false); return; }
        Vector3 start = TopEnd(true) - motor.TopTravelDirection * 0.008f;
        Vector3 end = other.TopEnd(false) + other.motor.TopTravelDirection * 0.008f;
        float length = Vector3.Distance(start, end);
        if (length < 0.005f || length > 0.4f) { if (bridge != null) bridge.SetActive(false); return; }
        if (bridge == null)
        {
            bridge = new GameObject("Powered transfer rollers"); bridge.transform.SetParent(transform, false);
            bridgeCollider = bridge.AddComponent<BoxCollider>(); bridgeCollider.contactOffset = 0.0007f;
            bridgeCollider.hasModifiableContacts = true; bridgeCollider.sharedMaterial = Material("Rubber"); colliders.Add(bridgeCollider);
            bridgeRollerMesh = ConveyorRollerMesh.Create();
            for (int i = 0; i < 10; i++)
            {
                var roller = new GameObject("Transfer roller " + (i + 1)); roller.transform.SetParent(bridge.transform, false);
                roller.AddComponent<MeshFilter>().sharedMesh = bridgeRollerMesh;
                roller.AddComponent<MeshRenderer>().sharedMaterial = motor.RollerMaterial;
                bridgeRollers.Add(roller.transform);
            }
            GetComponent<ConveyorSilhouette>()?.RefreshRenderers();
        }
        bridge.SetActive(true);
        bridgeCollider.enabled = !Editing;
        bridgeDirection = (end - start).normalized;
        bridgeSpeed = Editing ? 0f : Mathf.Min(motor.EffectiveSpeed, other.motor.EffectiveSpeed);
        if (bridgeGeometryReady && start.Equals(lastBridgeStart) && end.Equals(lastBridgeEnd)) return;
        lastBridgeStart = start; lastBridgeEnd = end; bridgeGeometryReady = true;
        Vector3 up = (SurfaceNormal + other.SurfaceNormal).normalized;
        // Recess the plate by 1 mm: a raised leading face catches a box's sharp
        // lower edge. The next belt's rounded nose provides a smooth exit lead-in.
        bridge.transform.SetPositionAndRotation((start + end) * 0.5f - up * 0.001f, Quaternion.LookRotation(bridgeDirection, up));
        float width = Mathf.Min(beltBounds.size.x * motor.transform.TransformVector(Vector3.right).magnitude,
            other.beltBounds.size.x * other.motor.transform.TransformVector(Vector3.right).magnitude) * 0.98f;
        bridgeCollider.center = Vector3.down * 0.004f; bridgeCollider.size = new Vector3(width, 0.008f, length);
        for (int i = 0; i < bridgeRollers.Count; i++)
        {
            bridgeRollers[i].localPosition = new Vector3(0, -0.012f, Mathf.Lerp(-length * 0.5f + 0.011f, length * 0.5f - 0.011f, i / 9f));
            bridgeRollers[i].localScale = new Vector3(width, 0.012f, 0.012f);
        }
        WakeNearby();
    }
    private void WakeNearby()
    {
        bool found = false; Bounds bounds = default;
        foreach (var collider in colliders)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (!found) { bounds = collider.bounds; found = true; } else bounds.Encapsulate(collider.bounds);
        }
        if (!found) return;
        bounds.Expand(0.12f);
        int count = gameObject.scene.GetPhysicsScene().OverlapBox(bounds.center, bounds.extents, nearby, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var body = nearby[i].attachedRigidbody;
            if (body == null || body.isKinematic) continue;
            if (body.TryGetComponent<PhysicsProduct>(out var product)) product.WakeContactPile(); else body.WakeUp();
        }
    }
    public Pose InfeedPose(float productLength)
    {
        float a = beltBounds.extents.y - beltBounds.extents.z;
        float inset = Mathf.Min(a * 0.75f, (productLength * 0.5f + 0.06f) / motor.transform.TransformVector(Vector3.up).magnitude);
        float y = motor.ReverseDirection ? -a + inset : a - inset;
        Vector3 point = motor.transform.TransformPoint(beltBounds.center + new Vector3(0, y, beltBounds.extents.z));
        return new Pose(point + SurfaceNormal * 0.014f, Quaternion.LookRotation(motor.TopTravelDirection, SurfaceNormal));
    }
    private static Mesh CapsulePrism(Bounds bounds)
    {
        var ring = new List<Vector2>(); const int steps = 16;
        float a = bounds.extents.y - bounds.extents.z, r = bounds.extents.z;
        for (int i = 0; i <= steps; i++) { float t = Mathf.PI * i / steps; ring.Add(new Vector2(a + Mathf.Sin(t) * r, Mathf.Cos(t) * r)); }
        for (int i = 0; i <= steps; i++) { float t = Mathf.PI + Mathf.PI * i / steps; ring.Add(new Vector2(-a + Mathf.Sin(t) * r, Mathf.Cos(t) * r)); }
        int n = ring.Count; var vertices = new Vector3[n * 2]; var triangles = new List<int>();
        for (int i = 0; i < n; i++)
        {
            vertices[i] = bounds.center + new Vector3(-bounds.extents.x, ring[i].x, ring[i].y);
            vertices[i + n] = bounds.center + new Vector3(bounds.extents.x, ring[i].x, ring[i].y);
            int j = (i + 1) % n;
            triangles.AddRange(new[] { i, j, i + n, j, j + n, i + n });
            if (i > 0 && i < n - 1) triangles.AddRange(new[] { 0, i + 1, i, n, n + i, n + i + 1 });
        }
        var mesh = new Mesh { name = "Smooth closed belt collision hull" }; mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateBounds(); return mesh;
    }
    private static Mesh ScaledMesh(Mesh source, float scale, bool releaseSource = false)
    {
        // Cook geometry in metres, independent of the FBX's tiny source units.
        var vertices = source.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] *= scale;
        var mesh = new Mesh { name = source.name + " (physics metres)", indexFormat = source.indexFormat };
        mesh.vertices = vertices; mesh.triangles = source.triangles; mesh.RecalculateBounds();
        if (releaseSource) Destroy(source);
        return mesh;
    }
}
