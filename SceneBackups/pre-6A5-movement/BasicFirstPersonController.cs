using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class BasicFirstPersonController : MonoBehaviour
{
    [SerializeField] private Transform cameraPivot;
    [SerializeField, Min(0.1f)] private float walkSpeed = 6f;
    [SerializeField, Min(0.1f)] private float sprintSpeed = 9f;
    [SerializeField, Min(0.1f)] private float jumpHeight = 1.5f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.15f;
    [SerializeField, Min(0.1f)] private float lookSensitivity = 0.12f;
    [SerializeField] private float gravity = -9f;
    [SerializeField] private float fallResetHeight = -12f;

    private CharacterController controller;
    private Vector3 spawnPosition;
    private float verticalSpeed;
    private float pitch;
    private float jumpBufferTimer;
    private bool jumpConsumed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;

        if (cameraPivot == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
            {
                cameraPivot = childCamera.transform;
            }
        }
    }

    /// <summary>
    /// Moves the respawn target used when the player falls below <see cref="fallResetHeight"/>.
    /// Called by <see cref="CheckpointVolume"/>; does not affect movement in any way.
    /// </summary>
    public void SetSpawn(Vector3 position)
    {
        spawnPosition = position;
    }

    /// <summary>
    /// Clears the accumulated vertical speed and any buffered jump. Called by
    /// <see cref="PlayerFreezeController"/> straight after a respawn teleport so the player does
    /// not inherit the fall velocity from before the reset. Changes no movement tuning.
    /// </summary>
    public void ResetMotion()
    {
        verticalSpeed = 0f;
        jumpBufferTimer = 0f;
        jumpConsumed = false;
    }

    private void OnEnable()
    {
        LockCursor();
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        HandleCursor(keyboard);
        HandleLook();
        HandleMovement(keyboard);

        if (transform.position.y < fallResetHeight)
        {
            controller.enabled = false;
            transform.position = spawnPosition;
            controller.enabled = true;
            verticalSpeed = 0f;
        }
    }

    private void HandleCursor(Keyboard keyboard)
    {
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            LockCursor();
        }
    }

    private void HandleLook()
    {
        if (cameraPivot == null || Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 delta = Mouse.current.delta.ReadValue() * lookSensitivity;
        pitch = Mathf.Clamp(pitch - delta.y, -85f, 85f);

        transform.Rotate(Vector3.up, delta.x, Space.Self);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement(Keyboard keyboard)
    {
        // CharacterController.isGrounded only reflects the most recent Move() call, and a Move
        // with no downward component performs no ground sweep. Sample it here, before this
        // frame's moves, so it still describes the previous frame's downward Move. Reading it
        // after the horizontal Move below reports false whenever the player is standing still
        // (planar == Vector3.zero), which would gate the jump off.
        bool grounded = controller.isGrounded;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed) horizontal -= 1f;
        if (keyboard.dKey.isPressed) horizontal += 1f;
        if (keyboard.sKey.isPressed) vertical -= 1f;
        if (keyboard.wKey.isPressed) vertical += 1f;

        Vector3 planar = transform.right * horizontal + transform.forward * vertical;
        if (planar.sqrMagnitude > 1f)
        {
            planar.Normalize();
        }

        bool sprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        float speed = sprinting ? sprintSpeed : walkSpeed;
        controller.Move(planar * speed * Time.deltaTime);

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.deltaTime);
        }

        if (grounded && verticalSpeed < 0f)
        {
            verticalSpeed = -2f;
            jumpConsumed = false;
        }

        bool jumpRequested = keyboard.spaceKey.isPressed || jumpBufferTimer > 0f;
        if (grounded && !jumpConsumed && jumpRequested)
        {
            verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
            jumpBufferTimer = 0f;
            jumpConsumed = true;
        }

        verticalSpeed += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalSpeed * Time.deltaTime);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
