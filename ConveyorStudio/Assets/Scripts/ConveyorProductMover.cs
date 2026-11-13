using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Conveyor Studio/Conveyor Product Mover")]
public sealed class ConveyorProductMover : MonoBehaviour
{
    [SerializeField] private ConveyorSection conveyor;

    [Tooltip("Fallback metres/second for a conveyor without a belt motor. Prepared belts supply their own speed.")]
    [SerializeField, Min(0f)] private float speed = 0.5f;

    [Tooltip("Return to the starting conveyor after reaching the end of the connected chain.")]
    [SerializeField] private bool loop = true;

    private float distanceTravelled;
    private ConveyorSection currentConveyor;

    public ConveyorSection StartingConveyor => conveyor;
    public ConveyorSection CurrentConveyor => currentConveyor;
    public float DistanceTravelled => distanceTravelled;

    public void Configure(ConveyorSection start, bool repeat = false)
    {
        conveyor = start;
        loop = repeat;
    }

    // Start runs once when this component first becomes active in Play mode.
    private void Start()
    {
        if (conveyor == null || !conveyor.HasEndpoints || conveyor.Length < 0.0001f)
        {
            Debug.LogError("Assign a conveyor with distinct Entry and Exit markers.", this);
            enabled = false;
            return;
        }

        distanceTravelled = 0f;
        currentConveyor = conveyor;
        PlaceOnConveyor(currentConveyor.Length);
    }

    // Update runs each rendered frame. deltaTime converts metres/second to metres/frame.
    private void Update()
    {
        if (currentConveyor == null || !currentConveyor.HasEndpoints)
        {
            return;
        }

        float length = currentConveyor.Length;
        if (length < 0.0001f)
        {
            return;
        }

        float remainingTime = Time.deltaTime;
        int transfers = 0;
        while (remainingTime > 0f)
        {
            ConveyorBeltSurface belt = currentConveyor.BeltSurface;
            float travelSpeed = belt != null ? belt.EffectiveSpeed : Mathf.Max(0f, speed);
            if (travelSpeed <= 0f) break;
            float distanceToExit = Mathf.Max(0f, length - distanceTravelled);
            float step = Mathf.Min(distanceToExit, travelSpeed * remainingTime);
            distanceTravelled += step;
            remainingTime = Mathf.Max(0f, remainingTime - step / travelSpeed);
            if (distanceTravelled < length) break;

            // Consume time on each section, so transfers respect different/stopped motor speeds.
            if (++transfers > 256)
            {
                Debug.LogError("Too many conveyor transfers in one frame. Check the route and speed.", this);
                enabled = false;
                return;
            }

            if (currentConveyor.TryGetNext(out ConveyorSection next))
            {
                distanceTravelled = 0f;
                currentConveyor = next;
                length = currentConveyor.Length;
                continue;
            }

            // A displaced connection is blocked; wait at its Exit until it is aligned again.
            if (currentConveyor.Next != null)
            {
                distanceTravelled = length;
                break;
            }

            if (loop && conveyor != null && conveyor.HasEndpoints && conveyor.Length >= 0.0001f)
            {
                distanceTravelled = 0f;
                currentConveyor = conveyor;
                length = currentConveyor.Length;
                continue;
            }

            distanceTravelled = length;
            break;
        }

        PlaceOnConveyor(length);
    }

    private void PlaceOnConveyor(float length)
    {
        // Lerp uses a fraction: 0 is Entry, 1 is Exit.
        float progress = distanceTravelled / length;
        Vector3 position = Vector3.Lerp(currentConveyor.Entry.position, currentConveyor.Exit.position, progress);
        Vector3 direction = currentConveyor.Exit.position - currentConveyor.Entry.position;

        // The product parent's origin is its bottom centre, so this rests it on the path.
        transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(direction, currentConveyor.transform.up));
    }
}
