using System;
using UnityEngine;

/// <summary>
/// Kills the player for a fall they should not have survived, measured from the top of the fall to
/// the surface they land on.
///
/// This is the component <see cref="CityDesign.SafeDropHeight"/> was written against. Phase 6C's
/// roof graph refuses to count a descent of more than three storeys as a connection, precisely so
/// that the redundancy it proved - every relay reachable from at least three separate ways in -
/// would still hold once falling started to cost something. <see cref="CityDesign.FatalFallHeight"/>
/// is one storey above that limit, so no route the network calls a route is ever within a storey of
/// killing the player, and stepping off a Corporate roof still is.
///
/// Two deliberate choices:
///
///   <b>Metres, not landing speed.</b> Every dimension in the city is expressed in metres and the
///   tier table grades jumps by rise, so a velocity threshold would put this one rule in units
///   nothing else in the design uses and make it impossible to check against the plan.
///
///   <b>The apex, not the take-off.</b> A player who jumps up off a roof and then falls has fallen
///   from the top of the jump, which is where the fall actually started. Measuring from the ledge
///   would quietly give every fall 1.5 m of free height.
///
/// Disarms itself before raising, exactly like <see cref="FallDetector"/>, so a refused death is
/// re-armed by the caller rather than firing every frame the player lies on the pavement.
/// </summary>
public sealed class FallImpactDetector : MonoBehaviour
{
    [Tooltip("The player's CharacterController. Grounded state and height are both read from it.")]
    [SerializeField] private CharacterController target;

    [Tooltip("Fall that kills, in metres. Skybound City sets this from CityDesign.FatalFallHeight.")]
    [SerializeField, Min(1f)] private float fatalFallHeight = 14.4f;

    /// <summary>Raised once per arming. Payload is how far the player fell, in metres.</summary>
    public event Action<float> FatalImpact;

    public float FatalFallHeight => fatalFallHeight;

    /// <summary>How far the last completed fall was. Survivable ones are recorded too.</summary>
    public float LastFallHeight { get; private set; }

    private bool armed = true;
    private bool airborne;
    private float peakY;

    private void Awake()
    {
        if (target == null)
        {
            target = GetComponent<CharacterController>();
        }

        ResetTracking();
    }

    /// <summary>
    /// Sampled after everything has moved. <see cref="CharacterController.isGrounded"/> only
    /// reflects the most recent Move call, so reading it in Update would sample the state the
    /// player was in last frame - and land the fall one frame late, on a different surface.
    /// </summary>
    private void LateUpdate()
    {
        if (target == null || !target.enabled)
        {
            return;
        }

        float y = target.transform.position.y;

        if (!target.isGrounded)
        {
            airborne = true;
            peakY = Mathf.Max(peakY, y);
            return;
        }

        if (airborne)
        {
            LastFallHeight = Mathf.Max(0f, peakY - y);
            airborne = false;

            if (armed && LastFallHeight > fatalFallHeight)
            {
                armed = false;
                FatalImpact?.Invoke(LastFallHeight);
            }
        }

        peakY = y;
    }

    /// <summary>Re-arms after a respawn, and forgets the fall that got the player there.</summary>
    public void Rearm()
    {
        armed = true;
        ResetTracking();
    }

    /// <summary>
    /// Also called on a teleport: a respawn moves the player without a fall having happened, and
    /// carrying the old apex across would kill them on landing at the anchor they just spawned on.
    /// </summary>
    public void ResetTracking()
    {
        airborne = false;
        peakY = target != null ? target.transform.position.y : transform.position.y;
        LastFallHeight = 0f;
    }
}
