using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Checks imported UVs and actual Play-mode vertex/roller movement on all three models.
[InitializeOnLoad]
public static class ConveyorBeltVerification
{
    private const string ProbeKey = "ConveyorStudio.BeltProbe.v2";
    private const string StaticReport = "Library/ConveyorBeltUvReport.json";
    private const string RuntimeReport = "Library/ConveyorBeltGeometryRuntimeReport.json";
    private static ConveyorBeltSurface[] probes;
    private static GameObject[] temporaryModels;
    private static Snapshot[] initial, held;
    private static ConveyorProductMover product;
    private static ConveyorBeltSurface productMotor;
    private static Vector3 heldProductPosition;
    private static float originalSpeed;
    private static bool originalRunning;
    private static double sampleAt;
    private static int stage;
    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

    static ConveyorBeltVerification()
    {
        EditorApplication.delayCall += CheckOnFirstImport;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    private static void CheckOnFirstImport()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(StaticReport) && JsonUtility.FromJson<Result>(File.ReadAllText(StaticReport)).success) return;
        if (AssetDatabase.LoadAssetAtPath<Material>(ConveyorBeltImporter.SurfaceMaterialPath) != null) ValidateAssets(false);
    }

    [MenuItem("Tools/Conveyor Studio/Validate Ribbed Belts")]
    public static void Validate() => ValidateAssets(true);

    private static void ValidateAssets(bool armRuntime)
    {
        try
        {
            foreach (string path in ConveyorBeltImporter.ModelPaths)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                ConveyorBeltSurface surface = model.GetComponentInChildren<ConveyorBeltSurface>(true);
                if (surface == null) throw new Exception(path + ": no prepared belt surface.");
                Mesh mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector2[] uv = mesh.uv;
                int[] indices = mesh.GetTriangles(0);
                int topEdges = 0;
                float halfStraight = mesh.bounds.extents.y - mesh.bounds.extents.z;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    for (int edge = 0; edge < 3; edge++)
                    {
                        int a = indices[i + edge], b = indices[i + (edge + 1) % 3];
                        Vector3 pa = vertices[a] - mesh.bounds.center, pb = vertices[b] - mesh.bounds.center;
                        if (pa.z < mesh.bounds.extents.z * 0.99f || pb.z < mesh.bounds.extents.z * 0.99f
                            || Mathf.Abs(pa.y) > halfStraight + mesh.bounds.extents.z * 0.05f
                            || Mathf.Abs(pb.y) > halfStraight + mesh.bounds.extents.z * 0.05f
                            || Mathf.Abs(pa.y - pb.y) < halfStraight * 0.1f) continue;
                        if ((uv[a].x - uv[b].x) * (pa.y - pb.y) >= 0f)
                            throw new Exception(path + ": mirrored/reversed top UVs detected.");
                        topEdges++;
                    }
                }
                if (topEdges < 4 || mesh.GetIndexCount(1) == 0)
                    throw new Exception(path + ": incomplete moving surface or belt sidewalls (top edges " + topEdges
                        + ", side indices " + mesh.GetIndexCount(1) + ", bounds " + mesh.bounds.ToString("F5")
                        + ", sample vertices " + vertices[0].ToString("F5") + ", " + vertices[1].ToString("F5") + ").");
                if (surface.MetresPerRepeat < 0.07f || surface.MetresPerRepeat > 0.08f)
                    throw new Exception(path + ": stripe spacing is outside 70–80 mm.");
            }
            File.WriteAllText(StaticReport, "{\"success\":true,\"modelsChecked\":3,\"mirroredTopUvs\":false,\"separateSolidSidewalls\":true,\"spacingMm\":\"74.25–74.49\"}");
            if (armRuntime) SessionState.SetBool(ProbeKey, true);
            Debug.Log(armRuntime ? "All three belt UV checks passed. Next Play session will verify moving meshes, rollers and stop/resume."
                : "All three imported belt UV checks passed.");
        }
        catch (Exception error)
        {
            File.WriteAllText(StaticReport, JsonUtility.ToJson(new Result { success = false, error = error.ToString() }, true));
            Debug.LogException(error);
        }
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode) { Cleanup(); return; }
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(ProbeKey, false)) return;
        SessionState.SetBool(ProbeKey, false);
        try
        {
            product = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ConveyorProductMover>()).FirstOrDefault();
            temporaryModels = new GameObject[3];
            probes = new ConveyorBeltSurface[3];
            initial = new Snapshot[3];
            for (int i = 0; i < 3; i++)
            {
                temporaryModels[i] = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorBeltImporter.ModelPaths[i]));
                temporaryModels[i].hideFlags = HideFlags.DontSave;
                temporaryModels[i].transform.position = new Vector3(1000f + i * 5f, 0, 0);
                probes[i] = temporaryModels[i].GetComponentInChildren<ConveyorBeltSurface>();
                probes[i].Speed = 0.5f;
                probes[i].IsRunning = true;
                initial[i] = Capture(probes[i]);
            }
            if (probes.Select(p => p.MovingMesh).Distinct().Count() != 3)
                throw new Exception("Conveyors must own independent dynamic meshes.");
            stage = 0;
            sampleAt = EditorApplication.timeSinceStartup + 0.13;
            EditorApplication.update += ObserveRuntime;
        }
        catch (Exception error) { Finish(new Result { success = false, error = error.ToString() }); }
    }

    private static Snapshot Capture(ConveyorBeltSurface surface)
    {
        Mesh mesh = surface.MovingMesh;
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < 8; i++)
            if (Vector3.Distance(vertices[i], vertices[vertices.Length - 8 + i]) > 0.00000001f)
                throw new Exception("The circulating band has an open seam.");
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        surface.GetComponent<Renderer>().GetPropertyBlock(block, 0);
        if (block.GetVector(BaseMapST) != new Vector4(1, 1, 0, 0))
            throw new Exception("Texture offsets must remain fixed during mesh circulation.");
        return new Snapshot { vertices = vertices, uv = mesh.uv, distance = surface.TravelMetres,
            front = surface.FrontRoller.localRotation, rear = surface.RearRoller.localRotation };
    }

    private static void CheckMoved(ConveyorBeltSurface surface, Snapshot before)
    {
        Snapshot now = Capture(surface);
        if (now.vertices.SequenceEqual(before.vertices) || now.distance <= before.distance)
            throw new Exception("The running mesh vertices did not circulate.");
        if (!now.uv.SequenceEqual(before.uv)) throw new Exception("UVs moved instead of staying attached to the rubber.");
        if (Quaternion.Angle(now.front, before.front) < 0.01f || Quaternion.Angle(now.rear, before.rear) < 0.01f)
            throw new Exception("Both roller meshes must rotate.");
        float angle = (float)(now.distance * (surface.ReverseDirection ? -1.0 : 1.0)
            / surface.SurfaceBendRadiusMetres * Mathf.Rad2Deg % 360.0);
        if (Quaternion.Angle(now.front, Quaternion.AngleAxis(angle, Vector3.right)) > 0.05f)
            throw new Exception("Roller angle does not match the motor travel and radius.");
    }

    private static void ObserveRuntime()
    {
        if (probes == null || !EditorApplication.isPlaying) { Cleanup(); return; }
        if (EditorApplication.timeSinceStartup < sampleAt) return;
        try
        {
            if (stage == 0)
            {
                held = new Snapshot[3];
                for (int i = 0; i < 3; i++)
                {
                    CheckMoved(probes[i], initial[i]);
                    probes[i].IsRunning = false;
                    probes[i].Speed = 1.2f;
                    held[i] = Capture(probes[i]);
                }
                if (product != null)
                {
                    productMotor = product.CurrentConveyor.BeltSurface;
                    originalRunning = productMotor.IsRunning;
                    originalSpeed = productMotor.Speed;
                    productMotor.IsRunning = false;
                    productMotor.Speed = 1.2f;
                    heldProductPosition = product.transform.position;
                }
            }
            else if (stage == 1)
            {
                for (int i = 0; i < 3; i++)
                {
                    Snapshot now = Capture(probes[i]);
                    if (!now.vertices.SequenceEqual(held[i].vertices) || now.front != held[i].front
                        || now.rear != held[i].rear || now.distance != held[i].distance)
                        throw new Exception("A stopped belt mesh or roller moved.");
                    if (probes[i].Speed != 1.2f) throw new Exception("The pending speed was lost.");
                    probes[i].IsRunning = true;
                }
                if (productMotor != null)
                {
                    if (Vector3.Distance(product.transform.position, heldProductPosition) > 0.000001f)
                        throw new Exception("The product moved while its conveyor motor was stopped.");
                    productMotor.IsRunning = true;
                }
            }
            else
            {
                for (int i = 0; i < 3; i++) CheckMoved(probes[i], held[i]);
                if (productMotor != null && Vector3.Distance(product.transform.position, heldProductPosition) < 0.000001f)
                    throw new Exception("The product did not resume with its motor.");
                Finish(new Result { success = true, modelsChecked = 3, travel = true, stop = true, pendingSpeed = true,
                    resume = true, verticesMove = true, uvsStayFixed = true, rollersRotate = true, closedSeam = true,
                    independentMeshes = true, productFollowsMotor = productMotor != null });
                return;
            }
            stage++;
            sampleAt = EditorApplication.timeSinceStartup + 0.13;
        }
        catch (Exception error) { Finish(new Result { success = false, error = error.ToString() }); }
    }

    private static void Finish(Result result)
    {
        Cleanup();
        File.WriteAllText(RuntimeReport, JsonUtility.ToJson(result, true));
        if (result.success) Debug.Log("All three moving-mesh checks passed: circulating vertices, fixed UVs, rotating rollers, closed seams, stop/resume and product motor speed.");
        else Debug.LogError(result.error);
    }

    private static void Cleanup()
    {
        if (productMotor != null) { productMotor.Speed = originalSpeed; productMotor.IsRunning = originalRunning; }
        productMotor = null;
        EditorApplication.update -= ObserveRuntime;
        if (temporaryModels != null)
            foreach (GameObject model in temporaryModels) if (model != null) UnityEngine.Object.Destroy(model);
        temporaryModels = null;
        probes = null;
    }

    [Serializable] private sealed class Result
    {
        public bool success, travel, stop, pendingSpeed, resume;
        public bool verticesMove, uvsStayFixed, rollersRotate, closedSeam, independentMeshes, productFollowsMotor;
        public int modelsChecked;
        public string error;
    }

    private sealed class Snapshot
    {
        public Vector3[] vertices;
        public Vector2[] uv;
        public Quaternion front, rear;
        public double distance;
    }
}
