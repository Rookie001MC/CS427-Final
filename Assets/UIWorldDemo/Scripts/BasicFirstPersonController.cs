using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person parkour controller.
///
/// Owns input, look, and the single Update that drives every movement state. The four parkour
/// abilities live in their own components (<see cref="SlideAbility"/>, <see cref="VaultDetector"/>,
/// <see cref="MantleDetector"/>, <see cref="WallRunAbility"/>) but none of them has an Update of
/// its own - this class asks them for decisions and applies the motion itself. That is deliberate:
/// <see cref="PlayerFreezeController"/> freezes the player by disabling this component, and a
/// separate ability with its own Update would keep running through pause, death and the countdown.
///
/// The class name, the file location and the field names walkSpeed / sprintSpeed / jumpHeight /
/// gravity / fallResetHeight are load-bearing. The editor route harnesses read them by string
/// through SerializedObject, and both shipped scenes reference this component by GUID.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public sealed class BasicFirstPersonController : MonoBehaviour
{
    /// <summary>What the player is currently doing. Exposed for the harnesses and the HUD.</summary>
    public enum MoveState
    {
        Normal,
        Sliding,
        WallRunning,

        /// <summary>Mid vault or mantle: a scripted displacement owns the transform.</summary>
        Traversing
    }

    private enum TraversalKind { Vault, Mantle }

    [SerializeField] private Transform cameraPivot;
    [SerializeField, Min(0.1f)] private float walkSpeed = 6f;
    [SerializeField, Min(0.1f)] private float sprintSpeed = 9f;
    [SerializeField, Min(0.1f)] private float jumpHeight = 1.5f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.15f;
    [SerializeField, Min(0.1f)] private float lookSensitivity = 0.12f;
    [SerializeField] private float gravity = -9f;
    [SerializeField] private float fallResetHeight = -12f;

    [Header("Parkour")]
    [Tooltip("Master switch. Off leaves exactly the original walk / sprint / jump controller.")]
    [SerializeField] private bool parkourEnabled = true;

    [Tooltip("Geometry the parkour probes may use. Triggers are always ignored.")]
    [SerializeField] private LayerMask obstacleMask = ~0;

    [Header("Traversal timing")]
    [SerializeField, Min(0.05f)] private float vaultDuration = 0.28f;
    [SerializeField, Min(0.05f)] private float mantleDuration = 0.42f;

    [Header("Launch momentum")]
    [Tooltip("How long a wall jump keeps authority over the player's horizontal velocity before\n"
             + "normal air control fades back in. Without this the outward push lasts one frame\n"
             + "and the player slides straight back down the wall they just jumped off.")]
    [SerializeField, Min(0.05f)] private float launchBlendTime = 0.45f;

    [Header("Ability components (auto-found if left empty)")]
    [SerializeField] private SlideAbility slide;
    [SerializeField] private VaultDetector vault;
    [SerializeField] private MantleDetector mantle;
    [SerializeField] private WallRunAbility wallRun;
    [SerializeField] private ParkourCameraRig cameraRig;

    private CharacterController controller;
    private Vector3 spawnPosition;
    private float verticalSpeed;
    private float pitch;
    private float jumpBufferTimer;
    private bool jumpConsumed;

    private MoveState state = MoveState.Normal;

    private float standingHeight;
    private Vector3 standingCentre;

    private Vector3 launchVelocity;
    private float launchRemaining;

    private TraversalKind traversalKind;
    private Vector3 traversalStart;
    private Vector3 traversalEnd;
    private float traversalElapsed;
    private float traversalDuration;

    /// <summary>Take-off velocity for the configured jump height. Shared with the wall jump.</summary>
    public float JumpVelocity => Mathf.Sqrt(jumpHeight * -2f * gravity);

    public MoveState CurrentState => state;
    public float VerticalSpeed => verticalSpeed;
    public float WalkSpeed => walkSpeed;
    public float SprintSpeed => sprintSpeed;
    public float JumpHeight => jumpHeight;
    public float Gravity => gravity;
    public LayerMask ObstacleMask => obstacleMask;
    public bool ParkourEnabled { get => parkourEnabled; set => parkourEnabled = value; }

    /// <summary>Horizontal speed the state machine is currently producing.</summary>
    public float CurrentHorizontalSpeed { get; private set; }

    // ------------------------------------------------------------------ integration

    /// <summary>
    /// One step of vertical motion under constant acceleration.
    ///
    /// Velocity-Verlet, not the forward Euler this controller originally used. For a constant
    /// acceleration the exact solution is a parabola, and displacement = (v + a*dt/2)*dt
    /// integrates it exactly at any step size - so apex height no longer drifts with frame rate.
    /// The old form applied a full gravity step before moving, which cost 86mm of apex between
    /// 30fps and 240fps: enough to make a 1.45m ledge reachable on one machine and not another.
    /// </summary>
    public static float IntegrateVertical(float velocity, float acceleration, float deltaTime,
        out float displacement)
    {
        displacement = (velocity + 0.5f * acceleration * deltaTime) * deltaTime;
        return velocity + acceleration * deltaTime;
    }

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        spawnPosition = transform.position;

        standingHeight = controller.height;
        standingCentre = controller.center;

        if (cameraPivot == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
            {
                cameraPivot = childCamera.transform;
            }
        }

        // Abilities are optional: a scene that predates them still gets the original controller.
        if (slide == null) slide = GetComponent<SlideAbility>();
        if (vault == null) vault = GetComponent<VaultDetector>();
        if (mantle == null) mantle = GetComponent<MantleDetector>();
        if (wallRun == null) wallRun = GetComponent<WallRunAbility>();
        if (cameraRig == null) cameraRig = GetComponent<ParkourCameraRig>();

        if (cameraRig != null)
        {
            cameraRig.Initialise(cameraPivot);
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
    /// Clears accumulated vertical speed, any buffered jump, and any parkour state. Called by
    /// <see cref="PlayerFreezeController"/> straight after a respawn teleport so the player does
    /// not inherit the fall velocity - or a half-finished slide - from before the reset.
    /// </summary>
    public void ResetMotion()
    {
        verticalSpeed = 0f;
        jumpBufferTimer = 0f;
        jumpConsumed = false;
        launchVelocity = Vector3.zero;
        launchRemaining = 0f;

        // A respawn during a slide would otherwise leave the capsule short and the camera low.
        RestoreStandingCapsule();

        if (slide != null) slide.ResetState();
        if (wallRun != null) wallRun.ResetState();
        if (cameraRig != null) cameraRig.ResetImmediate();

        state = MoveState.Normal;
        CurrentHorizontalSpeed = 0f;
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

        float dt = Time.deltaTime;

        HandleCursor(keyboard);
        HandleLook();
        HandleMovement(keyboard, dt);

        if (cameraRig != null)
        {
            float mantleProgress = state == MoveState.Traversing && traversalKind == TraversalKind.Mantle
                ? Mathf.Clamp01(traversalElapsed / Mathf.Max(0.0001f, traversalDuration))
                : -1f;

            cameraRig.Apply(dt,
                slide != null && slide.IsSliding,
                wallRun != null && wallRun.IsRunning ? wallRun.Side : 0,
                mantleProgress);
        }

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

        // Roll is a pure camera effect from the rig; it never feeds back into movement.
        float roll = cameraRig != null ? cameraRig.Roll : 0f;
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, roll);
    }

    // ------------------------------------------------------------------ movement

    private void HandleMovement(Keyboard keyboard, float dt)
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

        bool hasMoveInput = planar.sqrMagnitude > 0.01f;
        bool sprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        bool slidePressed = keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame;

        // Jump buffering is unchanged from the original controller.
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - dt);
        }

        bool jumpRequested = keyboard.spaceKey.isPressed || jumpBufferTimer > 0f;

        // The wall jump needs a fresh press, not a held key. The jump that put the player in the
        // air is usually still held at the moment they attach to a wall, and a held-key test
        // would fire the wall jump on the same frame - so the wall run would never happen.
        bool jumpPressedThisFrame = keyboard.spaceKey.wasPressedThisFrame;

        if (slide != null)
        {
            slide.TickCooldown(dt);
        }

        if (grounded && wallRun != null)
        {
            wallRun.ClearWallMemory();
        }

        switch (state)
        {
            case MoveState.Traversing:
                TickTraversal(dt);
                return;

            case MoveState.Sliding:
                TickSlide(dt, planar, grounded, jumpRequested);
                return;

            case MoveState.WallRunning:
                TickWallRun(dt, grounded, jumpPressedThisFrame);
                return;

            default:
                TickNormal(dt, planar, hasMoveInput, sprinting, grounded, jumpRequested, slidePressed);
                return;
        }
    }

    private void TickNormal(float dt, Vector3 planar, bool hasMoveInput, bool sprinting,
        bool grounded, bool jumpRequested, bool slidePressed)
    {
        if (grounded && verticalSpeed < 0f)
        {
            verticalSpeed = -2f;
            jumpConsumed = false;
            launchRemaining = 0f;
        }

        float speed = sprinting ? sprintSpeed : walkSpeed;
        Vector3 desired = planar * speed;

        // A wall jump owns the horizontal velocity briefly, then air control fades back in. The
        // blend is a lerp rather than an additive push so the player can never exceed the launch
        // speed by also holding a movement key.
        if (launchRemaining > 0f)
        {
            launchRemaining = Mathf.Max(0f, launchRemaining - dt);
            float weight = launchBlendTime <= 0f ? 0f : launchRemaining / launchBlendTime;
            desired = Vector3.Lerp(desired, launchVelocity, weight);
        }

        CurrentHorizontalSpeed = desired.magnitude;

        if (parkourEnabled)
        {
            // Every ability requires the player to be steering into the geometry. Without that
            // rule a contextual jump next to a wall would silently become a mantle, which would
            // change how the two shipped levels play.
            Vector3 intent = hasMoveInput ? planar : Vector3.zero;

            if (hasMoveInput && slidePressed && slide != null
                && slide.CanStart(grounded, CurrentHorizontalSpeed))
            {
                BeginSlide(intent, CurrentHorizontalSpeed);
                return;
            }

            if (hasMoveInput && jumpRequested && TryBeginTraversal(intent, grounded, speed))
            {
                return;
            }

            if (!grounded && hasMoveInput && wallRun != null
                && wallRun.TryAttach(transform.position, planar * speed, standingHeight,
                    controller.radius, obstacleMask, controller))
            {
                state = MoveState.WallRunning;
                jumpConsumed = true;
                return;
            }
        }

        if (grounded && !jumpConsumed && jumpRequested)
        {
            verticalSpeed = JumpVelocity;
            jumpBufferTimer = 0f;
            jumpConsumed = true;
        }

        controller.Move(desired * dt);
        ApplyVertical(dt, gravity);
    }

    private void TickSlide(float dt, Vector3 planar, bool grounded, bool jumpRequested)
    {
        bool continues = slide.Tick(dt, planar, grounded);

        // Jumping out of a slide is the intended exit, but only if there is room to stand.
        if (jumpRequested && !jumpConsumed && grounded && HasStandingRoom())
        {
            EndSlide();
            verticalSpeed = JumpVelocity;
            jumpBufferTimer = 0f;
            jumpConsumed = true;
            ApplyVertical(dt, gravity);
            return;
        }

        if (!continues)
        {
            // Refuse to stand up under an overhang: keep sliding, at a crawl, until clear.
            if (HasStandingRoom())
            {
                EndSlide();
            }
            else
            {
                slide.Begin(slide.Direction, slide.MinEntrySpeed * 0.45f);
            }
        }

        CurrentHorizontalSpeed = slide.Speed;
        controller.Move(slide.Direction * slide.Speed * dt);
        ApplyVertical(dt, gravity);
    }

    private void TickWallRun(float dt, bool grounded, bool jumpPressedThisFrame)
    {
        bool continues = wallRun.Tick(dt, transform.position, standingHeight, controller.radius,
            obstacleMask, controller, grounded);

        if (jumpPressedThisFrame && continues)
        {
            Vector3 launch = wallRun.GetJumpVelocity(JumpVelocity);
            wallRun.End();
            state = MoveState.Normal;

            verticalSpeed = launch.y;
            jumpBufferTimer = 0f;
            jumpConsumed = true;

            // Hand the horizontal launch to the blend rather than applying it for one frame.
            launchVelocity = new Vector3(launch.x, 0f, launch.z);
            launchRemaining = launchBlendTime;

            CurrentHorizontalSpeed = launchVelocity.magnitude;
            controller.Move(launchVelocity * dt);
            ApplyVertical(dt, gravity);
            return;
        }

        if (!continues)
        {
            wallRun.End();
            state = MoveState.Normal;
            ApplyVertical(dt, gravity);
            return;
        }

        Vector3 horizontal = wallRun.GetHorizontalVelocity();
        CurrentHorizontalSpeed = wallRun.Speed;
        controller.Move(horizontal * dt);

        // Reduced gravity is the whole point: it buys height across a gap without ever adding any.
        ApplyVertical(dt, gravity * wallRun.GravityScale);
    }

    // ------------------------------------------------------------------ abilities

    private void BeginSlide(Vector3 direction, float entrySpeed)
    {
        slide.Begin(direction, entrySpeed);
        state = MoveState.Sliding;

        controller.height = slide.SlideHeight;
        controller.center = new Vector3(standingCentre.x, slide.SlideHeight * 0.5f, standingCentre.z);
    }

    private void EndSlide()
    {
        slide.End();
        RestoreStandingCapsule();
        state = MoveState.Normal;
    }

    private void RestoreStandingCapsule()
    {
        if (controller == null)
        {
            return;
        }

        controller.height = standingHeight;
        controller.center = standingCentre;
    }

    private bool HasStandingRoom()
        => ParkourProbe.CapsuleFree(transform.position, standingHeight, controller.radius,
            obstacleMask, controller);

    /// <summary>
    /// Contextual Space. Mantle is tested before vault because their height bands meet at 1.2m and
    /// a ledge that qualifies as both should be climbed, not hurdled.
    /// </summary>
    private bool TryBeginTraversal(Vector3 intent, bool grounded, float speed)
    {
        float radius = controller.radius;

        if (mantle != null && mantle.TryFind(transform.position, intent, grounded, verticalSpeed,
                standingHeight, radius, obstacleMask, controller, out MantleDetector.Result m))
        {
            BeginTraversal(TraversalKind.Mantle, m.Standing, mantleDuration);
            return true;
        }

        if (grounded && vault != null && vault.TryFind(transform.position, intent, speed,
                standingHeight, radius, obstacleMask, controller, out VaultDetector.Result v))
        {
            BeginTraversal(TraversalKind.Vault, v.Landing, vaultDuration);
            return true;
        }

        return false;
    }

    private void BeginTraversal(TraversalKind kind, Vector3 destination, float duration)
    {
        traversalKind = kind;
        traversalStart = transform.position;
        traversalEnd = destination;
        traversalElapsed = 0f;
        traversalDuration = duration;

        verticalSpeed = 0f;
        jumpBufferTimer = 0f;
        jumpConsumed = true;
        state = MoveState.Traversing;

        // The CharacterController is off for the duration. Both destinations were capsule-tested
        // before we got here, and a swept Move would catch on the very obstacle being crossed.
        controller.enabled = false;
    }

    private void TickTraversal(float dt)
    {
        traversalElapsed += dt;
        float t = Mathf.Clamp01(traversalElapsed / traversalDuration);

        Vector3 start = traversalStart;
        Vector3 end = traversalEnd;

        float horizontalT;
        float y;

        if (traversalKind == TraversalKind.Mantle)
        {
            // Up first, then in. Moving both at once would clip a shoulder through the parapet.
            horizontalT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.65f));
            float verticalT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.60f));
            y = Mathf.Lerp(start.y, end.y, verticalT);
        }
        else
        {
            // A vault holds its speed through the obstacle, so the horizontal term stays linear
            // and only a small arc is added over the top.
            horizontalT = t;
            float arc = Mathf.Sin(t * Mathf.PI) * 0.18f;
            y = Mathf.Lerp(start.y, end.y, Mathf.SmoothStep(0f, 1f, t)) + arc;
        }

        Vector3 position = Vector3.Lerp(
            new Vector3(start.x, 0f, start.z),
            new Vector3(end.x, 0f, end.z),
            horizontalT);

        transform.position = new Vector3(position.x, y, position.z);

        if (t < 1f)
        {
            return;
        }

        transform.position = traversalEnd;
        controller.enabled = true;
        state = MoveState.Normal;

        // Leave the ground cleanly rather than with an inherited fall speed.
        verticalSpeed = traversalKind == TraversalKind.Vault ? -1f : 0f;
        jumpConsumed = true;
    }

    // ------------------------------------------------------------------ helpers

    private void ApplyVertical(float dt, float acceleration)
    {
        verticalSpeed = IntegrateVertical(verticalSpeed, acceleration, dt, out float displacement);
        controller.Move(Vector3.up * displacement);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
