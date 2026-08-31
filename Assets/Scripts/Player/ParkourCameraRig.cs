using UnityEngine;

/// <summary>
/// Camera feedback for the parkour abilities: eye-height dip during a slide, a small dip and
/// recovery through a mantle, and a roll into a wall run.
///
/// Strictly cosmetic. Nothing here is read back by the movement code, nothing here changes a
/// collider, and every value is a smoothed offset applied after the controller has set pitch and
/// yaw - so a camera effect can never alter where the player actually is or what they can reach.
/// The controller calls <see cref="Apply"/> once per frame, after its own look handling.
/// </summary>
public sealed class ParkourCameraRig : MonoBehaviour
{
    [Header("Eye height")]
    [Tooltip("Normal eye height above the player's feet. Read from the camera on Awake.")]
    [SerializeField] private float standingHeight = 1.7f;

    [Tooltip("Eye height while sliding.")]
    [SerializeField] private float slideHeight = 0.75f;

    [Tooltip("Seconds to move between eye heights. Kept short so it reads as a duck, not a lift.")]
    [SerializeField, Min(0.01f)] private float heightSmoothing = 0.09f;

    [Header("Wall run roll")]
    [Tooltip("Degrees of camera roll into the wall. Restrained on purpose.")]
    [SerializeField, Range(0f, 25f)] private float wallRunRoll = 13f;

    [SerializeField, Min(0.01f)] private float rollSmoothing = 0.12f;

    [Header("Mantle")]
    [Tooltip("How far the eye dips at the start of a mantle, in metres.")]
    [SerializeField, Range(0f, 0.5f)] private float mantleDip = 0.18f;

    private Transform pivot;
    private float currentHeight;
    private float heightVelocity;
    private float currentRoll;
    private float rollVelocity;
    private float mantleOffset;

    /// <summary>Roll in degrees currently applied. The controller folds this into the look.</summary>
    public float Roll => currentRoll;

    public void Initialise(Transform cameraPivot)
    {
        pivot = cameraPivot;

        if (pivot != null)
        {
            standingHeight = pivot.localPosition.y;
        }

        currentHeight = standingHeight;
        currentRoll = 0f;
        heightVelocity = 0f;
        rollVelocity = 0f;
        mantleOffset = 0f;
    }

    /// <summary>
    /// Updates the camera offsets. Runs on unscaled-independent delta supplied by the caller so
    /// it stays in step with the movement it is reacting to.
    /// </summary>
    /// <param name="sliding">Drives the eye dip.</param>
    /// <param name="wallRunSide">+1 right wall, -1 left wall, 0 none.</param>
    /// <param name="mantleProgress">0..1 through a mantle, or -1 when not mantling.</param>
    public void Apply(float deltaTime, bool sliding, int wallRunSide, float mantleProgress)
    {
        if (pivot == null)
        {
            return;
        }

        float targetHeight = sliding ? slideHeight : standingHeight;
        currentHeight = Mathf.SmoothDamp(currentHeight, targetHeight, ref heightVelocity,
            heightSmoothing, Mathf.Infinity, deltaTime);

        // A single dip that fades back in over the second half of the climb, so the mantle reads
        // as effort rather than as a teleport.
        mantleOffset = mantleProgress < 0f
            ? Mathf.MoveTowards(mantleOffset, 0f, deltaTime * 2f)
            : -mantleDip * Mathf.Sin(Mathf.Clamp01(mantleProgress) * Mathf.PI);

        Vector3 local = pivot.localPosition;
        local.y = currentHeight + mantleOffset;
        pivot.localPosition = local;

        // Roll toward the wall: a right-hand wall rolls the view right.
        float targetRoll = wallRunSide * wallRunRoll;
        currentRoll = Mathf.SmoothDamp(currentRoll, targetRoll, ref rollVelocity, rollSmoothing,
            Mathf.Infinity, deltaTime);
    }

    /// <summary>Snaps everything back to neutral. Used on respawn and teleport.</summary>
    public void ResetImmediate()
    {
        currentHeight = standingHeight;
        currentRoll = 0f;
        heightVelocity = 0f;
        rollVelocity = 0f;
        mantleOffset = 0f;

        if (pivot != null)
        {
            Vector3 local = pivot.localPosition;
            local.y = standingHeight;
            pivot.localPosition = local;
        }
    }
}
