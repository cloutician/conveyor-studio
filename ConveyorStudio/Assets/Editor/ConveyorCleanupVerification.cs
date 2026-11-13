using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Real contact and rendered-material checks use a separate physics scene and game clock.
[InitializeOnLoad]
public static class ConveyorCleanupVerification
{
    private const string Key = "ConveyorStudio.CleanupProbe.v1";
    private static readonly Vector3 Origin = new Vector3(1000f, 0f, 0f);
    private static readonly List<PhysicsProduct> products = new List<PhysicsProduct>();
    private static readonly List<Result> checks = new List<Result>();
    private static Scene scene;
    private static GameObject root;
    private static PhysicsProduct bottom, middle, top, lateCan, neighbour, beltProduct;
    private static PhysicsProduct arrivalBase, arrivalTop, arrivalFirst, arrivalSecond, pendingArrival, beltContactTarget, finalCan;
    private static PhysicsProduct sideA, sideB, floorFirst, fadingBase, fadeArrival;
    private static int pileStage;
    private static double sideDeadline, committedDeadline;
    private static float lastFadeOpacity;
    private static bool fadeStayedMonotonic;
    private static ConveyorCollision belt;
    private static Material sourceMaterial;
    private static float sourceAlpha;
    private static double started;
    private static int stage;
    private static int arrivalStage;
    private static double arrivalDeadline, secondArrivalAt;
    [Serializable] private sealed class Result { public string name, evidence; public bool passed; }
    [Serializable] private sealed class Report { public bool success; public string error; public Result[] checks; }

