using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorPhysicsVerification
{
    private const string Key = "ConveyorStudio.PhysicsProbe.v1", Path = "Library/ConveyorPhysicsRuntimeReport.json";
    private const float Dt = 1f / 120f;
    private static readonly Vector3 Origin = Vector3.zero;
    private static readonly List<PhysicsProduct> products = new List<PhysicsProduct>();
    private static readonly List<ConveyorCollision> belts = new List<ConveyorCollision>();
    private static readonly List<Result> results = new List<Result>();
    private static Scene scene;
    private static GameObject root;
    private static int stage;
    [Serializable] private class Result { public string name, evidence; public bool passed; }
    [Serializable] private class Report { public bool success; public string error; public Result[] checks; }
    static ConveyorPhysicsVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Product Physics %#F8")]
    public static void Arm()
    {
        if (EditorApplication.isPlaying) { Cleanup(); Begin(); }
        else { SessionState.SetBool(Key, true); Debug.Log("Rigid-body verification armed for the next Play session."); }
    }
    private static void Begin()
    {
        results.Clear(); stage = 0;
        scene = SceneManager.CreateScene("Conveyor physics verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        EditorApplication.update += Tick;
    }
    private static void Reset(bool floor = true)
    {
        foreach (var item in scene.GetRootGameObjects()) Object.DestroyImmediate(item);
        products.Clear(); belts.Clear(); root = new GameObject("Physics fixture"); SceneManager.MoveGameObjectToScene(root, scene);
        if (floor)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.SetParent(root.transform); ground.transform.position = Origin;
            ground.transform.localScale = Vector3.one * 3;
            var collider = ground.GetComponent<Collider>(); collider.sharedMaterial = ConveyorCollision.Material("Tile"); collider.contactOffset = 0.001f;
            ground.GetComponent<Renderer>().enabled = false;
        }
    }
    private static PhysicsProduct Product(ProductKind kind, Vector3 position, Quaternion? rotation = null)
    {
        string path = kind == ProductKind.Box ? ConveyorPhysicsSetup.BoxPath : ConveyorPhysicsSetup.CanPath;
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), Origin + position, rotation ?? Quaternion.identity, root.transform);
        foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        var product = go.GetComponent<PhysicsProduct>(); product.Body.interpolation = RigidbodyInterpolation.None; products.Add(product); return product;
    }
    private static ConveyorCollision Belt(ConveyorKind kind, Vector3 position)
    {
        string path = kind == ConveyorKind.Generic ? ConveyorModuleBuilder.GenericPath : kind == ConveyorKind.Short ? ConveyorModuleBuilder.ShortPath : ConveyorModuleBuilder.InclinePath;
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), Origin + position, Quaternion.identity, root.transform);
        foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        var belt = go.GetComponent<ConveyorCollision>(); belt.Section.BeltSurface.Speed = 0.65f; belts.Add(belt); return belt;
    }
    private static void Step(int count)
    {
        for (int i = 0; i < count; i++)
        {
            foreach (var belt in belts) belt.PreparePhysics();
            foreach (var product in products) if (product != null) product.ApplyResistance(Dt);
            ConveyorPhysicsWorld.Publish(); Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(Dt);
        }
    }
    private static void Check(bool pass, string name, string evidence)
    {
        results.Add(new Result { passed = pass, name = name, evidence = evidence });
    }
    private static PhysicsProduct Feed(ConveyorCollision belt, ProductKind kind)
    {
        string path = kind == ProductKind.Box ? ConveyorPhysicsSetup.BoxPath : ConveyorPhysicsSetup.CanPath;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<PhysicsProduct>();
        Pose pose = belt.InfeedPose(prefab.size.z);
        return Product(kind, pose.position - Origin, pose.rotation);
    }
    private static PhysicsProduct CentreProduct(ConveyorCollision belt, ProductKind kind)
    {
        Vector3 position = (belt.TopEnd(false) + belt.TopEnd(true)) * 0.5f + belt.SurfaceNormal * 0.014f;
        return Product(kind, position - Origin, Quaternion.LookRotation(belt.Section.BeltSurface.TopTravelDirection, belt.SurfaceNormal));
    }
    private static void VerifySleepingBeltMove(ConveyorKind conveyorKind, ProductKind productKind)
    {
        Reset(); var conveyor = Belt(conveyorKind, Vector3.zero); conveyor.Section.BeltSurface.IsRunning = false;
        var resting = CentreProduct(conveyor, productKind); Step(60);
        resting.Body.Sleep(); float before = resting.Body.worldCenterOfMass.y;
        string label = conveyorKind + " / " + productKind;
        Check(resting.Body.IsSleeping() && resting.CurrentConveyor == conveyor.Section,
            "The move regression starts with a genuinely sleeping, belt-supported product: " + label, "height=" + before);
        conveyor.SetEditing(true); conveyor.transform.position += Vector3.right * 5f;
        Step(12);
        Check(resting.Body.worldCenterOfMass.y < before - 0.02f && resting.Body.linearVelocity.y < -0.2f,
            "A sleeping product starts falling while its stopped belt is still being dragged: " + label,
            "drop=" + (before - resting.Body.worldCenterOfMass.y) + ", velocity=" + resting.Body.linearVelocity);
        conveyor.SetEditing(false); Step(1);
        Check(resting.CurrentConveyor == null, "A moved support leaves no stale conveyor contact: " + label,
            "current conveyor=" + resting.CurrentConveyor);
        Step(180);
        Check(resting.GetComponent<Collider>().bounds.min.y > -0.006f && resting.GetComponent<Collider>().bounds.min.y < 0.015f,
            "The released product reaches the tiled floor: " + label, "bottom=" + resting.GetComponent<Collider>().bounds.min.y);
    }
    private static void Tick()
    {
        try
        {
            switch (stage++)
            {
                case 0:
                    Reset(false);
                    var falling = Product(ProductKind.Box, new Vector3(0, 2, 0)); falling.Body.linearDamping = 0;
                    Step(24); float expected = 2f + 0.5f * Physics.gravity.y * 0.2f * 0.2f;
                    Check(Mathf.Abs(falling.Body.position.y - expected) < 0.015f, "Earth gravity produces ballistic free fall", "y=" + falling.Body.position.y + ", expected≈" + expected);
                    Check(falling.GetComponent<ConveyorProductMover>() == null && !falling.Body.isKinematic && falling.Body.constraints == RigidbodyConstraints.None,
                        "Products have unconstrained dynamic bodies and no path follower", "mass=" + falling.Body.mass + ", inertia=" + falling.Body.inertiaTensor);
                    break;
                case 1:
                    Reset(); var box = Product(ProductKind.Box, new Vector3(-1, 1.5f, 0));
                    var can = Product(ProductKind.Can, new Vector3(1, 1.5f, 0), Quaternion.Euler(0, 0, 35));
                    float minimum = 10;
                    for (int i = 0; i < 600; i++) { Step(1); foreach (var p in products) minimum = Mathf.Min(minimum, p.GetComponentInChildren<Collider>().bounds.min.y); }
                    Check(minimum > -0.006f, "Dropped boxes and cans collide with the floor without tunnelling", "minimum visible bounds y=" + minimum);
                    Check(box.Body.linearVelocity.magnitude < 0.03f && can.Body.position.y > -0.1f, "Dropped products settle instead of bouncing indefinitely", "box speed=" + box.Body.linearVelocity.magnitude + ", can y=" + can.Body.position.y);
                    break;
                case 2:
                    Reset(false); var a = Product(ProductKind.Box, new Vector3(-0.6f, 1, 0)); var b = Product(ProductKind.Box, new Vector3(0.1f, 1, 0));
                    foreach (var p in products) { p.Body.useGravity = false; p.Body.linearDamping = p.Body.angularDamping = 0; }
                    a.Body.linearVelocity = Vector3.right; Step(144);
                    float momentum = a.Body.mass * a.Body.linearVelocity.x + b.Body.mass * b.Body.linearVelocity.x;
                    Check(Mathf.Abs(momentum - a.Body.mass) < 0.03f && b.Body.linearVelocity.x > 0.2f, "Box collisions transfer and conserve linear momentum", "total px=" + momentum + ", receiver vx=" + b.Body.linearVelocity.x);
                    break;
                case 3:
                    Reset(false); var torque = Product(ProductKind.Box, new Vector3(0, 1, 0)); torque.Body.useGravity = false;
                    torque.Body.AddForceAtPosition(Vector3.forward * 0.35f, torque.Body.worldCenterOfMass + Vector3.right * 0.08f, ForceMode.Impulse);
                    Step(36);
                    Check(torque.Body.angularVelocity.magnitude > 0.2f && Quaternion.Angle(Quaternion.identity, torque.Body.rotation) > 3,
                        "An off-centre impulse produces rotation through the inertia tensor", "omega=" + torque.Body.angularVelocity + ", angle=" + Quaternion.Angle(Quaternion.identity, torque.Body.rotation));
                    break;
                case 4:
                    Reset(); var sliding = Product(ProductKind.Box, new Vector3(0, 0.003f, 0)); sliding.Body.linearVelocity = Vector3.forward;
                    Step(180); float distance = sliding.Body.position.z;
                    Check(sliding.Body.linearVelocity.magnitude < 0.03f && distance > 0.03f && distance < 0.5f, "Tile friction dissipates sliding momentum", "travel=" + distance + ", speed=" + sliding.Body.linearVelocity.magnitude);
                    break;
                case 5:
                    var calibration = new List<string>();
                    foreach (var mode in new[] { CollisionDetectionMode.Discrete, CollisionDetectionMode.ContinuousDynamic, CollisionDetectionMode.ContinuousSpeculative })
                    {
                        Reset(); var probe = Product(ProductKind.Can, Vector3.zero, Quaternion.Euler(0, 0, 90));
                        float pr = probe.size.x * 0.5f;
                        probe.rollingResistance = 0; probe.Body.linearDamping = probe.Body.angularDamping = 0; probe.Body.collisionDetectionMode = mode;
                        probe.Body.position = Origin + Vector3.up * (pr + 0.002f) - probe.Body.rotation * probe.Body.centerOfMass;
                        probe.Body.linearVelocity = Vector3.forward * 0.7f; probe.Body.angularVelocity = Vector3.right * (0.7f / pr);
                        Step(72); calibration.Add(mode + ": v=" + probe.Body.linearVelocity + ", w=" + probe.Body.angularVelocity);
                    }
                    File.WriteAllLines("Library/ConveyorCanRollingCalibration.txt", calibration);
                    Reset(); var rolling = Product(ProductKind.Can, Vector3.zero, Quaternion.Euler(0, 0, 90));
                    float radius = rolling.size.x * 0.5f;
                    rolling.Body.position = Origin + Vector3.up * (radius + 0.002f) - rolling.Body.rotation * rolling.Body.centerOfMass;
                    rolling.Body.linearVelocity = Vector3.forward * 0.7f; rolling.Body.angularVelocity = Vector3.right * (0.7f / radius);
                    Step(72); float first = rolling.Body.linearVelocity.magnitude; float firstZ = rolling.Body.position.z;
                    Check(first > 0.25f && firstZ > 0.2f && rolling.Body.angularVelocity.magnitude > 5f, "A sideways can rolls with coupled linear and angular motion", "v=" + first + ", z=" + firstZ + ", omega=" + rolling.Body.angularVelocity.magnitude);
                    Step(480);
                    Check(rolling.Body.linearVelocity.magnitude < first, "Contact rolling resistance gradually removes energy", "initial=" + first + ", later=" + rolling.Body.linearVelocity.magnitude);
                    break;
                case 6:
                    Reset(); var drive = Belt(ConveyorKind.Generic, Vector3.zero); drive.Section.BeltSurface.Speed = 0.5f;
                    var carried = Feed(drive, ProductKind.Box); Step(72);
                    float forward = Vector3.Dot(carried.Body.linearVelocity, drive.Section.BeltSurface.TopTravelDirection);
                    Check(forward > 0.43f && forward < 0.57f, "Belt contact friction accelerates a free body to the belt speed", "forward speed=" + forward);
                    drive.Section.BeltSurface.IsRunning = false; Step(1);
                    float coast = carried.Body.linearVelocity.magnitude;
                    Check(coast > 0.1f && coast < 0.52f, "Stopping a belt preserves product inertia while friction brakes it", "first stopped-step speed=" + coast);
                    Step(48); Check(carried.Body.linearVelocity.magnitude < 0.025f, "Stopped belt friction brings its product to rest", "speed=" + carried.Body.linearVelocity.magnitude);
                    drive.Section.BeltSurface.Speed = 0.8f; drive.Section.BeltSurface.IsRunning = true; Step(60);
                    Check(Vector3.Dot(carried.Body.linearVelocity, drive.Section.BeltSurface.TopTravelDirection) > 0.70f, "A restarted motor wakes and accelerates resting products", "velocity=" + carried.Body.linearVelocity);
                    break;
                case 7:
                    Reset(); var edge = Belt(ConveyorKind.Generic, Vector3.zero); edge.Section.BeltSurface.Speed = 0.8f;
                    var leaving = Feed(edge, ProductKind.Box); Step(720);
                    Check(leaving.Body.position.z > edge.Section.Exit.position.z && leaving.GetComponent<Collider>().bounds.min.y > -0.006f
                        && leaving.GetComponent<Collider>().bounds.min.y < 0.015f, "Products leave open belt ends and land on the floor", "position=" + leaving.Body.position + ", bottom=" + leaving.GetComponent<Collider>().bounds.min.y);
                    break;
                case 8:
                    Reset(); var upstream = Belt(ConveyorKind.Generic, Vector3.zero); var downstream = Belt(ConveyorKind.Short, Vector3.zero);
                    if (!downstream.Section.TrySnapAfter(upstream.Section, out var reason)) throw new Exception(reason);
                    var crossing = Feed(upstream, ProductKind.Can); crossing.Body.linearVelocity = Vector3.forward * 0.65f;
                    bool touchedDownstream = false;
                    for (int i = 0; i < 600; i++) { Step(1); touchedDownstream |= crossing.CurrentConveyor == downstream.Section; }
                    Check(touchedDownstream && crossing.Body.position.z > downstream.Section.Entry.position.z + 0.2f,
                        "A can crosses a powered end-to-end transfer without teleporting", "reached downstream=" + touchedDownstream + ", z=" + crossing.Body.position.z);
                    break;
                case 9:
                    Reset(); var incline = Belt(ConveyorKind.Incline, Vector3.zero); incline.Section.BeltSurface.Speed = 0.6f;
                    var climbing = Feed(incline, ProductKind.Box); climbing.Body.linearVelocity = incline.Section.BeltSurface.TopTravelDirection * 0.6f;
                    float initialHeight = climbing.Body.worldCenterOfMass.y; Step(180);
                    Check(climbing.Body.worldCenterOfMass.y > initialHeight + 0.12f, "Incline contact transfers the motor's uphill velocity", "rise=" + (climbing.Body.worldCenterOfMass.y - initialHeight));
                    break;
                case 10:
                    Reset(); var baseBox = Product(ProductKind.Box, new Vector3(-1, 0.003f, 0));
                    var topBox = Product(ProductKind.Box, new Vector3(-1, baseBox.size.y + 0.015f, 0));
                    var baseCan = Product(ProductKind.Can, new Vector3(1, 0.003f, 0));
                    var topCan = Product(ProductKind.Can, new Vector3(1, baseCan.size.y + 0.008f, 0));
                    Step(600);
                    Check(Mathf.Abs(topBox.Body.position.y - baseBox.size.y) < 0.008f && topBox.Body.linearVelocity.magnitude < 0.03f,
                        "Boxes form a stable collision-supported stack", "top base y=" + topBox.Body.position.y);
                    Check(Mathf.Abs(topCan.Body.position.y - baseCan.size.y) < 0.008f && topCan.Body.linearVelocity.magnitude < 0.03f,
                        "Cans stand and stack on flat ends", "top base y=" + topCan.Body.position.y + ", speed=" + topCan.Body.linearVelocity.magnitude);
                    break;
                case 11:
                    Reset(); var removable = Belt(ConveyorKind.Generic, Vector3.zero); removable.Section.BeltSurface.IsRunning = false;
                    var unsupported = Feed(removable, ProductKind.Box); Step(120);
                    float supported = unsupported.Body.worldCenterOfMass.y;
                    Object.DestroyImmediate(removable.gameObject); belts.Clear(); Step(180);
                    Check(unsupported != null && unsupported.Body.worldCenterOfMass.y < supported - 0.3f
                        && unsupported.GetComponent<Collider>().bounds.min.y > -0.006f,
                        "Removing a supporting machine leaves its product alive to fall onto the floor", "before=" + supported + ", after=" + unsupported.Body.worldCenterOfMass.y);
                    break;
                case 12:
                    Reset(false); var receiver = Product(ProductKind.Box, new Vector3(0, 1, 0));
                    var hitter = Product(ProductKind.Can, new Vector3(-0.5f, 1.05f, 0));
                    foreach (var p in products) { p.Body.useGravity = false; p.Body.linearDamping = p.Body.angularDamping = 0; }
                    hitter.Body.linearVelocity = Vector3.right; Step(120);
                    float total = receiver.Body.mass * receiver.Body.linearVelocity.x + hitter.Body.mass * hitter.Body.linearVelocity.x;
                    Check(receiver.Body.linearVelocity.x > 0.04f && Mathf.Abs(total - hitter.Body.mass) < 0.01f,
                        "Can-to-box impacts transfer momentum between different masses and shapes", "receiver vx=" + receiver.Body.linearVelocity.x + ", momentum=" + total);
                    break;
                case 13:
                    foreach (var kind in new[] { ConveyorKind.Generic, ConveyorKind.Short, ConveyorKind.Incline })
                    foreach (float speed in new[] { 0.5f, 1f, 2f })
                    {
                        Reset(); var conveyor = Belt(kind, Vector3.zero); conveyor.Section.BeltSurface.Speed = speed;
                        var parcel = Feed(conveyor, ProductKind.Box);
                        parcel.Body.linearVelocity = conveyor.Section.BeltSurface.TopTravelDirection * speed;
                        var landing = parcel.gameObject.AddComponent<ConveyorLandingProbe>();
                        for (int i = 0; i < 1080; i++) { Step(1); landing.AfterStep(Dt); }
                        string label = kind + " at " + speed + " m/s";
                        // Allow the final low-amplitude rocking of a tipped box;
                        // excessive translation is checked separately below.
                        Check(landing.landed && landing.settledAt >= 0f && landing.settledAt < 0.8f,
                            "Floor landing settles promptly: " + label,
                            "settled after=" + landing.settledAt + " s, max horizontal travel=" + landing.maximumTravel + " m, impact=" + landing.velocityBefore + ", after=" + landing.velocityAfter + ", angular=" + landing.angularAfter);
                        Check(landing.maximumTravel < 0.3f && parcel.Body.linearVelocity.magnitude < 0.03f,
                            "Floor landing does not keep gliding: " + label,
                            "max horizontal travel=" + landing.maximumTravel + " m, final speed=" + parcel.Body.linearVelocity.magnitude);
                    }
                    break;
                case 14:
                    foreach (var kind in new[] { ConveyorKind.Generic, ConveyorKind.Short, ConveyorKind.Incline })
                        foreach (var productKind in new[] { ProductKind.Box, ProductKind.Can }) VerifySleepingBeltMove(kind, productKind);
                    break;
                case 15:
                    Reset(); var sleepingSupport = Belt(ConveyorKind.Generic, Vector3.zero); sleepingSupport.Section.BeltSurface.IsRunning = false;
                    var stackBase = CentreProduct(sleepingSupport, ProductKind.Box);
                    var stackMiddle = Product(ProductKind.Box, stackBase.Body.position - Origin + Vector3.up * (stackBase.size.y + 0.01f));
                    var stackTop = Product(ProductKind.Box, stackMiddle.Body.position - Origin + Vector3.up * (stackMiddle.size.y + 0.01f));
                    var stackCan = Product(ProductKind.Can, stackTop.Body.position - Origin + Vector3.up * (stackTop.size.y + 0.01f));
                    Step(600);
                    foreach (var product in products) product.Body.Sleep();
                    var heights = new List<float>(); foreach (var product in products) heights.Add(product.Body.worldCenterOfMass.y);
                    Check(products.TrueForAll(product => product.Body.IsSleeping()) && stackBase.CurrentConveyor == sleepingSupport.Section,
                        "A complete box-and-can stack is asleep before its supporting belt is removed", "stack height=" + stackCan.Body.worldCenterOfMass.y);
                    Object.DestroyImmediate(sleepingSupport.gameObject); belts.Clear(); Step(12);
                    bool stackFalls = true;
                    for (int i = 0; i < products.Count; i++) stackFalls &= products[i].Body.worldCenterOfMass.y < heights[i] - 0.02f;
                    Check(stackFalls, "Deleting a belt wakes every layer of a sleeping stack", "top drop=" + (heights[3] - stackCan.Body.worldCenterOfMass.y));
                    Step(240);
                    Check(stackBase.GetComponent<Collider>().bounds.min.y > -0.006f && stackBase.Body.worldCenterOfMass.y < heights[0] - 0.3f,
                        "The released sleeping stack lands on the floor without tunnelling", "bottom=" + stackBase.GetComponent<Collider>().bounds.min.y);
                    break;
                case 16:
                    Reset(); var disappearingBase = Product(ProductKind.Box, new Vector3(0, 0.003f, 0));
                    var survivor = Product(ProductKind.Box, new Vector3(0, disappearingBase.size.y + 0.015f, 0));
                    var sleepingCan = Product(ProductKind.Can, new Vector3(0, disappearingBase.size.y * 2f + 0.03f, 0));
                    var untouched = Product(ProductKind.Box, new Vector3(3, 0.003f, 0)); Step(600);
                    foreach (var product in products) product.Body.Sleep();
                    float survivorHeight = survivor.Body.worldCenterOfMass.y, canHeight = sleepingCan.Body.worldCenterOfMass.y;
                    Object.DestroyImmediate(disappearingBase.gameObject); Step(12);
                    Check(survivor.Body.worldCenterOfMass.y < survivorHeight - 0.02f && sleepingCan.Body.worldCenterOfMass.y < canHeight - 0.02f,
                        "Removing a supporting box wakes the surviving sleeping box and can above it", "box drop=" + (survivorHeight - survivor.Body.worldCenterOfMass.y));
                    Check(untouched.Body.IsSleeping(), "Unrelated resting products keep their normal sleep optimisation", "sleeping=" + untouched.Body.IsSleeping());
                    break;
                case 17:
                    Reset(false); var strandedBox = Product(ProductKind.Box, new Vector3(-1, 2, 0));
                    var strandedCan = Product(ProductKind.Can, new Vector3(1, 2, 0)); Step(1);
                    strandedBox.Body.Sleep(); strandedCan.Body.Sleep();
                    float strandedBoxHeight = strandedBox.Body.position.y, strandedCanHeight = strandedCan.Body.position.y;
                    Check(strandedBox.Body.IsSleeping() && strandedCan.Body.IsSleeping(),
                        "The recovery probe begins with unsupported sleeping products", "box=" + strandedBoxHeight + ", can=" + strandedCanHeight);
                    Step(12);
                    Check(strandedBox.Body.position.y < strandedBoxHeight - 0.02f && strandedCan.Body.position.y < strandedCanHeight - 0.02f,
                        "Unsupported sleeping boxes and cans recover automatically instead of remaining suspended",
                        "box drop=" + (strandedBoxHeight - strandedBox.Body.position.y) + ", can drop=" + (strandedCanHeight - strandedCan.Body.position.y));
                    Finish(true, null); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }
    private static void Finish(bool success, string error)
    {
        if (success)
        {
            foreach (var result in results) if (!result.passed) { success = false; error += result.name + ": " + result.evidence + "\n"; }
        }
        File.WriteAllText(Path, JsonUtility.ToJson(new Report { success = success, error = error, checks = results.ToArray() }, true));
        if (success) Debug.Log("Rigid-body verification passed: " + results.Count + " gravity, momentum, collision, friction, rolling and conveyor checks.");
        else Debug.LogError("Rigid-body verification failed: " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        products.Clear(); belts.Clear();
    }
}
