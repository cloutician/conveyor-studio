using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorGapVerification
{
    private const string Key = "ConveyorStudio.GapProbe.v1";
    private static readonly Vector3 Origin = new Vector3(1000, 0, 0);
    private static readonly List<string> checks = new List<string>();
    private static Scene scene;
    private static ConveyorPlacementController app;
    private static PlacedConveyor upstream, middle, downstream;
    private static Pose slot, downstreamPose;
    private static int stage;
    private static double sampleAt;
    [Serializable] private sealed class Report { public bool success; public string error; public string[] checks; }
    static ConveyorGapVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Gap Reconnection and Reframe %#F11")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Gap reconnection and Reframe verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        Cleanup(); checks.Clear(); stage = 0;
        try
        {
            scene = SceneManager.CreateScene("Gap reconnection verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorStudioBuilder.AppPath));
            SceneManager.MoveGameObjectToScene(instance, scene); instance.transform.position = Origin;
            instance.name = "Temporary gap verification workspace"; instance.hideFlags = HideFlags.DontSave;
            app = instance.GetComponent<ConveyorPlacementController>(); app.enabled = false;
            instance.GetComponent<ConveyorStudioHud>().enabled = false;
            foreach (var camera in instance.GetComponentsInChildren<Camera>()) camera.enabled = false;
            foreach (var listener in instance.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>()) light.enabled = false;
            app.orbitCamera.enabled = false;
            sampleAt = EditorApplication.timeSinceStartup + 0.1;
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Check(bool pass, string text)
    { if (!pass) throw new Exception(text + " | " + app.Status + " | " + app.LastCollision); checks.Add(text); }
    private static PlacedConveyor Add(ConveyorKind kind, Vector3 position, bool snap)
    {
        var model = app.BeginAdd(kind);
        app.MovePreview(new Pose(position, Quaternion.identity), snap, true);
        Check(app.PreviewIsValid && app.TryCommit(), kind + " placed in the test layout");
        return model;
    }
    private static Vector3 After(PlacedConveyor before, ConveyorKind kind)
    {
        string path = kind == ConveyorKind.Short ? ConveyorModuleBuilder.ShortPath : ConveyorModuleBuilder.InclinePath;
        return before.Section.Exit.position - AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ConveyorSection>().Entry.localPosition;
    }
    private static void Approach(float offset, int frames = 16)
    {
        Vector3 point = slot.position + Vector3.right * offset; point.y = 0f;
        for (int i = 0; i < frames; i++) app.MovePreview(new Pose(point, slot.rotation), true, false, 1f / 60f);
    }
    private static bool Green(PlacedConveyor model)
    {
        var outline = model.GetComponent<ConveyorSilhouette>();
        return outline.Highlighted && outline.Colour.g > 0.9f && outline.Colour.r < 0.5f;
    }
    private static void Ready()
    {
        Check(app.PreviewIsValid && app.EntrySnapPartner == upstream && app.ExitSnapPartner == downstream,
            "The held incline recognises both ends of its elevated gap");
        Check(Green(upstream) && Green(downstream), "Both stationary neighbours show green readiness outlines");
        Check(Vector3.Distance(downstream.transform.position, downstreamPose.position) < 0.00001f,
            "The elevated short remains at its original height and position");
    }
    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup < sampleAt) return;
        try
        {
            if (stage == 0)
            {
                upstream = Add(ConveyorKind.Incline, Origin, false);
                middle = Add(ConveyorKind.Incline, After(upstream, ConveyorKind.Incline), true);
                downstream = Add(ConveyorKind.Short, After(middle, ConveyorKind.Short), true);
                slot = new Pose(middle.transform.position, middle.transform.rotation);
                downstreamPose = new Pose(downstream.transform.position, downstream.transform.rotation);
                Check(slot.position.y > 0.79f && downstreamPose.position.y > 1.58f, "Two inclines create the requested raised short-conveyor layout");
                Check(app.BeginMove(middle), "The middle incline can be picked up independently");
                Approach(0.7f);
                Check(app.EntrySnapPartner == null && app.ExitSnapPartner == null, "Moving clear releases readiness at both neighbours");
                Approach(0.16f); Ready();
                Check(upstream.Section.Next == null && downstream.Section.Previous == null,
                    "Neither transfer connection is activated before the moving incline settles");
                Check(app.TryCommit(), "Returning into the complete gap accepts the placement");
                stage = 1; sampleAt = EditorApplication.timeSinceStartup + 0.65;
            }
            else if (stage == 1)
            {
                Check(!middle.IsEditing && upstream.Section.Next == middle.Section && middle.Section.Previous == upstream.Section
                    && middle.Section.Next == downstream.Section && downstream.Section.Previous == middle.Section,
                    "Both reciprocal connections are restored after the return animation");
                Check(Vector3.Distance(middle.transform.position, slot.position) < 0.00001f, "The incline returns to the exact original elevated pose");
                app.BeginMove(middle); Approach(0.025f, 12);
                Check(app.SnapPartner == null, "A small initial pull-away remains free of magnetic glue");
                Approach(0.004f); Ready();
                Check(app.TryCommit(), "A small deliberate reversal can reconnect without a large detour");
                stage = 2; sampleAt = EditorApplication.timeSinceStartup + 0.65;
            }
            else if (stage == 2)
            {
                app.BeginMove(middle); Approach(0.7f);
                downstream.transform.position += Vector3.up * 0.08f; downstream.RefreshSupports();
                Approach(0.12f);
                Check(!(app.EntrySnapPartner == upstream && app.ExitSnapPartner == downstream) && !(Green(upstream) && Green(downstream)),
                    "Mismatched heights cannot falsely advertise both connections as ready");
                downstream.transform.SetPositionAndRotation(downstreamPose.position, downstreamPose.rotation); downstream.RefreshSupports();
                Approach(0.03f); Ready();
                app.CancelPreview(); stage = 3; sampleAt = EditorApplication.timeSinceStartup + 0.65;
            }
            else
            {
                var shortModel = Add(ConveyorKind.Short, Origin + Vector3.right * 6f, false);
                var camera = app.sceneCamera;
                app.WorldScreenRect = new Rect(0, 0, 1148, 600); camera.aspect = 1148f / 600f;
                app.orbitCamera.Reframe(new[] { shortModel }, false);
                VerifyView(new[] { shortModel }, "one short conveyor");
                Check(Vector3.Distance(camera.transform.position, shortModel.transform.position + Vector3.up * 0.27f) < 4.5f,
                    "Reframe brings a single short conveyor closer than the old five-metre minimum");
                app.orbitCamera.Reframe(app.Machines, false); VerifyView(app.Machines, "the complete elevated layout");
                app.WorldScreenRect = new Rect(0, 0, 600, 800); camera.aspect = 0.75f;
                app.orbitCamera.Reframe(app.Machines, false); VerifyView(app.Machines, "a narrow viewport");
                VerifyOverviewAngles(shortModel);
                Finish(true, null);
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void VerifyView(IReadOnlyList<PlacedConveyor> models, string label)
    {
        Vector2 low = Vector2.one, high = Vector2.zero;
        bool inside = true;
        foreach (var model in models)
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;
            var vertices = filter.sharedMesh.vertices;
            int stride = Mathf.Max(1, vertices.Length / 128);
            for (int i = 0; i < vertices.Length; i += stride)
            {
                Vector3 point = app.sceneCamera.WorldToViewportPoint(filter.transform.TransformPoint(vertices[i]));
                inside &= point.z > app.sceneCamera.nearClipPlane && point.x >= 0.02f && point.x <= 0.98f && point.y >= 0.02f && point.y <= 0.98f;
                low = Vector2.Min(low, point); high = Vector2.Max(high, point);
            }
        }
        Check(inside, "Reframe keeps all sampled model vertices visible: " + label);
        Check(Mathf.Max(high.x - low.x, high.y - low.y) > 0.74f, "Reframe fills the viewport without excessive empty space: " + label);
    }
    private static void VerifyOverviewAngles(PlacedConveyor shortModel)
    {
        var generic = Add(ConveyorKind.Generic, Origin + Vector3.right * 10f, false);
        var groups = new[] { new[] { shortModel }, new[] { generic }, new[] { upstream }, new[] { upstream, middle, downstream } };
        string[] labels = { "Short", "Generic", "Incline", "raised mixed line" };
        float[] pitches = { 5f, 10f, 20f, 42f, 75f }, yaws = { 0f, 35f, 90f, 180f }, aspects = { 1.8f, 0.75f };
        var orbit = app.orbitCamera; var camera = app.sceneCamera;
        var pitchField = typeof(ConveyorOrbitCamera).GetField("pitch", BindingFlags.Instance | BindingFlags.NonPublic);
        var yawField = typeof(ConveyorOrbitCamera).GetField("yaw", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int group = 0; group < groups.Length; group++)
        foreach (float aspect in aspects) foreach (float pitch in pitches) foreach (float yaw in yaws)
        {
            pitchField.SetValue(orbit, pitch); yawField.SetValue(orbit, yaw);
            app.WorldScreenRect = new Rect(0, 0, 600f * aspect, 600f); camera.aspect = aspect;
            orbit.Reframe(groups[group], false);
            Vector2 low = Vector2.one, high = Vector2.zero;
            float nearest = float.PositiveInfinity, farthest = 0f; bool inside = true;
            foreach (var model in groups[group]) foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var vertices = filter.sharedMesh.vertices; int stride = Mathf.Max(1, vertices.Length / 128);
                for (int i = 0; i < vertices.Length; i += stride)
                {
                    Vector3 point = camera.WorldToViewportPoint(filter.transform.TransformPoint(vertices[i]));
                    inside &= point.x >= 0.045f && point.x <= 0.955f && point.y >= 0.045f && point.y <= 0.955f;
                    nearest = Mathf.Min(nearest, point.z); farthest = Mathf.Max(farthest, point.z);
                    low = Vector2.Min(low, point); high = Vector2.Max(high, point);
                }
            }
            float span = Mathf.Max(high.x - low.x, high.y - low.y), perspective = farthest / nearest;
            Vector3 firstPosition = camera.transform.position;
            bool anglePreserved = Quaternion.Angle(camera.transform.rotation, Quaternion.Euler(pitch, yaw, 0f)) < 0.01f;
            orbit.Reframe(groups[group], false);
            bool stable = Vector3.Distance(firstPosition, camera.transform.position) < 0.00025f;
            float perspectiveLimit = pitch <= 10f ? 2.28f : 2.52f;
            Check(inside && nearest >= 1.05f && perspective <= perspectiveLimit && span <= 0.90f && anglePreserved && stable
                && camera.transform.position.y >= 0.12f,
                "Comfortable, stable Reframe: " + labels[group] + ", pitch " + pitch + ", yaw " + yaw + ", aspect " + aspect
                + " (near " + nearest.ToString("F2") + " m, perspective " + perspective.ToString("F2") + ", fill " + span.ToString("F2") + ")");
            if (aspect == 1.8f && pitch == 5f && yaw == 0f && group != 2)
                CaptureOverview(groups[group], labels[group].Replace(' ', '-'));
        }
    }
    private static void CaptureOverview(IReadOnlyList<PlacedConveyor> models, string label)
    {
        var hidden = new List<Renderer>();
        foreach (var other in app.Machines)
        {
            bool included = false; foreach (var model in models) included |= other == model;
            if (included) continue;
            foreach (var renderer in other.GetComponentsInChildren<Renderer>())
                if (renderer.enabled) { renderer.enabled = false; hidden.Add(renderer); }
        }
        var camera = app.sceneCamera;
        var target = RenderTexture.GetTemporary(1080, 600, 24, RenderTextureFormat.ARGB32);
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        Texture2D picture = null;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            picture = new Texture2D(1080, 600, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, 1080, 600), 0, 0); picture.Apply();
            Directory.CreateDirectory("Library/ConveyorReframeViews");
            File.WriteAllBytes("Library/ConveyorReframeViews/" + label + "-low-angle.png", picture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target); if (picture != null) Object.DestroyImmediate(picture);
            foreach (var renderer in hidden) if (renderer != null) renderer.enabled = true;
        }
    }
    private static void Finish(bool success, string error)
    {
        File.WriteAllText("Library/ConveyorGapRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Gap reconnection and Reframe verification passed: " + checks.Count + " checks.");
        else Debug.LogError("Gap reconnection and Reframe verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        app = null;
    }
}
