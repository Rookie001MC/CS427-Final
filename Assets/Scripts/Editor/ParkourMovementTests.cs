using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Movement tests for Phase 6A.5.
///
/// The integrator tests are pure maths and run instantly. The ability tests build real
/// BoxColliders and probe them with the real detector components, so they exercise the same
/// Physics queries the controller uses at runtime rather than a model of them - which is the
/// point the existing route harnesses in this project make about not trusting equations alone.
/// </summary>
public sealed class ParkourMovementTests
{
    private const float Gravity = -9f;
    private const float JumpHeight = 1.5f;
    private const float CapsuleHeight = 2f;
    private const float CapsuleRadius = 0.35f;

    private static float LaunchVelocity => Mathf.Sqrt(JumpHeight * -2f * Gravity);

    private readonly List<GameObject> spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        spawned.Clear();
        Physics.SyncTransforms();
    }

    // ------------------------------------------------------------------ integrator

    /// <summary>
    /// The whole point of the velocity-Verlet change. Forward Euler lost 86mm of apex between
    /// 30fps and 240fps, which is the difference between a 1.45m ledge being reachable and not.
    /// </summary>
    [Test]
    public void JumpApex_IsIdenticalAtEveryFrameRate()
    {
        float reference = -1f;

        foreach (float fps in new[] { 30f, 60f, 90f, 144f, 240f })
        {
            float apex = SimulateApex(1f / fps);

            if (reference < 0f)
            {
                reference = apex;
            }

            Assert.That(apex, Is.EqualTo(reference).Within(0.001f),
                $"Apex at {fps}fps drifted from the {1f / reference:F0}Hz reference.");
        }
    }

    [Test]
    public void JumpApex_MatchesTheConfiguredJumpHeight()
    {
        Assert.That(SimulateApex(1f / 60f), Is.EqualTo(JumpHeight).Within(0.002f),
            "A 1.5m jump setting must actually produce a 1.5m apex.");
    }

    [Test]
    public void IntegrateVertical_IsExactForConstantAcceleration()
    {
        // Closed form after one second, taken in a single step and in a thousand.
        const float duration = 1f;
        float coarse = StepTo(duration, duration);
        float fine = StepTo(duration, duration / 1000f);
        float closedForm = LaunchVelocity * duration + 0.5f * Gravity * duration * duration;

        Assert.That(coarse, Is.EqualTo(closedForm).Within(0.0001f));
        Assert.That(fine, Is.EqualTo(closedForm).Within(0.0001f));
    }

    [Test]
    public void RunStatsTracker_UsesMovementStateHorizontalSpeed()
    {
        GameObject player = new GameObject("Stats Player", typeof(CharacterController),
            typeof(BasicFirstPersonController));
        GameObject systems = new GameObject("Stats Systems", typeof(RunStatsTracker));
        spawned.Add(player);
        spawned.Add(systems);

        BasicFirstPersonController movement = player.GetComponent<BasicFirstPersonController>();
        CharacterController characterController = player.GetComponent<CharacterController>();
        RunStatsTracker stats = systems.GetComponent<RunStatsTracker>();

        PropertyInfo speed = typeof(BasicFirstPersonController).GetProperty(
            nameof(BasicFirstPersonController.CurrentHorizontalSpeed));
        Assert.That(speed, Is.Not.Null);
        MethodInfo setSpeed = speed.GetSetMethod(true);
        Assert.That(setSpeed, Is.Not.Null);
        setSpeed.Invoke(movement, new object[] { 7.25f });

        FieldInfo playerController = typeof(RunStatsTracker).GetField("playerController",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(playerController, Is.Not.Null);
        playerController.SetValue(stats, characterController);

        MethodInfo update = typeof(RunStatsTracker).GetMethod("Update",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(update, Is.Not.Null);
        update.Invoke(stats, null);

        Assert.That(stats.CurrentSpeed, Is.EqualTo(7.25f).Within(0.001f),
            "Run stats must read the horizontal speed produced by the movement state machine.");
    }

    private static float SimulateApex(float dt)
    {
        float v = LaunchVelocity;
        float y = 0f;
        float apex = 0f;

        for (int i = 0; i < Mathf.CeilToInt(4f / dt); i++)
        {
            v = BasicFirstPersonController.IntegrateVertical(v, Gravity, dt, out float dy);
            y += dy;
            apex = Mathf.Max(apex, y);

            if (y <= 0f)
            {
                break;
            }
        }

        return apex;
    }

    private static float StepTo(float duration, float dt)
    {
        float v = LaunchVelocity;
        float y = 0f;

        for (float t = 0f; t < duration - 1e-6f; t += dt)
        {
            v = BasicFirstPersonController.IntegrateVertical(v, Gravity, Mathf.Min(dt, duration - t),
                out float dy);
            y += dy;
        }

        return y;
    }

    // ------------------------------------------------------------------ vault

    [Test]
    public void Vault_AcceptsObstaclesInsideItsBand()
    {
        VaultDetector vault = NewAbility<VaultDetector>();

        foreach (float height in new[] { 0.45f, 0.7f, 1.0f, 1.15f })
        {
            BuildVaultObstacle(height);

            Assert.That(TryVault(vault, out _), Is.True,
                $"A {height:F2}m obstacle is inside the vault band and must be accepted.");

            TearDownProps();
        }
    }

    [Test]
    public void Vault_RefusesObstaclesTallerThanTheBand()
    {
        VaultDetector vault = NewAbility<VaultDetector>();
        BuildVaultObstacle(1.6f);

        Assert.That(TryVault(vault, out _), Is.False,
            "A 1.6m obstacle is a mantle or a wall, never a vault.");
    }

    [Test]
    public void Vault_RefusesWhenThereIsNoLandingOnTheFarSide()
    {
        VaultDetector vault = NewAbility<VaultDetector>();

        // A railing at the edge of a void: correct height, nothing beyond it. The floor stops
        // level with the railing's centre line, exactly as it did before this fixture was moved
        // into the detector's reach.
        Prop("Railing", new Vector3(0f, 0.5f, FaceDistance + 0.2f), new Vector3(6f, 1f, 0.4f));
        Prop("Floor", new Vector3(0f, -0.5f, FaceDistance + 0.2f - 2f), new Vector3(6f, 1f, 4f));
        Physics.SyncTransforms();

        Assert.That(TryVault(vault, out _), Is.False,
            "Vaulting a railing over a drop would be a suicide button, not a traversal.");
    }

    [Test]
    public void Vault_RefusesFromAStandstill()
    {
        VaultDetector vault = NewAbility<VaultDetector>();
        BuildVaultObstacle(0.8f);

        bool found = vault.TryFind(Vector3.zero, Vector3.forward, 0f, CapsuleHeight, CapsuleRadius,
            ~0, null, out _);

        Assert.That(found, Is.False, "Vault requires forward speed.");
    }

    // ------------------------------------------------------------------ mantle

    [Test]
    public void Mantle_AcceptsLedgesInsideItsBand()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();

        foreach (float height in new[] { 1.25f, 1.5f, 1.8f, 1.95f })
        {
            BuildLedge(height);

            Assert.That(TryMantle(mantle, true, out _), Is.True,
                $"A {height:F2}m ledge is inside the grounded mantle band.");

            TearDownProps();
        }
    }

    [Test]
    public void Mantle_RefusesLedgesAboveTheBand()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();
        BuildLedge(2.4f);

        Assert.That(TryMantle(mantle, true, out _), Is.False,
            "2.4m is above head height with nothing to reach with.");
    }

    [Test]
    public void Mantle_RefusesWhenThereIsNoHeadroomOnTheLedge()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();
        BuildLedge(1.5f);

        // A slab 1.2m above the ledge surface: the player cannot stand there.
        Prop("Overhang", new Vector3(0f, 1.5f + 1.2f, 3f), new Vector3(6f, 0.4f, 4f));
        Physics.SyncTransforms();

        Assert.That(TryMantle(mantle, true, out _), Is.False,
            "Climbing into a space too short to stand in would trap the player.");
    }

    [Test]
    public void Mantle_RefusesACeilingWithNoUpwardSurface()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();

        // The underside of a walkway. There is a face to probe, but nothing standable on top
        // within the band - the downward probe is what rejects it.
        Prop("Soffit", new Vector3(0f, 1.5f, 2f), new Vector3(6f, 0.3f, 4f));
        Prop("Tower", new Vector3(0f, 6f, 2f), new Vector3(6f, 8f, 4f));
        Physics.SyncTransforms();

        Assert.That(TryMantle(mantle, true, out _), Is.False,
            "An overhang's underside must never read as a ledge.");
    }

    [Test]
    public void Mantle_AirborneBandReachesLowerThanTheGroundedBand()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();

        Assert.That(mantle.AirborneMinHeight, Is.LessThan(mantle.MinHeight),
            "Airborne recovery exists to catch jumps that came up short; it must reach below " +
            "the grounded band or it cannot save them.");
    }

    [Test]
    public void Mantle_RefusesWhileStillRisingFast()
    {
        MantleDetector mantle = NewAbility<MantleDetector>();
        BuildLedge(1.0f);

        Assert.That(TryMantle(mantle, false, out _, verticalSpeed: LaunchVelocity), Is.False,
            "Snapping to a ledge on the way up would cancel deliberate jumps over it.");
    }

    // ------------------------------------------------------------------ slide

    [Test]
    public void Slide_CapsuleFitsUnderItsRatedClearanceButNotBelowIt()
    {
        SlideAbility slide = NewAbility<SlideAbility>();
        float h = slide.SlideHeight;

        Assert.That(h, Is.LessThan(CapsuleHeight),
            "The slide capsule has to be shorter than the standing capsule to be worth anything.");

        // Portal with its underside exactly one centimetre above the slide capsule.
        Prop("Lintel", new Vector3(0f, h + 0.01f + 0.5f, 2f), new Vector3(6f, 1f, 1f));
        Physics.SyncTransforms();

        Vector3 under = new Vector3(0f, 0f, 2f);
        Assert.That(ParkourProbe.CapsuleFree(under, h, CapsuleRadius, ~0, null), Is.True,
            "A sliding player must clear a portal rated for the slide height.");
        Assert.That(ParkourProbe.CapsuleFree(under, CapsuleHeight, CapsuleRadius, ~0, null), Is.False,
            "A standing player must NOT fit, or the slide has no purpose.");
    }

    [Test]
    public void Slide_CannotBeStartedBelowItsEntrySpeedOrWhileAirborne()
    {
        SlideAbility slide = NewAbility<SlideAbility>();

        Assert.That(slide.CanStart(true, slide.MinEntrySpeed - 0.5f), Is.False);
        Assert.That(slide.CanStart(false, slide.MinEntrySpeed + 2f), Is.False);
        Assert.That(slide.CanStart(true, slide.MinEntrySpeed + 2f), Is.True);
    }

    [Test]
    public void Slide_CooldownBlocksImmediateRechaining()
    {
        SlideAbility slide = NewAbility<SlideAbility>();

        slide.Begin(Vector3.forward, 9f);
        slide.End();

        Assert.That(slide.CanStart(true, 9f), Is.False, "A slide must not be re-armed instantly.");

        slide.TickCooldown(slide.Cooldown + 0.01f);
        Assert.That(slide.CanStart(true, 9f), Is.True, "...but must recover after the cooldown.");
    }

    // ------------------------------------------------------------------ wall run

    [Test]
    public void WallRun_RequiresMoreThanWalkSpeed()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();

        Assert.That(wall.MinEntrySpeed, Is.GreaterThan(6f),
            "Walk speed is 6 m/s. Wall running must be a sprint tool.");
        Assert.That(wall.MinEntrySpeed, Is.LessThanOrEqualTo(9f),
            "...but sprint speed is 9 m/s, so it has to be reachable.");
    }

    [Test]
    public void WallRun_AttachesToAVerticalWallAtSprintSpeed()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();

        // Wall on the player's right, running along +Z.
        Prop("Wall", new Vector3(0.7f, 4f, 6f), new Vector3(0.4f, 8f, 16f));
        Physics.SyncTransforms();

        bool attached = wall.TryAttach(new Vector3(0f, 3f, 4f), Vector3.forward * 9f,
            CapsuleHeight, CapsuleRadius, ~0, null);

        Assert.That(attached, Is.True, "A vertical wall beside a sprinting airborne player is valid.");
        Assert.That(wall.Side, Is.EqualTo(1), "The wall was on the right.");
    }

    [Test]
    public void WallRun_RefusesTooSlow()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();
        Prop("Wall", new Vector3(0.7f, 4f, 6f), new Vector3(0.4f, 8f, 16f));
        Physics.SyncTransforms();

        Assert.That(wall.TryAttach(new Vector3(0f, 3f, 4f), Vector3.forward * 3f,
            CapsuleHeight, CapsuleRadius, ~0, null), Is.False);
    }

    [Test]
    public void WallRun_RefusesAFloorMasqueradingAsAWall()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();
        Prop("Floor", new Vector3(0.7f, 3f, 6f), new Vector3(6f, 0.4f, 16f));
        Physics.SyncTransforms();

        Assert.That(wall.TryAttach(new Vector3(0f, 3f, 4f), Vector3.forward * 9f,
            CapsuleHeight, CapsuleRadius, ~0, null), Is.False,
            "Only near-vertical surfaces are wall-runnable.");
    }

    [Test]
    public void WallRun_CannotReuseTheSameWallUntilGrounded()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();
        Prop("Wall", new Vector3(0.7f, 4f, 6f), new Vector3(0.4f, 8f, 16f));
        Physics.SyncTransforms();

        Vector3 feet = new Vector3(0f, 3f, 4f);
        Vector3 velocity = Vector3.forward * 9f;

        Assert.That(wall.TryAttach(feet, velocity, CapsuleHeight, CapsuleRadius, ~0, null), Is.True);
        wall.End();

        Assert.That(wall.TryAttach(feet, velocity, CapsuleHeight, CapsuleRadius, ~0, null), Is.False,
            "Re-attaching to the same wall would let a player climb a corner indefinitely.");

        wall.ClearWallMemory();
        Assert.That(wall.TryAttach(feet, velocity, CapsuleHeight, CapsuleRadius, ~0, null), Is.True,
            "...but touching the ground clears the lockout.");
    }

    [Test]
    public void WallRun_ReducesGravityWithoutEverAddingHeight()
    {
        WallRunAbility wall = NewAbility<WallRunAbility>();

        Assert.That(wall.GravityScale, Is.GreaterThan(0f),
            "Zero or negative gravity would let the player hang or climb.");
        Assert.That(wall.GravityScale, Is.LessThan(1f), "It has to actually help.");
    }

    // ------------------------------------------------------------------ helpers

    private T NewAbility<T>() where T : Component
    {
        GameObject go = new GameObject($"Test_{typeof(T).Name}");
        spawned.Add(go);
        return go.AddComponent<T>();
    }

    private GameObject Prop(string name, Vector3 centre, Vector3 size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"Prop_{name}";
        go.transform.position = centre;
        go.transform.localScale = size;
        spawned.Add(go);
        return go;
    }

    private void TearDownProps()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null && spawned[i].name.StartsWith("Prop_"))
            {
                Object.DestroyImmediate(spawned[i]);
                spawned.RemoveAt(i);
            }
        }

        Physics.SyncTransforms();
    }

    /// <summary>
    /// Distance from the test player's feet to the near face of whatever they are about to
    /// traverse. The detectors reach capsuleRadius + their own forwardReach (0.90m for the vault,
    /// 1.00m for the mantle); this puts the face 0.15m clear of the capsule surface, which is the
    /// same standoff <c>ParkourMovementHarness.ProbeBand</c> uses when it walks the real scene.
    /// The props used to sit 1.70m out - beyond every reach the components advertise - so the
    /// acceptance cases could not be detected and the refusal cases passed without ever
    /// exercising the logic they name.
    /// </summary>
    private const float FaceDistance = CapsuleRadius + 0.15f;

    /// <summary>Obstacle at +Z with a floor either side, so a landing exists.</summary>
    private void BuildVaultObstacle(float height)
    {
        const float depth = 0.6f;
        Prop("Ground", new Vector3(0f, -0.5f, 3f), new Vector3(8f, 1f, 12f));
        Prop("Obstacle", new Vector3(0f, height * 0.5f, FaceDistance + depth * 0.5f),
            new Vector3(8f, height, depth));
        Physics.SyncTransforms();
    }

    /// <summary>A deep block at +Z whose top is at the requested height.</summary>
    private void BuildLedge(float height)
    {
        const float depth = 4f;
        Prop("Ground", new Vector3(0f, -0.5f, 0f), new Vector3(8f, 1f, 6f));
        Prop("Ledge", new Vector3(0f, height * 0.5f, FaceDistance + depth * 0.5f),
            new Vector3(8f, height, depth));
        Physics.SyncTransforms();
    }

    private bool TryVault(VaultDetector vault, out VaultDetector.Result result)
        => vault.TryFind(Vector3.zero, Vector3.forward, 9f, CapsuleHeight, CapsuleRadius,
            ~0, null, out result);

    private bool TryMantle(MantleDetector mantle, bool grounded, out MantleDetector.Result result,
        float verticalSpeed = 0f)
        => mantle.TryFind(Vector3.zero, Vector3.forward, grounded, verticalSpeed,
            CapsuleHeight, CapsuleRadius, ~0, null, out result);
}
