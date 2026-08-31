using UnityEngine;

/// <summary>
/// Difficulty grade of a single authored jump. Set in Phase 6A.5 against the measured movement
/// envelope, not chosen by feel.
/// </summary>
public enum RouteTier
{
    /// <summary>Walk-reachable. No sprint, no precision.</summary>
    Green,

    /// <summary>Comfortable sprint jump. The default for a main route.</summary>
    Blue,

    /// <summary>Sprint required and precise, or a mantle step.</summary>
    Orange,

    /// <summary>Near the ceiling: frame-perfect flat, or drop- / wall-run-assisted.</summary>
    Red,

    /// <summary>Beyond the movement set. A gap graded this way is a design error, not a hard jump.</summary>
    Unreachable
}

/// <summary>Per-tier limits. All three must hold for a jump to qualify.</summary>
public readonly struct RouteTierSpec
{
    public readonly RouteTier Tier;
    public readonly float MaxGap;
    public readonly float MaxRise;
    public readonly float MinLandingDepth;

    public RouteTierSpec(RouteTier tier, float maxGap, float maxRise, float minLandingDepth)
    {
        Tier = tier;
        MaxGap = maxGap;
        MaxRise = maxRise;
        MinLandingDepth = minLandingDepth;
    }
}

/// <summary>
/// The tier table from the Phase 6A.5 traversal envelope, and the classifier that decides which
/// tier a measured jump actually is.
///
/// The distinction that matters: <see cref="Classify"/> reports what a jump *is*, and
/// <see cref="Matches"/> reports whether that agrees with what the level author *claimed*. The
/// city is authored by declaring a tier and then having `RouteTierValidator` measure the geometry
/// and disagree - the Phase 6A report's point that every dimension in it is a prediction until
/// something measures it.
/// </summary>
public static class RouteTiers
{
    /// <summary>
    /// Ordered easiest-first. <see cref="Classify"/> returns the first entry that accepts the
    /// jump, so a 3 m hop grades GREEN rather than "also technically RED".
    /// </summary>
    public static readonly RouteTierSpec[] Table =
    {
        //                              gap    rise   landing
        new RouteTierSpec(RouteTier.Green,  4.5f, 0.5f, 3.0f),
        new RouteTierSpec(RouteTier.Blue,   7.3f, 1.0f, 2.0f),
        new RouteTierSpec(RouteTier.Orange, 9.2f, 1.2f, 1.2f),
        new RouteTierSpec(RouteTier.Red,   10.0f, 1.2f, 1.2f)
    };

    /// <summary>
    /// Minimum slack a RED jump must retain below the theoretical maximum. The residual ~100 mm
    /// frame-rate spread plus landing-frame quantisation eats anything thinner.
    /// </summary>
    public const float RedMinimumSlack = 0.5f;

    /// <summary>
    /// Rise a player can gain by mantling the far edge instead of clearing it, from ORANGE
    /// upward - the Phase 6A.5 tier table's "or a 2.0 m mantle step".
    ///
    /// The airborne mantle makes this real rather than optimistic: at apex the feet are
    /// <c>jumpHeight</c> up, so a 2.0 m ledge sits 0.5 m above them, comfortably inside the
    /// 0.15-1.80 m airborne band. It is what lets adjacent roofs in a cluster differ by more than
    /// a plain jump's 1.2 m rise and still be linked.
    /// </summary>
    public const float MantleStepRise = 2.0f;

    /// <summary>The rise limit for a tier, including the mantle step where the tier allows one.</summary>
    public static float EffectiveMaxRise(in RouteTierSpec spec)
        => spec.Tier >= RouteTier.Orange ? Mathf.Max(spec.MaxRise, MantleStepRise) : spec.MaxRise;

    public static RouteTierSpec Spec(RouteTier tier)
    {
        foreach (RouteTierSpec spec in Table)
        {
            if (spec.Tier == tier)
            {
                return spec;
            }
        }

        return new RouteTierSpec(RouteTier.Unreachable, 0f, 0f, 0f);
    }

    /// <summary>
    /// The tier a measured jump genuinely falls into.
    ///
    /// A descending jump has a negative rise and is never limited by the rise, so only the gap and
    /// the landing depth decide it.
    /// </summary>
    public static RouteTier Classify(float gap, float rise, float landingDepth)
    {
        foreach (RouteTierSpec spec in Table)
        {
            if (gap <= spec.MaxGap && rise <= EffectiveMaxRise(spec) &&
                landingDepth >= spec.MinLandingDepth)
            {
                return spec.Tier;
            }
        }

        return RouteTier.Unreachable;
    }

    /// <summary>
    /// Does the measured geometry support the tier the author declared?
    ///
    /// Declaring a jump *harder* than it measures is accepted - a route may be graded by its worst
    /// jump, and demoting every easy hop in a RED route to GREEN would be noise. Declaring it
    /// *easier* than it measures is the failure this exists to catch.
    /// </summary>
    public static bool Matches(RouteTier declared, float gap, float rise, float landingDepth,
        out string reason)
    {
        RouteTier actual = Classify(gap, rise, landingDepth);

        if (actual == RouteTier.Unreachable)
        {
            reason = $"unreachable: gap {gap:F2} m, rise {rise:F2} m, landing {landingDepth:F2} m";
            return false;
        }

        if (actual > declared)
        {
            reason = $"measures {actual} but is declared {declared}: " +
                     $"gap {gap:F2} m, rise {rise:F2} m, landing {landingDepth:F2} m";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Slack left on a jump at the tier's assumed speed. RED jumps are checked against
    /// <see cref="RedMinimumSlack"/>; everything below RED already carries the full
    /// <see cref="TraversalEnvelope.Slack"/> by construction.
    /// </summary>
    public static float Slack(in TraversalEnvelope.Movement movement, RouteTier tier,
        float gap, float rise)
    {
        float speed = tier == RouteTier.Green ? movement.Walk : movement.Sprint;
        float reach = TraversalEnvelope.Reach(movement, speed, rise);
        return reach <= 0f ? -1f : reach - TraversalEnvelope.Footing - gap;
    }
}
