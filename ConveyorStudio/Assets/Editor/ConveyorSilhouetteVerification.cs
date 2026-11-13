using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Pixel checks compare the effect against an independently rendered white model,
// not against the outline shader's own mask. Every test includes all model parts.
[InitializeOnLoad]
public static class ConveyorSilhouetteVerification
{
    private const string Key = "ConveyorStudio.SilhouetteProbe.v1";
    private const string ReportPath = "Library/ConveyorSilhouetteRuntimeReport.json";
    private const int Width = 800, Height = 600;
    private static GameObject cameraObject, machineObject;
    private static Camera camera;
    private static Material white;
    private static RenderTexture target;
    private static int view;
    private static bool originalAsync;
    private static readonly List<ViewResult> results = new List<ViewResult>();
    private static readonly float[] Yaws = { 0, 90, 180, 270, 35, 215 };
    [Serializable] private sealed class ViewResult
    {
        public string module; public float yaw, pitch;
        public int modelPixels, greenBorderPixels, redBorderPixels, changedInteriorPixels, distantBorderPixels;
        public bool materialsPreserved;
    }
    [Serializable] private sealed class Report { public bool success; public string error; public ViewResult[] views; }
    static ConveyorSilhouetteVerification()
    {
        if (!File.Exists(ReportPath)) SessionState.SetBool(Key, true);
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Silhouette Outlines")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Silhouette verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        Cleanup(); results.Clear(); view = 0;
        originalAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        cameraObject = new GameObject("Silhouette verification camera", typeof(Camera), typeof(ConveyorOrbitCamera));
        cameraObject.hideFlags = HideFlags.DontSave;
        cameraObject.GetComponent<ConveyorOrbitCamera>().enabled = false;
        camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        camera.fieldOfView = 38; camera.nearClipPlane = 0.05f; camera.farClipPlane = 20;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
        target.Create(); camera.targetTexture = target;
        white = new Material(Shader.Find("Universal Render Pipeline/Unlit")); white.SetColor("_BaseColor", Color.white);
        Directory.CreateDirectory("Library/ConveyorSilhouetteViews");
        EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { Cleanup(); return; }
        try
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ConveyorCatalog>(ConveyorStudioBuilder.CatalogPath);
            int moduleIndex = view / Yaws.Length, angleIndex = view % Yaws.Length;
            if (moduleIndex >= catalog.modules.Length) { Finish(true, null); return; }
            var definition = catalog.modules[moduleIndex];
            if (machineObject == null)
            {
                machineObject = Object.Instantiate(definition.prefab);
                machineObject.name = "Silhouette verification " + definition.title;
                machineObject.hideFlags = HideFlags.DontSave;
                machineObject.transform.position = new Vector3(1000, 0, 0);
                machineObject.GetComponent<ConveyorSection>().BeltSurface.IsRunning = false;
                machineObject.AddComponent<PlacedConveyor>().Initialize(definition);
            }
            var placed = machineObject.GetComponent<PlacedConveyor>();
            var renderers = machineObject.GetComponentsInChildren<MeshRenderer>();
            var originals = renderers.ToDictionary(r => r, r => r.sharedMaterials);
            float pitch = angleIndex >= 4 ? (angleIndex == 4 ? 72 : 24) : 38;
            Vector3 focus = machineObject.transform.position + Vector3.up * (definition.kind == ConveyorKind.Incline ? 0.65f : 0.28f);
            camera.transform.rotation = Quaternion.Euler(pitch, Yaws[angleIndex], 0);
            camera.transform.position = focus - camera.transform.forward * 4.8f;

            placed.ShowOutline(false, Color.clear);
            var baseline = Capture();
            // An unrelated unlit pass supplies the model coverage used by assertions.
            foreach (var renderer in renderers) renderer.sharedMaterials = Enumerable.Repeat(white, originals[renderer].Length).ToArray();
            Color32[] geometry;
            try { var maskImage = Capture(); geometry = maskImage.GetPixels32(); Object.DestroyImmediate(maskImage); }
            finally { foreach (var renderer in renderers) renderer.sharedMaterials = originals[renderer]; }
            placed.ShowOutline(true, new Color(0.26f, 1f, 0.63f)); var green = Capture();
            placed.ShowOutline(true, new Color(1f, 0.24f, 0.24f)); var red = Capture();
            bool preserved = originals.All(pair => pair.Key.sharedMaterials.SequenceEqual(pair.Value));
            var result = Compare(definition.title, Yaws[angleIndex], pitch, baseline.GetPixels32(), geometry, green.GetPixels32(), red.GetPixels32(), preserved);
            string prefix = "Library/ConveyorSilhouetteViews/" + definition.title + "-" + angleIndex;
            File.WriteAllBytes(prefix + "-green.png", green.EncodeToPNG()); File.WriteAllBytes(prefix + "-red.png", red.EncodeToPNG());
            results.Add(result);
            Object.DestroyImmediate(baseline); Object.DestroyImmediate(green); Object.DestroyImmediate(red);
            if (!result.materialsPreserved || result.greenBorderPixels < 200 || result.redBorderPixels < 200
                || result.changedInteriorPixels != 0 || result.distantBorderPixels != 0)
                throw new Exception("Silhouette pixel check failed: " + JsonUtility.ToJson(result));
            if (machineObject.GetComponentInChildren<LineRenderer>() != null) throw new Exception("A floor footprint is still attached to the model.");
            view++;
            if (view % Yaws.Length == 0) { Object.DestroyImmediate(machineObject); machineObject = null; }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static Texture2D Capture()
    {
        camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply(); RenderTexture.active = previous;
        return image;
    }
    private static ViewResult Compare(string module, float yaw, float pitch, Color32[] baseline, Color32[] coverage, Color32[] green, Color32[] red, bool preserved)
    {
        bool[] body = coverage.Select(p => p.r > 160 && p.g > 160 && p.b > 160).ToArray();
        var result = new ViewResult { module = module, yaw = yaw, pitch = pitch, materialsPreserved = preserved, modelPixels = body.Count(p => p) };
        for (int y = 6; y < Height - 6; y++) for (int x = 6; x < Width - 6; x++)
        {
            int i = y * Width + x;
            bool inside = true;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) inside &= body[i + dy * Width + dx];
            if (inside && (Difference(baseline[i], green[i]) > 4 || Difference(baseline[i], red[i]) > 4)) result.changedInteriorPixels++;
            bool isGreen = green[i].g > green[i].r + 30 && green[i].g > green[i].b + 10 && Difference(baseline[i], green[i]) > 8;
            bool isRed = red[i].r > red[i].g + 30 && red[i].r > red[i].b + 30 && Difference(baseline[i], red[i]) > 8;
            if (isGreen) result.greenBorderPixels++;
            if (isRed) result.redBorderPixels++;
            if (!isGreen && !isRed) continue;
            bool nearBody = false;
            for (int dy = -5; dy <= 5 && !nearBody; dy++) for (int dx = -5; dx <= 5; dx++) if (body[i + dy * Width + dx]) { nearBody = true; break; }
            if (!nearBody) result.distantBorderPixels++;
        }
        return result;
    }
    private static int Difference(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));
    private static void Finish(bool success, string error)
    {
        File.WriteAllText(ReportPath, JsonUtility.ToJson(new Report { success = success, error = error, views = results.ToArray() }, true));
        if (success) Debug.Log("Silhouette verification passed for all three models at six camera angles: green/red borders, unchanged interiors and original materials.");
        else Debug.LogError("Silhouette verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (cameraObject != null) { ShaderUtil.allowAsyncCompilation = originalAsync; Object.DestroyImmediate(cameraObject); }
        if (machineObject != null) Object.DestroyImmediate(machineObject);
        if (target != null) { target.Release(); Object.DestroyImmediate(target); }
        if (white != null) Object.DestroyImmediate(white);
        cameraObject = machineObject = null; camera = null; target = null; white = null;
    }
}
