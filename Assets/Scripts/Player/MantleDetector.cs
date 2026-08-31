using UnityEngine;

/// <summary>
/// Finds ledges the player can pull up onto.
///
/// This is the ability the Phase 6A report identified as load-bearing for a tall city: without it
/// every rooftop jump that lands short becomes a full fall. Two bands are supported - a grounded
/// mantle from a standstill or a run, and an airborne recovery where the reference height is the
/// player's current feet rather than the ground, so a jump that comes up slightly low still
/// catches the ledge.
///
/// Detection only; <see cref="BasicFirstPersonController"/> executes the movement.
/// </summary>
public sealed class MantleDetector : MonoBehaviour
{
    [Header("Grounded mantle - height above the player's feet")]
    [Tooltip("Below this the obstacle belongs to the vault, not the mantle.")]
    [SerializeField, Min(0.2f)] private float minHeight = 1.20f;

    [Tooltip("Roughly the player's own height. Above this there is nothing to reach with.")]
    [SerializeField, Min(0.5f)] private float maxHeight = 2.00f;

    [Header("Airborne recovery")]
    [Tooltip("Allow catching a ledge while in the air. This is the anti-frustration rule.")]
    [SerializeField] private bool allowAirborne = true;

    [Tooltip("Lowest ledge, relative to the airborne player's feet, that can still be caught.")]
    [SerializeField] private float airborneMinHeight = 0.15f;

    // 1.80 rather than ~2.0 on purpose. The absolute climb ceiling from a standing jump is
    // jumpHeight + this, and at 1.95 that came to 3.45m - only 5cm under a 3.5m storey. Any
    // building with a cornice or sill per floor would then be a ladder, which would undo the
    // rule that vertical gain must always be designed geometry. 1.80 gives a 3.30m ceiling.
    [Tooltip("Highest ledge catchable while airborne, relative to feet.")]
    [SerializeField, Min(0.5f)] private float airborneMaxHeight = 1.80f;

    [Tooltip("Only catch while at or past the apex. Stops a rising jump snapping to a ledge.")]
    [SerializeField] private float maxRisingSpeed = 1.5f;

    [Header("Reach")]
    [SerializeField, Min(0.05f)] private float forwardReach = 0.65f;

    [Tooltip("How far onto the surface the player is placed.")]
    [SerializeField, Min(0.1f)] private float standInset = 0.45f;

    public float MinHeight => minHeight;
    public float MaxHeight => maxHeight;
    public float AirborneMinHeight => airborneMinHeight;
    public float AirborneMaxHeight => airborneMaxHeight;
    public float ForwardReach => forwardReach;

    /// <summary>Where a successful mantle ends and how high the ledge was.</summary>
    public readonly struct Result
    {
        public readonly Vector3 Standing;
        public readonly Vector3 LedgePoint;
        public readonly float Height;

        public Result(Vector3 standing, Vector3 ledgePoint, float height)
        {
            Standing = standing;
            LedgePoint = ledgePoint;
            Height = height;
        }
    }

    /// <summary>
    /// Tests for a mantleable ledge ahead.
    /// </summary>
    /// <param name="grounded">Chooses the grounded band or the airborne recovery band.</param>
    /// <param name="verticalSpeed">Used to refuse a catch while still rising fast.</param>
    public bool TryFind(Vector3 feet, Vector3 forward, bool grounded, float verticalSpeed,
        float capsuleHeight, float capsuleRadius, LayerMask mask, Collider ignore, out Result result)
    {
        result = default;

        float lowBand, highBand;

        if (grounded)
        {
            lowBand = minHeight;
            highBand = maxHeight;
        }
        else
        {
            if (!allowAirborne || verticalSpeed > maxRisingSpeed)
            {
                return false;
            }

            lowBand = airborneMinHeight;
            highBand = airborneMaxHeight;
        }

        forward = new Vector3(forward.x, 0f, forward.z).normalized;
        if (forward.sqrMagnitude < 0.5f)
        {
            return false;
        }

        // Probe for a wall to climb across the whole band, top-down.
        //
        // The previous ladder started at lowBand + 0.25 and only climbed from there, which
        // addressed the wrong end of the problem: a ledge is only visible to a ray *below* its
        // top, so the lowest quarter-metre of every band - a 1.25m ledge in the grounded band -
        // had no ray that could see it at all and was silently unmantleable. Each candidate face
        // gets the full test, so a kerb in front of a real ledge cannot mask it.
        float castDistance = capsuleRadius + forwardReach;

        for (int i = 0; i < ParkourProbe.BandProbeCount; i++)
        {
            Vector3 origin = feet + Vector3.up * ParkourProbe.BandProbeHeight(i, lowBand, highBand);

            if (!ParkourProbe.Raycast(origin, forward, castDistance, mask, ignore, out RaycastHit hit)
                || !ParkourProbe.IsFacingWall(hit.normal, forward))
            {
                continue;
            }

            if (TryResolveLedge(hit, feet, forward, lowBand, highBand, capsuleHeight, capsuleRadius,
                    mask, ignore, out result))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Turns one candidate face into a mantle, or rejects it. Split out of <see cref="TryFind"/>
    /// so every probe height gets the same complete test.
    /// </summary>
    private bool TryResolveLedge(RaycastHit wall, Vector3 feet, Vector3 forward, float lowBand,
        float highBand, float capsuleHeight, float capsuleRadius, LayerMask mask, Collider ignore,
        out Result result)
    {
        result = default;

        // Find the top from above. This is what rejects ceilings, soffits and the undersides of
        // walkways - none of them produce an upward-facing surface within the band.
        if (!ParkourProbe.FindLedgeTop(wall.point, forward, capsuleRadius * 0.5f + 0.15f,
                highBand, feet.y, mask, ignore, out Vector3 top, out _))
        {
            return false;
        }

        float height = top.y - feet.y;
        if (height < lowBand || height > highBand)
        {
            return false;
        }

        // Standing room on the ledge, far enough in that the capsule is not half over the edge.
        Vector3 standing = top + forward * (capsuleRadius + standInset);
        standing.y = top.y;

        if (ParkourProbe.CapsuleBlocked(standing, capsuleHeight, capsuleRadius, mask, ignore))
        {
            return false;
        }

        // The surface has to continue under where the player will stand, or they would be placed
        // hovering past the far edge of a thin parapet.
        Vector3 footingProbe = standing + Vector3.up * 0.35f;
        if (!ParkourProbe.Raycast(footingProbe, Vector3.down, 0.8f, mask, ignore, out RaycastHit footing)
            || !ParkourProbe.IsStandableSurface(footing.normal, 0.5f))
        {
            return false;
        }

        standing.y = footing.point.y;

        result = new Result(standing, top, height);
        return true;
    }
}
