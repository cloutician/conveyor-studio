using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ConveyorBeltSetup
{
    private const string Folder = "Assets/Art/Conveyors";
    private const string ReportPath = "Library/ConveyorBeltSetupReport.json";
    private const string StampPath = "Library/ConveyorBeltSetup-v4.done";

    [InitializeOnLoadMethod]
    private static void QueueInitialSetup()
    {
        // Existing committed art assets are authoritative; opening a checkout must not reset them.
        if (!File.Exists(Folder + "/BeltRibs.png") || !File.Exists(ConveyorBeltImporter.SurfaceMaterialPath)
            || !File.Exists(ConveyorBeltImporter.RollerMaterialPath)) EditorApplication.delayCall += InitialSetup;
    }

    private static void InitialSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += InitialSetup;
            return;
        }
        Apply();
    }

    [MenuItem("Tools/Conveyor Studio/Apply Ribbed Belts (All Types)")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before applying the conveyor materials.");
            return;
        }
        try
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            CreateTexture(false);
            CreateTexture(true);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP/Lit shader is unavailable.");
            Material surface = LoadOrCreateMaterial(ConveyorBeltImporter.SurfaceMaterialPath, shader);
            surface.SetColor("_BaseColor", Color.white);
            surface.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/BeltRibs.png"));
            surface.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/BeltRibsNormal.png"));
            surface.SetFloat("_BumpScale", 0.35f);
            surface.EnableKeyword("_NORMALMAP");
            surface.SetFloat("_Metallic", 0.03f);
            surface.SetFloat("_Smoothness", 0.3f);
            Material edge = LoadOrCreateMaterial(ConveyorBeltImporter.EdgeMaterialPath, shader);
            edge.SetColor("_BaseColor", new Color(0.07f, 0.13f, 0.2f));
            edge.SetFloat("_Metallic", 0.02f);
            edge.SetFloat("_Smoothness", 0.27f);
            Material roller = LoadOrCreateMaterial(ConveyorBeltImporter.RollerMaterialPath, shader);
            roller.SetColor("_BaseColor", new Color(0.5f, 0.55f, 0.57f));
            roller.SetFloat("_Metallic", 0.8f);
            roller.SetFloat("_Smoothness", 0.4f);
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(edge);
            EditorUtility.SetDirty(roller);
            AssetDatabase.SaveAssets();

            foreach (string path in ConveyorBeltImporter.ModelPaths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            List<ModelCheck> checks = new List<ModelCheck>();
            foreach (string path in ConveyorBeltImporter.ModelPaths)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ConveyorBeltSurface[] belts = model.GetComponentsInChildren<ConveyorBeltSurface>(true);
                if (belts.Length != 1) throw new InvalidOperationException(path + " must contain one prepared belt surface.");
                Mesh mesh = belts[0].GetComponent<MeshFilter>().sharedMesh;
                if (mesh.vertexCount == 0 || mesh.subMeshCount != 2)
                    throw new InvalidOperationException(path + " has an invalid prepared belt mesh.");
                checks.Add(new ModelCheck { path = path, pitchMetres = belts[0].MetresPerRepeat,
                    submeshes = mesh.subMeshCount, vertices = mesh.vertexCount });
            }
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new SetupReport { success = true, models = checks.ToArray() }, true));
            File.WriteAllText(StampPath, "Ribbed belt materials and continuous loop UVs applied.");
            Debug.Log("Ribbed belts prepared for Generic, Short and Incline. Press Play to see the belt surfaces travel.");
            SceneView.RepaintAll();
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new SetupReport { success = false, error = error.ToString() }, true));
            Debug.LogException(error);
        }
    }

    private static Material LoadOrCreateMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static void CreateTexture(bool normal)
    {
        const int width = 256, height = 8;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGB24, false, normal);
        Color[] pixels = new Color[width * height];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            float groove = Mathf.Exp(-Mathf.Pow((u - 0.5f) / 0.035f, 2f));
            float lip = Mathf.Exp(-Mathf.Pow((u - 0.445f) / 0.025f, 2f));
            Color color;
            if (normal)
            {
                float slope = -0.65f * (u - 0.5f) / 0.035f * groove;
                Vector3 n = new Vector3(slope, 0f, 1f).normalized;
                color = new Color(n.x * 0.5f + 0.5f, 0.5f, n.z * 0.5f + 0.5f);
            }
            else color = new Color((22f - 10f * groove + 9f * lip) / 255f,
                (42f - 17f * groove + 12f * lip) / 255f, (65f - 22f * groove + 14f * lip) / 255f);
            for (int y = 0; y < height; y++) pixels[y * width + x] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply();
        string path = Folder + (normal ? "/BeltRibsNormal.png" : "/BeltRibs.png");
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !normal;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    [Serializable] private sealed class SetupReport { public bool success; public string error; public ModelCheck[] models; }
    [Serializable] private sealed class ModelCheck { public string path; public float pitchMetres; public int submeshes; public int vertices; }
}
