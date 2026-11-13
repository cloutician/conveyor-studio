using System.Collections.Generic;
using UnityEngine;

public sealed class PlacedConveyor : MonoBehaviour
{
    [System.Serializable] private sealed class Part
    {
        public MeshFilter filter;
        public Mesh original, extended;
        public Bounds bounds;
        public Vector3 centre, scale;
        public Quaternion rotation;
        public bool legs;
        public Vector3[] vertices;
        public float[] weights;
        public BoxCollider picker;
    }
    [SerializeField, HideInInspector] private List<Part> parts = new List<Part>();
    private ConveyorSilhouette silhouette;
    private float supportHeight = -1f;
    private bool wasRunning;
    [SerializeField, Range(0, 9)] private int speedLevel = 4;
    public int SpeedLevel => speedLevel;
    public void ApplyLineState(bool running, int level)
    {
        speedLevel = Mathf.Clamp(level, 0, 9);
        wasRunning = running;
        if (Section == null) Section = GetComponent<ConveyorSection>();
        if (Section.BeltSurface == null) return;
        Section.BeltSurface.Speed = speedLevel * 0.2f;
        Section.BeltSurface.IsRunning = running && !IsEditing;
    }
    public ConveyorSection Section { get; private set; }
    public ConveyorCatalog.Module Definition { get; private set; }
    public string UnitId { get; set; }
    public string DisplayName => Definition.title + " " + (UnitId ?? "· NEW");
    public Pose LastValidPose { get; set; }
    public bool IsReturning { get; set; }
    public bool IsSettling { get; set; }
    public bool IsEditing { get; private set; }

