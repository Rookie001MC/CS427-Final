using UnityEngine;

/// <summary>
/// Turns a stream of player positions into travelled distance, and rejects everything that was
/// not the player moving.
///
/// This exists as its own type, with no engine dependency beyond Vector3, because the whole
/// difficulty of "how far has the player run" is one question - was that displacement real - and
/// that question is pure arithmetic over two positions and a timestep. Kept here it can be tested
/// without a scene, and there is exactly one copy of the rule.
///
/// Two guards, and both are needed:
///
///  * <b>An explicit discontinuity.</b> Every respawn, restart and fall-plane reset goes through
///    a teleport, and the teleport is announced (see <see cref="PlayerFreezeController"/> and
///    <see cref="BasicFirstPersonController"/>). The next sample after one is dropped outright
///    rather than measured, because a respawn at an anchor the player died two metres from is a
///    perfectly plausible-looking two metres of travel and no threshold can catch it.
///
///  * <b>A plausibility ceiling.</b> Anything the first guard misses - a scene spawn, a
///    transform written by something that never announced it - is caught by the fact that the
///    player cannot cover more than <see cref="PlausibleSpeedCeiling"/> metres per second.
///
/// Distance is horizontal. The whole movement system measures itself horizontally
/// (<see cref="BasicFirstPersonController.CurrentHorizontalSpeed"/>, and
/// <see cref="RunStatsTracker"/> after it), and counting a 180 m fall down a Skybound City tower
/// as 180 m "travelled" would make the figure mean two different things at once.
/// </summary>
public struct MotionSampler
{
    /// <summary>
    /// The fastest the player can plausibly be moving, in m/s.
    ///
    /// The movement envelope tops out well below this: walk 6, sprint 9, a slide capped at 11,
    /// and a wall jump that leaves the wall at about 10. 40 is therefore over three and a half
    /// times the fastest sustained motion the game can produce - loose enough that a frame-rate
    /// hitch during a legitimate sprint is never discarded, and tight enough that any teleport
    /// across a 600 m city is. It is the same ceiling <see cref="RunStatsTracker"/> defaults to,
    /// so the run HUD and the career screen can never disagree about what is possible.
    /// </summary>
    public const float PlausibleSpeedCeiling = 40f;

    private Vector3 previous;
    private bool hasPrevious;

    /// <summary>
    /// True when the next sample can be measured against a previous one. False after a
    /// discontinuity and before the first sample.
    /// </summary>
    public bool IsPrimed => hasPrevious;

    /// <summary>
    /// Forgets where the player was, so the next sample establishes a new origin and contributes
    /// nothing. Called on teleports, on binding to a new scene, and whenever the run is not
    /// running.
    /// </summary>
    public void Discontinuity() => hasPrevious = false;

    /// <summary>
    /// Offers one frame of motion.
    ///
    /// Returns true - with the horizontal metres to credit - only when this frame followed
    /// continuously from the last one and was physically possible. A rejected frame still becomes
    /// the new origin, so one bad frame costs one frame of distance rather than desynchronising
    /// everything after it.
    /// </summary>
    public bool TryAdvance(Vector3 position, float deltaTime, out float metres)
    {
        metres = 0f;

        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z))
        {
            hasPrevious = false;
            return false;
        }

        if (!hasPrevious || deltaTime <= 0f || float.IsNaN(deltaTime))
        {
            previous = position;
            hasPrevious = true;
            return false;
        }

        float dx = position.x - previous.x;
        float dz = position.z - previous.z;
        float travelled = Mathf.Sqrt(dx * dx + dz * dz);

        previous = position;

        // The step budget scales with the frame, so a long frame is allowed to have covered more
        // ground rather than being mistaken for a jump in the transform.
        if (travelled > PlausibleSpeedCeiling * deltaTime)
        {
            return false;
        }

        metres = travelled;
        return travelled > 0f;
    }

    /// <summary>
    /// Whether a speed reading can be believed. A teleport, a transform snap or a scene load can
    /// all produce a figure in the hundreds; none of them is the player running.
    /// </summary>
    public static bool IsPlausibleSpeed(float metresPerSecond)
        => metresPerSecond >= 0f
           && !float.IsNaN(metresPerSecond)
           && !float.IsInfinity(metresPerSecond)
           && metresPerSecond <= PlausibleSpeedCeiling;
}
