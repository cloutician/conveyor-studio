using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class ConveyorPlacementController
{
    private readonly Collider[] infeedClearance = new Collider[128];
    public bool CanSpawnProducts
    {
        get
        {
            var candidates = selection.Count > 0 ? (IReadOnlyList<PlacedConveyor>)selection : machines;
            foreach (var machine in candidates) if (UsableInfeed(machine)) return true;
            return false;
        }
    }
    private static bool UsableInfeed(PlacedConveyor machine) => machine != null && !machine.IsEditing && !machine.IsReturning && !machine.IsSettling;
    public List<PlacedConveyor> ProductInfeeds()
    {
        var heads = new List<PlacedConveyor>();
        foreach (var machine in machines)
        {
            if (!UsableInfeed(machine)) continue;
            var previous = machine.Section.Previous;
            if (previous != null && previous.TryGetNext(out var next) && next == machine.Section
                && UsableInfeed(previous.GetComponent<PlacedConveyor>())) continue;
            heads.Add(machine);
        }
        return heads;
    }
    public List<PhysicsProduct> SpawnProducts(ProductKind kind)
    {
        var added = new List<PhysicsProduct>();
        GameObject prefab = kind == ProductKind.Box ? catalog.productPrefab : catalog.canPrefab;
        if (prefab == null) return added;
        var definition = prefab.GetComponent<PhysicsProduct>(); if (definition == null) return added;
        int blocked = 0;
        var targets = selection.Count > 0 ? new List<PlacedConveyor>(selection) : ProductInfeeds();
        foreach (var machine in targets)
        {
            if (!UsableInfeed(machine)) continue;
            Pose pose = machine.GetComponent<ConveyorCollision>().InfeedPose(definition.size.z);
            Vector3 centre = pose.position + pose.rotation * Vector3.up * definition.size.y * 0.5f;
            int count = gameObject.scene.GetPhysicsScene().OverlapBox(centre, definition.size * 0.48f, infeedClearance, pose.rotation, ~0, QueryTriggerInteraction.Ignore);
            bool clear = true;
            for (int i = 0; i < count; i++) if (infeedClearance[i].GetComponentInParent<PhysicsProduct>() != null) { clear = false; break; }
            if (!clear) { blocked++; continue; }
            var product = Instantiate(prefab, pose.position, pose.rotation, machinesRoot).GetComponent<PhysicsProduct>();
            product.Body.linearVelocity = machine.Section.BeltSurface.TopTravelDirection * machine.Section.BeltSurface.EffectiveSpeed;
            added.Add(product); Physics.SyncTransforms();
        }
        string productName = kind == ProductKind.Box ? (added.Count == 1 ? "box" : "boxes") : (added.Count == 1 ? "can" : "cans");
        status = added.Count > 0 ? added.Count + " " + productName + (selection.Count > 0 ? " added to selected machines." : " added at line starts.")
            : blocked > 0 ? "Let the line infeeds clear before adding another product." : "Add a conveyor before adding products.";
        if (added.Count > 0 && blocked > 0) status += blocked == 1 ? " One infeed is still blocked." : " " + blocked + " infeeds are still blocked.";
        ClearUiFocus(); return added;
    }
}
