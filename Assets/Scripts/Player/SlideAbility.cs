using UnityEngine;

/// <summary>
/// Momentum slide. Owns its own speed and direction while active, and owns the capsule resize.
///
/// Tuned to feel responsive rather than heavy: a small entry boost, a firm linear decay, and a
/// hard duration cap, so it reads as a traversal tool rather than a physics simulation. The
/// cooldown is what stops the player chaining slides to cross flat ground faster than sprinting.
///
/// Driven by <see cref="BasicFirstPersonController"/>; it has no Update of its own, so disabling
/// the controller (which is how <see cref="PlayerFreezeController"/> freezes the player) stops
/// this too.
/// </summary>
public sealed class SlideAbility : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Multiplier applied to entry speed. Small - the slide is for going under things.")]
    [SerializeField, Min(1f)] private float entryBoost = 1.12f;

    [Tooltip("Hard ceiling on slide speed, whatever the entry speed was.")]
    [SerializeField, Min(1f)] private float maxSpeed = 11f;

    [Tooltip("Deceleration in m/s^2.")]
    [SerializeField, Min(0.1f)] private float friction = 7.5f;

    [Tooltip("Below this the slide ends on its own.")]
    [SerializeField, Min(0.1f)] private float exitSpeed = 4.0f;

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float maxDuration = 1.4f;

    [Tooltip("Delay before another slide can start. Prevents infinite chaining.")]
    [SerializeField, Min(0f)] private float cooldown = 0.40f;

    [Tooltip("Minimum ground speed required to start a slide.")]
    [SerializeField, Min(0.1f)] private float minEntrySpeed = 7.0f;

    [Header("Capsule")]
    [Tooltip("Capsule height while sliding. Standing height is read from the controller.")]
    [SerializeField, Min(0.4f)] private float slideHeight = 1.0f;

    [Header("Steering")]
    [Tooltip("Degrees per second the slide direction can be turned. 0 locks it completely.")]
    [SerializeField, Min(0f)] private float steerRate = 55f;

    public bool IsSliding { get; private set; }
    public float Speed { get; private set; }
    public Vector3 Direction { get; private set; }
    public float SlideHeight => slideHeight;
    public float MinEntrySpeed => minEntrySpeed;
    public float Cooldown => cooldown;
    public float MaxDuration => maxDuration;

    /// <summary>0 at the start of a slide, 1 at its natural end. Drives the camera dip.</summary>
    public float Progress => maxDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / maxDuration);

    private float elapsed;
    private float cooldownRemaining;

    /// <summary>True when a new slide is allowed to start right now.</summary>
    public bool CanStart(bool grounded, float horizontalSpeed)
        => !IsSliding && grounded && cooldownRemaining <= 0f && horizontalSpeed >= minEntrySpeed;

    public void TickCooldown(float deltaTime)
    {
        if (cooldownRemaining > 0f)
        {
            cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        }
    }

    public void Begin(Vector3 direction, float entrySpeed)
    {
        IsSliding = true;
        elapsed = 0f;
        Direction = new Vector3(direction.x, 0f, direction.z).normalized;
        Speed = Mathf.Min(entrySpeed * entryBoost, maxSpeed);
    }

    /// <summary>
    /// Advances the slide. Returns false when it has run its course - the caller still has to
    /// check headroom before actually standing up.
    /// </summary>
    public bool Tick(float deltaTime, Vector3 steerInput, bool grounded)
    {
        elapsed += deltaTime;
        Speed = Mathf.MoveTowards(Speed, 0f, friction * deltaTime);

        if (steerRate > 0f && steerInput.sqrMagnitude > 0.01f)
        {
            Vector3 desired = new Vector3(steerInput.x, 0f, steerInput.z).normalized;
            Direction = Vector3.RotateTowards(Direction, desired,
                steerRate * Mathf.Deg2Rad * deltaTime, 0f).normalized;
        }

        // Leaving the ground ends it: a slide that carries off a rooftop should become a normal
        // fall, not a floating one.
        return grounded && Speed > exitSpeed && elapsed < maxDuration;
    }

    /// <summary>Ends the slide and starts the cooldown.</summary>
    public void End()
    {
        if (!IsSliding)
        {
            return;
        }

        IsSliding = false;
        Speed = 0f;
        cooldownRemaining = cooldown;
    }

    /// <summary>Clears all state without arming the cooldown. Used on respawn.</summary>
    public void ResetState()
    {
        IsSliding = false;
        Speed = 0f;
        elapsed = 0f;
        cooldownRemaining = 0f;
    }
}
