using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ConveyorRibSpacingVerification
{
    [Serializable] public sealed class Measurement
    {
        public string model;
        public float straightPitchMm, curvedPitchMm, relativeDifference;
        public int samples;
    }
    [Serializable] private sealed class Report { public bool success; public Measurement[] models; }

    [MenuItem("Tools/Conveyor Studio/Validate Rib Spacing")]
    public static void Validate()
    {
        List<Measurement> checks = new List<Measurement>();
        foreach (string path in ConveyorBeltImporter.ModelPaths)
        {
            ConveyorBeltSurface surface = AssetDatabase.LoadAssetAtPath<GameObject>(path)
                .GetComponentInChildren<ConveyorBeltSurface>(true);
            Mesh source = surface.GetComponent<MeshFilter>().sharedMesh;
            using (ConveyorBeltLoopMesh loop = new ConveyorBeltLoopMesh(source,
                surface.transform.TransformVector(Vector3.up).magnitude, surface.MetresPerRepeat))
            {
                List<float> straight = new List<float>(), curved = new List<float>();
                foreach (float phase in new[] { 0f, 0.271f })
                {
                    loop.SetTravelMetres(loop.WorldLoopLength * phase);
                    Vector3[] vertices = loop.Mesh.vertices;
                    int ribs = loop.Segments / 4;
                    for (int rib = 0; rib < ribs; rib++)
                    {
                        Vector3 a = vertices[(rib * 4 + 2) * 8] - loop.Centre;
                        Vector3 b = vertices[(((rib + 1) % ribs) * 4 + 2) * 8] - loop.Centre;
                        float limit = loop.HalfStraight;
                        if (Mathf.Abs(a.y) < limit && Mathf.Abs(b.y) < limit && a.z * b.z > 0f)
                            straight.Add(Mathf.Abs(a.y - b.y) * loop.UnitsToMetres);
                        else if ((a.y > limit && b.y > limit) || (a.y < -limit && b.y < -limit))
                        {
                            float centre = a.y > 0f ? limit : -limit;
                            Vector2 ra = new Vector2(a.y - centre, a.z), rb = new Vector2(b.y - centre, b.z);
                            float angle = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(ra.y, ra.x) * Mathf.Rad2Deg,
                                Mathf.Atan2(rb.y, rb.x) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                            curved.Add((ra.magnitude + rb.magnitude) * 0.5f * angle * loop.UnitsToMetres);
                        }
                    }
                }
                if (straight.Count == 0 || curved.Count < 4) throw new Exception("Insufficient rib-spacing samples: " + path);
                float flat = straight.Average(), bend = curved.Average();
                float difference = Mathf.Abs(bend - flat) / flat;
                // Groove centres are 0.8 mm inset, so their bend radius is slightly smaller.
                if (difference > 0.015f) throw new Exception("Ribs stretch around the bends: " + path + " ratio " + bend / flat);
                checks.Add(new Measurement { model = Path.GetFileNameWithoutExtension(path), straightPitchMm = flat * 1000f,
                    curvedPitchMm = bend * 1000f, relativeDifference = difference, samples = straight.Count + curved.Count });
            }
        }
        File.WriteAllText("Library/ConveyorRibSpacingReport.json", JsonUtility.ToJson(new Report { success = true, models = checks.ToArray() }, true));
        Debug.Log("Rib spacing passed on all three types: straight and curved surface spacing agree within 1.5%.");
    }
}
