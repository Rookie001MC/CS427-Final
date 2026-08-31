using UnityEngine;

/// <summary>
/// The reach formulas that every Skybound City dimension is derived from.
///
/// This is deliberately the *only* copy. `IndustrialRouteHarness`, `ParkourLevelBuilder.Step8` and
/// `ParkourMovementHarness` each grew their own inline version of the same algebra, and the Phase
/// 6A report had to re-derive it a fourth time to size the streets. A 600 x 600 m city authored
/// against a stale copy of these numbers would be unfixable, so the city tools all call in here.
///
/// Pure maths, no scene access, no UnityEditor - which is what lets the EditMode tests assert on
/// the city's dimensions without opening it.
///
/// Sign convention matches <see cref="BasicFirstPersonController"/>: gravity is negative, and
/// <c>rise</c> is positive upward, so a drop is a negative rise.
/// </summary>
public static class TraversalEnvelope
{
    /// <summary>
    /// Landing allowance, i.e. how much of the target surface is consumed just by having feet.
    /// 0.4 m, matching <c>IndustrialRouteHarness.Footing</c> - changing it here would silently
    /// re-grade every jump the older harnesses already validated.
    /// </summary>
    public const float Footing = 0.4f;

    /// <summary>
    /// Margin an authored gap must keep below the theoretical maximum. 0.75 m is the existing
    /// "OK" threshold in `IndustrialRouteHarness`; below it a jump is reported as tight.
    /// </summary>
    public const float Slack = 0.75f;

    /// <summary>The controller values a route is measured against.</summary>
    public readonly struct Movement
    {
        public readonly float Walk;
        public readonly float Sprint;
        public readonly float JumpHeight;

        /// <summary>Negative, as serialised on the controller.</summary>
        public readonly float Gravity;

        public Movement(float walk, float sprint, float jumpHeight, float gravity)
        {
            Walk = walk;
            Sprint = sprint;
            JumpHeight = jumpHeight;
            Gravity = gravity;
        }

        public float LaunchVelocity => Mathf.Sqrt(JumpHeight * -2f * Gravity);
    }

    /// <summary>
    /// The values serialised on the player in both shipped scenes and in the movement sandbox.
    /// The harnesses read the live component instead; this is the fallback the pure-maths tests
    /// and the offline city planner use.
    /// </summary>
    public static readonly Movement Default = new Movement(6f, 9f, 1.5f, -9f);

    /// <summary>
    /// Airtime available for a jump that ends <paramref name="rise"/> metres above the take-off
    /// surface. Negative rise is a drop and buys airtime.
    ///
    /// Returns false when the target is at or above the jump apex, which is the hard ceiling on
    /// unassisted climbing - no amount of speed reaches a ledge higher than <c>jumpHeight</c>.
    /// </summary>
    public static bool TryAirtime(in Movement m, float rise, out float airtime)
    {
        float g = -m.Gravity;
        float v0 = m.LaunchVelocity;
        float discriminant = v0 * v0 - 2f * g * rise;

        if (discriminant <= 0f)
        {
            airtime = 0f;
            return false;
        }

        airtime = (v0 + Mathf.Sqrt(discriminant)) / g;
        return true;
    }

    /// <summary>Horizontal distance covered at <paramref name="speed"/>, or 0 if unreachable.</summary>
    public static float Reach(in Movement m, float speed, float rise)
        => TryAirtime(m, rise, out float t) ? speed * t : 0f;

    /// <summary>
    /// The widest gap that may be *authored* at this rise - reach less footing less slack.
    /// Returns a negative number when the rise itself is unreachable, so callers that only test
    /// <c>gap &lt;= DesignGap</c> still fail correctly.
    /// </summary>
    public static float DesignGap(in Movement m, float speed, float rise)
        => TryAirtime(m, rise, out float t) ? speed * t - Footing - Slack : -1f;

    public static float SprintDesignGap(in Movement m, float rise) => DesignGap(m, m.Sprint, rise);

    public static float WalkDesignGap(in Movement m, float rise) => DesignGap(m, m.Walk, rise);

    /// <summary>
    /// Sprint design gap for a jump that *descends* <paramref name="drop"/> metres. This is the
    /// figure that sizes the avenues: a street is only genuinely un-crossable at roof level if it
    /// is wider than what a player can clear by dropping onto the far side.
    /// </summary>
    public static float DropAssistedSprintGap(in Movement m, float drop)
        => SprintDesignGap(m, -Mathf.Abs(drop));

    /// <summary>
    /// Highest surface a standing player can reach without a mantle. Equal to the jump height:
    /// above it <see cref="TryAirtime"/> has no solution.
    /// </summary>
    public static float UnassistedClimb(in Movement m) => m.JumpHeight;

    /// <summary>
    /// Absolute climb ceiling *with* the Phase 6A.5 airborne mantle: the jump apex plus the
    /// highest ledge the airborne band will still catch relative to the player's feet.
    ///
    /// 1.5 + 1.8 = 3.30 m. Storey height is set above this on purpose - see
    /// <see cref="CityDesign.StoreyHeight"/>. Keep this in step with
    /// <c>MantleDetector.airborneMaxHeight</c>.
    /// </summary>
    public const float AirborneMantleMaxHeight = 1.80f;

    public static float MantleAssistedClimb(in Movement m) => m.JumpHeight + AirborneMantleMaxHeight;
}
