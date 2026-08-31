using UnityEngine;

/// <summary>
/// The shape of one chevron, as data.
///
/// It lives here rather than in `SkyboundCityBuilder` for the reason every other dimension in this
/// city does - a builder may not hold a number the design owns - but also for a second one that
/// this particular shape earned. Every cyan chevron on the ground pointed <b>exactly backwards</b>
/// along the player's route, for as long as the feature existed, and nothing said so. It could not:
/// the route was right, <see cref="Breadcrumb.Forward"/> was right, `RouteGuide` aimed the marker's
/// local +Z along it correctly, and the tests checked all three. The error was a metre of local
/// offset in the builder - the arms were centred behind the origin and splayed outwards in front of
/// it, so they met at -Z and opened towards +Z, which makes an arrowhead aimed at where the player
/// has just been.
///
/// A shape a test cannot reach is a shape that can be wrong forever. So the arms are described here
/// as numbers, the builder is the only thing that turns them into cubes, and
/// <see cref="Apex"/> answers the question that actually matters - where does the point of the
/// arrow end up in the world - without a scene, a mesh or a camera.
///
/// Arms are laid out in the marker's local space with +Z forward, which is the axis `RouteGuide`
/// aligns with the direction of travel.
/// </summary>
public static class GuideChevron
{
    /// <summary>Half the distance between the open ends of the two arms, in marker sizes.</summary>
    public const float ArmOffset = 0.26f;

    /// <summary>
    /// How far ahead of the marker's origin each arm's centre sits, in marker sizes.
    ///
    /// Positive. This is the sign that was wrong: at -0.16 the arms converge behind the origin.
    /// </summary>
    public const float ArmForward = 0.16f;

    /// <summary>Yaw of the right arm, in degrees. The left arm is its mirror.</summary>
    public const float ArmSplay = 38f;

    public const float ArmThickness = 0.16f;

    public const float ArmLength = 0.95f;

    /// <summary>Where one arm's centre sits. <paramref name="sign"/> is -1 left, +1 right.</summary>
    public static Vector3 ArmCentre(float size, float sign, float lift)
        => new Vector3(sign * ArmOffset * size, lift, ArmForward * size);

    /// <summary>That arm's yaw, in degrees, about the marker's up axis.</summary>
    public static float ArmYaw(float sign) => -sign * ArmSplay;

    public static Vector3 ArmScale(float size)
        => new Vector3(ArmThickness * size, ArmThickness * size, ArmLength * size);

    /// <summary>
    /// The forward end of one arm, in the marker's local space. The two arms meet here - this is
    /// the point of the arrowhead, and the whole of what a player reads off a chevron.
    /// </summary>
    public static Vector3 ArmTip(float size, float sign, float lift)
        => ArmEnd(size, sign, lift, 0.5f);

    /// <summary>The other end of the same arm: one of the two open corners at the back.</summary>
    public static Vector3 ArmTail(float size, float sign, float lift)
        => ArmEnd(size, sign, lift, -0.5f);

    private static Vector3 ArmEnd(float size, float sign, float lift, float t)
    {
        // Explicit trig rather than Quaternion.Euler: this is the runtime City layer, and the
        // offline test runner has no engine to make that ECall into.
        float yaw = ArmYaw(sign) * Mathf.Deg2Rad;
        float reach = t * ArmLength * size;

        return ArmCentre(size, sign, lift)
               + new Vector3(Mathf.Sin(yaw) * reach, 0f, Mathf.Cos(yaw) * reach);
    }

    /// <summary>
    /// Where the point of the arrow lands in the world, for a marker standing at
    /// <paramref name="position"/> aimed along <paramref name="forward"/>.
    ///
    /// The basis is the one `Quaternion.LookRotation(forward, Vector3.up)` builds - local +Z along
    /// the heading, local +X along <c>cross(up, forward)</c> - written out so that "which way does
    /// the player see this pointing" is a question answerable by arithmetic.
    /// </summary>
    public static Vector3 Apex(Vector3 position, Vector3 forward, float size)
    {
        Vector3 ahead = new Vector3(forward.x, 0f, forward.z);

        if (ahead.sqrMagnitude < 0.0001f)
        {
            return position;
        }

        ahead = ahead.normalized;

        Vector3 right = new Vector3(ahead.z, 0f, -ahead.x);
        Vector3 tip = ArmTip(size, 1f, 0f);

        // Both arms meet at the same Z; take the midpoint of the two tips, which is on the axis.
        return position + right * (tip.x + ArmTip(size, -1f, 0f).x) * 0.5f
               + Vector3.up * tip.y + ahead * tip.z;
    }
}
