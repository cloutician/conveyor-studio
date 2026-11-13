using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorTransferVerification
{
    private const string Key = "ConveyorStudio.TransferProbe.v1";
    private static readonly Vector3 Origin = new Vector3(1000, 0, 0);
    private static readonly List<Result> results = new List<Result>();
    private static Scene scene;
    private static GameObject root;
    private static int stage;
    [Serializable] private sealed class Result { public string pairing, product, evidence; public bool passed; }
    [Serializable] private sealed class LiveModule { public string name, previous, next; public bool connected, running, editing, bridge; public float speed; }
    [Serializable] private sealed class Report { public bool success; public string error; public Result[] checks; public LiveModule[] liveModules; }
    private static LiveModule[] liveModules;
    static ConveyorTransferVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify All Product Transfers %#&F12")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("All product-transfer verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        Cleanup(); results.Clear(); stage = 0;
        var inventory = new List<LiveModule>();
        foreach (var item in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (var model in item.GetComponentsInChildren<PlacedConveyor>())
        {
            var section = model.GetComponent<ConveyorSection>();
            var bridge = model.transform.Find("Powered transfer rollers");
            inventory.Add(new LiveModule { name = model.name, previous = section.Previous != null ? section.Previous.name : null,
                next = section.Next != null ? section.Next.name : null, connected = section.TryGetNext(out _),
                running = section.BeltSurface.IsRunning, speed = section.BeltSurface.EffectiveSpeed, editing = model.IsEditing,
                bridge = bridge != null && bridge.gameObject.activeInHierarchy });
        }
        liveModules = inventory.ToArray();
        scene = SceneManager.CreateScene("Product-transfer verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        EditorApplication.update += Tick;
    }
    private static string Path(ConveyorKind kind) => kind == ConveyorKind.Generic ? ConveyorModuleBuilder.GenericPath
        : kind == ConveyorKind.Short ? ConveyorModuleBuilder.ShortPath : ConveyorModuleBuilder.InclinePath;
    private static ConveyorCollision Belt(ConveyorKind kind)
    {
        var item = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path(kind)), Origin, Quaternion.identity, root.transform);
        var belt = item.GetComponent<ConveyorCollision>(); belt.Section.BeltSurface.Speed = 0.5f; return belt;
    }
    private static void Tick()
    {
        try
        {
            foreach (var item in scene.GetRootGameObjects()) Object.DestroyImmediate(item);
            root = new GameObject("Temporary transfer fixture"); SceneManager.MoveGameObjectToScene(root, scene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.SetParent(root.transform); ground.transform.position = Origin; ground.transform.localScale = Vector3.one * 3;
            ground.GetComponent<Collider>().sharedMaterial = ConveyorCollision.Material("Tile");
            ConveyorKind from = (ConveyorKind)(stage / 6), to = (ConveyorKind)((stage / 2) % 3);
            ProductKind kind = stage % 2 == 0 ? ProductKind.Box : ProductKind.Can;
            var a = Belt(from); var b = Belt(to);
            if (!b.Section.TrySnapAfter(a.Section, out var reason)) throw new Exception(reason);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kind == ProductKind.Box ? ConveyorPhysicsSetup.BoxPath : ConveyorPhysicsSetup.CanPath);
            Pose pose = a.InfeedPose(prefab.GetComponent<PhysicsProduct>().size.z);
            var product = Object.Instantiate(prefab, pose.position, pose.rotation, root.transform).GetComponent<PhysicsProduct>();
            product.Body.interpolation = RigidbodyInterpolation.None;
            product.Body.linearVelocity = a.Section.BeltSurface.TopTravelDirection * 0.5f;
            var probe = product.gameObject.AddComponent<ConveyorLandingProbe>();
            bool touched = false; float farthest = float.NegativeInfinity;
            const float dt = 1f / 120f;
            for (int i = 0; i < 1560; i++)
            {
                a.PreparePhysics(); b.PreparePhysics(); product.ApplyResistance(dt);
                ConveyorPhysicsWorld.Publish(); Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(dt);
                touched |= product.CurrentConveyor == b.Section;
                farthest = Mathf.Max(farthest, Vector3.Dot(product.Body.worldCenterOfMass - b.Section.Entry.position, b.Section.BeltSurface.TopTravelDirection));
            }
            results.Add(new Result { pairing = from + " → " + to, product = kind.ToString(),
                // Require the complete product to clear the join. A narrow can may
                // legitimately tip further along an incline after completing transfer.
                passed = touched && farthest > (kind == ProductKind.Box ? 0.7f : 0.2f),
                evidence = "downstream contact=" + touched + "; progress=" + farthest + "; final=" + (product.Body.position - Origin)
                    + "; velocity=" + product.Body.linearVelocity + "; wall=" + probe.lastWallContact + "; normal=" + probe.lastWallNormal });
            if (++stage >= 18) Finish(null);
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    private static void Finish(string error)
    {
        bool success = error == null;
        foreach (var result in results) success &= result.passed;
        File.WriteAllText("Library/ConveyorTransferRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = results.ToArray(), liveModules = liveModules }, true));
        if (success) Debug.Log("Product-transfer verification passed: all 18 box/can conveyor pairings.");
        else Debug.LogError("Product-transfer verification failed; see Library/ConveyorTransferRuntimeReport.json. " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        root = null;
    }
}
