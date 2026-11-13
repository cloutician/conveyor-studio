using System;
using System.Collections.Generic;
using UnityEngine;

// A closed rubber band. Material coordinates stay attached to vertices as they circulate.
public sealed class ConveyorBeltLoopMesh : IDisposable
{
    public Mesh Mesh { get; }
    public float WorldLoopLength { get; }
    public float MetresPerRepeat { get; }
    public float RollerRadius { get; }
    public float SurfaceRadius { get; }
    public float HalfStraight { get; }
    public float HalfWidth { get; }
    public Vector3 Centre { get; }
    public int Segments { get; }
    public float UnitsToMetres { get; }

    private readonly Vector3[] vertices, normals;
    private readonly Vector4[] tangents;
    private readonly float loopLength, thickness;
    private readonly int repeats;

    public ConveyorBeltLoopMesh(Mesh source, float unitsToMetres, float requestedPitch)
    {
        Bounds bounds = source.bounds;
        Centre = bounds.center;
        HalfWidth = bounds.extents.x;
        HalfStraight = bounds.extents.y - bounds.extents.z;
        UnitsToMetres = Mathf.Max(unitsToMetres, 0.000001f);
        if (HalfStraight <= 0f || bounds.extents.z <= 0f)
            throw new ArgumentException("Expected the supplied conveyor's Y/Z capsule cross section.");

        // Locate the inner top face in the supplied hollow belt, rather than guess its thickness.
        Vector3[] sourceVertices = source.vertices, sourceNormals = source.normals;
        float inner = float.PositiveInfinity;
        for (int i = 0; i < sourceVertices.Length; i++)
        {
            Vector3 p = sourceVertices[i] - Centre;
            if (p.z > 0f && sourceNormals[i].z < -0.95f
                && Mathf.Abs(p.y) <= HalfStraight + bounds.extents.z * 0.05f)
                inner = Mathf.Min(inner, p.z);
        }
        if (float.IsPositiveInfinity(inner)) throw new ArgumentException("The belt's inner contact face was not found.");
        RollerRadius = inner;
        SurfaceRadius = bounds.extents.z;
        thickness = bounds.extents.z - inner;
        if (thickness <= 0f) throw new ArgumentException("The belt must have positive thickness.");

        // Spacing is measured on the visible outside of the rubber, including both bends.
        // Using the inner contact loop stretches the outer ribs by outerRadius/innerRadius.
        loopLength = 4f * HalfStraight + 2f * Mathf.PI * SurfaceRadius;
        WorldLoopLength = loopLength * UnitsToMetres;
        repeats = Mathf.Max(8, Mathf.RoundToInt(WorldLoopLength / Mathf.Max(0.001f, requestedPitch)));
        MetresPerRepeat = WorldLoopLength / repeats;
        Segments = repeats * 4;
        vertices = new Vector3[(Segments + 1) * 8];
        normals = new Vector3[vertices.Length];
        tangents = new Vector4[vertices.Length];
        Vector2[] uv = new Vector2[vertices.Length];
        List<int> surface = new List<int>(Segments * 12), edges = new List<int>(Segments * 12);
        for (int ring = 0; ring <= Segments; ring++)
        {
            float u = (float)ring / Segments * repeats;
            int b = ring * 8;
            for (int face = 0; face < 4; face++)
            {
                uv[b + face * 2] = new Vector2(u, 0f);
                uv[b + face * 2 + 1] = new Vector2(u, 1f);
            }
            if (ring == Segments) continue;
            int n = b + 8;
            surface.AddRange(new[] { b, n, b + 1, b + 1, n, n + 1,
                b + 2, b + 3, n + 2, n + 2, b + 3, n + 3 });
            edges.AddRange(new[] { b + 4, b + 5, n + 4, n + 4, b + 5, n + 5,
                b + 6, n + 6, b + 7, b + 7, n + 6, n + 7 });
        }
        Mesh = new Mesh { name = source.name + "_Circulating", hideFlags = HideFlags.DontSave };
        Mesh.MarkDynamic();
        SetTravelMetres(0);
        Mesh.uv = uv;
        Mesh.subMeshCount = 2;
        Mesh.SetTriangles(surface, 0);
        Mesh.SetTriangles(edges, 1);
        Mesh.bounds = bounds;
    }

    public void SetTravelMetres(double travel)
    {
        float offset = (float)(travel % WorldLoopLength) / UnitsToMetres;
        for (int ring = 0; ring <= Segments; ring++)
        {
            // The final ring exactly duplicates ring zero spatially, with a different UV seam.
            int materialRing = ring == Segments ? 0 : ring;
            float materialFraction = (float)materialRing / Segments;
            Evaluate(materialFraction * loopLength + offset, out Vector3 point, out Vector3 normal, out Vector3 tangent);
            float ribPhase = Mathf.Repeat(materialFraction * repeats, 1f);
            float groove = -0.0008f / UnitsToMetres * Mathf.Exp(-Mathf.Pow((ribPhase - 0.5f) / 0.13f, 2f));
            Vector3 outer = Centre + point + normal * groove, inner = Centre + point - normal * thickness;
            Vector3 width = Vector3.right * HalfWidth;
            int b = ring * 8;
            vertices[b] = vertices[b + 4] = outer - width;
            vertices[b + 1] = vertices[b + 6] = outer + width;
            vertices[b + 2] = vertices[b + 5] = inner - width;
            vertices[b + 3] = vertices[b + 7] = inner + width;
            normals[b] = normals[b + 1] = normal;
            normals[b + 2] = normals[b + 3] = -normal;
            normals[b + 4] = normals[b + 5] = Vector3.left;
            normals[b + 6] = normals[b + 7] = Vector3.right;
            for (int v = 0; v < 8; v++)
                tangents[b + v] = new Vector4(tangent.x, tangent.y, tangent.z, v == 2 || v == 3 ? -1f : 1f);
        }
        Mesh.SetVertices(vertices);
        Mesh.SetNormals(normals);
        Mesh.SetTangents(tangents);
    }

    private void Evaluate(float distance, out Vector3 point, out Vector3 normal, out Vector3 tangent)
    {
        float d = Mathf.Repeat(distance, loopLength), straight = 2f * HalfStraight;
        if (d < straight)
        {
            point = new Vector3(0, HalfStraight - d, SurfaceRadius);
            normal = Vector3.forward;
            tangent = Vector3.down;
            return;
        }
        d -= straight;
        if (d < Mathf.PI * SurfaceRadius)
        {
            float angle = Mathf.PI / 2f + d / SurfaceRadius;
            normal = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
            point = new Vector3(0, -HalfStraight, 0) + normal * SurfaceRadius;
            tangent = new Vector3(0, -Mathf.Sin(angle), Mathf.Cos(angle));
            return;
        }
        d -= Mathf.PI * SurfaceRadius;
        if (d < straight)
        {
            point = new Vector3(0, -HalfStraight + d, -SurfaceRadius);
            normal = Vector3.back;
            tangent = Vector3.up;
            return;
        }
        float lastAngle = 3f * Mathf.PI / 2f + (d - straight) / SurfaceRadius;
        normal = new Vector3(0, Mathf.Cos(lastAngle), Mathf.Sin(lastAngle));
        point = new Vector3(0, HalfStraight, 0) + normal * SurfaceRadius;
        tangent = new Vector3(0, -Mathf.Sin(lastAngle), Mathf.Cos(lastAngle));
    }

    public void Dispose()
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(Mesh);
        else UnityEngine.Object.DestroyImmediate(Mesh);
    }
}
