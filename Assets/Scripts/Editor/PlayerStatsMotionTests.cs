using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Distance and speed, and the two things that would make both of them nonsense.
///
/// The reference mockup shows 52.4 m/s and 38.7 km. Nothing in this game moves at 52 m/s - the
/// fastest sustained motion it can produce is an 11 m/s slide - so a career screen showing a
/// figure like that would be reporting a teleport, not a run. These tests pin the two guards that
/// stop that: an announced discontinuity, and a plausibility ceiling.
///
/// The wall-run case at the end is about the other half of the same problem. The counter has to
/// move once per wall run and not once per frame of one, and the frame-by-frame part of that
/// contract belongs to <see cref="WallRunAbility"/>: it is probed against real colliders here,
/// the same way <see cref="ParkourMovementTests"/> does, because the controller's raise site sits
/// in the branch where <c>TryAttach</c> returned true and nowhere else.
/// </summary>
public sealed class PlayerStatsMotionTests
{
    private const float Frame = 1f / 60f;
    private const float CapsuleHeight = 2f;
    private const float CapsuleRadius = 0.35f;

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

    // ------------------------------------------------------------------ distance

    [Test]
    public void NormalTravel_AddsTheDistanceItCovered()
    {
        var sampler = new MotionSampler();
        float total = 0f;

        // One second of sprinting at 9 m/s, in 60 steps.
        Vector3 position = Vector3.zero;
        sampler.TryAdvance(position, Frame, out _);

        for (int i = 0; i < 60; i++)
        {
            position += Vector3.forward * (9f * Frame);

            if (sampler.TryAdvance(position, Frame, out float metres))
            {
                total += metres;
            }
        }

        Assert.That(total, Is.EqualTo(9f).Within(0.01f));
    }

    [Test]
    public void FirstSampleAfterBinding_EstablishesAnOriginWithoutCountingIt()
    {
        var sampler = new MotionSampler();

        Assert.That(sampler.IsPrimed, Is.False);
        Assert.That(sampler.TryAdvance(new Vector3(300f, 40f, 300f), Frame, out float metres),
            Is.False, "The spawn placement is not travel.");
        Assert.That(metres, Is.Zero);
        Assert.That(sampler.IsPrimed, Is.True);
    }

    [Test]
    public void Teleport_AddsNoDistance()
    {
        var sampler = new MotionSampler();
        sampler.TryAdvance(Vector3.zero, Frame, out _);

        // A respawn across Skybound City: 600 m in one frame.
        Assert.That(sampler.TryAdvance(new Vector3(600f, 0f, 0f), Frame, out float jumped),
            Is.False);
        Assert.That(jumped, Is.Zero);

        // ...and the frame after it measures from where the player now is, not from where they
        // were before the teleport, so one bad frame costs one frame.
        Assert.That(sampler.TryAdvance(new Vector3(600.15f, 0f, 0f), Frame, out float after),
            Is.True);
        Assert.That(after, Is.EqualTo(0.15f).Within(0.001f));
    }

    [Test]
    public void Respawn_AddsNoDistanceEvenWhenItIsOnlyAFewMetres()
    {
        var sampler = new MotionSampler();
        sampler.TryAdvance(Vector3.zero, Frame, out _);

        // The case a threshold alone cannot catch: an anchor two metres from where the player
        // died. It is caught because PlayerFreezeController.Teleport announces itself.
        sampler.Discontinuity();

        Assert.That(sampler.TryAdvance(new Vector3(2f, 0f, 0f), Frame, out float metres), Is.False);
        Assert.That(metres, Is.Zero);
    }

    [Test]
    public void Distance_IgnoresVerticalMotion()
    {
        var sampler = new MotionSampler();
        sampler.TryAdvance(Vector3.zero, Frame, out _);

        Assert.That(sampler.TryAdvance(new Vector3(0f, -0.5f, 0f), Frame, out float falling),
            Is.False, "A pure fall covers no ground.");
        Assert.That(falling, Is.Zero);
    }

    [Test]
    public void LongFrames_AreAllowedToHaveCoveredMoreGround()
    {
        var sampler = new MotionSampler();
        sampler.TryAdvance(Vector3.zero, 0.25f, out _);

        // A quarter-second hitch during a 9 m/s sprint is 2.25 m of real travel, and must not be
        // mistaken for a transform jump.
        Assert.That(sampler.TryAdvance(new Vector3(0f, 0f, 2.25f), 0.25f, out float metres),
            Is.True);
        Assert.That(metres, Is.EqualTo(2.25f).Within(0.001f));
    }

