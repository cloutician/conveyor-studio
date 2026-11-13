using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorStudioBuilder
{
    public const string ScenePath = "Assets/Scenes/ConveyorStudio.unity";
    public const string AppPath = "Assets/Prefabs/ConveyorStudioApplication.prefab";
    public const string CatalogPath = "Assets/Data/ConveyorCatalog.asset";
    private const string Art = "Assets/Art/Studio";
    static ConveyorStudioBuilder()
    {
        EditorApplication.delayCall += FirstBuild;
        EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += FirstBuild; };
    }
    private static void FirstBuild()
    {
        if (File.Exists(ScenePath) || !File.Exists(ConveyorModuleBuilder.InclinePath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += FirstBuild; return; }
        Build();
    }

    [MenuItem("Tools/Conveyor Studio/Build Placement Studio")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before rebuilding the studio."); return; }
        Scene original = SceneManager.GetActiveScene();
        Scene work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(work);
        try
        {
            Directory.CreateDirectory(Art); Directory.CreateDirectory("Assets/Data"); AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<ConveyorCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<ConveyorCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.validMaterial = Ghost("PlacementValid", new Color(0.15f, 0.95f, 0.55f, 0.47f));
            catalog.invalidMaterial = Ghost("PlacementInvalid", new Color(1f, 0.16f, 0.15f, 0.53f));
            catalog.lineMaterial = MaterialAsset("PlacementLine", "Sprites/Default");
            catalog.productPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorPhysicsSetup.BoxPath);
            catalog.canPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorPhysicsSetup.CanPath);
            catalog.modules = new[] {
                Module(ConveyorKind.Generic, "Generic", "2.22 m  ·  straight", ConveyorModuleBuilder.GenericPath),
                Module(ConveyorKind.Short, "Short", "1.58 m  ·  straight", ConveyorModuleBuilder.ShortPath),
                Module(ConveyorKind.Incline, "Incline", "2.22 m  ·  20.9° rise", ConveyorModuleBuilder.InclinePath)
            };
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.69f, 0.76f);
            var root = new GameObject("Conveyor Studio");
            var controller = root.AddComponent<ConveyorPlacementController>(); controller.catalog = catalog;
            var cameraObject = new GameObject("Workspace camera", typeof(Camera), typeof(AudioListener), typeof(ConveyorOrbitCamera));
            cameraObject.transform.SetParent(root.transform, false); cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView = 40; camera.nearClipPlane = 0.05f; camera.farClipPlane = 350;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.10f, 0.14f, 0.18f);
            camera.allowHDR = true; camera.allowMSAA = true;
            cameraObject.transform.SetPositionAndRotation(new Vector3(4, 6, -8), Quaternion.Euler(36, -27, 0));
            controller.sceneCamera = camera;
            controller.orbitCamera = cameraObject.GetComponent<ConveyorOrbitCamera>(); controller.orbitCamera.placement = controller;
            var sunObject = new GameObject("Soft key light", typeof(Light)); sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(48, -32, 0);
            var sun = sunObject.GetComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.8f;
            sun.color = new Color(1f, 0.95f, 0.88f); sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.55f; sun.shadowBias = 0.025f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Ground · Y = 0"; floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = Vector3.one * 40f;
            floor.GetComponent<MeshRenderer>().sharedMaterial = FloorMaterial();
            floor.GetComponent<Collider>().sharedMaterial = ConveyorCollision.Material("Tile");
            var hud = root.AddComponent<ConveyorStudioHud>(); hud.placement = controller;
            PrefabUtility.SaveAsPrefabAsset(root, AppPath);
            Object.DestroyImmediate(root);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AppPath), work);
            foreach (var module in catalog.modules) module.thumbnail = Thumbnail(module, work);
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(work, ScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray();
            File.WriteAllText("Library/ConveyorStudioBuildReport.json", "{\"success\":true,\"scene\":\"" + ScenePath + "\",\"rotationStepDegrees\":90}");
            SessionState.SetBool("ConveyorStudio.PlacementProbe.v1", true);
            Debug.Log("Placement Studio built. Open Assets/Scenes/ConveyorStudio and press Play. The next Play session also verifies placement behaviour.");
        }
        catch (Exception error) { File.WriteAllText("Library/ConveyorStudioBuildReport.json", JsonUtility.ToJson(new Failure { error = error.ToString() }, true)); Debug.LogException(error); }
        finally
        {
            EditorSceneManager.CloseScene(work, true);
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
        }
    }
    [Serializable] private class Failure { public bool success; public string error; }
    private static ConveyorCatalog.Module Module(ConveyorKind kind, string title, string description, string path)
        => new ConveyorCatalog.Module { kind = kind, title = title, description = description, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path) };
    private static Material MaterialAsset(string name, string shader)
    {
        string path = Art + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(material, path); }
        return material;
    }
    private static Material Ghost(string name, Color colour)
    {
        var material = MaterialAsset(name, "Universal Render Pipeline/Lit");
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0); material.SetFloat("_ZWrite", 0);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_Smoothness", 0.4f); material.SetColor("_BaseColor", colour);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("ShadowCaster", false); material.renderQueue = 3000;
        EditorUtility.SetDirty(material); return material;
    }
    private static Material FloorMaterial()
    {
        return ConveyorFloorTiles.GetOrCreateMaterial();
    }
    private static Sprite Thumbnail(ConveyorCatalog.Module module, Scene scene)
    {
        // Render the actual module, rather than using a generic prefab icon.
        var model = (GameObject)PrefabUtility.InstantiatePrefab(module.prefab, scene); model.transform.position = new Vector3(1000, 0, 0);
        var cameraObject = new GameObject("Thumbnail camera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.10f, 0.135f, 0.16f, 0f);
        Vector3 target = model.transform.position + Vector3.up * (module.kind == ConveyorKind.Incline ? 0.66f : 0.29f);
        camera.transform.position = target + new Vector3(2.7f, 2.5f, -3.7f); camera.transform.LookAt(target);
        camera.orthographic = true; camera.orthographicSize = module.kind == ConveyorKind.Short ? 1.0f : 1.25f;
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 20f;
        var rt = new RenderTexture(320, 192, 24); rt.Create(); camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(320, 192, TextureFormat.RGBA32, false); image.ReadPixels(new Rect(0, 0, 320, 192), 0, 0); image.Apply();
        string path = Art + "/" + module.title + "Thumbnail.png"; File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(model);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single; importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
