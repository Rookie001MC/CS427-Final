using UnityEngine;

/// <summary>
/// Thin adapter around the existing <see cref="BasicFirstPersonController"/> so the run state
/// machine can freeze, unfreeze and teleport the player without the movement code knowing that
/// any of those systems exist.
///
/// Freezing works by disabling the movement component. That is not a shortcut: the controller's
/// OnDisable already releases the cursor and its OnEnable re-locks it, which is exactly the
/// behaviour menus need, and a disabled Update cannot fight a menu for the cursor.
/// </summary>
public sealed class PlayerFreezeController : MonoBehaviour
{
    [SerializeField] private BasicFirstPersonController movement;
    [SerializeField] private CharacterController characterController;

    public CharacterController Controller => characterController;
    public bool MovementEnabled => movement != null && movement.enabled;

    private void Awake()
    {
        if (movement == null)
        {
            movement = GetComponent<BasicFirstPersonController>();
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }
    }

    /// <summary>
    /// Stops the player moving. Pass <paramref name="keepCursorLocked"/> during the countdown,
    /// where the player should be held still but the view should still feel in-game.
    /// </summary>
    public void Freeze(bool keepCursorLocked)
    {
        SetMovementEnabled(false);

        if (keepCursorLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>Hands control back. The controller's OnEnable re-locks the cursor.</summary>
    public void Unfreeze() => SetMovementEnabled(true);

    /// <summary>Explicitly shows the cursor for a menu, without touching movement state.</summary>
    public void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void SetMovementEnabled(bool value)
    {
        if (movement != null && movement.enabled != value)
        {
            movement.enabled = value;
        }
    }

    /// <summary>
    /// Moves the player without letting the CharacterController sweep through the geometry in
    /// between, then clears the fall velocity carried over from before the teleport.
    /// </summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        bool controllerWasEnabled = characterController != null && characterController.enabled;

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (characterController != null)
        {
            characterController.enabled = controllerWasEnabled;
        }

        ResetMotion();
    }

    /// <summary>Drops accumulated vertical speed so a respawn does not inherit a fall.</summary>
    public void ResetMotion()
    {
        if (movement != null)
        {
            movement.ResetMotion();
        }
    }
}
