using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorInterfaceArtSetup
{
    private const string Folder = "Assets/Resources/Studio";
    static ConveyorInterfaceArtSetup() { EditorApplication.delayCall += Ensure; }
    private static void Ensure()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Ensure; return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorPhysicsSetup.BoxPath) == null) return;
        try
        {
            Directory.CreateDirectory(Folder);
            bool current = File.Exists("Library/ConveyorInterfaceArtReport.json") && File.ReadAllText("Library/ConveyorInterfaceArtReport.json").Contains("\"version\":5");
            if (!current || !File.Exists(Folder + "/BoxIcon.png")) RenderProduct(ConveyorPhysicsSetup.BoxPath, "BoxIcon");
            if (!current || !File.Exists(Folder + "/CanIcon.png")) RenderProduct(ConveyorPhysicsSetup.CanPath, "CanIcon");
            foreach (string name in new[] { "ConveyorStudioIcon", "BoxIcon", "CanIcon" }) ImportSprite(Folder + "/" + name + ".png");
            foreach (var hud in Object.FindObjectsByType<ConveyorStudioHud>()) if (hud.enabled) hud.ReloadArtwork();
            File.WriteAllText("Library/ConveyorInterfaceArtReport.json", "{\"success\":true,\"icons\":3,\"version\":5}");
        }
        catch (System.Exception error) { Debug.LogError("Interface artwork import failed: " + error); }
    }
    private static void ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType == TextureImporterType.Sprite && !importer.mipmapEnabled && importer.maxTextureSize == 256) return;
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false; importer.maxTextureSize = 256; importer.npotScale = TextureImporterNPOTScale.None;
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
    }
    private static void RenderProduct(string prefabPath, string name)
    {
        var preview = new PreviewRenderUtility(); Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            var model = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            model.transform.position = Vector3.zero;
            foreach (var child in model.GetComponentsInChildren<Transform>()) child.gameObject.layer = 29;
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
            foreach (var body in model.GetComponentsInChildren<Rigidbody>()) { body.useGravity = false; body.isKinematic = true; }
            var renderers = model.GetComponentsInChildren<Renderer>(); Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = preview.camera; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear; camera.allowHDR = false;
            camera.cullingMask = 1 << 29;
            camera.orthographic = true; camera.orthographicSize = bounds.size.magnitude * 0.58f;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = 10f;
            camera.transform.position = bounds.center + new Vector3(1.5f, 1.05f, -1.6f); camera.transform.LookAt(bounds.center);
            var settings = new[] { new Vector4(40, -35, 0, 1.3f), new Vector4(20, 140, 0, 0.8f) };
            for (int i = 0; i < settings.Length; i++)
            {
                var setting = settings[i]; var light = preview.lights[i];
                light.transform.rotation = Quaternion.Euler(setting.x, setting.y, setting.z);
                light.intensity = setting.w; light.shadows = LightShadows.None;
                light.cullingMask = 1 << 29;
            }
            preview.ambientColor = new Color(0.65f, 0.69f, 0.74f);
            preview.BeginPreview(new Rect(0, 0, 256, 256), GUIStyle.none); preview.Render(true);
            var rendered = (RenderTexture)preview.EndPreview(); RenderTexture.active = rendered;
            image = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0); image.Apply();
            int visible = 0; foreach (var pixel in image.GetPixels32()) if (pixel.a > 0) visible++;
            if (visible < image.width * image.height / 100) throw new System.InvalidOperationException("The " + name + " preview contains no visible product.");
            File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) Object.DestroyImmediate(image);
            preview.Cleanup();
        }
    }
}
