using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Only the supplied blue belt mesh is changed; frames, legs and source FBXs are preserved.
public sealed class ConveyorBeltImporter : AssetPostprocessor
{
    public const string SurfaceMaterialPath = "Assets/Art/Conveyors/BeltRibbed.mat";
    public const string EdgeMaterialPath = "Assets/Art/Conveyors/BeltEdge.mat";
    public const string RollerMaterialPath = "Assets/Art/Conveyors/BeltRoller.mat";
    public static readonly string[] ModelPaths =
    {
        "Assets/VACAC/BOUTIQUE/Conveyors/VA_Conveyor_Belt_Generic.fbx",
        "Assets/VACAC/BOUTIQUE/Conveyors/VA_Conveyor_Belt_Short.fbx",
        "Assets/VACAC/BOUTIQUE/Conveyors/VA_incline_Conveyor_Belt.fbx"
    };

    public override uint GetVersion() => 5;

    private void OnPreprocessModel()
    {
        if (Array.IndexOf(ModelPaths, assetPath) >= 0)
            ((ModelImporter)assetImporter).isReadable = true;
    }

    private void OnPostprocessModel(GameObject root)
    {
        if (Array.IndexOf(ModelPaths, assetPath) < 0) return;
        // Native material assets are import dependencies, including their serialized references.
        context.DependsOnArtifact(SurfaceMaterialPath);
        context.DependsOnArtifact(EdgeMaterialPath);
        context.DependsOnArtifact(RollerMaterialPath);
        Material ribbed = AssetDatabase.LoadAssetAtPath<Material>(SurfaceMaterialPath);
        Material edge = AssetDatabase.LoadAssetAtPath<Material>(EdgeMaterialPath);
        if (ribbed == null || edge == null) return;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null) continue;
            if (!Array.Exists(renderer.sharedMaterials, material => material != null
                && material.name.Contains("VA_PLA_Conveyor_Blue_Dark"))) continue;

            float pitch = RewrapBelt(filter);
            renderer.sharedMaterials = new[] { ribbed, edge };
            ConveyorBeltSurface surface = filter.GetComponent<ConveyorBeltSurface>();
            if (surface == null) surface = filter.gameObject.AddComponent<ConveyorBeltSurface>();
            surface.Configure(renderer, pitch, AssetDatabase.LoadAssetAtPath<Material>(RollerMaterialPath));
        }
    }

    private static float RewrapBelt(MeshFilter filter)
    {
        Mesh mesh = filter.sharedMesh;
        Vector3[] source = mesh.vertices;
        Vector3[] sourceNormals = mesh.normals;
        int[] triangles = mesh.triangles;
        if (source.Length == 0 || triangles.Length == 0 || sourceNormals.Length != source.Length)
            throw new InvalidOperationException("The belt mesh must have readable vertices, normals and triangles.");
        Bounds bounds = mesh.bounds;
        float radius = bounds.extents.z;
        float halfStraight = bounds.extents.y - radius;
        if (radius <= 0f || halfStraight <= 0f)
            throw new InvalidOperationException("The supplied belt must have a capsule cross section in local Y/Z.");

        float loop = 4f * halfStraight + 2f * Mathf.PI * radius;
        float worldScale = filter.transform.TransformVector(Vector3.up).magnitude;
        float worldLoop = loop * worldScale;
        int repeats = Mathf.Max(1, Mathf.RoundToInt(worldLoop / 0.075f));
        List<Vector3> vertices = new List<Vector3>(triangles.Length);
        List<Vector3> normals = new List<Vector3>(triangles.Length);
        List<Vector2> uv = new List<Vector2>(triangles.Length);
        List<int> moving = new List<int>();
        List<int> edges = new List<int>();

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = source[triangles[i]], b = source[triangles[i + 1]], c = source[triangles[i + 2]];
            // Imported vertices are in centimetre-scale mesh units. Avoid Vector3.normalized's
            // epsilon, which would treat the small sidewall triangle cross products as zero.
            Vector3 face = Vector3.Cross(b - a, c - a);
            bool side = Mathf.Abs(face.x) > face.magnitude * 0.9f;
            float[] distances = new float[3];
            for (int corner = 0; corner < 3; corner++)
                distances[corner] = LoopCoordinate(source[triangles[i + corner]] - bounds.center, halfStraight, radius, loop);

            // Split the underside seam per triangle, so UV interpolation never crosses the loop.
            bool crossesSeam = Mathf.Max(distances) - Mathf.Min(distances) > 0.5f;
            for (int corner = 0; corner < 3; corner++)
            {
                int original = triangles[i + corner];
                float distance = distances[corner];
                if (crossesSeam && distance < 0.5f) distance += 1f;
                (side ? edges : moving).Add(vertices.Count);
                vertices.Add(source[original]);
                normals.Add(sourceNormals[original]);
                uv.Add(new Vector2(distance * repeats,
                    (source[original].x - bounds.min.x) / bounds.size.x));
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(moving, 0);
        mesh.SetTriangles(edges, 1);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return worldLoop / repeats;
    }

    private static float LoopCoordinate(Vector3 p, float halfStraight, float radius, float loop)
    {
        float distance;
        if (p.y < -halfStraight)
        {
            float angle = Mathf.Atan2(p.z, p.y + halfStraight);
            if (angle < 0f) angle += 2f * Mathf.PI;
            distance = 2f * halfStraight + radius * (angle - Mathf.PI / 2f);
        }
        else if (p.y > halfStraight)
            distance = 4f * halfStraight + Mathf.PI * radius
                + radius * (Mathf.Atan2(p.z, p.y - halfStraight) + Mathf.PI / 2f);
        else if (p.z >= 0f) distance = halfStraight - p.y;
        else distance = 3f * halfStraight + Mathf.PI * radius + p.y;

        // Put the UV seam underneath the belt, not on its visible top run.
        return Mathf.Repeat(distance - (3f * halfStraight + Mathf.PI * radius), loop) / loop;
    }
}
