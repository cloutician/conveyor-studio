using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// An editor-only setup command. The application itself uses ConveyorProductMover.
public static class ConveyorDemoSetup
{
    private const string ModelPath = "Assets/VACAC/BOUTIQUE/Products/VA_Cardboard_Box.fbx";
    private const string PrefabPath = "Assets/Prefabs/ProductBox.prefab";
    private const string DemoName = "ProductBoxDemo";
    private const string SecondConveyorName = "ConveyorStraight_02";

    [MenuItem("Tools/Conveyor Studio/Add Second Conveyor")]
    private static void AddSecondConveyor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            Debug.LogWarning("Return to the main scene in Edit mode before adding the second conveyor.");
            return;
        }

        GameObject product = GameObject.Find(DemoName);
        ConveyorProductMover mover = product != null ? product.GetComponent<ConveyorProductMover>() : null;
        ConveyorSection first = mover != null ? mover.StartingConveyor : null;
        if (first == null || !first.HasEndpoints)
        {
            Debug.LogError("Create the box demo first so its starting conveyor is assigned.");
            return;
        }

        GameObject conveyorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ConveyorStraight.prefab");
        if (conveyorPrefab == null)
        {
            Debug.LogError("ConveyorStraight.prefab could not be loaded.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect second conveyor");

        GameObject secondObject = GameObject.Find(SecondConveyorName);
        if (secondObject == null)
        {
            secondObject = (GameObject)PrefabUtility.InstantiatePrefab(conveyorPrefab);
            secondObject.name = SecondConveyorName;
            Undo.RegisterCreatedObjectUndo(secondObject, "Create second conveyor");

            // Start misaligned so the command exercises both rotation and position snapping.
            secondObject.transform.SetPositionAndRotation(
                new Vector3(3f, 0f, -2f), Quaternion.Euler(0f, 73f, 0f));
        }

        ConveyorSection second = secondObject.GetComponent<ConveyorSection>();
        if (second == null)
        {
            Debug.LogError("The second conveyor needs a ConveyorSection component.");
            return;
        }

        Undo.RecordObjects(new Object[] { first, second, second.transform }, "Snap conveyor connectors");
        if (!second.TrySnapAfter(first, out string reason))
        {
            Debug.LogError(reason, second);
            return;
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(first);
        PrefabUtility.RecordPrefabInstancePropertyModifications(second);
        PrefabUtility.RecordPrefabInstancePropertyModifications(second.transform);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(secondObject.scene);
        Selection.activeGameObject = secondObject;

        Bounds viewBounds = GetRendererBounds(first.transform);
        viewBounds.Encapsulate(GetRendererBounds(second.transform));
        viewBounds.Encapsulate(GetRendererBounds(product.transform));
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.Frame(viewBounds, false);
        }

        float gap = Vector3.Distance(first.Exit.position, second.Entry.position);
        Debug.Log($"Connected two conveyors. Connector gap: {gap:F6} m. Route length: {first.Length + second.Length:F3} m.", second);
    }

    [MenuItem("Tools/Conveyor Studio/Validate Demo Connection")]
    private static void ValidateDemoConnection()
    {
        GameObject product = GameObject.Find(DemoName);
        ConveyorProductMover mover = product != null ? product.GetComponent<ConveyorProductMover>() : null;
        ConveyorSection first = mover != null ? mover.StartingConveyor : null;
        if (first == null || !first.TryGetNext(out ConveyorSection second))
        {
            Debug.LogError("The demo needs two correctly connected conveyors before validation.");
            return;
        }

        // Exercise real connection rules without moving or disconnecting the demo.
        bool rejectsSelf = !first.TrySnapAfter(first, out _);
        bool rejectsCycle = !first.TrySnapAfter(second, out _);
        bool joinValid = first.Next == second && second.Previous == first;
        if (!rejectsSelf || !rejectsCycle || !joinValid)
        {
            Debug.LogError("Connection validation failed: inspect the conveyor graph.");
            return;
        }

        Debug.Log("Connection checks passed: aligned join, reciprocal links, self-connection rejected, cycle rejected.", second);
    }

    [MenuItem("Tools/Conveyor Studio/Create Box Demo")]
    private static void CreateBoxDemo()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Stop Play mode before creating the box demo.");
            return;
        }

        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            Debug.LogWarning("Return to the main scene before creating the box demo.");
            return;
        }

        ConveyorSection conveyor = Object.FindAnyObjectByType<ConveyorSection>();
        if (conveyor == null || !conveyor.HasEndpoints || conveyor.Length < 0.0001f)
        {
            Debug.LogError("The scene needs a conveyor with valid Entry and Exit markers.");
            return;
        }

        GameObject existingDemo = GameObject.Find(DemoName);
        if (existingDemo != null)
        {
            Selection.activeGameObject = existingDemo;
            Debug.Log("The box demo already exists. Select Play to run it.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogWarning("ProductBox.prefab already exists. Drag it into the scene and assign its Conveyor field.");
            return;
        }

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null || model.GetComponentsInChildren<Renderer>(true).Length == 0)
        {
            Debug.LogError("The supplied cardboard box model could not be loaded.");
            return;
        }

        GameObject product = new GameObject("ProductBox");
        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(model, product.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;

        // Preserve the model's imported rotation/scale and calculate its contact point.
        Bounds importedBounds = GetRendererBounds(visual.transform);
        Vector3 bottomCentre = new Vector3(
            importedBounds.center.x, importedBounds.min.y, importedBounds.center.z);
        visual.transform.position += product.transform.position - bottomCentre;

        product.AddComponent<ConveyorProductMover>();
        PrefabUtility.SaveAsPrefabAssetAndConnect(product, PrefabPath, InteractionMode.AutomatedAction);

        // A prefab cannot store a reference to a scene conveyor; assign it on this instance.
        product.name = DemoName;
        ConveyorProductMover mover = product.GetComponent<ConveyorProductMover>();
        SerializedObject serializedMover = new SerializedObject(mover);
        serializedMover.FindProperty("conveyor").objectReferenceValue = conveyor;
        serializedMover.ApplyModifiedProperties();

        product.transform.SetPositionAndRotation(
            conveyor.Entry.position,
            Quaternion.LookRotation(conveyor.Exit.position - conveyor.Entry.position, conveyor.transform.up));
        PrefabUtility.RecordPrefabInstancePropertyModifications(product.transform);

        Undo.RegisterCreatedObjectUndo(product, "Create conveyor box demo");
        EditorSceneManager.MarkSceneDirty(product.scene);
        Selection.activeGameObject = product;

        Bounds viewBounds = GetRendererBounds(conveyor.transform);
        viewBounds.Encapsulate(GetRendererBounds(product.transform));
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.Frame(viewBounds, false);
        }

        Debug.Log($"Created box demo. Imported model size: {importedBounds.size:F3} metres. Speed: 0.5 m/s.", product);
    }

    private static Bounds GetRendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }
}