    static ConveyorCleanupVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode) Cleanup();
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Key, false))
            { SessionState.SetBool(Key, false); Begin(); }
        };
    }
    [MenuItem("Tools/Conveyor Studio/Verify Stack Cleanup %#F6")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying) Begin();
        else { SessionState.SetBool(Key, true); Debug.Log("Stack cleanup verification armed for the next Play session."); }
    }
    private static PhysicsProduct Product(ProductKind kind, Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kind == ProductKind.Box ? ConveyorPhysicsSetup.BoxPath : ConveyorPhysicsSetup.CanPath);
        var product = Object.Instantiate(prefab, Origin + position, Quaternion.identity, root.transform).GetComponent<PhysicsProduct>();
        product.Body.interpolation = RigidbodyInterpolation.None; products.Add(product); return product;
    }
    private static void Step(int count)
    {
        const float dt = 1f / 120f;
        for (int i = 0; i < count; i++)
        {
            if (belt != null) belt.PreparePhysics();
            foreach (var product in products) if (product != null) product.ApplyResistance(dt);
            ConveyorPhysicsWorld.Publish(); Physics.SyncTransforms(); scene.GetPhysicsScene().Simulate(dt);
        }
    }
    private static void Begin()
    {
        Cleanup(); checks.Clear(); stage = arrivalStage = pileStage = 0;
        lastFadeOpacity = 1f; fadeStayedMonotonic = true;
        try
        {
            scene = SceneManager.CreateScene("Stack cleanup verification", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            root = new GameObject("Temporary stack cleanup fixture"); SceneManager.MoveGameObjectToScene(root, scene);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.SetParent(root.transform); ground.transform.position = Origin; ground.transform.localScale = Vector3.one * 3;
            ground.GetComponent<Collider>().sharedMaterial = ConveyorCollision.Material("Tile");
            bottom = Product(ProductKind.Box, new Vector3(0, 0.005f, 0));
            middle = Product(ProductKind.Box, new Vector3(0, bottom.size.y + 0.012f, 0));
            top = Product(ProductKind.Box, new Vector3(0, bottom.size.y * 2f + 0.02f, 0));
            arrivalBase = Product(ProductKind.Box, new Vector3(-4, 0.005f, 0));
            arrivalTop = Product(ProductKind.Box, new Vector3(-4, bottom.size.y + 0.012f, 0));
            beltContactTarget = Product(ProductKind.Box, new Vector3(6, 0.005f, 0));
            sideA = Product(ProductKind.Box, new Vector3(-9, 0.005f, 0));
            sideB = Product(ProductKind.Box, new Vector3(-9 + bottom.size.x * 0.99f, 0.005f, 0));
            fadingBase = Product(ProductKind.Box, new Vector3(-12, 0.005f, 0));
            var module = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorModuleBuilder.GenericPath), Origin + Vector3.right * 4f, Quaternion.identity, root.transform);
            belt = module.GetComponent<ConveyorCollision>(); belt.Section.BeltSurface.IsRunning = false;
            var definition = AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorPhysicsSetup.BoxPath).GetComponent<PhysicsProduct>();
            Pose pose = belt.InfeedPose(definition.size.z);
            beltProduct = Product(ProductKind.Box, pose.position - Origin); beltProduct.transform.rotation = pose.rotation;
            sourceMaterial = definition.GetComponentInChildren<Renderer>().sharedMaterial;
            sourceAlpha = Alpha(sourceMaterial);
            started = Time.timeAsDouble; Step(600);
            Check(Mathf.Abs(top.Body.position.y - bottom.size.y * 2f) < 0.01f, "Three boxes form a physical stack", "top base y=" + top.Body.position.y);
            EditorApplication.update += Tick;
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    private static float Alpha(Material material) => material.GetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color").a;
    private static float Alpha(PhysicsProduct product) => Alpha(product.GetComponentInChildren<Renderer>().sharedMaterial);
    private static void Check(bool passed, string name, string evidence) => checks.Add(new Result { passed = passed, name = name, evidence = evidence });
    private static double FadeAt(PhysicsProduct product) => (double)typeof(PhysicsProduct).GetField("fadeStartsAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(product);
    private static void Move(PhysicsProduct product, Vector3 point)
    {
        product.Body.position = point; product.Body.linearVelocity = product.Body.angularVelocity = Vector3.zero;
        product.Body.WakeUp();
    }
    private static void TickArrivals(double elapsed)
    {
        if (arrivalStage == 0 && elapsed >= 4.0)
        {
            double original = FadeAt(arrivalBase), canDeadline = FadeAt(bottom), beltDeadline = FadeAt(beltContactTarget);
            Check(beltProduct.CurrentConveyor != null, "The exclusion probe is genuinely touching a conveyor", "conveyor=" + beltProduct.CurrentConveyor);
            finalCan = Product(ProductKind.Can, lateCan.Body.position - Origin + Vector3.up * (lateCan.size.y + 0.01f));
            Move(beltContactTarget, beltProduct.Body.position + Vector3.right * (beltProduct.size.x * 0.98f));
            arrivalFirst = Product(ProductKind.Box, arrivalTop.Body.position - Origin + Vector3.up * (arrivalTop.size.y + 0.01f));
            Step(180); arrivalDeadline = FadeAt(arrivalBase);
            Check(arrivalDeadline > original + 0.8 && System.Math.Abs(arrivalDeadline - Time.timeAsDouble - 2.0) < 0.12,
                "A fresh off-belt box in the final two seconds gives its pile two more seconds", "original=" + original + ", extended=" + arrivalDeadline);
            Check(System.Math.Abs(FadeAt(arrivalTop) - arrivalDeadline) < 0.001 && System.Math.Abs(FadeAt(arrivalFirst) - arrivalDeadline) < 0.001,
                "The complete stack and its new box inherit the extended deadline", "deadline=" + arrivalDeadline);
            Check(System.Math.Abs(FadeAt(bottom) - canDeadline) < 0.001 && System.Math.Abs(FadeAt(finalCan) - canDeadline) < 0.001,
                "A late can joins cleanup without triggering the fresh-box grace period", "deadline=" + FadeAt(bottom));
            Check(System.Math.Abs(FadeAt(beltContactTarget) - beltDeadline) < 0.001 && FadeAt(beltProduct) == 0.0,
                "A box still touching a conveyor cannot postpone another box's cleanup", "deadline=" + FadeAt(beltContactTarget));
            arrivalStage++;
        }
        else if (arrivalStage == 1 && Time.timeAsDouble >= arrivalDeadline - 0.65)
        {
            double previous = arrivalDeadline;
            arrivalSecond = Product(ProductKind.Box, arrivalFirst.Body.position - Origin + Vector3.up * (arrivalFirst.size.y + 0.01f));
            secondArrivalAt = Time.timeAsDouble; Step(180); arrivalDeadline = FadeAt(arrivalBase);
            Check(arrivalDeadline > previous + 1.1 && System.Math.Abs(arrivalDeadline - secondArrivalAt - 2.0) < 0.12,
                "Each genuinely new box restarts the two-second quiet period", "previous=" + previous + ", latest=" + arrivalDeadline);
            Check(Alpha(arrivalBase) > 0.99f && Alpha(arrivalFirst) > 0.99f,
                "Fresh arrivals keep the pile opaque before fading starts", "alpha=" + Alpha(arrivalBase));
            arrivalStage++;
        }
        else if (arrivalStage == 2 && Time.timeAsDouble >= secondArrivalAt + 0.3)
        {
            Move(pendingArrival, arrivalSecond.Body.position + Vector3.up * (arrivalSecond.size.y + 0.015f)); Step(180);
            double joined = FadeAt(arrivalBase);
            Move(arrivalFirst, arrivalFirst.Body.position + Vector3.up * 0.04f); Step(180);
            arrivalDeadline = FadeAt(arrivalBase);
            Check(System.Math.Abs(arrivalDeadline - joined) < 0.03,
                "Repeated contacts between members of the same pile cannot extend its deadline", "before=" + joined + ", after=" + arrivalDeadline);
            Check(System.Math.Abs(FadeAt(pendingArrival) - arrivalDeadline) < 0.001,
                "A floor-first box joins the complete pile's shared deadline", "deadline=" + FadeAt(pendingArrival));
            arrivalStage++;
        }
        else if (arrivalStage == 3 && Time.timeAsDouble >= arrivalDeadline + 0.35)
        {
            float alpha = Alpha(arrivalBase);
            Check(alpha > 0.02f && alpha < 0.98f && System.Math.Abs(Alpha(arrivalTop) - alpha) < 0.02f
                && System.Math.Abs(Alpha(arrivalFirst) - alpha) < 0.02f && System.Math.Abs(Alpha(arrivalSecond) - alpha) < 0.02f,
                "After two quiet seconds the delayed pile still fades together smoothly", "alpha=" + alpha);
            arrivalStage++;
        }
        else if (arrivalStage == 4 && Time.timeAsDouble >= arrivalDeadline + 0.9)
        {
            Check(arrivalBase == null && arrivalTop == null && arrivalFirst == null && arrivalSecond == null && pendingArrival == null,
                "The delayed pile is removed once no further fresh box arrives", "time=" + Time.timeAsDouble);
            arrivalStage++;
        }
    }
    private static void TickCongestion(double elapsed)
    {
        if (pileStage == 0 && elapsed >= 4.0)
        {
            Check(System.Math.Abs(FadeAt(sideA) - FadeAt(sideB)) < 0.001,
                "Sideways touching boxes share a cleanup batch before any new arrival", "deadlines=" + FadeAt(sideA) + ", " + FadeAt(sideB));
            floorFirst = Product(ProductKind.Box, new Vector3(-10.5f, 0.005f, 0)); Step(120);
            double freshFloorDeadline = FadeAt(floorFirst);
            Move(floorFirst, sideA.Body.position - Vector3.right * (sideA.size.x * 0.99f)); Step(180);
            sideDeadline = FadeAt(sideA);
            Check(sideDeadline >= freshFloorDeadline && System.Math.Abs(FadeAt(sideB) - sideDeadline) < 0.001
                && System.Math.Abs(FadeAt(floorFirst) - sideDeadline) < 0.001,
                "A floor-first sideways arrival merges old and new boxes into one congestion batch", "shared deadline=" + sideDeadline);
            pileStage++;
        }
        else if (pileStage == 1 && elapsed >= 5.35)
        {
            Check(Alpha(fadingBase) > 0.02f && Alpha(fadingBase) < 0.98f,
                "A separate pile has genuinely started fading before its late arrival", "alpha=" + Alpha(fadingBase));
            committedDeadline = FadeAt(fadingBase); lastFadeOpacity = Alpha(fadingBase);
            fadeArrival = Product(ProductKind.Box, fadingBase.Body.position - Origin + Vector3.up * (fadingBase.size.y + 0.01f)); Step(180);
            Check(System.Math.Abs(FadeAt(fadingBase) - committedDeadline) < 0.001,
                "A new box cannot postpone a fade that has already started", "deadline=" + FadeAt(fadingBase));
            Check(FadeAt(fadeArrival) == 0.0 && Alpha(fadeArrival) > 0.99f,
                "The newcomer stays opaque and does not inherit the already-fading batch", "deadline=" + FadeAt(fadeArrival));
            pileStage++;
        }
        else if (pileStage == 2 && elapsed >= 6.0)
        {
            Check(fadingBase == null && fadeStayedMonotonic,
                "The original box never regains opacity and is removed on its original fade schedule", "monotonic=" + fadeStayedMonotonic);
            Check(fadeArrival != null && Alpha(fadeArrival) > 0.99f,
                "The late arrival survives the older box's completed fade", "alive=" + (fadeArrival != null));
            Step(180);
            Check(FadeAt(fadeArrival) >= Time.timeAsDouble + 4.9,
                "The late arrival starts its own five-second countdown when it reaches the floor", "deadline=" + FadeAt(fadeArrival));
            Check(sideA != null && sideB != null && floorFirst != null && Alpha(sideA) > 0.99f,
                "Congested older boxes survive their original deadlines alongside floor-first arrivals", "shared deadline=" + sideDeadline);
            pileStage++;
        }
        else if (pileStage == 3 && Time.timeAsDouble >= sideDeadline + 0.35)
        {
            Check(System.Math.Abs(Alpha(sideA) - Alpha(sideB)) < 0.02f && System.Math.Abs(Alpha(sideA) - Alpha(floorFirst)) < 0.02f
                && Alpha(sideA) > 0.02f && Alpha(sideA) < 0.98f,
                "The sideways conveyor-exit congestion batch fades in exact synchrony", "alpha=" + Alpha(sideA));
            pileStage++;
        }
        else if (pileStage == 4 && Time.timeAsDouble >= sideDeadline + 0.9)
        {
            Check(sideA == null && sideB == null && floorFirst == null,
                "All congestion batches finish together after their shared quiet period", "time=" + Time.timeAsDouble);
            Check(fadingBase == null && fadeArrival != null && Alpha(fadeArrival) > 0.99f,
                "The later cleanup batch remains independent of both older batches", "new deadline=" + FadeAt(fadeArrival));
            pileStage++;
        }
        if (pileStage >= 2 && fadingBase != null)
        {
            float opacity = Alpha(fadingBase);
            fadeStayedMonotonic &= opacity <= lastFadeOpacity + 0.0001f;
            lastFadeOpacity = opacity;
        }
    }
    private static void Tick()
    {
        try
        {
            double elapsed = Time.timeAsDouble - started;
            TickArrivals(elapsed);
            TickCongestion(elapsed);
            if (stage == 0 && elapsed >= 2.5)
            {
                lateCan = Product(ProductKind.Can, new Vector3(0, bottom.size.y * 3f + 0.01f, 0));
                neighbour = Product(ProductKind.Box, new Vector3(bottom.size.x + 0.08f, 0.005f, 0));
                pendingArrival = Product(ProductKind.Box, new Vector3(-6, 0.005f, 0));
                Step(360); stage++;
                Check(lateCan.Body.position.y > bottom.size.y * 2.8f, "A later can settles on the existing stack", "can base y=" + lateCan.Body.position.y);
            }
            else if (stage == 1 && elapsed >= 4.6)
            {
                Check(bottom != null && middle != null && top != null && lateCan != null && Alpha(bottom) > 0.99f && Alpha(lateCan) > 0.99f,
                    "The whole stack remains opaque before five seconds", "elapsed=" + elapsed); stage++;
            }
            else if (stage == 2 && elapsed >= 5.35)
            {
                float a = Alpha(bottom), b = Alpha(middle), c = Alpha(top), d = Alpha(lateCan);
                Check(a > 0.02f && a < 0.98f, "Cleanup uses a visible fade rather than an abrupt deletion", "alpha=" + a + ", elapsed=" + elapsed);
                Check(Mathf.Max(Mathf.Abs(a - b), Mathf.Abs(a - c), Mathf.Abs(a - d)) < 0.02f,
                    "All stacked boxes and the later can share the same fade progress", "alphas=" + a + ", " + b + ", " + c + ", " + d);
                Check(neighbour != null && Alpha(neighbour) > 0.99f, "A side-by-side box keeps its independent countdown", "neighbour alpha=" + Alpha(neighbour));
                stage++;
            }
            else if (stage == 3 && elapsed >= 6.0)
            {
                Check(bottom == null && middle == null && top == null && lateCan == null, "The complete stack is removed together after its fade", "elapsed=" + elapsed);
                Check(neighbour != null, "A later neighbouring box survives the stack's removal", "alive=" + (neighbour != null));
                Check(beltProduct != null && Alpha(beltProduct) > 0.99f, "A product supported by a conveyor does not start cleanup", "alive=" + (beltProduct != null));
                Check(Mathf.Abs(Alpha(sourceMaterial) - sourceAlpha) < 0.0001f, "Fading leaves the source material unchanged", "source alpha=" + Alpha(sourceMaterial));
                stage++;
            }
            else if (stage == 4 && elapsed >= 8.6 && arrivalStage == 5 && pileStage == 5)
            {
                Check(neighbour == null, "The independent box expires on its own five-second countdown", "elapsed=" + elapsed);
                Finish(null);
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    private static void Finish(string error)
    {
        bool success = error == null;
        foreach (var check in checks) success &= check.passed;
        File.WriteAllText("Library/ConveyorCleanupRuntimeReport.json", JsonUtility.ToJson(new Report { success = success, error = error, checks = checks.ToArray() }, true));
        if (success) Debug.Log("Stack cleanup verification passed: " + checks.Count + " timing, support, transparency and independent-product checks.");
        else Debug.LogError("Stack cleanup verification failed; see Library/ConveyorCleanupRuntimeReport.json. " + error);
        Cleanup();
    }
    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        products.Clear(); root = null; belt = null;
    }
}
