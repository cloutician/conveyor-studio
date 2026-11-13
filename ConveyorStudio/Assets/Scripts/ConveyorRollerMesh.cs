using System.Collections.Generic;
using UnityEngine;

public static class ConveyorRollerMesh
{
    // Unit cylinder along X, with hard cap normals and a one-unit axial length.
    public static Mesh Create()
    {
        const int segments = 32;
        List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        List<Vector2> uv = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * 2f * Mathf.PI / segments;
            Vector3 n = new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle));
            vertices.Add(new Vector3(-0.5f, n.y, n.z));
            vertices.Add(new Vector3(0.5f, n.y, n.z));
            normals.Add(n); normals.Add(n);
            uv.Add(new Vector2((float)i / segments, 0)); uv.Add(new Vector2((float)i / segments, 1));
            if (i < segments)
            {
                int b = i * 2;
                triangles.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
        }
        for (int side = -1; side <= 1; side += 2)
        {
            int centre = vertices.Count;
            vertices.Add(new Vector3(side * 0.5f, 0, 0)); normals.Add(Vector3.right * side); uv.Add(Vector2.one * 0.5f);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                float y = Mathf.Cos(angle), z = Mathf.Sin(angle);
                vertices.Add(new Vector3(side * 0.5f, y, z)); normals.Add(Vector3.right * side);
                uv.Add(new Vector2(y, z) * 0.5f + Vector2.one * 0.5f);
                if (i < segments)
                {
                    if (side > 0) triangles.AddRange(new[] { centre, centre + i + 1, centre + i + 2 });
                    else triangles.AddRange(new[] { centre, centre + i + 2, centre + i + 1 });
                }
            }
        }
        Mesh mesh = new Mesh { name = "ConveyorRoller_Unit", hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
