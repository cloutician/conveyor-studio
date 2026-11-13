#if UNITY_EDITOR
using UnityEngine;

// Records real solver contacts for the isolated editor physics verification.
[AddComponentMenu("")]
public sealed class ConveyorLandingProbe : MonoBehaviour
{
    public bool landed;
    public float maximumTravel, elapsed, settledAt = -1f;
    public Vector3 velocityBefore, velocityAfter, angularAfter;
    public string lastWallContact;
    public Vector3 lastWallNormal;
    private Rigidbody body;
    private Vector3 landingPosition;
    private float stillFor;
    private void Awake() => body = GetComponent<Rigidbody>();
    private void OnCollisionEnter(Collision collision)
    {
        ObserveWall(collision);
        if (landed || collision.collider.sharedMaterial != ConveyorCollision.Material("Tile")) return;
        landed = true; landingPosition = body.worldCenterOfMass;
        velocityBefore = collision.relativeVelocity; velocityAfter = body.linearVelocity; angularAfter = body.angularVelocity;
    }
    private void OnCollisionStay(Collision collision) => ObserveWall(collision);
    private void ObserveWall(Collision collision)
    {
        if (collision.collider.GetComponentInParent<ConveyorSection>() == null) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            if (Vector3.ProjectOnPlane(contact.normal, Vector3.up).magnitude < 0.5f) continue;
            lastWallContact = collision.collider.name + " / " + collision.collider.transform.parent.name;
            lastWallNormal = contact.normal;
        }
    }
    public void AfterStep(float dt)
    {
        if (!landed) return;
        elapsed += dt;
        maximumTravel = Mathf.Max(maximumTravel, Vector3.ProjectOnPlane(body.worldCenterOfMass - landingPosition, Vector3.up).magnitude);
        bool still = body.linearVelocity.magnitude < 0.03f && body.angularVelocity.magnitude < 0.1f;
        stillFor = still ? stillFor + dt : 0f;
        if (stillFor >= 0.1f && settledAt < 0f) settledAt = elapsed - stillFor;
    }
}
#endif
