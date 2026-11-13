using UnityEngine;

// Oriented boxes follow each of the four model parts. Their dimensions come from
// the source meshes, so rotating a machine never relies on a stale world AABB.
public static class ConveyorPlacementGeometry
{
    public struct Box
    {
        public Vector3 centre, half;
        public Quaternion rotation;
        public bool legs;
        public string name;
        public Vector3 Axis(int i) => rotation * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
        public float Radius(Vector3 axis) => Mathf.Abs(Vector3.Dot(axis, Axis(0))) * half.x
            + Mathf.Abs(Vector3.Dot(axis, Axis(1))) * half.y + Mathf.Abs(Vector3.Dot(axis, Axis(2))) * half.z;
        public float MinimumY => centre.y - Radius(Vector3.up);
    }

    public static bool Overlaps(Box a, Box b, out float penetration, out Vector3 normal)
    {
        penetration = float.PositiveInfinity;
        normal = Vector3.zero;
        Vector3 delta = b.centre - a.centre;
        for (int i = 0; i < 15; i++)
        {
            Vector3 axis = i < 3 ? a.Axis(i) : i < 6 ? b.Axis(i - 3)
                : Vector3.Cross(a.Axis((i - 6) / 3), b.Axis((i - 6) % 3));
            float magnitude = axis.magnitude;
            if (magnitude < 0.00001f) continue;
            axis /= magnitude;
            float depth = a.Radius(axis) + b.Radius(axis) - Mathf.Abs(Vector3.Dot(delta, axis));
            if (depth <= 0.00015f) return false; // Contact is allowed; penetration is not.
            if (depth < penetration) { penetration = depth; normal = axis; }
        }
        return true;
    }

    // Incline end brackets project 47 mm past the belt marker. Their rotated bounds
    // reach 78 mm. Reserve an 85 mm mating zone ONLY at an exactly aligned port.
    // This never permits a side overlap, a displaced port, or intersecting supports.
    public static bool IsConnectorClearance(Box a, Box b, Vector3 port, Vector3 direction)
    {
        if (a.legs || b.legs) return false;
        float ca = Vector3.Dot(a.centre - port, direction), cb = Vector3.Dot(b.centre - port, direction);
        float low = Mathf.Max(ca - a.Radius(direction), cb - b.Radius(direction));
        float high = Mathf.Min(ca + a.Radius(direction), cb + b.Radius(direction));
        return low > -0.085f && high < 0.085f;
    }
}
