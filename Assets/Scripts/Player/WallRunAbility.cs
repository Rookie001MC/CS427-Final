using UnityEngine;

/// <summary>
/// Short horizontal wall runs.
///
/// Deliberately modest: about a second of reduced gravity along a wall the player was already
/// moving past at speed. It extends a route that would otherwise be a hair too long; it is not a
/// flight mechanic, and it cannot be used to gain height. The same wall cannot be re-used until
/// the player touches the ground or a different wall, which is what stops a player zig-zagging up
/// a corner forever.
///
/// Detection and state only - <see cref="BasicFirstPersonController"/> applies the motion.
/// </summary>
public sealed class WallRunAbility : MonoBehaviour
{
    [Header("Entry conditions")]
    // Above walk speed (6) on purpose: wall running is a sprint tool, and must not be enterable
    // by strolling into a wall.
    [Tooltip("Minimum horizontal speed to attach to a wall.")]
    [SerializeField, Min(0.1f)] private float minEntrySpeed = 7.0f;

    [Tooltip("How far beyond the capsule surface a wall is detected.")]
    [SerializeField, Min(0.05f)] private float detectionDistance = 0.35f;

    [Tooltip("Largest angle between travel direction and the wall's tangent, in degrees.")]
    [SerializeField, Range(5f, 80f)] private float maxApproachAngle = 55f;

    [Tooltip("Refuse to attach this close to the ground - it would look like scraping a kerb.")]
    [SerializeField, Min(0f)] private float minHeightAboveGround = 0.6f;

    [Header("While running")]
    [SerializeField, Min(0.1f)] private float maxDuration = 1.10f;

    [Tooltip("Fraction of normal gravity applied while attached.")]
    [SerializeField, Range(0f, 1f)] private float gravityScale = 0.28f;

    [Tooltip("Speed decay along the wall, m/s^2.")]
    [SerializeField, Min(0f)] private float drag = 1.6f;

    [Tooltip("Below this the run ends.")]
    [SerializeField, Min(0.1f)] private float minSustainSpeed = 4.5f;

    [Tooltip("Inward pull that keeps the player against the wall.")]
    [SerializeField, Min(0f)] private float stickForce = 3.0f;

    [Header("Wall jump")]
    [Tooltip("Outward push away from the wall.")]
    [SerializeField, Min(0f)] private float jumpOutward = 5.0f;

    [Tooltip("Fraction of the normal jump's upward velocity.")]
    [SerializeField, Range(0.2f, 1.2f)] private float jumpUpScale = 0.95f;

    [Tooltip("Fraction of along-wall speed carried out of the jump.")]
    [SerializeField, Range(0f, 1.2f)] private float jumpForwardScale = 0.85f;

    public bool IsRunning { get; private set; }

    /// <summary>+1 when the wall is on the player's right, -1 on the left.</summary>
    public int Side { get; private set; }

    /// <summary>Outward-facing normal of the attached wall.</summary>
    public Vector3 WallNormal { get; private set; }

    /// <summary>Unit direction of travel along the wall.</summary>
    public Vector3 Tangent { get; private set; }

    public float Speed { get; private set; }
    public float MinEntrySpeed => minEntrySpeed;
    public float DetectionDistance => detectionDistance;
    public float MaxDuration => maxDuration;
    public float GravityScale => gravityScale;

