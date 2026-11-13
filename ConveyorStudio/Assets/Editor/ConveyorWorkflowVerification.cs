using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorWorkflowVerification
{
    private static Scene scene;
    private static ConveyorPlacementController app;
    private static PlacedConveyor upstream, first, second, downstream, otherHead, otherTail;
    private static GameObject connector;
    private static Pose slot;
    private static int stage;
    private static double next;
    private static readonly List<string> checks = new List<string>();
    [Serializable] private class Report { public bool success; public string error; public string[] checks; }
    static ConveyorWorkflowVerification() { EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Cleanup(); }; }
    [MenuItem("Tools/Conveyor Studio/Verify Group Workflow %#&F9")]
    public static void Verify()
    {
        if (!EditorApplication.isPlaying) return;
        Cleanup(); checks.Clear(); stage = 0;
        try
        {
            scene = SceneManager.CreateScene("Group workflow verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = new Vector3(1400, 0, 0);
            app = root.GetComponent<ConveyorPlacementController>(); app.enabled = false;
            root.GetComponent<ConveyorStudioHud>().enabled = false; app.orbitCamera.enabled = false;
            foreach (var camera in root.GetComponentsInChildren<Camera>()) camera.enabled = false;
            foreach (var light in root.GetComponentsInChildren<Light>()) light.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            next = EditorApplication.timeSinceStartup + 0.1; EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool value, string name) { if (!value) throw new Exception(name + " | " + app.Status + " | " + app.LastCollision); checks.Add(name); }
    private static PlacedConveyor Add(ConveyorKind kind, Vector3 position, PlacedConveyor after = null)
    {
        var model = app.BeginAdd(kind);
        if (after != null) position = after.Section.Exit.position - model.Section.Entry.localPosition;
        app.MovePreview(new Pose(position, Quaternion.identity), after != null, true);
        Check(app.PreviewIsValid && app.TryCommit(), kind + " placed in the workflow fixture"); return model;
    }
    private static bool Green(PlacedConveyor machine)
    { var outline = machine.GetComponent<ConveyorSilhouette>(); return outline.Highlighted && outline.Colour.g > 0.9f && outline.Colour.r < 0.5f; }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (stage == 0)
            {
                foreach (string name in new[] { "ConveyorStudioIcon", "BoxIcon", "CanIcon" })
                    Check(Resources.Load<Sprite>("Studio/" + name) != null, name + " is available as a runtime interface sprite");
                upstream = Add(ConveyorKind.Incline, new Vector3(1400, 0, 0));
                first = Add(ConveyorKind.Incline, Vector3.zero, upstream);
                second = Add(ConveyorKind.Short, Vector3.zero, first);
                downstream = Add(ConveyorKind.Generic, Vector3.zero, second);
                app.Select(first); app.Select(second, true);
                slot = new Pose(second.transform.position, second.transform.rotation);
                connector = first.transform.Find("Powered transfer rollers").gameObject;
                Check(connector.activeSelf, "The connected selected pair initially has a visible transfer connector");
                Check(app.BeginMove(second), "The elevated selected pair can be picked up as one group");
                Check(first.Section.Next == second.Section && connector.activeSelf && !connector.GetComponent<Collider>().enabled
                    && upstream.Section.Next == null && downstream.Section.Previous == null,
                    "Picking up a group retains internal links and connector visuals while disabling their physics and detaching outside joins");
                app.MovePreview(new Pose(slot.position + Vector3.right * 0.65f, slot.rotation), true, false, 1f / 60f);
                Check(app.SnapPartner == null && connector.activeSelf, "Pulling a connected group away remains free and keeps its connector visible");
                for (int i = 0; i < 20; i++) app.MovePreview(new Pose(slot.position + Vector3.right * 0.16f, slot.rotation), true, false, 1f / 60f);
                Check(app.PreviewIsValid && app.SnapPartner != null && app.EntrySnapPartner == upstream && app.ExitSnapPartner == downstream
                    && Green(upstream) && Green(downstream), "Group magnetism recognises both sides of an elevated gap and highlights both stationary neighbours");
                Check(connector.activeSelf && first.Section.Next == second.Section && first.transform.Find("Powered transfer rollers").gameObject == connector,
                    "The same internal connector survives the magnetic approach without disappearing or rebuilding");
                Check(app.TryCommit(), "The magnetically aligned group can be released into its gap");
                stage = 1; next = EditorApplication.timeSinceStartup + 0.7;
            }
            else if (stage == 1)
            {
                Check(!first.IsEditing && !second.IsEditing && upstream.Section.Next == first.Section && first.Section.Next == second.Section
                    && second.Section.Next == downstream.Section && Vector3.Distance(second.transform.position, slot.position) < 0.0002f,
                    "Group settling restores the exact elevated layout and both external connections");
                Check(connector.activeSelf && connector.GetComponent<Collider>().enabled, "The retained internal connector resumes its physical transfer surface after release");
                app.Select(first); app.Select(second, true); app.CompleteSelectionGesture(first, false, false);
                Check(app.SelectedMachines.Count == 1 && app.Selected == first, "A plain click on one group member collapses selection to that machine");
                app.Select(second, true); app.CompleteSelectionGesture(first, false, true);
                Check(app.SelectedMachines.Count == 2, "A drag preserves the selected group instead of collapsing it on release");
                for (int number = 1; number <= 3; number++)
                {
                    app.Select(first); var preview = app.BeginAddShortcut(number);
                    Check(app.IsAdding && preview.Definition.kind == (number == 1 ? ConveyorKind.Generic : number == 2 ? ConveyorKind.Short : ConveyorKind.Incline)
                        && app.Machines.Count == 4, "Shortcut " + number + " starts its catalogue preview even when a machine was selected");
                    app.CancelPreview();
                }
                otherHead = Add(ConveyorKind.Generic, new Vector3(1408, 0, 0)); otherTail = Add(ConveyorKind.Short, Vector3.zero, otherHead);
                app.Select(first); app.Select(second, true); app.BeginMove(second);
                Vector3 rejected = otherHead.Section.Entry.position - second.Section.Exit.localPosition; rejected.y = slot.position.y;
                for (int i = 0; i < 12; i++) app.MovePreview(new Pose(rejected, Quaternion.identity), true, false, 1f / 60f);
                Check(app.SnapPartner == null && first.transform.position.y >= 0f && second.transform.position.y >= 0f,
                    "A group cannot snap a lower member through the floor to meet a ground-level port");
                app.CancelPreview(); stage = 2; next = EditorApplication.timeSinceStartup + 0.7;
            }
            else
            {
                app.Select(downstream);
                var heads = app.ProductInfeeds();
                Check(heads.Count == 2 && heads.Contains(upstream) && heads.Contains(otherHead), "Each disconnected conveyor chain exposes only its most preceding belt as a product infeed");
                var targetedBoxes = app.SpawnProducts(ProductKind.Box);
                Check(targetedBoxes.Count == 1 && Vector3.Distance(targetedBoxes[0].transform.position,
                    downstream.GetComponent<ConveyorCollision>().InfeedPose(targetedBoxes[0].size.z).position) < 0.001f,
                    "Selecting a non-first belt overrides automatic line-head spawning for boxes");
                targetedBoxes[0].Body.position += Vector3.up * 3f; Physics.SyncTransforms();
                var targetedCans = app.SpawnProducts(ProductKind.Can);
                Check(targetedCans.Count == 1 && Vector3.Distance(targetedCans[0].transform.position,
                    downstream.GetComponent<ConveyorCollision>().InfeedPose(targetedCans[0].size.z).position) < 0.001f,
                    "Cans also spawn on the specifically selected non-first belt");
                targetedCans[0].Body.position += Vector3.up * 3f; Physics.SyncTransforms();
                app.Select(first); app.Select(second, true);
                var selectedBoxes = app.SpawnProducts(ProductKind.Box);
                Check(selectedBoxes.Count == 2 && Vector3.Distance(selectedBoxes[0].transform.position,
                    first.GetComponent<ConveyorCollision>().InfeedPose(selectedBoxes[0].size.z).position) < 0.001f
                    && Vector3.Distance(selectedBoxes[1].transform.position,
                    second.GetComponent<ConveyorCollision>().InfeedPose(selectedBoxes[1].size.z).position) < 0.001f,
                    "Multiple selected belts each receive a product at their own infeed");
                foreach (var product in selectedBoxes) product.Body.position += Vector3.up * 3f; Physics.SyncTransforms();
                app.Select(null);
                var boxes = app.SpawnProducts(ProductKind.Box);
                Check(boxes.Count == 2, "With no selection one box command feeds both independent line starts");
                var expectedA = upstream.GetComponent<ConveyorCollision>().InfeedPose(boxes[0].size.z).position;
                var expectedB = otherHead.GetComponent<ConveyorCollision>().InfeedPose(boxes[1].size.z).position;
                Check(Vector3.Distance(boxes[0].transform.position, expectedA) < 0.001f && Vector3.Distance(boxes[1].transform.position, expectedB) < 0.001f,
                    "Automatic products spawn at the two true line starts when selection is cleared");
                Check(app.SpawnProducts(ProductKind.Box).Count == 0, "Occupied line infeeds reject overlapping product spawns");
                foreach (var product in boxes) product.Body.position += Vector3.up * 3f; Physics.SyncTransforms();
                app.Select(null); var cans = app.SpawnProducts(ProductKind.Can);
                Check(cans.Count == 2 && app.CanSpawnProducts, "Can icons feed every line without requiring a machine selection");
                cans[0].Body.position += Vector3.up * 3f; Physics.SyncTransforms();
                Check(app.SpawnProducts(ProductKind.Box).Count == 1, "One blocked infeed does not prevent spawning on another clear line");
                Finish(true, null);
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        File.WriteAllText("Library/ConveyorWorkflowRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Group workflow verification passed: " + checks.Count + " checks.");
        else Debug.LogError("Group workflow verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        app = null;
    }
}
