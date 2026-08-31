using UnityEngine;

/// <summary>
/// Shared collision queries for the parkour abilities.
///
/// Every query here ignores triggers and ignores one nominated collider - always the player's own
/// CharacterController. That matters more than it looks: checkpoint volumes, kill zones and finish
/// lines are all triggers sitting directly on the route, and a vault or mantle probe that saw them
/// would let the player climb thin air in the middle of a level.
/// </summary>
public static class ParkourProbe
{
    // Shared buffers. These queries run several times per frame per ability, and the allocating
    // overloads (RaycastAll / OverlapCapsule) would produce garbage every frame.
    private const int BufferSize = 16;
    private static readonly RaycastHit[] HitBuffer = new RaycastHit[BufferSize];
    private static readonly Collider[] OverlapBuffer = new Collider[BufferSize];

    /// <summary>
    /// Nearest hit along a ray, skipping <paramref name="ignore"/> and any trigger.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, float distance, LayerMask mask,
        Collider ignore, out RaycastHit hit)
    {
        hit = default;

        int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, distance, mask,
            QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (HitBuffer[i].collider == ignore || HitBuffer[i].distance >= nearest)
            {
                continue;
            }

            nearest = HitBuffer[i].distance;
            hit = HitBuffer[i];
            found = true;
        }

        return found;
    }

    /// <summary>
    /// True when a capsule of the given shape, standing with its feet at
    /// <paramref name="footPosition"/>, would overlap solid geometry.
    ///
    /// The capsule is inset by a small epsilon so that resting exactly on a floor, or standing
    /// flush against a wall, does not read as blocked - CharacterController.skinWidth means the
    /// player is always fractionally intersecting whatever they are standing on.
    /// </summary>
    public static bool CapsuleBlocked(Vector3 footPosition, float height, float radius,
        LayerMask mask, Collider ignore)
    {
        const float epsilon = 0.02f;

        // A capsule's sphere centres sit one radius in from each cap, so the offset is the
        // radius - not half the height. Using half the height collapsed a 2.0m capsule into a
        // single 0.33m sphere at waist level, which saw nothing at head or ankle height: a
        // player could be cleared to stand up into a ceiling or to land straddling a kerb.
        // The radius is additionally clamped so a capsule shorter than its own diameter
        // degenerates to a sphere rather than inverting its two centres.
        float shrunkRadius = Mathf.Max(0.01f, Mathf.Min(radius - epsilon, height * 0.5f - epsilon));

        Vector3 bottom = footPosition + Vector3.up * (shrunkRadius + epsilon);
        Vector3 top = footPosition + Vector3.up * (height - shrunkRadius - epsilon);

        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, shrunkRadius, OverlapBuffer, mask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (OverlapBuffer[i] != ignore)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Convenience inverse of <see cref="CapsuleBlocked"/>.</summary>
    public static bool CapsuleFree(Vector3 footPosition, float height, float radius,
        LayerMask mask, Collider ignore)
        => !CapsuleBlocked(footPosition, height, radius, mask, ignore);

    // Where along a height band to look for an obstacle's face, top-down, as fractions of the
    // band. The last sample is deliberately *below* the band floor: an obstacle whose top sits at
    // the floor of the band presents no face above it, so a ladder that starts inside the band
    // can never see the lowest obstacles the band is supposed to accept.
    private static readonly float[] BandFractions = { 0.90f, 0.60f, 0.30f, -0.08f };

    /// <summary>Number of face-probe heights <see cref="BandProbeHeight"/> will produce.</summary>
    public static int BandProbeCount => BandFractions.Length;

    /// <summary>
    /// Height above the player's feet at which to probe for an obstacle face, for one sample of a
    /// height band. Ordered from the top of the band downwards, so a real ledge is preferred over
    /// a kerb standing in front of it. Allocation-free: these run every frame per ability.
    /// </summary>
    public static float BandProbeHeight(int index, float bandLow, float bandHigh)
        => Mathf.Max(0.05f, Mathf.LerpUnclamped(bandLow, bandHigh, BandFractions[index]));

    /// <summary>
    /// True when a surface is flat enough to stand on. Used to reject ceilings, undersides and
    /// steep faces as vault tops or mantle ledges.
    /// </summary>
    public static bool IsStandableSurface(Vector3 normal, float minUpDot = 0.7f)
        => Vector3.Dot(normal, Vector3.up) >= minUpDot;

    /// <summary>
    /// True when a surface is close enough to vertical to be a wall the player can run along or
    /// vault over, and is facing back towards <paramref name="approach"/>.
    /// </summary>
    public static bool IsFacingWall(Vector3 normal, Vector3 approach, float maxTilt = 0.35f,
        float minFacing = 0.4f)
    {
        if (Mathf.Abs(normal.y) > maxTilt)
        {
            return false;
        }

        Vector3 flatApproach = new Vector3(approach.x, 0f, approach.z);
        if (flatApproach.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        return Vector3.Dot(normal.normalized, -flatApproach.normalized) >= minFacing;
    }

    /// <summary>
    /// Finds the top surface of an obstacle the player is facing.
    ///
    /// Probes downward from above the obstacle rather than upward from below, because an upward
    /// probe cannot tell a real ledge from the underside of an overhang. Returns the world height
    /// of the surface and the point that was hit.
    /// </summary>
    public static bool FindLedgeTop(Vector3 wallPoint, Vector3 forward, float probeInset,
        float maxHeightAboveFeet, float feetY, LayerMask mask, Collider ignore,
        out Vector3 topPoint, out Vector3 topNormal)
    {
        topPoint = default;
        topNormal = default;

        // Step in past the face so the downward ray lands on the top surface, not the wall itself.
        Vector3 origin = wallPoint + forward.normalized * probeInset;
        origin.y = feetY + maxHeightAboveFeet + 0.5f;

        if (!Raycast(origin, Vector3.down, maxHeightAboveFeet + 1.0f, mask, ignore, out RaycastHit hit))
        {
            return false;
        }

        if (!IsStandableSurface(hit.normal))
        {
            return false;
        }

        topPoint = hit.point;
        topNormal = hit.normal;
        return true;
    }
}
