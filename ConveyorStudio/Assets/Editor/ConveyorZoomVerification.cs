using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorZoomVerification
{
    private const string Key = "ConveyorStudio.ZoomProbe.v1";
    private static readonly Vector3 Origin = new Vector3(1000, 0, 0);
    private static readonly List<string> checks = new List<string>();
    private static Scene scene;
    private static ConveyorPlacementController app;
    private static PlacedConveyor shortModel;
    private static double runAt;
    private static ConveyorOrbitCamera Orbit => app.orbitCamera;
    private static Camera Camera => app.sceneCamera;
    [Serializable] private sealed class Report { public bool success; public string error; public string[] checks; }
    static ConveyorZoomVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Trackpad Zoom %#&F5")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Trackpad zoom verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        Cleanup(); checks.Clear();
        try
        {
            scene = SceneManager.CreateScene("Trackpad zoom verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            SceneManager.MoveGameObjectToScene(instance, scene); instance.transform.position = Origin;
            instance.name = "Temporary zoom verification workspace"; instance.hideFlags = HideFlags.DontSave;
            app = instance.GetComponent<ConveyorPlacementController>(); app.enabled = false;
            instance.GetComponent<ConveyorStudioHud>().enabled = false;
            foreach (var camera in instance.GetComponentsInChildren<Camera>()) camera.enabled = false;
            foreach (var listener in instance.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>()) light.enabled = false;
            Orbit.enabled = false;
            Camera.pixelRect = new Rect(0, 0, 1200, 700); Camera.aspect = 1200f / 700f;
            app.WorldScreenRect = Camera.pixelRect;
            shortModel = app.BeginAdd(ConveyorKind.Short);
            app.MovePreview(new Pose(Origin, Quaternion.identity), false, true);
            Check(app.TryCommit(), "A real Short conveyor is placed in an isolated zoom workspace");
            runAt = EditorApplication.timeSinceStartup + 0.15;
            EditorApplication.update += Run;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool pass, string text)
    { if (!pass) throw new Exception(text); checks.Add(text); }
    private static float Read(string field) => (float)typeof(ConveyorOrbitCamera).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Orbit);
    private static Vector3 ReadVector(string field) => (Vector3)typeof(ConveyorOrbitCamera).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Orbit);
    private static void Set(string field, float value) => typeof(ConveyorOrbitCamera).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Orbit, value);
    private static bool AboveFloor()
    {
        float floorY = app.transform.position.y;
        if (Camera.transform.position.y < floorY + 0.119f) return false;
        foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
            if (Camera.ViewportToWorldPoint(new Vector3(corner.x, corner.y, Camera.nearClipPlane)).y < floorY + 0.019f) return false;
        return true;
    }
    private static void VerifyFloorNavigation()
    {
        for (int i = 0; i < 20; i++)
        {
            app.BeginAdd(ConveyorKind.Generic);
            app.MovePreview(new Pose(Origin + new Vector3(i * 3f, 0, 10), Quaternion.identity), false, true);
            Check(app.TryCommit(), "The large floor-safety layout accepts machine " + (i + 1));
        }
        Frame(); float largeDistance = Mathf.Max(30f, Read("distance"));
        Set("pitch", 42f); Set("distance", largeDistance);
        Orbit.Focus(Origin - Vector3.up * (largeDistance * 0.25f + 0.3f));
        Check(ReadVector("focus").y < -5f && Camera.transform.position.y > 1f,
            "A distant, below-ground pivot reproduces the large-layout navigation conditions");
        Orbit.NavigatePointer(Centre, new Vector2(0, 5000), true, false);
        Check(AboveFloor() && Read("pitch") == 5f,
            "Orbiting a large layout down to its lowest angle cannot put the camera or near plane through the floor");
        Orbit.NavigatePointer(Centre, Vector2.zero, false, false);
        bool safe = true;
        for (int i = 0; i < 12; i++)
        {
            Orbit.NavigatePointer(Centre, new Vector2(20000f * (i % 2 == 0 ? 1 : -1), 14000f), false, true);
            safe &= AboveFloor();
        }
        Orbit.NavigatePointer(Centre, Vector2.zero, false, false);
        Check(safe, "Very large pans remain above floor level even beyond the physical floor collider");
        float originalNear = Camera.nearClipPlane; Camera.nearClipPlane = 0.4f;
        Orbit.Focus(Origin - Vector3.up * largeDistance);
        Check(AboveFloor(), "Floor clearance protects all near-plane corners when the clipping distance is enlarged");
        Camera.nearClipPlane = originalNear;
        Set("distance", 1f);
        typeof(ConveyorOrbitCamera).GetField("focus", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Orbit, Origin - Vector3.up * 20f);
        Orbit.enabled = true; Orbit.enabled = false;
        Check(AboveFloor(), "Re-enabling or reloading the camera repairs an inherited underground view immediately");
        Orbit.Reframe(app.Machines); safe = true;
        for (int i = 0; i < 30; i++) { Orbit.AdvanceFocus(1f / 60f); safe &= AboveFloor(); }
        Check(safe && !Orbit.IsFocusing, "Every frame of a large-layout Reframe transition stays above the floor");
        Orbit.ScrollZoom(1000f, Centre);
        for (int i = 0; i < 180; i++) { Orbit.AdvanceZoom(1f / 60f); safe &= AboveFloor(); }
        Check(safe, "Zooming after a constrained orbit or pan cannot pull the repaired camera underground");
    }
    private static void Settle(int fps = 60, float seconds = 0.6f)
    { for (int i = 0; i < Mathf.RoundToInt(fps * seconds); i++) Orbit.AdvanceZoom(1f / fps); }
    private static void Frame() { Orbit.Reframe(app.Machines, false); Physics.SyncTransforms(); }
    private static Vector2 Centre => Camera.ViewportToScreenPoint(new Vector3(0.5f, 0.5f, 1f));
    private static Vector2 EmptyPoint => Camera.ViewportToScreenPoint(new Vector3(0.83f, 0.85f, 1f));
    private static Vector2 VisibleCentre(PlacedConveyor model)
    {
        Vector2 low = Vector2.one, high = Vector2.zero;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = Camera.WorldToViewportPoint(filter.transform.TransformPoint(vertex));
                if (point.z <= Camera.nearClipPlane) continue;
                low = Vector2.Min(low, new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y)));
                high = Vector2.Max(high, new Vector2(Mathf.Clamp01(point.x), Mathf.Clamp01(point.y)));
            }
        }
        return (low + high) * 0.5f;
    }
    private static void Run()
    {
        if (EditorApplication.timeSinceStartup < runAt) return;
        EditorApplication.update -= Run;
        try
        {
            var normalize = typeof(ConveyorOrbitCamera).GetMethod("NormalizedScroll", BindingFlags.Static | BindingFlags.NonPublic);
            float Uniform(float value) => (float)normalize.Invoke(null, new object[] { value, InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms, RuntimePlatform.WindowsPlayer });
            float Native(float value, RuntimePlatform platform) => (float)normalize.Invoke(null, new object[] { value, InputSettings.ScrollDeltaBehavior.KeepPlatformSpecificInputRange, platform });
            Check(Uniform(1f) == 1f && Mathf.Abs(Uniform(0.0075f) - 0.0075f) < 0.000001f,
                "Normalized wheel and very small two-finger deltas retain their full resolution");
            Check(Native(120f, RuntimePlatform.WindowsPlayer) == 1f && Native(1f, RuntimePlatform.OSXPlayer) == 1f
                && Native(1f, RuntimePlatform.LinuxPlayer) == 1f, "Legacy native scroll ranges produce the same zoom across desktop platforms");
            Frame(); Vector3 framed = Camera.transform.position; Quaternion angle = Camera.transform.rotation;
            Orbit.Focus(Origin + Vector3.right * 1.2f); Vector3 unframed = Camera.transform.position;
            Orbit.Reframe(app.Machines);
            Check(Orbit.IsFocusing && Vector3.Distance(unframed, Camera.transform.position) < 0.00001f,
                "Reframe begins a transition without switching the camera position instantly");
            Orbit.AdvanceFocus(1f / 60f);
            Check(Vector3.Distance(unframed, Camera.transform.position) > 0.0001f
                && Vector3.Distance(unframed, Camera.transform.position) < Vector3.Distance(unframed, framed) * 0.03f,
                "Reframe starts with a small eased movement");
            float remaining = Vector3.Distance(Camera.transform.position, framed); bool approaching = true;
            for (int i = 0; i < 24; i++)
            {
                Orbit.AdvanceFocus(1f / 60f);
                float current = Vector3.Distance(Camera.transform.position, framed);
                approaching &= current <= remaining + 0.0001f; remaining = current;
            }
            Check(approaching && !Orbit.IsFocusing && remaining < 0.0001f && Quaternion.Angle(angle, Camera.transform.rotation) < 0.01f,
                "Reframe settles quickly at the fitted view without overshoot or changing the viewing angle");
            Orbit.Focus(Origin + Vector3.right * 1.2f); Orbit.Reframe(app.Machines); Orbit.AdvanceFocus(0.05f);
            Orbit.NavigatePointer(Centre, new Vector2(10, 0), true, false);
            Vector3 interrupted = Camera.transform.position; Orbit.AdvanceFocus(0.05f);
            Check(!Orbit.IsFocusing && Vector3.Distance(interrupted, Camera.transform.position) < 0.00001f,
                "A manual camera gesture immediately interrupts the Reframe transition");
            Orbit.NavigatePointer(Centre, new Vector2(-10, 0), true, false); Orbit.NavigatePointer(Centre, Vector2.zero, false, false);
            Frame(); float start = Read("distance"); Vector2 empty = EmptyPoint;
            Orbit.ScrollZoom(1f, empty);
            Check(Read("distance") == start, "Scroll queues a response without an immediate camera jump");
            Orbit.AdvanceZoom(1f / 60f);
            Check(Read("distance") < start && Read("distance") > start * 0.94f, "Zoom starts promptly with a small first-frame movement");
            float previous = Read("distance"); bool monotonic = true;
            for (int i = 0; i < 30; i++) { Orbit.AdvanceZoom(1f / 60f); float current = Read("distance"); monotonic &= current <= previous + 0.00001f; previous = current; }
            Check(monotonic && Mathf.Abs(Read("distance") / start - Mathf.Exp(-0.14f)) < 0.001f,
                "One wheel step settles at a useful proportional zoom without overshoot");
            Frame(); Orbit.ScrollZoom(0.01f, EmptyPoint); Settle();
            Check(Read("distance") < start && Read("distance") > start * 0.997f, "A tiny trackpad movement produces a precise adjustment rather than a dead zone");
            float fine = Read("distance");
            Frame(); for (int i = 0; i < 100; i++) Orbit.ScrollZoom(0.0001f, EmptyPoint); Settle();
            Check(Mathf.Abs(Read("distance") - fine) < 0.0001f, "Fractional input is not rounded into wheel notches");
            float[] distances = new float[3]; int[] rates = { 30, 60, 120 };
            for (int i = 0; i < rates.Length; i++)
            { Frame(); Orbit.ScrollZoom(1f, EmptyPoint); Settle(rates[i], 0.2f); distances[i] = Read("distance"); }
            Check(Mathf.Max(distances) - Mathf.Min(distances) < 0.0001f, "Zoom response agrees at 30, 60 and 120 frames per second");
            Frame(); Orbit.ScrollZoom(6f, EmptyPoint); Orbit.AdvanceZoom(1f / 60f);
            float beforeReverse = Read("distance"); Orbit.ScrollZoom(-0.5f, EmptyPoint); Orbit.AdvanceZoom(1f / 60f);
            Check(Read("distance") > beforeReverse, "Reversing fingers immediately reverses zoom without queued movement in the old direction");
            Frame(); Orbit.ScrollZoom(2f, EmptyPoint); Settle(); Vector3 rightPointerPosition = Camera.transform.position;
            Frame(); Orbit.ScrollZoom(2f, Camera.ViewportToScreenPoint(new Vector3(0.12f, 0.2f, 1f))); Settle();
            Check(Vector3.Distance(rightPointerPosition, Camera.transform.position) < 0.00001f,
                "The same scroll produces the same camera movement at opposite pointer positions");
            Frame(); Orbit.Focus(Origin + Vector3.right * 0.35f);
            Vector2 modelCentre = VisibleCentre(shortModel); Vector3 beforeGesture = Camera.transform.position;
            Orbit.ScrollZoom(2f, EmptyPoint); Vector3 pivot = ReadVector("zoomAnchor");
            Check(Vector3.Distance(beforeGesture, Camera.transform.position) < 0.00001f,
                "Acquiring the model centre does not snap or reframe a manually positioned camera");
            Check(Vector2.Distance(modelCentre, Camera.WorldToViewportPoint(pivot)) < 0.035f,
                "Zoom centres on the visible conveyor silhouette after manual panning");
            Vector2 projectedBefore = Camera.WorldToScreenPoint(pivot); Settle();
            Vector2 projectedAfter = Camera.WorldToScreenPoint(pivot);
            float drift = Vector2.Distance(projectedBefore, projectedAfter);
            Check(drift < 0.25f,
                "The visible-model centre stays fixed on screen during smooth zoom (drift " + drift.ToString("F3") + " px)");
            Frame(); Orbit.ScrollZoom(0.5f, EmptyPoint); Vector3 gesturePivot = ReadVector("zoomAnchor");
            Orbit.AdvanceZoom(1f / 60f); Orbit.ScrollZoom(0.5f, Centre);
            Check(Vector3.Distance(gesturePivot, ReadVector("zoomAnchor")) < 0.00001f,
                "Moving the pointer during a gesture cannot retarget or pull the scene sideways");
            Frame(); float smallMax = Read("maximumDistance"), smallMin = Read("minimumDistance");
            Check(smallMin >= 0.35f && smallMin < 0.8f && smallMax < 15f && smallMax > start,
                "A single machine has close detail access and a proportionate overview limit");
            Orbit.ScrollZoom(-1000f, EmptyPoint); Settle(60, 2f);
            Check(Mathf.Abs(Read("distance") - smallMax) < 0.005f, "A very large gesture stops at the overview limit without accumulated overscroll");
            float atLimit = Read("distance"); Orbit.ScrollZoom(0.2f, EmptyPoint); Orbit.AdvanceZoom(1f / 60f);
            Check(Read("distance") < atLimit, "A small reversal works immediately at a zoom limit");
            Frame(); Orbit.Focus(Origin + Vector3.right * 20f);
            Orbit.ScrollZoom(1000f, EmptyPoint);
            Check(Vector3.Distance(ReadVector("zoomAnchor"), ReadVector("focus")) < 0.00001f,
                "An empty panned view keeps its existing centre instead of jumping towards off-screen machines");
            float beforeStall = Read("distance"); Orbit.AdvanceZoom(1f);
            Check(Read("distance") >= beforeStall * Mathf.Exp(-0.251f), "A long frame stall cannot cause a large zoom jump");
            Settle(60, 2f);
            Check(Mathf.Abs(Read("distance") - smallMin) < 0.005f, "Close-up limit is reachable with a safe clear line of sight");
            Frame(); Vector3 beforeOutside = Camera.transform.position;
            Orbit.ScrollZoom(5f, new Vector2(-20, -20)); Settle();
            Check(Vector3.Distance(beforeOutside, Camera.transform.position) < 0.00001f, "Scrolling outside the world viewport does not move the camera");
            Frame(); Set("distance", smallMax * 3f); Orbit.Focus(Origin);
            float oldFar = Read("distance"); Orbit.ScrollZoom(-1f, EmptyPoint); Settle();
            Check(Mathf.Abs(Read("distance") - oldFar) < 0.0001f, "A view inherited beyond a new limit never zooms in when asked to zoom out");
            Orbit.ScrollZoom(1f, EmptyPoint); Settle();
            Check(Read("distance") < oldFar, "An inherited distant view can return smoothly into the useful range");
            Frame(); Vector3 from = Camera.transform.position;
            Vector3 direction = Camera.transform.forward;
            var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(obstruction, scene); obstruction.transform.position = from + direction * 0.5f;
            obstruction.transform.rotation = Camera.transform.rotation; obstruction.transform.localScale = new Vector3(2f, 2f, 0.1f);
            obstruction.GetComponent<Renderer>().enabled = false; Physics.SyncTransforms();
            Orbit.ScrollZoom(100f, Centre); Settle(60, 2f);
            Check(Vector3.Distance(from, Camera.transform.position) < 0.41f
                && Vector3.Distance(from, Camera.transform.position) > 0.25f, "Close inspection stops before a solid surface rather than passing through it");
            Object.DestroyImmediate(obstruction);
            Frame(); Set("pitch", 5f); Orbit.Focus(Origin + Vector3.right * 20f - Vector3.up * 0.3f);
            Orbit.ScrollZoom(100f, Centre); Settle(60, 2f);
            Check(Camera.transform.position.y >= 0.119f, "Low-angle close inspection keeps the camera above the floor");
            Set("pitch", 42f);
            var far = app.BeginAdd(ConveyorKind.Incline); app.MovePreview(new Pose(Origin + Vector3.right * 18f, Quaternion.identity), false, true);
            Check(app.TryCommit(), "A second conveyor extends the scene for adaptive-range verification");
            Frame(); Check(Read("maximumDistance") > smallMax * 2f, "Overview range expands with the real span of a larger line");
            Set("distance", start); Orbit.Focus(Origin);
            Vector3 farOnScreen = Camera.WorldToViewportPoint(far.transform.position);
            Check(farOnScreen.x < 0f || farOnScreen.x > 1f || farOnScreen.z < 0f,
                "The distant test conveyor is outside the current view");
            modelCentre = VisibleCentre(shortModel); Orbit.ScrollZoom(1f, EmptyPoint);
            Check(Vector2.Distance(modelCentre, Camera.WorldToViewportPoint(ReadVector("zoomAnchor"))) < 0.035f,
                "Off-screen conveyors cannot drag the zoom centre away from the visible machine");
            Frame(); Camera.pixelRect = new Rect(0, 0, 600, 800); Camera.aspect = 0.75f; app.WorldScreenRect = Camera.pixelRect;
            Orbit.Reframe(new[] { shortModel }, false); Orbit.ScrollZoom(1f, EmptyPoint);
            Check(Vector2.Distance(VisibleCentre(shortModel), Camera.WorldToViewportPoint(ReadVector("zoomAnchor"))) < 0.035f,
                "Visible-model centring remains accurate in a narrow viewport");
            VerifyFloorNavigation();
            Finish(true, null);
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        File.WriteAllText("Library/ConveyorZoomRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Trackpad zoom verification passed: " + checks.Count + " checks.");
        else Debug.LogError("Trackpad zoom verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Run;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        app = null;
    }
}
