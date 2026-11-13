using UnityEngine;

// Motor-driven mesh circulation. This is kinematic geometry, not a flexible-body solver.
[DisallowMultipleComponent]
[AddComponentMenu("Conveyor Studio/Conveyor Belt Surface")]
public sealed class ConveyorBeltSurface : MonoBehaviour
{
    [SerializeField] private Renderer beltRenderer;
    [SerializeField, Min(0.001f)] private float metresPerRepeat = 0.075f;
    [SerializeField, Min(0f)] private float speed = 0.5f;
    [SerializeField] private bool isRunning = true;
    [SerializeField] private Material rollerMaterial;
    [SerializeField] private bool reverseDirection;
    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
    private MaterialPropertyBlock properties;
    private MeshFilter filter;
    private Mesh originalMesh, rollerMesh;
    private ConveyorBeltLoopMesh loopMesh;
    private Transform rollerRoot;
    private Transform[] rollers;
    private double travelMetres;

    public float Speed { get => speed; set => speed = Mathf.Max(0f, value); }
    public bool IsRunning { get => isRunning; set => isRunning = value; }
    public float MetresPerRepeat => metresPerRepeat;
    public float EffectiveSpeed => isRunning && isActiveAndEnabled ? speed : 0f;
    public bool ReverseDirection { get => reverseDirection; set => reverseDirection = value; }
    public Material RollerMaterial => rollerMaterial;
    public Vector3 TopTravelDirection => transform.TransformDirection(reverseDirection ? Vector3.up : Vector3.down);
    public double TravelMetres => travelMetres;
    public Mesh MovingMesh => loopMesh?.Mesh;
    public Transform FrontRoller => rollers == null ? null : rollers[0];
    public Transform RearRoller => rollers == null ? null : rollers[1];
    public float RollerRadiusMetres => loopMesh == null ? 0f : loopMesh.RollerRadius * loopMesh.UnitsToMetres;
    public float SurfaceBendRadiusMetres => loopMesh == null ? 0f : loopMesh.SurfaceRadius * loopMesh.UnitsToMetres;

    public void Configure(Renderer surface, float repeatLength, Material metal = null)
    {
        beltRenderer = surface;
        metresPerRepeat = Mathf.Max(0.001f, repeatLength);
        rollerMaterial = metal;
    }

    private void Awake()
    {
        if (beltRenderer == null) beltRenderer = GetComponent<Renderer>();
        properties = new MaterialPropertyBlock();
        filter = GetComponent<MeshFilter>();
        if (beltRenderer == null || filter == null || filter.sharedMesh == null)
        {
            Debug.LogError("A moving belt needs its renderer and source mesh.", this);
            enabled = false;
            return;
        }
        originalMesh = filter.sharedMesh;
        loopMesh = new ConveyorBeltLoopMesh(originalMesh, transform.TransformVector(Vector3.up).magnitude, metresPerRepeat);
        metresPerRepeat = loopMesh.MetresPerRepeat;
        filter.sharedMesh = loopMesh.Mesh;
        // UVs are fixed to the rubber. The actual vertices carry the pattern around the loop.
        beltRenderer.GetPropertyBlock(properties, 0);
        properties.SetVector(BaseMapST, new Vector4(1f, 1f, 0f, 0f));
        beltRenderer.SetPropertyBlock(properties, 0);
        CreateRollers();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || loopMesh != null || originalMesh == null || filter == null) return;
        Mesh previous = filter.sharedMesh;
        loopMesh = new ConveyorBeltLoopMesh(originalMesh, transform.TransformVector(Vector3.up).magnitude, metresPerRepeat);
        loopMesh.SetTravelMetres(travelMetres * (reverseDirection ? -1.0 : 1.0));
        filter.sharedMesh = loopMesh.Mesh;
        if (previous != null && previous != originalMesh) Release(previous);
        properties = new MaterialPropertyBlock();
        beltRenderer.GetPropertyBlock(properties, 0);
        properties.SetVector(BaseMapST, new Vector4(1f, 1f, 0f, 0f));
        beltRenderer.SetPropertyBlock(properties, 0);
    }

    private void Update()
    {
        if (loopMesh == null || EffectiveSpeed <= 0f) return;
        travelMetres += EffectiveSpeed * Time.deltaTime;
        double signedTravel = travelMetres * (reverseDirection ? -1.0 : 1.0);
        loopMesh.SetTravelMetres(signedTravel);
        float angle = (float)(signedTravel / SurfaceBendRadiusMetres * Mathf.Rad2Deg % 360.0);
        foreach (Transform roller in rollers) roller.localRotation = Quaternion.AngleAxis(angle, Vector3.right);
    }

    private void CreateRollers()
    {
        rollerRoot = new GameObject("MovingRollers").transform;
        rollerRoot.SetParent(transform, false);
        rollerMesh = ConveyorRollerMesh.Create();
        rollers = new Transform[2];
        Material metal = rollerMaterial != null ? rollerMaterial : beltRenderer.sharedMaterials[1];
        for (int i = 0; i < 2; i++)
        {
            Transform pivot = new GameObject(i == 0 ? "FrontRoller_Pivot" : "RearRoller_Pivot").transform;
            pivot.SetParent(rollerRoot, false);
            pivot.localPosition = loopMesh.Centre + Vector3.up * (i == 0 ? -loopMesh.HalfStraight : loopMesh.HalfStraight);
            rollers[i] = pivot;
            AddCylinder("RollerDrum", pivot, Vector3.zero,
                new Vector3(loopMesh.HalfWidth * 1.98f, loopMesh.RollerRadius, loopMesh.RollerRadius), metal);
            for (int side = -1; side <= 1; side += 2)
            {
                float capX = side * (loopMesh.HalfWidth + 0.04f / loopMesh.UnitsToMetres);
                AddCylinder("AxleCap", pivot, Vector3.right * capX,
                    new Vector3(0.007f / loopMesh.UnitsToMetres, loopMesh.RollerRadius * 0.6f, loopMesh.RollerRadius * 0.6f), metal);
                // An offset fastener makes rotation observable even on a plain metal cap.
                AddCylinder("RotationWitness", pivot,
                    new Vector3(capX + side * 0.005f / loopMesh.UnitsToMetres, loopMesh.RollerRadius * 0.35f, 0),
                    new Vector3(0.004f / loopMesh.UnitsToMetres, loopMesh.RollerRadius * 0.1f, loopMesh.RollerRadius * 0.1f),
                    beltRenderer.sharedMaterials[1]);
            }
        }
    }

    private void AddCylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject item = new GameObject(name);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        item.transform.localScale = scale;
        item.AddComponent<MeshFilter>().sharedMesh = rollerMesh;
        item.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private void OnDestroy()
    {
        if (filter != null && filter.sharedMesh == loopMesh?.Mesh) filter.sharedMesh = originalMesh;
        loopMesh?.Dispose();
        if (rollerMesh != null) Release(rollerMesh);
        if (rollerRoot != null) Release(rollerRoot.gameObject);
    }

    private static void Release(Object item)
    {
        if (Application.isPlaying) Destroy(item);
        else DestroyImmediate(item);
    }
}
