using UnityEngine;

/// <summary>
/// Finds low obstacles the player can cross without breaking stride.
///
/// Detection only - it never moves the player. <see cref="BasicFirstPersonController"/> asks this
/// component for a result and owns the movement, which keeps the traversal execution in one place
/// and means freezing the controller freezes the ability too.
/// </summary>
public sealed class VaultDetector : MonoBehaviour
{
    [Header("Obstacle height above the player's feet")]
    [Tooltip("Below this the CharacterController's 0.3m step offset already walks it.")]
    [SerializeField, Min(0.1f)] private float minHeight = 0.40f;

    [Tooltip("Above this the obstacle is a mantle, not a vault.")]
    [SerializeField, Min(0.2f)] private float maxHeight = 1.20f;

    [Header("Reach")]
    [Tooltip("How far in front of the capsule surface an obstacle can be and still vault.")]
    [SerializeField, Min(0.05f)] private float forwardReach = 0.55f;

    [Tooltip("Minimum ground speed. Stops vaults triggering from a standstill.")]
    [SerializeField, Min(0f)] private float minSpeed = 2.5f;

    [Tooltip("How far past the obstacle's top the player is placed.")]
    [SerializeField, Min(0.2f)] private float landingClearance = 0.75f;

    [Tooltip("Largest drop accepted on the far side before the vault is refused.")]
    [SerializeField, Min(0.5f)] private float maxLandingDrop = 3.0f;

    public float MinHeight => minHeight;
    public float MaxHeight => maxHeight;
    public float ForwardReach => forwardReach;
    public float MinSpeed => minSpeed;

    /// <summary>Where a successful vault ends and how high the obstacle was.</summary>
    public readonly struct Result
    {
        public readonly Vector3 Landing;
        public readonly Vector3 ObstacleTop;
        public readonly float Height;

        public Result(Vector3 landing, Vector3 obstacleTop, float height)
        {
            Landing = landing;
            ObstacleTop = obstacleTop;
            Height = height;
        }
    }

    /// <summary>
    /// Tests for a vaultable obstacle directly ahead.
    /// </summary>
    /// <param name="feet">World position of the player's feet.</param>
    /// <param name="forward">Flattened facing direction.</param>
    /// <param name="speed">Current horizontal speed.</param>
    public bool TryFind(Vector3 feet, Vector3 forward, float speed, float capsuleHeight,
        float capsuleRadius, LayerMask mask, Collider ignore, out Result result)
    {
        result = default;

        if (speed < minSpeed)
        {
            return false;
        }

        forward = new Vector3(forward.x, 0f, forward.z).normalized;
        if (forward.sqrMagnitude < 0.5f)
        {
            return false;
        }

        // 1. A wall face somewhere in the vaultable band.
        //
        //    One ray at the midpoint of the band cannot see the whole band. A 0.45m crate has no
        //    face at 0.80m for that ray to hit, so everything in the lower half of the 0.40-1.20m
        //    band was invisible and simply never vaulted. Sample from the top of the band down to
        //    just under its floor instead, and run the full test on each candidate rather than
        //    committing to the first face found - otherwise a kerb standing in front of a real
        //    obstacle masks it, and the kerb is then thrown out on height.
        float castDistance = capsuleRadius + forwardReach;

        for (int i = 0; i < ParkourProbe.BandProbeCount; i++)
        {
            Vector3 origin = feet + Vector3.up * ParkourProbe.BandProbeHeight(i, minHeight, maxHeight);

            if (!ParkourProbe.Raycast(origin, forward, castDistance, mask, ignore, out RaycastHit wall)
                || !ParkourProbe.IsFacingWall(wall.normal, forward))
            {
                continue;
            }

            if (TryResolve(wall, feet, forward, capsuleHeight, capsuleRadius, mask, ignore,
                    out result))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Turns one candidate obstacle face into a vault, or rejects it. Split out of
    /// <see cref="TryFind"/> so every probe height gets the same complete test.
    /// </summary>
    private bool TryResolve(RaycastHit wall, Vector3 feet, Vector3 forward, float capsuleHeight,
        float capsuleRadius, LayerMask mask, Collider ignore, out Result result)
    {
        result = default;

        // 2. The top of it, found from above so an overhang cannot masquerade as a ledge.
        if (!ParkourProbe.FindLedgeTop(wall.point, forward, capsuleRadius * 0.5f + 0.1f,
                maxHeight, feet.y, mask, ignore, out Vector3 top, out _))
        {
            return false;
        }

        float height = top.y - feet.y;
        if (height < minHeight || height > maxHeight)
        {
            return false;
        }

        // 3. Somewhere to come down on the far side.
        Vector3 landingProbe = top + forward * (capsuleRadius + landingClearance);
        landingProbe.y = top.y + 0.5f;

        Vector3 landing;
        if (ParkourProbe.Raycast(landingProbe, Vector3.down, maxLandingDrop + 1.0f, mask, ignore,
                out RaycastHit ground) && ParkourProbe.IsStandableSurface(ground.normal, 0.5f))
        {
            landing = ground.point;
        }
        else
        {
            // Nothing within range on the far side: this is a railing over a drop, not a vault.
            return false;
        }

        // 4. The player has to fit where they land, and has to fit crossing the top.
        if (ParkourProbe.CapsuleBlocked(landing, capsuleHeight, capsuleRadius, mask, ignore))
        {
            return false;
        }

        Vector3 crossing = top + Vector3.up * 0.05f;
        if (ParkourProbe.CapsuleBlocked(crossing, capsuleHeight * 0.55f, capsuleRadius * 0.85f, mask, ignore))
        {
            return false;
        }

        result = new Result(landing, top, height);
        return true;
    }
}
