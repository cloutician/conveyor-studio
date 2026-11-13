using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorPlacementVerification
{
    private const string Key = "ConveyorStudio.PlacementProbe.v1";
    private static ConveyorPlacementController app;
    private static GameObject instance;
    private static PlacedConveyor a, b, c, d, e;
    private static Pose bPose;
    private static Vector3 invalidPosition;
    private static int stage;
    private static double sampleAt;
    private static readonly List<string> checks = new List<string>();
    private static readonly List<GameObject> hiddenRoots = new List<GameObject>();
    [Serializable] private sealed class Report { public bool success; public string error; public string[] checks; }

    static ConveyorPlacementVerification() => EditorApplication.playModeStateChanged += OnPlayMode;
    [MenuItem("Tools/Conveyor Studio/Verify Placement on Next Play %#F9")]
    public static void Arm() { SessionState.SetBool(Key, true); Debug.Log("Placement verification armed for the next Play session."); }
    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode) { Cleanup(); return; }
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false); checks.Clear(); stage = 0;
        try
        {
            // Temporary Play-only isolation leaves the user's editing scene untouched.
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.activeSelf) { hiddenRoots.Add(root); root.SetActive(false); }
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            instance.name = "Placement verification workspace"; instance.hideFlags = HideFlags.DontSave;
            app = instance.GetComponent<ConveyorPlacementController>();
            sampleAt = EditorApplication.timeSinceStartup + 0.3;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message + (app != null ? " | " + app.Status + " | " + app.LastCollision : ""));
        checks.Add(message);
    }
    private static PlacedConveyor Add(ConveyorKind kind, Vector3 position, float yaw, bool snap = false)
    {
        var machine = app.BeginAdd(kind);
        app.MovePreview(new Pose(position, Quaternion.Euler(0, yaw, 0)), snap, true);
        Check(app.PreviewIsValid && app.TryCommit(), kind + " placement accepted");
        return machine;
    }
    private static Vector3 After(PlacedConveyor upstream, ConveyorKind kind)
    {
        var prototype = AssetDatabase.LoadAssetAtPath<GameObject>(kind == ConveyorKind.Short ? ConveyorModuleBuilder.ShortPath : kind == ConveyorKind.Incline ? ConveyorModuleBuilder.InclinePath : ConveyorModuleBuilder.GenericPath);
        return upstream.Section.Exit.position - prototype.GetComponent<ConveyorSection>().Entry.localPosition;
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < sampleAt || !EditorApplication.isPlaying) return;
        try
        {
            if (stage == 0)
            {
                a = Add(ConveyorKind.Generic, Vector3.zero, 0);
                b = Add(ConveyorKind.Short, new Vector3(3, 0, 0), 90);
                app.BeginAdd(ConveyorKind.Generic); app.MovePreview(new Pose(Vector3.zero, Quaternion.identity), false);
                Check(!app.PreviewIsValid && !app.TryCommit(), "Overlapping placement rejected"); app.CancelPreview();
                app.BeginAdd(ConveyorKind.Short); app.MovePreview(new Pose(new Vector3(-3, -0.5f, 0), Quaternion.Euler(25, 42, 17)), false);
                Check(!app.PreviewIsValid && app.PreviewMachine.transform.position.y >= 0f, "Below-floor placement rejected and preview clamped above ground");
                Check(Vector3.Dot(app.PreviewMachine.transform.up, Vector3.up) > 0.99999f && Mathf.Abs(app.PreviewMachine.transform.eulerAngles.y) < 0.001f, "Pitch and roll removed; yaw quantized to 90 degrees"); app.CancelPreview();
                app.BeginAdd(ConveyorKind.Incline);
                app.MovePreview(new Pose(a.Section.Entry.position - new Vector3(0, 0.546743f, 1.037499f), Quaternion.identity), true, true);
                Check(!app.PreviewIsValid && app.PreviewMachine.transform.position.y >= 0, "Snapping cannot pull an incline output below the floor"); app.CancelPreview();
                c = Add(ConveyorKind.Short, After(a, ConveyorKind.Short) + Vector3.forward * 0.06f, 0, true);
                Check(a.Section.Next == c.Section && c.Section.Previous == a.Section, "Magnetic end-to-end snap records reciprocal connection");
                d = Add(ConveyorKind.Incline, After(c, ConveyorKind.Incline) + Vector3.forward * 0.06f, 0, true);
                e = Add(ConveyorKind.Generic, new Vector3(After(d, ConveyorKind.Generic).x, 0, After(d, ConveyorKind.Generic).z + 0.06f), 0, true);
                Check(e.transform.position.y > 0.79f && d.Section.Next == e.Section, "Elevated port found from horizontal cursor distance");
                Check(MinimumFootY(e) > -0.0002f && MinimumFootY(e) < 0.0002f, "Elevated machine supports extend to the floor");
                Check(app.Machines.Count == 5, "More than three machines supported");
                app.Select(b); app.Rotate(1);
                Check(Mathf.Abs(Mathf.DeltaAngle(b.transform.eulerAngles.y, 180)) < 0.001f, "Rotate control advances exactly 90 degrees");
                Check(Mathf.Abs(MinimumFootY(b)) < 0.0002f, "Rotating keeps feet on the floor");
                bPose = new Pose(b.transform.position, b.transform.rotation);
                app.BeginMove(b); app.MovePreview(new Pose(Vector3.zero, b.transform.rotation), false);
                Check(!app.PreviewIsValid, "Existing machine overlap rejected");
                invalidPosition = b.transform.position; app.ReleasePreview();
                Check(b.IsReturning && Vector3.Distance(b.transform.position, bPose.position) > 1f, "Invalid release begins a return instead of teleporting");
                stage = 1; sampleAt = EditorApplication.timeSinceStartup + 0.07;
            }
            else if (stage == 1)
            {
                Check(Vector3.Distance(b.transform.position, invalidPosition) > 0.0001f && Vector3.Distance(b.transform.position, bPose.position) > 0.0001f, "Return visibly passes through intermediate positions");
                stage = 2; sampleAt = EditorApplication.timeSinceStartup + 0.7;
            }
            else if (stage == 2)
            {
                Check(!b.IsReturning && !b.IsEditing && Vector3.Distance(b.transform.position, bPose.position) < 0.00001f, "Smooth return restores the exact last valid pose and picking");
                app.Select(a); app.RemoveSelected();
                Check(b.UnitId == "B" && c.UnitId == "C" && d.UnitId == "D" && e.UnitId == "E", "Removal preserves surviving machine names");
                Check(c.Section.Previous == null && c.Section.Next == d.Section, "Removing a machine releases only its own connections");
                Check(ConveyorPlacementController.Label(26) == "AA", "Unit labels continue beyond Z");
                // This is deliberately a layout beside the line, with no side-transfer connection.
                var f = Add(ConveyorKind.Short, new Vector3(-3, 0, 0), 90);
                Check(f.UnitId == "F" && f.Section.Next == null && f.Section.Previous == null, "Perpendicular free placement remains unconnected and names are not reused");
                app.Select(c); app.AddParcel(); app.orbitCamera.FrameAll(app.Machines);
                Finish(true, null);
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static float MinimumFootY(PlacedConveyor machine)
    {
        float min = float.PositiveInfinity;
        foreach (var filter in machine.GetComponentsInChildren<MeshFilter>())
            if (filter.name.Contains("Legs")) foreach (var vertex in filter.sharedMesh.vertices) min = Mathf.Min(min, filter.transform.TransformPoint(vertex).y);
        return min;
    }
    private static void Finish(bool success, string error)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText("Library/ConveyorPlacementRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Placement verification passed: " + checks.Count + " checks. The temporary workspace remains available until you stop Play.");
        else Debug.LogError("Placement verification failed: " + error);
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (instance != null) Object.DestroyImmediate(instance);
        foreach (var root in hiddenRoots) if (root != null) root.SetActive(true);
        hiddenRoots.Clear(); app = null;
    }
}