    // ------------------------------------------------------------------ max speed

    [Test]
    public void MaxSpeed_AcceptsRealMovementAndRejectsATeleport()
    {
        var store = new PlayerStatsStore(new MemoryOnly());

        store.ReportSpeed(9f);
        Assert.That(store.MaxSpeed, Is.EqualTo(9f));

        store.ReportSpeed(11f);
        Assert.That(store.MaxSpeed, Is.EqualTo(11f), "A slide is faster than a sprint.");

        store.ReportSpeed(7f);
        Assert.That(store.MaxSpeed, Is.EqualTo(11f), "A peak never goes down.");

        store.ReportSpeed(52.4f);
        store.ReportSpeed(4200f);
        store.ReportSpeed(float.PositiveInfinity);
        store.ReportSpeed(float.NaN);
        store.ReportSpeed(-30f);

        Assert.That(store.MaxSpeed, Is.EqualTo(11f),
            "None of those is the player running, so none of them is a peak.");
    }

    [Test]
    public void PlausibilityCeiling_SitsWellAboveTheMovementEnvelope()
    {
        // The numbers the movement system actually produces, from the controller and the slide.
        Assert.That(MotionSampler.IsPlausibleSpeed(9f), Is.True, "Sprint speed.");
        Assert.That(MotionSampler.IsPlausibleSpeed(11f), Is.True, "The slide's own cap.");
        Assert.That(MotionSampler.IsPlausibleSpeed(20f), Is.True,
            "Headroom, so a legitimate frame is never discarded.");
        Assert.That(MotionSampler.IsPlausibleSpeed(MotionSampler.PlausibleSpeedCeiling), Is.True);
        Assert.That(MotionSampler.IsPlausibleSpeed(MotionSampler.PlausibleSpeedCeiling + 0.1f),
            Is.False);

        Assert.That(MotionSampler.PlausibleSpeedCeiling, Is.GreaterThanOrEqualTo(30f));
        Assert.That(MotionSampler.PlausibleSpeedCeiling, Is.LessThanOrEqualTo(60f),
            "A ceiling high enough to admit a teleport is not a ceiling.");
    }

    // ------------------------------------------------------------------ wall run

    [Test]
    public void WallRun_AttachesOnceAndThenTicksForManyFramesWithoutReattaching()
    {
        GameObject host = new GameObject("Test_WallRunAbility");
        spawned.Add(host);
        WallRunAbility wall = host.AddComponent<WallRunAbility>();

        Prop("Wall", new Vector3(0.7f, 4f, 6f), new Vector3(0.4f, 8f, 16f));
        Physics.SyncTransforms();

        Vector3 feet = new Vector3(0f, 3f, 4f);
        Vector3 velocity = Vector3.forward * 9f;

        var store = new PlayerStatsStore(new MemoryOnly());
        int attaches = 0;
        int ticks = 0;

        // The controller's own loop, minus the movement: attach if not running, otherwise tick.
        for (int frame = 0; frame < 40; frame++)
        {
            if (!wall.IsRunning)
            {
                if (wall.TryAttach(feet, velocity, CapsuleHeight, CapsuleRadius, ~0, null))
                {
                    attaches++;

                    // The one raise site, exactly where BasicFirstPersonController has it.
                    store.RecordAction(ParkourAction.WallRun);
                }

                continue;
            }

            ticks++;

            if (!wall.Tick(Frame, feet, CapsuleHeight, CapsuleRadius, ~0, null, false))
            {
                wall.End();
            }
        }

        Assert.That(attaches, Is.EqualTo(1), "One wall, one attach.");
        Assert.That(ticks, Is.GreaterThan(20),
            "The run has to have lasted many frames, or this proves nothing.");
        Assert.That(store.GetAction(ParkourAction.WallRun), Is.EqualTo(1),
            $"{ticks} frames of wall running counted as {store.GetAction(ParkourAction.WallRun)} " +
            "wall runs.");
    }

    // ------------------------------------------------------------------ helpers

    private GameObject Prop(string name, Vector3 centre, Vector3 size)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = centre;
        go.transform.localScale = size;
        spawned.Add(go);
        return go;
    }

    private sealed class MemoryOnly : IRunRecordPersistence
    {
        private string json = string.Empty;
        public string Load() => json;
        public void Save(string value) => json = value;
    }
}