    /// <summary>0 at attach, 1 at the duration cap. Drives the camera roll fade.</summary>
    public float Progress => maxDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / maxDuration);

    private float elapsed;
    private Collider currentWall;
    private Collider lastWall;

    /// <summary>
    /// Looks for a wall on either side. Returns true and fills the state when one is valid.
    /// </summary>
    public bool TryAttach(Vector3 feet, Vector3 velocity, float capsuleHeight, float capsuleRadius,
        LayerMask mask, Collider ignore)
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;

        if (speed < minEntrySpeed)
        {
            return false;
        }

        // Never attach right above the floor.
        if (minHeightAboveGround > 0f
            && ParkourProbe.Raycast(feet + Vector3.up * 0.1f, Vector3.down, minHeightAboveGround,
                mask, ignore, out _))
        {
            return false;
        }

        Vector3 travel = flat / speed;
        Vector3 origin = feet + Vector3.up * (capsuleHeight * 0.55f);
        float distance = capsuleRadius + detectionDistance;

        // Right first, then left. Ties go to the right wall; with both walls in range the player
        // is in a shaft narrow enough that either choice reads the same.
        for (int i = 0; i < 2; i++)
        {
            int side = i == 0 ? 1 : -1;
            Vector3 direction = side > 0 ? transform.right : -transform.right;

            if (!ParkourProbe.Raycast(origin, direction, distance, mask, ignore, out RaycastHit hit))
            {
                continue;
            }

            if (hit.collider == lastWall)
            {
                continue;   // already used this wall since last touching ground
            }

            if (Mathf.Abs(hit.normal.y) > 0.25f)
            {
                continue;   // not near-vertical
            }

            // The player has to be travelling along the wall, not into it.
            Vector3 tangent = Vector3.ProjectOnPlane(travel, hit.normal).normalized;
            if (tangent.sqrMagnitude < 0.5f)
            {
                continue;
            }

            float angle = Vector3.Angle(travel, tangent);
            if (angle > maxApproachAngle)
            {
                continue;
            }

            IsRunning = true;
            elapsed = 0f;
            Side = side;
            WallNormal = hit.normal;
            Tangent = tangent;
            Speed = speed;
            currentWall = hit.collider;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Advances the run and refreshes the wall contact. Returns false when it should end.
    /// </summary>
    public bool Tick(float deltaTime, Vector3 feet, float capsuleHeight, float capsuleRadius,
        LayerMask mask, Collider ignore, bool grounded)
    {
        elapsed += deltaTime;
        Speed = Mathf.MoveTowards(Speed, 0f, drag * deltaTime);

        if (grounded || elapsed >= maxDuration || Speed < minSustainSpeed)
        {
            return false;
        }

        // Re-probe every frame: the wall ending is one of the required exit conditions.
        Vector3 origin = feet + Vector3.up * (capsuleHeight * 0.55f);
        Vector3 direction = Side > 0 ? transform.right : -transform.right;
        float distance = capsuleRadius + detectionDistance + 0.15f;

        if (!ParkourProbe.Raycast(origin, direction, distance, mask, ignore, out RaycastHit hit)
            || Mathf.Abs(hit.normal.y) > 0.35f)
        {
            return false;
        }

        WallNormal = hit.normal;
        currentWall = hit.collider;

        // Keep the tangent aligned with the wall as it curves.
        Vector3 projected = Vector3.ProjectOnPlane(Tangent, WallNormal);
        if (projected.sqrMagnitude > 0.01f)
        {
            Tangent = projected.normalized;
        }

        return true;
    }

    /// <summary>Velocity to leave the wall with. Call before <see cref="End"/>.</summary>
    public Vector3 GetJumpVelocity(float normalJumpUpSpeed)
        => Tangent * (Speed * jumpForwardScale)
           + WallNormal * jumpOutward
           + Vector3.up * (normalJumpUpSpeed * jumpUpScale);

    /// <summary>Horizontal velocity to apply this frame, including the inward stick.</summary>
    public Vector3 GetHorizontalVelocity() => Tangent * Speed - WallNormal * stickForce;

    /// <summary>Ends the run and blocks re-attaching to the same wall.</summary>
    public void End()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        lastWall = currentWall;
        currentWall = null;
        Speed = 0f;
    }

    /// <summary>Clears the same-wall lockout. Called whenever the player touches the ground.</summary>
    public void ClearWallMemory() => lastWall = null;

    /// <summary>Full reset. Used on respawn.</summary>
    public void ResetState()
    {
        IsRunning = false;
        elapsed = 0f;
        Speed = 0f;
        Side = 0;
        currentWall = null;
        lastWall = null;
    }
}
