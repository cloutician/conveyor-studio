using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorPhysicsSetup
{
    public const string BoxPath = "Assets/Prefabs/PhysicalBox.prefab", CanPath = "Assets/Prefabs/PhysicalCan.prefab";
    private const string Folder = "Assets/Resources/Physics";
    static ConveyorPhysicsSetup() => EditorApplication.delayCall += FirstBuild;
    private static void FirstBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += FirstBuild; return; }
        if (!File.Exists(BoxPath) || !File.Exists(CanPath)) Build();
        else { ConfigureProjectPhysics(); AssetDatabase.SaveAssets(); }
    }
    [MenuItem("Tools/Conveyor Studio/Build Physical Products %#F7")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            CreateMaterial("Cardboard", 0.60f, 0.45f, 0.04f);
            CreateMaterial("Can", 0.45f, 0.32f, 0.08f);
            CreateMaterial("Rubber", 0.85f, 0.70f, 0.015f);
            CreateMaterial("Steel", 0.45f, 0.30f, 0.04f);
            CreateMaterial("Tile", 0.90f, 0.80f, 0f, PhysicsMaterialCombine.Maximum);
            var box = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProductBox.prefab"), scene);
            PrefabUtility.UnpackPrefabInstance(box, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            Object.DestroyImmediate(box.GetComponent<ConveyorProductMover>());
            box.name = "Physical Box";
            Bounds bb = BoundsOf(box);
            var boxCollider = box.AddComponent<BoxCollider>(); boxCollider.center = bb.center; boxCollider.size = bb.size;
            boxCollider.contactOffset = 0.001f; boxCollider.sharedMaterial = ConveyorCollision.Material("Cardboard");
            ConfigureBody(box, ProductKind.Box, bb, 2f);
            PrefabUtility.SaveAsPrefabAsset(box, BoxPath); Object.DestroyImmediate(box);

            var can = new GameObject("Physical Can"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(can, scene);
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VACAC/BOUTIQUE/Products/VA_Slim_Can.fbx"), can.transform);
            visual.name = "Visual";
            Bounds cb = BoundsOf(can);
            // The imported can is already upright. Preserve the supplied dimensions
            // when they are in a practical product range, rather than scaling it by eye.
            if (cb.size.y < 0.04f || cb.size.y > 0.5f) { visual.transform.localScale *= 0.14f / cb.size.y; cb = BoundsOf(can); }
            visual.transform.position -= new Vector3(cb.center.x, cb.min.y, cb.center.z); cb = BoundsOf(can);
            var hull = CanHull(cb);
            string hullPath = Folder + "/CanCollisionHull.asset";
            var oldHull = AssetDatabase.LoadAssetAtPath<Mesh>(hullPath);
            if (oldHull == null) { AssetDatabase.CreateAsset(hull, hullPath); oldHull = hull; }
            else { EditorUtility.CopySerialized(hull, oldHull); Object.DestroyImmediate(hull); }
            var canProxy = new GameObject("Precision can collision"); canProxy.transform.SetParent(can.transform, false);
            canProxy.transform.localScale = new Vector3(0.009f, 0.01f, 0.009f);
            var canCollider = canProxy.AddComponent<MeshCollider>(); canCollider.sharedMesh = oldHull; canCollider.convex = true;
            canCollider.contactOffset = 0.0007f; canCollider.sharedMaterial = ConveyorCollision.Material("Can");
            // Smooth side contact avoids artificial energy loss at polygon edges.
            // The slightly inset hull supplies the flat ends and shoulder contacts.
            var roundSide = can.AddComponent<CapsuleCollider>(); roundSide.direction = 1;
            roundSide.center = cb.center; roundSide.radius = Mathf.Min(cb.extents.x, cb.extents.z); roundSide.height = cb.size.y;
            roundSide.contactOffset = 0.0003f; roundSide.sharedMaterial = ConveyorCollision.Material("Can");
            ConfigureBody(can, ProductKind.Can, cb, 0.27f);
            PrefabUtility.SaveAsPrefabAsset(can, CanPath); Object.DestroyImmediate(can);
            var catalog = AssetDatabase.LoadAssetAtPath<ConveyorCatalog>(ConveyorStudioBuilder.CatalogPath);
            catalog.productPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoxPath);
            catalog.canPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CanPath); EditorUtility.SetDirty(catalog);
            var app = PrefabUtility.LoadPrefabContents(ConveyorStudioBuilder.AppPath);
            try
            {
                foreach (var collider in app.GetComponentsInChildren<MeshCollider>())
                { collider.sharedMaterial = ConveyorCollision.Material("Tile"); collider.contactOffset = 0.001f; }
                PrefabUtility.SaveAsPrefabAsset(app, ConveyorStudioBuilder.AppPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(app); }
            ConfigureProjectPhysics(); AssetDatabase.SaveAssets();
            File.WriteAllText("Library/ConveyorPhysicsSetupReport.json", JsonUtility.ToJson(new Report
            { success = true, boxSize = bb.size, canSize = cb.size, boxMassKg = 2f, filledCanMassKg = 0.27f, physicsHz = 120 }, true));
            SessionState.SetBool("ConveyorStudio.PhysicsProbe.v1", true);
            Debug.Log("Built physical box and can prefabs, surface materials and 120 Hz physics settings. Add box / Add can now use rigid bodies.");
        }
        catch (Exception error) { Debug.LogException(error); }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    [Serializable] private class Report { public bool success; public Vector3 boxSize, canSize; public float boxMassKg, filledCanMassKg; public int physicsHz; }
    private static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        return new Bounds(root.transform.InverseTransformPoint(bounds.center), bounds.size);
    }
    private static void ConfigureBody(GameObject root, ProductKind kind, Bounds bounds, float mass)
    {
        var body = root.AddComponent<Rigidbody>(); body.mass = mass; body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = kind == ProductKind.Box ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.ContinuousSpeculative;
        body.linearDamping = 0.01f; body.angularDamping = 0.015f; body.maxAngularVelocity = 100f;
        body.solverIterations = 16; body.solverVelocityIterations = 8;
        body.maxDepenetrationVelocity = 2f; body.sleepThreshold = 0.0002f;
        body.centerOfMass = bounds.center; body.inertiaTensorRotation = Quaternion.identity;
        Vector3 s = bounds.size;
        if (kind == ProductKind.Box) body.inertiaTensor = new Vector3(s.y * s.y + s.z * s.z, s.x * s.x + s.z * s.z, s.x * s.x + s.y * s.y) * (mass / 12f);
        else
        {
            float r = Mathf.Min(s.x, s.z) * 0.5f;
            float side = mass * (3f * r * r + s.y * s.y) / 12f;
            body.inertiaTensor = new Vector3(side, mass * r * r * 0.5f, side);
        }
        var product = root.AddComponent<PhysicsProduct>(); product.kind = kind; product.size = s;
    }
    private static Mesh CanHull(Bounds bounds)
    {
        const int n = 64;
        var vertices = new Vector3[n * 2]; var triangles = new System.Collections.Generic.List<int>();
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        for (int i = 0; i < n; i++)
        {
            float angle = i * Mathf.PI * 2f / n;
            vertices[i] = bounds.center + new Vector3(Mathf.Cos(angle) * radius, -bounds.extents.y, Mathf.Sin(angle) * radius);
            vertices[i + n] = bounds.center + new Vector3(Mathf.Cos(angle) * radius, bounds.extents.y, Mathf.Sin(angle) * radius);
            int j = (i + 1) % n; triangles.AddRange(new[] { i, i + n, j, j, i + n, j + n });
            if (i > 0 && i < n - 1) triangles.AddRange(new[] { 0, i, i + 1, n, n + i + 1, n + i });
        }
        // PhysX's absolute hull tolerance must not collapse a 60 mm cylinder's rim.
        // Cook a larger canonical hull and apply its exact inverse on the collider child.
        for (int i = 0; i < vertices.Length; i++) vertices[i] *= 100f;
        var mesh = new Mesh { name = "64-sided flat-ended can collision hull" }; mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateBounds(); return mesh;
    }
    private static void CreateMaterial(string name, float stat, float dyn, float bounce, PhysicsMaterialCombine combine = PhysicsMaterialCombine.Average)
    {
        string path = Folder + "/" + name + ".physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null) { material = new PhysicsMaterial(name); AssetDatabase.CreateAsset(material, path); }
        material.staticFriction = stat; material.dynamicFriction = dyn; material.bounciness = bounce;
        material.frictionCombine = combine; material.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(material);
    }
    private static void ConfigureProjectPhysics()
    {
        var physics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        physics.FindProperty("m_DefaultSolverIterations").intValue = 16;
        physics.FindProperty("m_DefaultSolverVelocityIterations").intValue = 8;
        physics.FindProperty("m_SolverType").intValue = 1; // Temporal Gauss-Seidel.
        physics.FindProperty("m_ImprovedPatchFriction").boolValue = true;
        physics.FindProperty("m_EnableEnhancedDeterminism").boolValue = true;
        physics.ApplyModifiedPropertiesWithoutUndo();
        var time = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        var step = time.FindProperty("Fixed Timestep");
        if (step.propertyType == SerializedPropertyType.Float) step.floatValue = 1f / 120f;
        else
        {
            // Unity 6.6 serializes fixed time as integer rational ticks.
            long numerator = step.FindPropertyRelative("m_Rate.m_Numerator").longValue;
            long denominator = step.FindPropertyRelative("m_Rate.m_Denominator").longValue;
            step.FindPropertyRelative("m_Count").longValue = numerator / (120L * denominator);
        }
        time.FindProperty("Maximum Allowed Timestep").floatValue = 0.1f; time.ApplyModifiedPropertiesWithoutUndo();
    }
}
