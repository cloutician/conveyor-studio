using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorControlBoxSetup
{
    public const string ModelPath = "Assets/Art/ControlBox/control-box.glb";
    public const string PrefabPath = "Assets/Resources/ControlBox/ControlBox.prefab";
    static ConveyorControlBoxSetup()
    {
        EditorApplication.delayCall += Ensure;
        UnityEditor.PackageManager.Events.registeredPackages += _ => EditorApplication.delayCall += Ensure;
        if (File.Exists("Packages/packages-lock.json") && !File.ReadAllText("Packages/packages-lock.json").Contains("com.unity.cloud.gltfast")
            && !SessionState.GetBool("ConveyorStudio.ControlBoxResolve", false))
        {
            SessionState.SetBool("ConveyorStudio.ControlBoxResolve", true);
            EditorApplication.delayCall += () => UnityEditor.PackageManager.Client.Resolve();
        }
    }
    [MenuItem("Tools/Conveyor Studio/Build Control Box")]
    public static void Build() => Ensure(true);
    private static void Ensure() => Ensure(false);
    private static void Ensure(bool force)
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Ensure; return; }
        EnsurePresentationRenderer();
        if (!force && File.Exists(PrefabPath) && File.Exists("Library/ConveyorControlBoxImportReport.json")
            && File.ReadAllText("Library/ConveyorControlBoxImportReport.json").Contains("\"nativeMaterials\":2")) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) return;
        var work = EditorSceneManager.NewPreviewScene();
        try
        {
            Directory.CreateDirectory("Assets/Resources/ControlBox/Materials"); AssetDatabase.Refresh();
            var root = new GameObject("Control box"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, work);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, work); instance.transform.SetParent(root.transform, false);
            instance.name = "Imported control box";
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) if (materials[i] != null) materials[i] = NativeMaterial(materials[i].name);
                renderer.sharedMaterials = materials;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/ConveyorControlBoxImportReport.json", "{\"success\":true,\"nativeMaterials\":2,\"source\":\"" + ModelPath + "\",\"prefab\":\"" + PrefabPath + "\"}");
            Debug.Log("The imported control box is ready as a separate Unity prefab.");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(work); }
    }
    private static void EnsurePresentationRenderer()
    {
        const string path = "Assets/Settings/ControlBox_Renderer.asset";
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
        bool changed = false;
        if (renderer == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Mobile_Renderer.asset");
            if (source == null) return;
            renderer = Object.Instantiate(source); renderer.name = "ControlBox_Renderer";
            // The small presentation model needs its own forward pass, without
            // metre-scale world SSAO or any world selection/compositing features.
            renderer.rendererFeatures.Clear(); AssetDatabase.CreateAsset(renderer, path); changed = true;
        }
        foreach (string pipelinePath in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if (pipeline == null) continue;
            var settings = new SerializedObject(pipeline); var list = settings.FindProperty("m_RendererDataList");
            bool registered = false;
            for (int i = 0; i < list.arraySize; i++) registered |= list.GetArrayElementAtIndex(i).objectReferenceValue == renderer;
            if (registered) continue;
            int index = list.arraySize; list.arraySize++;
            list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
            settings.ApplyModifiedPropertiesWithoutUndo(); changed = true;
        }
        if (changed) AssetDatabase.SaveAssets();
        foreach (var box in Object.FindObjectsByType<ConveyorControlBox>()) box.RefreshRendering();
    }
    private static Material NativeMaterial(string name)
    {
        string key = name.Replace(" (Instance)", "");
        string path = "Assets/Resources/ControlBox/Materials/" + key + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool unlit = key.Contains("DisplayDots");
        if (material == null)
        { material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        string hex = "71817E"; float metallic = 0.12f, smooth = 0.44f;
        if (key.Contains("Faceplate")) { hex = "C7CDC2"; metallic = 0.06f; smooth = 0.36f; }
        else if (key.Contains("BackCover")) hex = "62726F";
        else if (key.Contains("Fittings")) { hex = "A5B0AA"; metallic = 0.48f; smooth = 0.66f; }
        else if (key.Contains("KnobCap")) hex = "3D4B47";
        else if (key.Contains("Knob_Plain")) hex = "303C3B";
        else if (key.Contains("Recess")) hex = "152A28";
        else if (key.Contains("RaisedMarkings")) hex = "344C46";
        else if (key.Contains("EmergencyMarking")) hex = "A72D29";
        else if (key.Contains("ButtonLettering")) hex = "E7E9D5";
        else if (key.Contains("KnobPointer")) hex = "DDC586";
        else if (key.Contains("StartCap")) { hex = "426E50"; smooth = 0.6f; }
        else if (key.Contains("EmergencyCap")) { hex = "B7473D"; smooth = 0.58f; }
        else if (key.Contains("EmergencyCollar")) hex = "D9B45C";
        else if (key.Contains("DisplayDots")) hex = "80D7B2";
        else if (key.Contains("LinkLamp")) hex = "375A47";
        ColorUtility.TryParseHtmlString("#" + hex, out Color colour);
        material.SetColor("_BaseColor", colour);
        if (key.Contains("StartCap") || key.Contains("LinkLamp")) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
        if (!unlit) { material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", smooth); }
        EditorUtility.SetDirty(material); return material;
    }
}
