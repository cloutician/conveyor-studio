using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
public static class ConveyorSilhouetteSetup
{
    private const string Art = "Assets/Art/Studio";
    private static readonly string[] RendererPaths = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };
    static ConveyorSilhouetteSetup()
    {
        EditorApplication.delayCall += InstallIfMissing;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += InstallIfMissing; };
    }
    private static void InstallIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += InstallIfMissing; return; }
        foreach (var path in RendererPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data != null && !data.rendererFeatures.OfType<ConveyorSilhouetteFeature>().Any()) { Install(); return; }
        }
    }
    [MenuItem("Tools/Conveyor Studio/Install Silhouette Outlines")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            Directory.CreateDirectory(Art); AssetDatabase.Refresh();
            var mask = CreateMaterial("ConveyorSilhouetteMask", "Conveyor Studio/Silhouette Mask");
            var composite = CreateMaterial("ConveyorSilhouetteBorder", "Conveyor Studio/Silhouette Border");
            foreach (var path in RendererPaths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data == null) continue;
                var feature = data.rendererFeatures.OfType<ConveyorSilhouetteFeature>().FirstOrDefault();
                if (feature == null)
                {
                    feature = ScriptableObject.CreateInstance<ConveyorSilhouetteFeature>();
                    feature.name = "Conveyor silhouette outlines";
                    AssetDatabase.AddObjectToAsset(feature, data);
                    data.rendererFeatures.Add(feature);
                }
                feature.maskMaterial = mask; feature.compositeMaterial = composite; feature.widthPixels = 3f;
                feature.SetActive(true); feature.Create();
                EditorUtility.SetDirty(feature); EditorUtility.SetDirty(data); data.SetDirty();
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/ConveyorSilhouetteSetupReport.json", "{\"success\":true,\"outlineWidthPixels\":3,\"modelMaterialsUnchanged\":true}");
            Debug.Log("Installed screen-space conveyor silhouettes. Normal model materials are retained; only the outside border is coloured.");
        }
        catch (Exception error) { Debug.LogException(error); }
    }
    private static Material CreateMaterial(string name, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (shader == null) throw new Exception("Silhouette shader has not imported: " + shaderName);
        string path = Art + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
        else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
        return material;
    }
}