    private void OnEnable()
    {
        if (!Application.isPlaying || parts.Count != 0) return;
        Section = GetComponent<ConveyorSection>();
        var controller = GetComponentInParent<ConveyorPlacementController>();
        if (controller == null || controller.catalog == null) return;
        Definition = controller.catalog.Get(Section.ModuleType);
        // Restore caches for a model created before these fields became serializable.
        foreach (var source in Definition.prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (source.sharedMesh == null) continue;
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.name != source.name || HasRollerParent(filter.transform)) continue;
                var pick = filter.transform.Find("Placement pick volume");
                if (pick == null) continue;
                var part = new Part { filter = filter, original = source.sharedMesh, bounds = source.sharedMesh.bounds,
                    centre = transform.InverseTransformPoint(filter.transform.TransformPoint(source.sharedMesh.bounds.center)),
                    rotation = Quaternion.Inverse(transform.rotation) * filter.transform.rotation, scale = filter.transform.lossyScale,
                    legs = filter.name.Contains("Legs"), picker = pick.GetComponent<BoxCollider>() };
                if (part.legs)
                {
                    if (filter.sharedMesh != source.sharedMesh) part.extended = filter.sharedMesh;
                    part.vertices = part.original.vertices; part.weights = new float[part.vertices.Length];
                    for (int i = 0; i < part.vertices.Length; i++)
                    {
                        float y = transform.InverseTransformPoint(filter.transform.TransformPoint(part.vertices[i])).y;
                        part.weights[i] = 1f - Mathf.InverseLerp(0.025f, 0.16f, y);
                    }
                }
                parts.Add(part); break;
            }
        }
        silhouette = GetComponent<ConveyorSilhouette>(); supportHeight = -1f; RefreshSupports();
    }

    public void Initialize(ConveyorCatalog.Module definition)
    {
        Definition = definition;
        Section = GetComponent<ConveyorSection>();
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            // Belt motors create their own moving rollers; those are inside the belt's bounds.
            if (filter.transform.name == "MovingRollers" || HasRollerParent(filter.transform)) continue;
            if (filter.sharedMesh == null) continue;
            var p = new Part { filter = filter, original = filter.sharedMesh, bounds = filter.sharedMesh.bounds,
                legs = filter.name.Contains("Legs") };
            p.centre = transform.InverseTransformPoint(filter.transform.TransformPoint(p.bounds.center));
            p.rotation = Quaternion.Inverse(transform.rotation) * filter.transform.rotation;
            p.scale = filter.transform.lossyScale;
            if (p.legs)
            {
                p.vertices = p.original.vertices;
                p.weights = new float[p.vertices.Length];
                for (int i = 0; i < p.vertices.Length; i++)
                {
                    float y = transform.InverseTransformPoint(filter.transform.TransformPoint(p.vertices[i])).y;
                    p.weights[i] = 1f - Mathf.InverseLerp(0.025f, 0.16f, y);
                }
            }
            var pick = new GameObject("Placement pick volume");
            pick.transform.SetParent(filter.transform, false);
            p.picker = pick.AddComponent<BoxCollider>();
            p.picker.isTrigger = true;
            p.picker.center = p.bounds.center;
            p.picker.size = p.bounds.size;
            parts.Add(p);
        }
        silhouette = gameObject.AddComponent<ConveyorSilhouette>();
        LastValidPose = new Pose(transform.position, transform.rotation);
        RefreshSupports();
    }

    private bool HasRollerParent(Transform child)
    {
        for (var t = child; t != transform && t != null; t = t.parent)
            if (t.name == "MovingRollers") return true;
        return false;
    }

    public List<ConveyorPlacementGeometry.Box> Boxes(Pose pose)
    {
        var boxes = new List<ConveyorPlacementGeometry.Box>(parts.Count);
        foreach (var p in parts)
        {
            var box = new ConveyorPlacementGeometry.Box { centre = pose.position + pose.rotation * p.centre,
                rotation = pose.rotation * p.rotation,
                half = Vector3.Scale(p.bounds.extents, new Vector3(Mathf.Abs(p.scale.x), Mathf.Abs(p.scale.y), Mathf.Abs(p.scale.z))), legs = p.legs, name = p.filter.name };
            if (p.legs && pose.position.y > 0.0001f)
            {
                // Prepared support shafts are vertical. Extend their OBB along its most vertical axis.
                int vertical = 0;
                for (int i = 1; i < 3; i++) if (Mathf.Abs(box.Axis(i).y) > Mathf.Abs(box.Axis(vertical).y)) vertical = i;
                box.centre -= Vector3.up * pose.position.y * 0.5f;
                var half = box.half; half[vertical] += pose.position.y * 0.5f; box.half = half;
            }
            boxes.Add(box);
        }
        return boxes;
    }

    public void RefreshSupports()
    {
        float height = Mathf.Max(0f, transform.position.y);
        if (Mathf.Abs(height - supportHeight) < 0.00001f) return;
        supportHeight = height;
        foreach (var p in parts)
        {
            if (!p.legs) continue;
            if (height < 0.00001f)
            {
                p.filter.sharedMesh = p.original;
                p.picker.center = p.bounds.center; p.picker.size = p.bounds.size;
                continue;
            }
            if (p.extended == null) { p.extended = Instantiate(p.original); p.extended.name = "Runtime extended support"; }
            var vertices = new Vector3[p.vertices.Length];
            Vector3 offset = p.filter.transform.InverseTransformVector(Vector3.down * height);
            for (int i = 0; i < vertices.Length; i++) vertices[i] = p.vertices[i] + offset * p.weights[i];
            p.extended.vertices = vertices;
            p.extended.RecalculateBounds();
            p.filter.sharedMesh = p.extended;
            p.picker.center = p.extended.bounds.center; p.picker.size = p.extended.bounds.size;
        }
    }

    public void BeginEditing()
    {
        if (IsEditing) return;
        IsEditing = true;
        wasRunning = Section.BeltSurface != null && Section.BeltSurface.IsRunning;
        if (Section.BeltSurface != null) Section.BeltSurface.IsRunning = false;
        foreach (var p in parts) p.picker.enabled = false;
        GetComponent<ConveyorCollision>()?.SetEditing(true);
    }
    public void FinishEditing()
    {
        foreach (var p in parts) p.picker.enabled = true;
        GetComponent<ConveyorCollision>()?.SetEditing(false);
        if (Section.BeltSurface != null && IsEditing) Section.BeltSurface.IsRunning = wasRunning;
        IsEditing = false;
        IsReturning = false;
        IsSettling = false;
        Physics.SyncTransforms();
    }
    public void ShowOutline(bool show, Color colour)
    {
        if (silhouette != null) silhouette.SetHighlight(show, colour);
    }
    private void OnDestroy()
    {
        foreach (var p in parts) if (p.extended != null) Destroy(p.extended);
    }
}
