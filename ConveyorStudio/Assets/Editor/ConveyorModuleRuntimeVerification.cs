using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ConveyorModuleRuntimeVerification
{
    private static GameObject shortObject, inclineObject, productObject;
    private static ConveyorProductMover mover;
    private static ConveyorSection shortSection, inclineSection;
    private static int stage, witnessIndex;
    private static double started, sampleAt;
    private static Vector3 witnessStart, heldPosition;
    private const string ProbeKey = "ConveyorStudio.ModuleRuntimeProbe.v1";

    static ConveyorModuleRuntimeVerification() => EditorApplication.playModeStateChanged += OnPlayMode;

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode) { Cleanup(); return; }
        if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(ProbeKey, false)) return;
        SessionState.SetBool(ProbeKey, false);
        try
        {
            shortObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorModuleBuilder.ShortPath));
            inclineObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ConveyorModuleBuilder.InclinePath));
            shortObject.hideFlags = inclineObject.hideFlags = HideFlags.DontSave;
            shortObject.transform.position = new Vector3(2000, 0, 0);
            shortSection = shortObject.GetComponent<ConveyorSection>();
            inclineSection = inclineObject.GetComponent<ConveyorSection>();
            if (!inclineSection.TrySnapAfter(shortSection, out string reason)) throw new Exception(reason);
            if (Mathf.Abs(inclineObject.transform.position.y) > 0.0001f)
                throw new Exception("The standard floor-level join lifts or sinks the incline.");
            shortSection.BeltSurface.Speed = inclineSection.BeltSurface.Speed = 1.8f;
            shortSection.BeltSurface.IsRunning = inclineSection.BeltSurface.IsRunning = true;
            productObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ProductBox.prefab"));
            productObject.hideFlags = HideFlags.DontSave;
            mover = productObject.GetComponent<ConveyorProductMover>();
            SerializedObject serialized = new SerializedObject(mover);
            serialized.FindProperty("conveyor").objectReferenceValue = shortSection;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Mesh mesh = inclineSection.BeltSurface.MovingMesh;
            Vector3[] vertices = mesh.vertices;
            witnessIndex = -1;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length - 8; i += 8)
            {
                if (vertices[i].z < mesh.bounds.max.z * 0.97f || Mathf.Abs(vertices[i].y) >= nearest) continue;
                nearest = Mathf.Abs(vertices[i].y);
                witnessIndex = i;
            }
            if (witnessIndex < 0) throw new Exception("No top-run geometry witness.");
            witnessStart = inclineSection.BeltSurface.transform.TransformPoint(vertices[witnessIndex]);
            stage = 0;
            started = EditorApplication.timeSinceStartup;
            sampleAt = started + 0.13;
            EditorApplication.update += Observe;
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Observe()
    {
        if (!EditorApplication.isPlaying || mover == null) { Cleanup(); return; }
        double now = EditorApplication.timeSinceStartup;
        if (now < sampleAt) return;
        try
        {
            if (now - started > 8) throw new Exception("Module product-transfer check timed out.");
            if (stage == 0)
            {
                Vector3 witness = inclineSection.BeltSurface.transform.TransformPoint(inclineSection.BeltSurface.MovingMesh.vertices[witnessIndex]);
                if (Vector3.Dot((witness - witnessStart).normalized,
                    (inclineSection.Exit.position - inclineSection.Entry.position).normalized) < 0.99f)
                    throw new Exception("The incline's actual mesh is moving downhill.");
                stage = 1;
            }
            else if (stage == 1)
            {
                if (mover.CurrentConveyor != inclineSection) return;
                stage = 2; sampleAt = now + 0.15;
            }
            else if (stage == 2)
            {
                if (productObject.transform.position.y <= inclineSection.Entry.position.y + 0.02f)
                    throw new Exception("The product failed to climb the incline after transfer.");
                inclineSection.BeltSurface.IsRunning = false;
                heldPosition = productObject.transform.position;
                stage = 3; sampleAt = now + 0.13;
            }
            else if (stage == 3)
            {
                if (Vector3.Distance(productObject.transform.position, heldPosition) > 0.000001f)
                    throw new Exception("The product did not stop on the incline.");
                inclineSection.BeltSurface.Speed = 0.8f;
                inclineSection.BeltSurface.IsRunning = true;
                stage = 4; sampleAt = now + 0.15;
            }
            else
            {
                if (productObject.transform.position.y <= heldPosition.y + 0.01f)
                    throw new Exception("The inclined product failed to resume uphill.");
                Finish(true, "");
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Finish(bool success, string error)
    {
        File.WriteAllText("Library/ConveyorModuleRuntimeReport.json", JsonUtility.ToJson(new Report {
            success = success, error = error, floorLevelJoin = success, uphillMeshTravel = success,
            productTransfer = success, productClimbs = success, stopResume = success }, true));
        Cleanup();
        if (success) Debug.Log("Short/Incline prefab runtime checks passed: level-floor join, real uphill belt travel, product transfer, climbing and stop/resume.");
        else Debug.LogError(error);
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Observe;
        if (productObject != null) Object.Destroy(productObject);
        if (shortObject != null) Object.Destroy(shortObject);
        if (inclineObject != null) Object.Destroy(inclineObject);
        mover = null; productObject = shortObject = inclineObject = null;
    }

    [Serializable] private sealed class Report
    {
        public bool success, floorLevelJoin, uphillMeshTravel, productTransfer, productClimbs, stopResume;
        public string error;
    }
}
