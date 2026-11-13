using System.Collections.Generic;
using UnityEngine;

// Flat, unlit pixels; fixed faceplate lettering remains the imported relief mesh.
public sealed class ConveyorDotMatrix : MonoBehaviour
{
    private Mesh mesh;
    private string lastText;
    private Vector3 right, up, front;
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>();
    static ConveyorDotMatrix()
    {
        string[] rows = {
            "A:01110/10001/10001/11111/10001/10001/10001", "B:11110/10001/10001/11110/10001/10001/11110",
            "C:01111/10000/10000/10000/10000/10000/01111", "D:11110/10001/10001/10001/10001/10001/11110",
            "E:11111/10000/10000/11110/10000/10000/11111", "F:11111/10000/10000/11110/10000/10000/10000",
            "G:01110/10001/10000/10111/10001/10001/01110", "H:10001/10001/10001/11111/10001/10001/10001",
            "I:11111/00100/00100/00100/00100/00100/11111", "J:00111/00010/00010/00010/10010/10010/01100",
            "K:10001/10010/10100/11000/10100/10010/10001", "L:10000/10000/10000/10000/10000/10000/11111",
            "M:10001/11011/10101/10101/10001/10001/10001", "N:10001/11001/11001/10101/10011/10011/10001",
            "O:01110/10001/10001/10001/10001/10001/01110", "P:11110/10001/10001/11110/10000/10000/10000",
            "Q:01110/10001/10001/10001/10101/10010/01101", "R:11110/10001/10001/11110/10100/10010/10001",
            "S:01111/10000/10000/01110/00001/00001/11110", "T:11111/00100/00100/00100/00100/00100/00100",
            "U:10001/10001/10001/10001/10001/10001/01110", "V:10001/10001/10001/10001/10001/01010/00100",
            "W:10001/10001/10001/10101/10101/10101/01010", "X:10001/10001/01010/00100/01010/10001/10001",
            "Y:10001/10001/01010/00100/00100/00100/00100", "Z:11111/00001/00010/00100/01000/10000/11111",
            "0:01110/10001/10011/10101/11001/10001/01110", "1:00100/01100/00100/00100/00100/00100/01110",
            "2:01110/10001/00001/00010/00100/01000/11111", "3:11110/00001/00001/01110/00001/00001/11110",
            "4:00010/00110/01010/10010/11111/00010/00010", "5:11111/10000/10000/11110/00001/00001/11110",
            "6:01110/10000/10000/11110/10001/10001/01110", "7:11111/00001/00010/00100/01000/01000/01000",
            "8:01110/10001/10001/01110/10001/10001/01110", "9:01110/10001/10001/01111/00001/00001/01110",
            ",:00000/00000/00000/00000/00100/00100/01000"
        };
        foreach (string row in rows) Glyphs.Add(row[0], row.Substring(2).Split('/'));
    }
    public void Configure(Vector3 rightAxis, Vector3 upAxis, Vector3 frontAxis, Material material)
    {
        right = rightAxis; up = upAxis; front = frontAxis;
        mesh = new Mesh { name = "Live flat dot-matrix display" };
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    public void SetText(string top, string bottom)
    {
        string text = top + "\n" + bottom;
        if (lastText == text || mesh == null) return;
        lastText = text; vertices.Clear(); triangles.Clear();
        string[] lines = { top.ToUpperInvariant(), bottom.ToUpperInvariant() };
        bool reverse = Vector3.Dot(Vector3.Cross(right, up), front) < 0;
        for (int line = 0; line < 2; line++)
        {
            float cell = Mathf.Min(0.003f, 0.237f / Mathf.Max(1, lines[line].Length * 6 - 1));
            float start = -((lines[line].Length - 1) * 6 + 4) * cell * 0.5f;
            for (int c = 0; c < lines[line].Length; c++)
            {
                if (!Glyphs.TryGetValue(lines[line][c], out var glyph)) continue;
                for (int y = 0; y < 7; y++) for (int x = 0; x < 5; x++)
                {
                    if (glyph[y][x] != '1') continue;
                    Vector3 centre = right * (start + (c * 6 + x) * cell)
                        + up * ((line == 0 ? 0.0115f : -0.0115f) + (3 - y) * cell) + front * 0.0083f;
                    int first = vertices.Count; vertices.Add(centre);
                    for (int i = 0; i <= 10; i++)
                    { float a = i * Mathf.PI * 0.2f; vertices.Add(centre + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.0009f); }
                    for (int i = 0; i < 10; i++)
                    { triangles.Add(first); triangles.Add(first + i + (reverse ? 2 : 1)); triangles.Add(first + i + (reverse ? 1 : 2)); }
                }
            }
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
    }
    private void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
