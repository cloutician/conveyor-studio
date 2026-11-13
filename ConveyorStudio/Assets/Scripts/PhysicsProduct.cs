using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum ProductKind { Box, Can }

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsProduct : MonoBehaviour
{
    public ProductKind kind;
    public Vector3 size;
    [Min(0f)] public float rollingResistance = 0.003f;
    [Tooltip("Angular settling resistance for boxes touching the floor only.")]
    [Min(0f)] public float floorAngularResistance = 12f;
    public Rigidbody Body { get; private set; }
    public ConveyorSection CurrentConveyor { get; private set; }
    private float normalLoad;
    private Vector3 contactNormal;
    private PhysicsMaterial floorMaterial;
    private bool floorContactThisStep;
    private bool floorImpactAbsorbed;
    private bool floorCleanupScheduled;
    private int cleanupBatchId;
    private double fadeStartsAt;
    private double fadeCommittedAt;
    private const double CleanupDelay = 5.0;
    private const double NewBoxGrace = 2.0;
    private const float FadeDuration = 0.8f;
    // Off-belt contact piles share one persistent batch, including side contacts.
    private readonly Dictionary<Collider, PhysicsProduct> supportContacts = new Dictionary<Collider, PhysicsProduct>();
    private readonly HashSet<Collider> conveyorContacts = new HashSet<Collider>();
    private readonly List<Collider> expiredConveyorContacts = new List<Collider>();
    private Collider productCollider;
    private struct SupportPose { public Vector3 position, scale; public Quaternion rotation; }
    private readonly Dictionary<Collider, SupportPose> supportingSurfaces = new Dictionary<Collider, SupportPose>();
    private sealed class CleanupBatch
    {
        public int id;
        public double deadline;
        public readonly HashSet<PhysicsProduct> members = new HashSet<PhysicsProduct>();
    }
    private static readonly Dictionary<int, CleanupBatch> cleanupBatches = new Dictionary<int, CleanupBatch>();
    private static int nextCleanupBatch;
    private float fadeOpacity = 1f;
    [System.Serializable]
    private sealed class FadeMaterial
    {
        public Material material;
        public Color colour;
        public int colourProperty;
    }
    private List<FadeMaterial> fadeMaterials;
    private void Awake() => Body = GetComponent<Rigidbody>();
    private void OnEnable()
    {
        if (Body == null) Body = GetComponent<Rigidbody>();
        productCollider = GetComponent<Collider>();
        Body.WakeUp();
        // Products already awaiting cleanup when scripts reload can finish fading too.
        if (floorCleanupScheduled && fadeStartsAt == 0.0) fadeStartsAt = Time.timeAsDouble;
        if (floorCleanupScheduled) fadeStartsAt = System.Math.Min(fadeStartsAt, Time.timeAsDouble + CleanupDelay);
        if (floorCleanupScheduled && fadeMaterials != null && fadeCommittedAt == 0.0)
            fadeCommittedAt = System.Math.Min(fadeStartsAt, Time.timeAsDouble);
        if (floorCleanupScheduled) RegisterCleanupBatch();
    }
    private void OnDisable()
    {
        foreach (var other in supportContacts.Values) if (other != null) other.ForgetSupport(this);
        supportContacts.Clear();
        supportingSurfaces.Clear();
        conveyorContacts.Clear(); CurrentConveyor = null;
        if (cleanupBatchId != 0 && cleanupBatches.TryGetValue(cleanupBatchId, out var batch))
        { batch.members.Remove(this); if (batch.members.Count == 0) cleanupBatches.Remove(batch.id); }
    }
    private void ForgetSupport(PhysicsProduct product)
    {
        var remove = new List<Collider>();
        foreach (var pair in supportContacts) if (pair.Value == product) remove.Add(pair.Key);
        foreach (var collider in remove) { supportContacts.Remove(collider); supportingSurfaces.Remove(collider); }
        if (remove.Count > 0) WakeContactPile();
    }
    public void WakeContactPile()
    {
        // Sleeping layers above a removed support must resume together. Contact
        // graphs can contain cycles and sideways neighbours, so visit each once.
        var pending = new Stack<PhysicsProduct>(); var visited = new HashSet<PhysicsProduct>();
        pending.Push(this);
        while (pending.Count > 0)
        {
            var product = pending.Pop();
            if (product == null || !product.isActiveAndEnabled || !visited.Add(product)) continue;
            if (product.Body != null) product.Body.WakeUp();
            foreach (var other in product.supportContacts.Values) if (other != null) pending.Push(other);
        }
    }
    private void RefreshConveyorContacts()
    {
        expiredConveyorContacts.Clear();
        Bounds bounds = productCollider.bounds; bounds.Expand(0.02f);
        foreach (var contact in conveyorContacts)
            if (contact == null || !contact.enabled || !contact.gameObject.activeInHierarchy || !bounds.Intersects(contact.bounds))
                expiredConveyorContacts.Add(contact);
        foreach (var contact in expiredConveyorContacts) { conveyorContacts.Remove(contact); supportingSurfaces.Remove(contact); }
        CurrentConveyor = null;
        foreach (var contact in conveyorContacts)
        { CurrentConveyor = contact.GetComponentInParent<ConveyorSection>(); if (CurrentConveyor != null) break; }
        if (expiredConveyorContacts.Count > 0) WakeContactPile();
    }
    private bool HasUnchangedSupport()
    {
        foreach (var pair in supportingSurfaces)
        {
            var collider = pair.Key;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            var pose = pair.Value; var surface = collider.transform;
            if ((surface.position - pose.position).sqrMagnitude < 0.00000001f
                && Quaternion.Angle(surface.rotation, pose.rotation) < 0.01f && surface.lossyScale == pose.scale) return true;
        }
        return false;
    }
    private void UpdateSupport(Collision collision)
    {
        var other = collision.collider.GetComponentInParent<PhysicsProduct>();
        if (other == null || other == this) return;
        supportContacts[collision.collider] = other;
        JoinCleanupPile(other);
    }
    private CleanupBatch RegisterCleanupBatch()
    {
        if (cleanupBatchId == 0) cleanupBatchId = ++nextCleanupBatch;
        nextCleanupBatch = Mathf.Max(nextCleanupBatch, cleanupBatchId);
        if (!cleanupBatches.TryGetValue(cleanupBatchId, out var batch))
        {
            batch = new CleanupBatch { id = cleanupBatchId, deadline = fadeStartsAt };
            cleanupBatches.Add(batch.id, batch);
        }
        if (batch.members.Add(this)) { batch.deadline = System.Math.Max(batch.deadline, fadeStartsAt); PublishDeadline(batch); }
        return batch;
    }
    private static void PublishDeadline(CleanupBatch batch)
    {
        foreach (var member in batch.members)
        {
            if (member == null) continue;
            member.floorCleanupScheduled = true; member.cleanupBatchId = batch.id; member.fadeStartsAt = batch.deadline;
        }
    }
    private void JoinCleanupPile(PhysicsProduct other)
    {
        if (other == null || other == this || (!floorCleanupScheduled && !other.floorCleanupScheduled)
            || FadeIsCommitted || other.FadeIsCommitted
            || TouchesConveyor() || other.TouchesConveyor()) return;
        if (floorCleanupScheduled && other.floorCleanupScheduled && cleanupBatchId != 0 && cleanupBatchId == other.cleanupBatchId) return;
        var batch = floorCleanupScheduled ? RegisterCleanupBatch() : other.RegisterCleanupBatch();
        var arriving = floorCleanupScheduled ? other : this;
        if (!arriving.floorCleanupScheduled)
        {
            batch.members.Add(arriving);
            if (arriving.kind == ProductKind.Box) batch.deadline = System.Math.Max(batch.deadline, Time.timeAsDouble + NewBoxGrace);
        }
        else
        {
            var incoming = arriving.RegisterCleanupBatch(); if (batch == incoming) return;
            // Floor-first arrivals are still new to this pile. Merge their entire
            // batches, so no old subset can disappear under the newer congestion.
            batch.deadline = System.Math.Max(System.Math.Max(batch.deadline, incoming.deadline), Time.timeAsDouble + NewBoxGrace);
            foreach (var member in incoming.members) if (member != null) batch.members.Add(member);
            cleanupBatches.Remove(incoming.id);
        }
        PublishDeadline(batch);
    }
    private void FixedUpdate() => ApplyResistance(Time.fixedDeltaTime);
    private bool FadeIsCommitted => fadeCommittedAt > 0.0 || (floorCleanupScheduled && Time.timeAsDouble >= fadeStartsAt);
    private void Update()
    {
        if (!floorCleanupScheduled) return;
        if (fadeCommittedAt == 0.0)
        {
            if (Time.timeAsDouble < fadeStartsAt) return;
            fadeCommittedAt = fadeStartsAt;
        }
        if (fadeMaterials == null) BeginFade();
        float progress = Mathf.Clamp01((float)((Time.timeAsDouble - fadeCommittedAt) / FadeDuration));
        fadeOpacity = Mathf.Min(fadeOpacity, 1f - Mathf.SmoothStep(0f, 1f, progress)); ApplyOpacity();
        if (progress >= 1f) Destroy(gameObject);
    }
    private void ApplyOpacity()
    {
        foreach (var item in fadeMaterials)
        {
            Color colour = item.colour; colour.a *= fadeOpacity;
            item.material.SetColor(item.colourProperty, colour);
        }
    }
    private void BeginFade()
    {
        fadeMaterials = new List<FadeMaterial>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                // Each fading product owns its copies; other products and asset materials stay intact.
                var material = new Material(materials[i]) { name = materials[i].name + " (product fade)" };
                int colourProperty = Shader.PropertyToID(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
                fadeMaterials.Add(new FadeMaterial { material = material, colour = material.GetColor(colourProperty), colourProperty = colourProperty });
                material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
                material.SetFloat("_BlendModePreserveSpecular", 0f);
                material.SetFloat("_AlphaClip", 0f); material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_ALPHAMODULATE_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetShaderPassEnabled("ShadowCaster", false);
                material.SetShaderPassEnabled("DepthOnly", false);
                material.SetShaderPassEnabled("DepthNormals", false);
                material.SetShaderPassEnabled("DepthNormalsOnly", false);
                material.renderQueue = (int)RenderQueue.Transparent;
                // Resources/ProductFade.mat retains this alpha-blended Lit variant in player builds.
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
        }
    }
    private void OnDestroy()
    {
        if (fadeMaterials != null) foreach (var item in fadeMaterials)
            if (item.material != null) Destroy(item.material);
    }
    // Small rolling resistance models contact deformation rather than damping cans
    // in mid-air. Coulomb sliding/static friction remains the solver's responsibility.
    public void ApplyResistance(float dt)
    {
        RefreshConveyorContacts();
        // Collider removal/teleportation need not issue a collision-exit callback
        // to a sleeping body. Verify real support instead of trusting old contacts.
        // The sweep uses the body's actual compound shape and its own physics scene.
        // An unchanged upward contact is authoritative even when a shape sweep
        // omits an initially overlapping floor. Query only when that support is gone.
        if (!Body.isKinematic && Body.useGravity && Body.IsSleeping() && Physics.gravity.sqrMagnitude > 0.0001f && !HasUnchangedSupport()
            && !Body.SweepTest(Physics.gravity.normalized, out _, 0.015f, QueryTriggerInteraction.Ignore))
            WakeContactPile();
        if (kind == ProductKind.Box && floorContactThisStep && !Body.IsSleeping())
        {
            // Cardboard contact absorbs tumbling energy. Apply a dissipative torque
            // in the body's principal-inertia frame; floor friction supplies braking.
            Quaternion principal = Body.rotation * Body.inertiaTensorRotation;
            Vector3 angularMomentum = principal * Vector3.Scale(Body.inertiaTensor,
                Quaternion.Inverse(principal) * Body.angularVelocity);
            float rate = Mathf.Min(floorAngularResistance, 1f / Mathf.Max(dt, 0.0001f));
            Body.AddTorque(-angularMomentum * rate, ForceMode.Force);
        }
        if (kind == ProductKind.Can && normalLoad > 0f && !Body.IsSleeping())
        {
            Vector3 axis = Vector3.ProjectOnPlane(Body.angularVelocity, contactNormal.normalized);
            float speed = axis.magnitude;
            if (speed > 0.001f)
            {
                float radius = Mathf.Min(size.x, size.z) * 0.5f;
                float torque = rollingResistance * normalLoad * radius;
                float smallestInertia = Mathf.Min(Body.inertiaTensor.x, Body.inertiaTensor.y, Body.inertiaTensor.z);
                torque = Mathf.Min(torque, smallestInertia * speed / Mathf.Max(dt, 0.0001f));
                Body.AddTorque(-axis / speed * torque, ForceMode.Force);
            }
        }
        normalLoad = 0f; contactNormal = Vector3.zero; floorContactThisStep = false;
    }
    private void OnCollisionEnter(Collision collision) => Contact(collision);
    private bool TouchesConveyor()
    {
        // Removing or editing a machine can disable its collider without an
        // exit callback. Only live, enabled contacts exclude a fresh arrival.
        foreach (var contact in conveyorContacts)
            if (contact != null && contact.enabled && contact.gameObject.activeInHierarchy) return true;
        return false;
    }
    private void OnCollisionStay(Collision collision) => Contact(collision);
    private void Contact(Collision collision)
    {
        if (floorMaterial == null) floorMaterial = ConveyorCollision.Material("Tile");
        bool touchesFloor = floorMaterial != null && collision.collider.sharedMaterial == floorMaterial;
        floorContactThisStep |= touchesFloor;
        Vector3 floorUp = Physics.gravity.sqrMagnitude > 0.0001f ? -Physics.gravity.normalized : Vector3.up;
        bool supporting = false;
        for (int i = 0; i < collision.contactCount; i++) supporting |= Vector3.Dot(collision.GetContact(i).normal, floorUp) > 0.2f;
        if (supporting)
        {
            var surface = collision.collider.transform;
            supportingSurfaces[collision.collider] = new SupportPose { position = surface.position, rotation = surface.rotation, scale = surface.lossyScale };
        }
        else supportingSurfaces.Remove(collision.collider);
        if (touchesFloor && kind == ProductKind.Box && !floorImpactAbsorbed
            && Mathf.Abs(Vector3.Dot(collision.relativeVelocity, floorUp)) > 0.75f)
        {
            floorImpactAbsorbed = true;
            // A rigid sharp-edged box otherwise turns a floor impact into a sideways
            // tumble. Bounded impulses approximate energy absorbed by cardboard.
            Vector3 tangent = Vector3.ProjectOnPlane(Body.linearVelocity, floorUp);
            Body.AddForce(-tangent * (Body.mass * 0.65f), ForceMode.Impulse);
            Quaternion principal = Body.rotation * Body.inertiaTensorRotation;
            Vector3 momentum = principal * Vector3.Scale(Body.inertiaTensor,
                Quaternion.Inverse(principal) * Body.angularVelocity);
            Body.AddTorque(-momentum * 0.8f, ForceMode.Impulse);
        }
        if (!floorCleanupScheduled)
        {
            // Tile is the physical material assigned to the studio's ground collider.
            // Schedule once, so bouncing or continued contact cannot restart the timer.
            if (touchesFloor)
            {
                floorCleanupScheduled = true;
                fadeStartsAt = Time.timeAsDouble + CleanupDelay;
                RegisterCleanupBatch();
                foreach (var other in supportContacts.Values) JoinCleanupPile(other);
            }
        }
        var conveyor = collision.collider.GetComponentInParent<ConveyorSection>();
        if (conveyor != null) { conveyorContacts.Add(collision.collider); CurrentConveyor = conveyor; }
        UpdateSupport(collision);
        float load = collision.impulse.magnitude / Time.fixedDeltaTime;
        normalLoad += load;
        for (int i = 0; i < collision.contactCount; i++) contactNormal += collision.GetContact(i).normal * load;
    }
    private void OnCollisionExit(Collision collision)
    {
        bool lostSupport = supportingSurfaces.Remove(collision.collider);
        lostSupport |= supportContacts.Remove(collision.collider);
        if (conveyorContacts.Remove(collision.collider)) { RefreshConveyorContacts(); lostSupport = true; }
        if (lostSupport) WakeContactPile();
    }
}
