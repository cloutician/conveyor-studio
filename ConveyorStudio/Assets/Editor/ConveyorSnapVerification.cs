using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorSnapVerification
{
    private const string Key = "ConveyorStudio.SoftSnapProbe.v1";
    private const string ReportPath = "Library/ConveyorSnapRuntimeReport.json";
    private static ConveyorPlacementController app;
    private static GameObject instance;
    private static Scene scene;
    private static readonly Vector3 Origin = new Vector3(1000, 0, 0);
    private static PlacedConveyor anchor, moving;
    private static Vector3 goal, settlingStart;
    private static int stage;
    private static double sampleAt;
    private static readonly List<string> checks = new List<string>();
    private static readonly List<float> approachErrors = new List<float>();
    [Serializable] private sealed class Report { public bool success; public string error; public string[] checks; public float[] approachErrorsMetres; }
    static ConveyorSnapVerification()
    {
        if (!File.Exists(ReportPath)) SessionState.SetBool(Key, true);
        EditorApplication.playModeStateChanged += OnPlayMode;
    }
    [MenuItem("Tools/Conveyor Studio/Verify Soft Snapping %#F10")]
    public static void Arm()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Soft snap verification armed for the next Play session."); }
    }
    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) { Cleanup(); return; }
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false); Begin();
    }
    private static void Begin()
    {
        Cleanup(); checks.Clear(); approachErrors.Clear(); stage = 0;
        try
        {
            scene = SceneManager.CreateScene("Soft snap verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            SceneManager.MoveGameObjectToScene(instance, scene); instance.transform.position = Origin;
            instance.name = "Soft snap verification workspace"; instance.hideFlags = HideFlags.DontSave;
            app = instance.GetComponent<ConveyorPlacementController>();
            instance.GetComponent<ConveyorStudioHud>().enabled = false;
            foreach (var camera in instance.GetComponentsInChildren<Camera>()) camera.enabled = false;
            foreach (var listener in instance.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>()) light.enabled = false;
            app.orbitCamera.enabled = false;
            // Isolate the production motion calls from live mouse input during checks.
            // Disabling Update leaves its settling/return coroutines running normally.
            app.enabled = false;
            sampleAt = EditorApplication.timeSinceStartup + 0.15;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception(text + " | " + app.Status);
        checks.Add(text);
    }
    private static Pose Near(float distance) => new Pose(goal + Vector3.forward * distance, Quaternion.identity);
    private static void Step(float distance, int frames = 1, float dt = 1f / 60f)
    { for (int i = 0; i < frames; i++) app.MovePreview(Near(distance), true, false, dt); }
    private static void NewPreview() { app.CancelPreview(); moving = app.BeginAdd(ConveyorKind.Short); }
    private static float Error => Vector3.Distance(moving.transform.position, goal);
    private static float ErrorAtRate(int hz)
    {
        NewPreview(); Step(0.08f, Mathf.RoundToInt(hz * 0.4f), 1f / hz);
        return Error;
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < sampleAt) return;
        try
        {
            if (stage == 0)
            {
                anchor = app.BeginAdd(ConveyorKind.Generic);
                app.MovePreview(new Pose(Origin, Quaternion.identity), false, true);
                Check(app.TryCommit(), "Placed an anchor using the production controller");
                var shortPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorModuleBuilder.ShortPath);
                goal = anchor.Section.Exit.position - shortPrefab.GetComponent<ConveyorSection>().Entry.localPosition;
                NewPreview(); Step(0.23f, 20);
                Check(app.SnapPartner == null && Vector3.Distance(moving.transform.position, Near(0.23f).position) < 0.0001f,
                    "Twenty-three centimetres away is a guide only, with no magnetic pull");
                Step(0.16f);
                Check(app.SnapPartner == null, "Brief proximity does not immediately capture a connector");
                Step(0.16f, 2);
                Check(app.SnapPartner == anchor && Error > 0.08f && Error < 0.16f, "Capture at sixteen centimetres starts with an eased movement, not a teleport");
                float previous = Error;
                bool monotonic = true;
                for (int i = 0; i < 32; i++)
                {
                    Step(0.16f); approachErrors.Add(Error);
                    monotonic &= Error <= previous + 0.000001f && moving.transform.position.z >= goal.z - 0.00001f;
                    previous = Error;
                }
                Check(monotonic && Error < 0.001f, "Approach converges smoothly without overshooting");
                float drift = 0f; bool retained = true;
                for (int i = 0; i < 60; i++)
                {
                    Step(0.235f + Mathf.Sin(i * 0.5f) * 0.006f);
                    drift = Mathf.Max(drift, Error); retained &= app.SnapPartner == anchor;
                }
                Check(retained && drift < 0.001f, "Pointer jitter across the capture boundary cannot flicker or steal the held snap");
                Step(0.285f);
                Check(app.SnapPartner == null && Error > 0.0001f && Error < 0.12f, "Pulling away releases promptly and eases back toward the pointer");
                Step(0.285f, 28);
                Check(Vector3.Distance(moving.transform.position, Near(0.285f).position) < 0.001f, "After release the machine tracks the pointer freely");
                Step(0.08f); bool swept = app.SnapPartner == null;
                Step(0f); swept &= app.SnapPartner == null;
                Step(-0.08f); swept &= app.SnapPartner == null;
                Check(swept, "A fast pass over a connector does not capture it");
                float sixty = ErrorAtRate(60), thirty = ErrorAtRate(30);
                Check(Mathf.Abs(sixty - thirty) < 0.001f, "Thirty and sixty frames per second settle to within one millimetre at equal elapsed time");
                NewPreview(); Step(0.08f, 4);
                Check(Error > 0.02f && app.TryCommit() && moving.IsSettling && Error > 0.001f,
                    "Release accepts a valid connection while animating its remaining travel");
                Check(anchor.Section.Next == null, "Product transfer waits until the connector is physically aligned");
                settlingStart = moving.transform.position;
                var obstruction = app.BeginAdd(ConveyorKind.Short);
                app.MovePreview(new Pose(goal, Quaternion.identity), false, true);
                Check(!app.PreviewIsValid, "A settling machine reserves its destination against other placements"); app.CancelPreview();
                app.BeginAdd(ConveyorKind.Short); app.MovePreview(new Pose(Origin + new Vector3(4, 0, 0), Quaternion.identity), false, true);
                Check(app.PreviewIsValid, "Other placement remains available while a connection settles"); app.CancelPreview();
                stage = 1; sampleAt = EditorApplication.timeSinceStartup + 0.06;
            }
            else if (stage == 1)
            {
                Check(Vector3.Distance(moving.transform.position, settlingStart) > 0.00001f && Error > 0.00005f,
                    "Release animation visibly passes through intermediate positions");
                stage = 2; sampleAt = EditorApplication.timeSinceStartup + 0.55;
            }
            else if (stage == 2)
            {
                Check(!moving.IsSettling && !moving.IsEditing && Error < 0.00001f
                    && anchor.Section.Next == moving.Section && moving.Section.Previous == anchor.Section,
                    "Settling ends at the exact pose with reciprocal links and restored motor/picking");
                app.BeginMove(moving); Step(0.06f, 20);
                Check(app.SnapPartner == null && Vector3.Distance(moving.transform.position, Near(0.06f).position) < 0.0001f,
                    "Picking up a connected machine does not glue it back onto the old connector");
                Step(0.035f, 4);
                Check(app.SnapPartner == anchor, "Returning deliberately towards the old join re-engages its magnet without a large detour");
                Step(0.29f, 10); Step(0.06f, 8);
                Check(app.SnapPartner == anchor, "The old connector becomes available again after deliberately moving clear");
                app.CancelPreview(); stage = 3; sampleAt = EditorApplication.timeSinceStartup + 0.55;
            }
            else if (stage == 3)
            {
                Check(!moving.IsReturning && Error < 0.00001f && anchor.Section.Next == moving.Section, "Cancelling restores the original connected placement");
                app.BeginMove(moving); Step(0.29f, 12); Step(0.06f, 8);
                Check(app.SnapPartner == anchor, "The connector can be approached again after detachment");
                Step(0.285f);
                Check(app.SnapPartner == null && app.PreviewIsValid && app.TryCommit() && moving.IsSettling,
                    "An immediate release after pulling away commits the pointer position instead of sticking near the old snap");
                stage = 4; sampleAt = EditorApplication.timeSinceStartup + 0.55;
            }
            else
            {
                Check(!moving.IsSettling && Vector3.Distance(moving.transform.position, Near(0.285f).position) < 0.00001f
                    && moving.Section.Previous == null && anchor.Section.Next == null,
                    "Quick pull-away finishes at the intended free position with no stale connection");
                app.Select(moving); app.orbitCamera.FrameAll(app.Machines); app.enabled = true;
                Finish(true, null);
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray(), approachErrorsMetres = approachErrors.ToArray() }, true));
        if (app != null) app.enabled = true;
        if (success) Debug.Log("Soft snapping verification passed: " + checks.Count + " motion, release and connection checks.");
        else Debug.LogError("Soft snapping verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        app = null; instance = null;
    }
}
