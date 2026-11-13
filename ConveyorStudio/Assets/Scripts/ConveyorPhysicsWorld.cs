using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only immutable value data reaches PhysX's worker-thread contact callback.
// The solver supplies the forces and torques; products are never velocity-locked.
[DefaultExecutionOrder(10000)]
public sealed class ConveyorPhysicsWorld : MonoBehaviour
{
    public struct Surface
    {
        public EntityId collider;
        public Matrix4x4 toLocal, toWorld;
        public Vector3 centre;
        public float halfStraight, speed;
        public bool reverse;
        public bool flat;
        public Vector3 flatVelocity;
        public bool SameAs(Surface other) => collider.Equals(other.collider) && toLocal.Equals(other.toLocal)
            && toWorld.Equals(other.toWorld) && centre.Equals(other.centre) && halfStraight == other.halfStraight
            && speed == other.speed && reverse == other.reverse && flat == other.flat && flatVelocity.Equals(other.flatVelocity);
        public Vector3 VelocityAt(Vector3 point)
        {
            if (flat) return flatVelocity;
            Vector3 local = toLocal.MultiplyPoint3x4(point) - centre;
            Vector3 radial = new Vector3(0f, local.y - Mathf.Clamp(local.y, -halfStraight, halfStraight), local.z).normalized;
            Vector3 tangent = Vector3.Cross(Vector3.right, radial);
            return toWorld.MultiplyVector(tangent).normalized * (reverse ? -speed : speed);
        }
    }
    private static readonly List<ConveyorCollision> belts = new List<ConveyorCollision>();
    private static readonly Dictionary<Scene, ConveyorPhysicsWorld> worlds = new Dictionary<Scene, ConveyorPhysicsWorld>();
    private static Surface[] published = new Surface[0];
    private static readonly List<Surface> staging = new List<Surface>();
    private static bool contactsSubscribed;
    private Scene ownerScene;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        belts.Clear(); worlds.Clear(); staging.Clear(); published = new Surface[0];
        contactsSubscribed = false; SubscribeContacts();
    }
    private static void SubscribeContacts()
    {
        if (contactsSubscribed) return;
        Physics.ContactModifyEvent -= Modify; Physics.ContactModifyEventCCD -= Modify;
        Physics.ContactModifyEvent += Modify; Physics.ContactModifyEventCCD += Modify;
        contactsSubscribed = true;
    }
    private void OnEnable()
    {
        ownerScene = gameObject.scene; worlds[ownerScene] = this; SubscribeContacts();
    }
    public static void Ensure(Scene scene)
    {
        SubscribeContacts();
        if (worlds.TryGetValue(scene, out var world) && world != null)
        { if (!world.gameObject.activeSelf) world.gameObject.SetActive(true); return; }
        // Reuse the scene's publisher after a live script reload, preserving the workspace.
        foreach (var root in scene.GetRootGameObjects())
            if (root.TryGetComponent<ConveyorPhysicsWorld>(out var existing))
            { worlds[scene] = existing; if (!root.activeSelf) root.SetActive(true); return; }
        var go = new GameObject("Conveyor physics world"); SceneManager.MoveGameObjectToScene(go, scene);
        var instance = go.AddComponent<ConveyorPhysicsWorld>(); instance.ownerScene = scene; worlds[scene] = instance;
        Time.fixedDeltaTime = 1f / 120f;
        Time.maximumDeltaTime = 0.1f;
    }
    public static void Register(ConveyorCollision belt) { if (!belts.Contains(belt)) belts.Add(belt); Ensure(belt.gameObject.scene); }
    public static void Unregister(ConveyorCollision belt) => belts.Remove(belt);
    public static void Publish()
    {
        staging.Clear();
        foreach (var belt in belts) if (belt != null && belt.isActiveAndEnabled) belt.AppendSurfaces(staging);
        Surface[] previous = Volatile.Read(ref published);
        bool changed = previous.Length != staging.Count;
        for (int i = 0; !changed && i < staging.Count; i++) changed = !staging[i].SameAs(previous[i]);
        // Reuse unchanged immutable snapshots; the callback can still read old ones safely.
        if (changed) Volatile.Write(ref published, staging.ToArray());
    }
    private void FixedUpdate() => Publish();
    private void OnDestroy() { if (worlds.TryGetValue(ownerScene, out var current) && current == this) worlds.Remove(ownerScene); }
    private static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
    {
        Surface[] data = Volatile.Read(ref published);
        for (int p = 0; p < pairs.Length; p++)
        {
            var pair = pairs[p]; int a = -1, b = -1;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].collider.Equals(pair.colliderEntityId)) a = i;
                if (data[i].collider.Equals(pair.otherColliderEntityId)) b = i;
            }
            if (a < 0 && b < 0) continue;
            for (int i = 0; i < pair.contactCount; i++)
            {
                Vector3 point = pair.GetPoint(i);
                Vector3 first = a >= 0 ? data[a].VelocityAt(point) : Vector3.zero;
                Vector3 second = b >= 0 ? data[b].VelocityAt(point) : Vector3.zero;
                pair.SetTargetVelocity(i, Vector3.ProjectOnPlane(second - first, pair.GetNormal(i)));
            }
        }
    }
}
