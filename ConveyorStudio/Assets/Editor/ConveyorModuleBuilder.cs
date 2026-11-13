using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConveyorModuleBuilder
{
    public const string GenericPath = "Assets/Prefabs/ConveyorStraight.prefab";
    public const string ShortPath = "Assets/Prefabs/ConveyorShort.prefab";
    public const string InclinePath = "Assets/Prefabs/ConveyorIncline.prefab";
    public const string ReviewPath = "Assets/Scenes/ConveyorModulesPreview.unity";
    private const string ReportPath = "Library/ConveyorModulePrefabReport.json";

    [InitializeOnLoadMethod]
    private static void QueueFirstBuild()
    {
        if ((!File.Exists(ShortPath) || !File.Exists(InclinePath)) && File.Exists(GenericPath))
            EditorApplication.delayCall += FirstBuild;
    }

    private static void FirstBuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += FirstBuild;
            return;
        }
        Build();
    }

    [MenuItem("Tools/Conveyor Studio/Build Short and Incline Prefabs")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before building conveyor prefabs.");
            return;
        }
        Scene originalScene = SceneManager.GetActiveScene();
        Scene work = EditorSceneManager.NewPreviewScene();
        try
        {
            GameObject generic = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GenericPath), work);
            Quaternion uprightLegRotation = FindLegs(generic).rotation;
            List<ModuleCheck> modules = new List<ModuleCheck>();
            float standardInfeed = generic.GetComponent<ConveyorSection>().Entry.localPosition.y;
            modules.Add(BuildOne(ConveyorBeltImporter.ModelPaths[1], ShortPath, ConveyorKind.Short, uprightLegRotation, standardInfeed, work));
            modules.Add(BuildOne(ConveyorBeltImporter.ModelPaths[2], InclinePath, ConveyorKind.Incline, uprightLegRotation, standardInfeed, work));
            List<SnapCheck> joins = ValidateJoins(work);
            ConveyorRibSpacingVerification.Validate();
            CreateReviewScene();
            AssetDatabase.SaveAssets();
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new BuildReport { success = true,
                modules = modules.ToArray(), joins = joins.ToArray(), reviewScene = ReviewPath }, true));
            Debug.Log("Created Short and Incline prefabs with grounded origins, measured connectors and matching belt travel. Module preview scene is ready.");
            // This explicit build operation also prepares the next Play session's geometry checks.
            ConveyorBeltVerification.Validate();
            SessionState.SetBool("ConveyorStudio.ModuleRuntimeProbe.v1", true);
        }
        catch (Exception error)
        {
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new BuildReport { success = false, error = error.ToString() }, true));
            Debug.LogException(error);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(work);
            if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
        }
    }

    private static ModuleCheck BuildOne(string modelPath, string path, ConveyorKind kind, Quaternion legRotation, float standardInfeed, Scene work)
    {
        GameObject root = new GameObject(kind == ConveyorKind.Short ? "ConveyorShort" : "ConveyorIncline");
        SceneManager.MoveGameObjectToScene(root, work);
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        Transform legs = FindLegs(visual);
        visual.transform.rotation = legRotation * Quaternion.Inverse(legs.rotation) * visual.transform.rotation;
        ConveyorBeltSurface motor = visual.GetComponentInChildren<ConveyorBeltSurface>(true);
        if (motor == null) throw new Exception("The source model needs its prepared moving belt: " + modelPath);
        GetEndpoints(motor, out Vector3 a, out Vector3 b);
        bool ascending = kind == ConveyorKind.Incline;
        Vector3 input = ascending && a.y > b.y ? b : a;
        Vector3 output = ascending && a.y > b.y ? a : b;
        Vector3 flatDirection = Vector3.ProjectOnPlane(output - input, Vector3.up).normalized;
        float yaw = Vector3.SignedAngle(flatDirection, Vector3.forward, Vector3.up);
        visual.transform.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * visual.transform.rotation;

        GetEndpoints(motor, out a, out b);
        Vector3 midpoint = (a + b) * 0.5f;
        visual.transform.position += new Vector3(-midpoint.x, 0f, -midpoint.z);
        // Use transformed mesh vertices, rather than the loose AABB of a rotated renderer.
        visual.transform.position += Vector3.up * -ExactBounds(visual).min.y;
        GetEndpoints(motor, out a, out b);
        input = a.z < b.z ? a : b;
        output = a.z < b.z ? b : a;
        float supportAdjustment = 0f;
        if (ascending)
        {
            supportAdjustment = standardInfeed - input.y;
            if (Mathf.Abs(supportAdjustment) > 0.08f) throw new Exception("Unexpected incline support adjustment.");
            StandardizeInclineLegs(legs.GetComponent<MeshFilter>(), visual.transform, supportAdjustment);
            GetEndpoints(motor, out a, out b);
            input = a.z < b.z ? a : b;
            output = a.z < b.z ? b : a;
        }
        motor.ReverseDirection = Vector3.Dot(motor.transform.TransformDirection(Vector3.down), output - input) < 0f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
        PrefabUtility.RecordPrefabInstancePropertyModifications(visual.transform);

        Transform entry = MakeMarker("Entry", root.transform, input, Quaternion.Euler(0f, 180f, 0f));
        Transform exit = MakeMarker("Exit", root.transform, output, Quaternion.identity);
        ConveyorSection section = root.AddComponent<ConveyorSection>();
        SerializedObject serialized = new SerializedObject(section);
        serialized.FindProperty("entry").objectReferenceValue = entry;
        serialized.FindProperty("exit").objectReferenceValue = exit;
        serialized.FindProperty("moduleType").enumValueIndex = (int)kind;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (ascending && exit.position.y <= entry.position.y + 0.01f) throw new Exception("Incline must rise towards +Z.");
        if (Vector3.Dot(motor.TopTravelDirection, (exit.position - entry.position).normalized) < 0.9999f)
            throw new Exception("The belt motor is moving against its connector route.");
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        // Validate the saved asset after a real prefab round trip.
        GameObject saved = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), work);
        ConveyorSection checkedSection = saved.GetComponent<ConveyorSection>();
        Bounds bounds = ExactBounds(saved);
        if (Mathf.Abs(bounds.min.y) > 0.00001f || saved.transform.localScale != Vector3.one
            || saved.transform.localPosition != Vector3.zero || Quaternion.Angle(saved.transform.rotation, Quaternion.identity) > 0.001f)
            throw new Exception("The saved prefab needs a clean, grounded root: " + path);
        float tilt = Vector3.Angle(Vector3.ProjectOnPlane(checkedSection.Exit.position - checkedSection.Entry.position, Vector3.up),
            checkedSection.Exit.position - checkedSection.Entry.position);
        ModuleCheck check = new ModuleCheck { prefab = path, type = kind.ToString(), floorY = bounds.min.y,
            boundsSize = bounds.size, entry = checkedSection.Entry.localPosition, exit = checkedSection.Exit.localPosition,
            pathLength = checkedSection.Length, slopeDegrees = tilt, reversedMotor = checkedSection.BeltSurface.ReverseDirection };
        check.supportAdjustmentMetres = supportAdjustment;
        check.maxFootHeight = MaximumFootHeight(saved);
        if (check.maxFootHeight > 0.001f) throw new Exception("A prefab foot is floating above the floor.");
        Object.DestroyImmediate(saved);
        return check;
    }

    private static void StandardizeInclineLegs(MeshFilter legs, Transform visual, float adjustment)
    {
        if (Mathf.Abs(adjustment) < 0.00001f) return;
        Mesh derived = Object.Instantiate(legs.sharedMesh);
        derived.name = "InclineLegs_StandardInfeed";
        Vector3[] vertices = derived.vertices;
        Vector3 extension = legs.transform.InverseTransformVector(Vector3.down * adjustment);
        for (int i = 0; i < vertices.Length; i++)
        {
            float height = legs.transform.TransformPoint(vertices[i]).y;
            // Translate the foot pads rigidly and extend only the lower shaft region.
            float weight = 1f - Mathf.InverseLerp(0.025f, 0.16f, height);
            vertices[i] += extension * weight;
        }
        derived.vertices = vertices;
        derived.RecalculateBounds();
        derived.RecalculateNormals();
        derived.RecalculateTangents();
        const string assetPath = "Assets/Art/Conveyors/InclineLegsStandard.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing == null) AssetDatabase.CreateAsset(derived, assetPath);
        else
        {
            EditorUtility.CopySerialized(derived, existing);
            Object.DestroyImmediate(derived);
            derived = existing;
            EditorUtility.SetDirty(derived);
        }
        legs.sharedMesh = derived;
        PrefabUtility.RecordPrefabInstancePropertyModifications(legs);
        visual.position += Vector3.up * adjustment;
    }

    private static float MaximumFootHeight(GameObject root)
    {
        MeshFilter legs = FindLegs(root).GetComponent<MeshFilter>();
        float[] heights = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        foreach (Vector3 vertex in legs.sharedMesh.vertices)
        {
            Vector3 point = legs.transform.TransformPoint(vertex);
            int quadrant = (point.x >= root.transform.position.x ? 1 : 0) + (point.z >= root.transform.position.z ? 2 : 0);
            heights[quadrant] = Mathf.Min(heights[quadrant], point.y);
        }
        if (heights.Any(float.IsPositiveInfinity)) throw new Exception("Could not identify all four foot positions.");
        return heights.Max();
    }

    private static Transform MakeMarker(string name, Transform parent, Vector3 position, Quaternion rotation)
    {
        Transform marker = new GameObject(name).transform;
        marker.SetParent(parent, false);
        marker.position = position;
        // Connector frames stay level so connecting an incline never tips the whole machine.
        marker.localRotation = rotation;
        return marker;
    }

    private static Transform FindLegs(GameObject root) => root.GetComponentsInChildren<MeshFilter>(true)
        .First(filter => filter.name.StartsWith("Conveyor_Legs", StringComparison.Ordinal)).transform;

    private static void GetEndpoints(ConveyorBeltSurface motor, out Vector3 a, out Vector3 b)
    {
        Bounds bounds = motor.GetComponent<MeshFilter>().sharedMesh.bounds;
        a = motor.transform.TransformPoint(bounds.center + new Vector3(0f, bounds.extents.y, bounds.extents.z));
        b = motor.transform.TransformPoint(bounds.center + new Vector3(0f, -bounds.extents.y, bounds.extents.z));
    }

    public static Bounds ExactBounds(GameObject root)
    {
        Bounds bounds = default;
        bool hasPoint = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 world = filter.transform.TransformPoint(vertex);
                if (!hasPoint) { bounds = new Bounds(world, Vector3.zero); hasPoint = true; }
                else bounds.Encapsulate(world);
            }
        }
        if (!hasPoint) throw new Exception("No mesh geometry in " + root.name);
        return bounds;
    }

    private static List<SnapCheck> ValidateJoins(Scene work)
    {
        List<SnapCheck> checks = new List<SnapCheck>();
        string[] paths = { GenericPath, ShortPath, InclinePath };
        foreach (string before in paths) foreach (string after in paths)
        {
            GameObject a = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(before), work);
            GameObject b = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(after), work);
            a.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
            b.transform.SetPositionAndRotation(new Vector3(4, 0, -3), Quaternion.Euler(0f, -23f, 0f));
            ConveyorSection first = a.GetComponent<ConveyorSection>(), second = b.GetComponent<ConveyorSection>();
            if (!second.TrySnapAfter(first, out string reason) || !first.TryGetNext(out ConveyorSection next) || next != second
                || Vector3.Dot(second.transform.up, Vector3.up) < 0.9999f)
                throw new Exception("Connector validation failed for " + before + " -> " + after + ": " + reason);
            checks.Add(new SnapCheck { from = Path.GetFileNameWithoutExtension(before), to = Path.GetFileNameWithoutExtension(after),
                gapMetres = Vector3.Distance(first.Exit.position, second.Entry.position), downstreamRootHeight = second.transform.position.y });
            Object.DestroyImmediate(a); Object.DestroyImmediate(b);
        }
        return checks;
    }

    private static void CreateReviewScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            string[] paths = { GenericPath, ShortPath, InclinePath };
            string[] names = { "Generic_A", "Short_B", "Incline_C" };
            for (int i = 0; i < 3; i++)
            {
                GameObject module = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]), scene);
                module.name = names[i];
                module.transform.position = new Vector3((i - 1) * 1.7f, 0, 0);
                GameObject product = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProductBox.prefab"), scene);
                product.name = "Product_" + names[i];
                ConveyorSection section = module.GetComponent<ConveyorSection>();
                SerializedObject mover = new SerializedObject(product.GetComponent<ConveyorProductMover>());
                mover.FindProperty("conveyor").objectReferenceValue = section;
                mover.ApplyModifiedPropertiesWithoutUndo();
                product.transform.SetPositionAndRotation(section.Entry.position,
                    Quaternion.LookRotation(section.Exit.position - section.Entry.position, Vector3.up));
                PrefabUtility.RecordPrefabInstancePropertyModifications(product.transform);
            }
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground";
            floor.transform.localScale = new Vector3(1.2f, 1, 1f);
            Camera camera = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Camera>()).First();
            camera.transform.position = new Vector3(5.2f, 4.2f, -6.5f);
            camera.transform.LookAt(new Vector3(0, 0.5f, 0));
            camera.orthographic = true; camera.orthographicSize = 2.1f;
            Light light = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Light>()).First();
            light.transform.rotation = Quaternion.Euler(50, -35, 0);
            light.intensity = 1.7f;
            light.shadows = LightShadows.Soft;
            if (!EditorSceneManager.SaveScene(scene, ReviewPath)) throw new Exception("Could not save module preview scene.");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    [Serializable] private sealed class BuildReport
    {
        public bool success; public string error; public string reviewScene;
        public ModuleCheck[] modules; public SnapCheck[] joins;
    }
    [Serializable] private sealed class ModuleCheck
    {
        public string prefab, type; public Vector3 boundsSize, entry, exit;
        public float floorY, pathLength, slopeDegrees, supportAdjustmentMetres, maxFootHeight; public bool reversedMotor;
    }
    [Serializable] private sealed class SnapCheck
    {
        public string from, to; public float gapMetres, downstreamRootHeight;
    }
}
