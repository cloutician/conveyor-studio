using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// A regular manufactured tile pattern is generated at real-world scale, with
// matching colour, bevel normals, smoothness and grout occlusion maps.
[InitializeOnLoad]
public static class ConveyorFloorTiles
{
    public const string MaterialPath = "Assets/Art/Studio/FloorGrid.mat";
    private const string Folder = "Assets/Art/Studio/FloorTiles";
    private const int Resolution = 2048, TilesPerSide = 4;
    private const float TileMetres = 0.6f, GroutMetres = 0.003f;
    private const float RepeatMetres = TileMetres * TilesPerSide;

    static ConveyorFloorTiles()
    {
        EditorApplication.delayCall += Ensure;
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Ensure; };
    }
    private static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Ensure; return; }
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!File.Exists(Folder + "/TileColour.png") || !File.Exists(Folder + "/TileNormal.png")
            || !File.Exists(Folder + "/TileMetallicSmoothness.png") || !File.Exists(Folder + "/TileOcclusion.png")
            || material == null || material.GetTexture("_BumpMap") == null) Apply();
    }

    [MenuItem("Tools/Conveyor Studio/Apply Regular Floor Tiles")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before rebuilding floor textures."); return; }
        try
        {
            Directory.CreateDirectory(Folder);
            GenerateMaps();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureTexture("TileColour", true, false);
            ConfigureTexture("TileNormal", false, true);
            ConfigureTexture("TileMetallicSmoothness", false, false);
            ConfigureTexture("TileOcclusion", false, false);
            ConfigureMaterial();
            AssetDatabase.SaveAssets();
            // The material keeps its existing GUID, so the scene, app prefab and
            // their floor colliders stay intact. No scene is saved by this tool.
            File.WriteAllText("Library/ConveyorFloorTilesReport.json", JsonUtility.ToJson(new Report
            { success = true, tileMetres = TileMetres, groutMillimetres = GroutMetres * 1000f,
                mapResolution = Resolution, repeatMetres = RepeatMetres, material = MaterialPath,
                geometryAndColliderUnchanged = true }, true));
            Debug.Log("Applied regular grey floor tiles: 600 mm tiles, 3 mm grout, subtle bevels and a satin finish. Floor geometry and collider retained.");
        }
        catch (Exception error) { Debug.LogException(error); }
    }
    [Serializable] private class Report
    {
        public bool success, geometryAndColliderUnchanged;
        public float tileMetres, groutMillimetres, repeatMetres;
        public int mapResolution;
        public string material;
    }
    public static Material GetOrCreateMaterial()
    {
        if (!File.Exists(Folder + "/TileColour.png")) Apply();
        return AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }
    private static void GenerateMaps()
    {
        int count = Resolution * Resolution, tilePixels = Resolution / TilesPerSide;
        float metresPerPixel = RepeatMetres / Resolution;
        var colour = new Color32[count]; var normal = new Color32[count];
        var surface = new Color32[count]; var occlusion = new Color32[count]; var height = new float[count];
        for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution; x++)
        {
            int i = y * Resolution + x;
            float px = x % tilePixels + 0.5f, py = y % tilePixels + 0.5f;
            float edge = Mathf.Min(px, tilePixels - px, py, tilePixels - py) * metresPerPixel;
            float face = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GroutMetres * 0.5f, GroutMetres * 0.5f + 0.0015f, edge));
            float grain = Hash(x, y) - 0.5f;
            float tone = (Hash(x / tilePixels + 13, y / tilePixels + 71) - 0.5f) * 0.022f;
            float broad = Mathf.Sin(2f * Mathf.PI * (x * 3f + y * 5f) / Resolution)
                * Mathf.Sin(2f * Mathf.PI * (x * 7f - y * 2f) / Resolution);
            float faceColour = 0.52f + tone + grain * 0.009f + broad * 0.002f;
            float c = Mathf.Lerp(0.345f + grain * 0.009f, faceColour, face);
            colour[i] = new Color(c, c * 1.003f, c * 1.008f, 1f);
            float smoothness = Mathf.Lerp(0.10f, 0.35f + grain * 0.035f + broad * 0.015f, face);
            surface[i] = new Color(0f, 0f, 0f, smoothness);
            float ao = Mathf.Lerp(0.90f, 1f, face); occlusion[i] = new Color(ao, ao, ao, 1f);
            height[i] = face * (0.00065f + grain * 0.00002f + broad * 0.000015f);
        }
        for (int y = 0; y < Resolution; y++) for (int x = 0; x < Resolution; x++)
        {
            float dx = (height[y * Resolution + (x + 1) % Resolution]
                - height[y * Resolution + (x + Resolution - 1) % Resolution]) / (2f * metresPerPixel);
            float dy = (height[((y + 1) % Resolution) * Resolution + x]
                - height[((y + Resolution - 1) % Resolution) * Resolution + x]) / (2f * metresPerPixel);
            Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
            normal[y * Resolution + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }
        SaveMap("TileColour", colour); SaveMap("TileNormal", normal);
        SaveMap("TileMetallicSmoothness", surface); SaveMap("TileOcclusion", occlusion);
    }
    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint n = (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu;
            n ^= n >> 16; n *= 0x7FEB352Du; n ^= n >> 15; n *= 0x846CA68Bu; n ^= n >> 16;
            return (n & 0xFFFFFFu) / 16777215f;
        }
    }
    private static void SaveMap(string name, Color32[] pixels)
    {
        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
        texture.SetPixels32(pixels); texture.Apply(false);
        File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG()); Object.DestroyImmediate(texture);
    }
    private static void ConfigureTexture(string name, bool srgb, bool isNormal)
    {
        string path = Folder + "/" + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = srgb; importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat; importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8; importer.maxTextureSize = Resolution; importer.alphaIsTransparency = false;
        importer.SaveAndReimport();
    }
    private static void ConfigureMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.name = "Factory Floor Tiles";
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/TileColour.png"));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/TileNormal.png"));
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/TileMetallicSmoothness.png"));
        material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/TileOcclusion.png"));
        material.SetColor("_BaseColor", Color.white); material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 1f); material.SetFloat("_SmoothnessTextureChannel", 0f);
        material.SetFloat("_BumpScale", 0.55f); material.SetFloat("_OcclusionStrength", 0.65f);
        float repeats = 400f / RepeatMetres;
        material.SetTextureScale("_BaseMap", Vector2.one * repeats);
        // Put the origin at a tile centre rather than an arbitrary grout crossing.
        material.SetTextureOffset("_BaseMap", Vector2.one * (0.125f - Mathf.Repeat(repeats * 0.5f, 1f)));
        material.EnableKeyword("_NORMALMAP"); material.EnableKeyword("_METALLICSPECGLOSSMAP"); material.EnableKeyword("_OCCLUSIONMAP");
        EditorUtility.SetDirty(material);
    }
}
