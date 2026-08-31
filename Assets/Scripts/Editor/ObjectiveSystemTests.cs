using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase 6D tests that need components rather than a plan.
///
/// <see cref="SkyboundCityTests"/> deliberately creates no GameObjects: everything it asserts is a
/// pure function of <see cref="CityDesign"/>, which is what lets it settle the city's dimensions in
/// milliseconds. The mission has a second half that arithmetic cannot reach - whether a set of
/// checkpoints really does accept any order, whether a respawn prefers the anchor the player last
/// stood in, whether capturing the last relay really opens the tower - and that half is here.
///
/// The claim these exist to settle is the Phase 6D exit criterion, stated the way the roadmap
/// states it: the mission is completable in any relay order. The city half of it - that every relay
/// can be reached from every other one - is measured against the roof graph in
/// <see cref="SkyboundCityTests"/>. This is the systems half: that nothing in the run systems cares
/// which order they arrive in.
/// </summary>
public sealed class ObjectiveSystemTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

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
    }

    // ------------------------------------------------------------------ the set

    /// <summary>
    /// The exit criterion, in the systems. All 120 orderings of five relays complete the route,
    /// and none of them completes it early.
    /// </summary>
    [Test]
    public void CheckpointSet_CompletesInEveryOrderOfFiveRelays()
    {
        Fixture fixture = BuildSet(5);
        int orders = 0;

        foreach (List<int> order in Permutations(new List<int> { 0, 1, 2, 3, 4 }))
        {
            fixture.Checkpoints.ResetProgress();
            orders++;

            for (int i = 0; i < order.Count; i++)
            {
                Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[order[i]]), Is.True,
                    $"Crossing {order[i]} was refused in the order {string.Join(",", order)}.");
                Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(i + 1));
                Assert.That(fixture.Checkpoints.AllReached, Is.EqualTo(i == order.Count - 1),
                    "A set is finished when it is empty, and not one crossing before.");
            }
        }

        Assert.That(orders, Is.EqualTo(120), "5! orderings.");
    }

    [Test]
    public void CheckpointSet_CountsEachRelayOnlyOnce()
    {
        Fixture fixture = BuildSet(3);

        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[2]), Is.True);
        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[2]), Is.False,
            "Walking back over a captured relay must not count twice.");
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(1));
    }

    [Test]
    public void CheckpointSet_RemembersWhichRelayWasLastRatherThanWhichIsHighest()
    {
        // The distinction a set forces: progress is a count, and the place to respawn is a
        // specific checkpoint. On a sequence those two are the same object; here they are not.
        Fixture fixture = BuildSet(3);

        Activate(fixture.Checkpoints, fixture.Volumes[2]);
        Activate(fixture.Checkpoints, fixture.Volumes[0]);

        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(2));
        Assert.That(fixture.Checkpoints.Current, Is.SameAs(fixture.Volumes[0]));
    }

    [Test]
    public void CheckpointSet_ResetClearsEveryCrossing()
    {
        Fixture fixture = BuildSet(3);

        Activate(fixture.Checkpoints, fixture.Volumes[1]);
        fixture.Checkpoints.ResetProgress();

        Assert.That(fixture.Checkpoints.Reached, Is.Zero);
        Assert.That(fixture.Checkpoints.Current, Is.Null);
        Assert.That(fixture.Checkpoints.IsReached(fixture.Volumes[1]), Is.False);
        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[1]), Is.True,
            "A restarted run has to be able to capture the same relay again.");
    }

    /// <summary>
    /// Levels 1 and 2 are sequences and stay sequences. Phase 6D adds a mode; it does not change
    /// the one that was already there.
    /// </summary>
    [Test]
    public void CheckpointSequence_StillRefusesAnOutOfOrderCrossing()
    {
        Fixture fixture = Build(3, CheckpointRouteOrder.Sequential);

        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[1]), Is.False);
        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[0]), Is.True);
        Assert.That(Activate(fixture.Checkpoints, fixture.Volumes[1]), Is.True);
        Assert.That(fixture.Checkpoints.Reached, Is.EqualTo(2));
        Assert.That(fixture.Checkpoints.Current, Is.SameAs(fixture.Volumes[1]));
    }

    [Test]
    public void CheckpointSequence_IsTheDefault()
    {
        // Both shipped scenes serialise a checkpoint manager, and neither of them can be edited by
        // this phase. Whatever the default is, is what they get.
        GameObject go = new GameObject("~default", typeof(CheckpointManager));
        spawned.Add(go);

        Assert.That(go.GetComponent<CheckpointManager>().Order,
            Is.EqualTo(CheckpointRouteOrder.Sequential));
    }

    /// <summary>
    /// The Phase 6D regression, named.
    ///
    /// Progress was briefly held as a flag per route position, sized in <c>Awake</c>. A component
    /// added in code runs its Awake when it is added, which is before anything has had the chance
    /// to assign its route, so the array was sized against an empty list and every crossing after
    /// that threw. Assigning the route after the manager exists is ordinary - the Skybound City
    /// builder does exactly this - so it has to work.
    /// </summary>
    [Test]
    public void CheckpointRoute_WorksWhenItIsAssignedAfterTheManagerExists()
    {
        GameObject volumeObject = new GameObject("~late", typeof(BoxCollider));
        spawned.Add(volumeObject);
        CheckpointVolume volume = volumeObject.AddComponent<CheckpointVolume>();

        GameObject managerObject = new GameObject("~lateManager");
        spawned.Add(managerObject);

        CheckpointManager manager = managerObject.AddComponent<CheckpointManager>();
        SetPrivate(manager, "checkpoints", new List<CheckpointVolume> { volume });
        SetPrivate(manager, "order", CheckpointRouteOrder.Set);

        Assert.That(Activate(manager, volume), Is.True);
        Assert.That(manager.Reached, Is.EqualTo(1));
        Assert.That(manager.IsReached(volume), Is.True);
        Assert.That(manager.AllReached, Is.True);
    }

    // ------------------------------------------------------------------ the tracker

    [Test]
    public void Tracker_OpensTheTowerOnlyOnceEveryRelayIsCaptured()
    {
        Fixture fixture = BuildMission(5);

        // Deliberately not in list order: an order-free mission that only works in list order is
        // the exact bug this phase exists to not have.
        int[] order = { 3, 0, 4, 1, 2 };

        for (int i = 0; i < order.Length; i++)
        {
            Assert.That(fixture.Tracker.TowerUnlocked, Is.False,
                $"The tower opened after {i} of {order.Length} relays.");
            Assert.That(fixture.Gate.activeSelf, Is.True);

            Activate(fixture.Checkpoints, fixture.Volumes[order[i]]);

            Assert.That(fixture.Relays[order[i]].Captured, Is.True);
            Assert.That(fixture.Tracker.Captured, Is.EqualTo(i + 1));
        }

        Assert.That(fixture.Tracker.TowerUnlocked, Is.True);
        Assert.That(fixture.Tracker.AllCaptured, Is.True);
        Assert.That(fixture.Gate.activeSelf, Is.False, "The hoarding comes down.");
    }

    [Test]
    public void Tracker_PointsAtTheNearestUncapturedRelayAndThenAtTheSummit()
    {
        Fixture fixture = BuildMission(3);

        // Relays are placed at x = 0, 40, 80 by the fixture.
        Assert.That(fixture.Tracker.TryGetTarget(new Vector3(75f, 0f, 0f), out Vector3 first,
            out string label, out bool summit), Is.True);
        Assert.That(first.x, Is.EqualTo(80f).Within(0.001f), "The nearest one, not the first one.");
        Assert.That(summit, Is.False);
        Assert.That(label, Is.Not.Empty);

        foreach (CheckpointVolume volume in fixture.Volumes)
        {
            Activate(fixture.Checkpoints, volume);
        }

        Assert.That(fixture.Tracker.TryGetTarget(Vector3.zero, out Vector3 last, out string name,
            out bool isSummit), Is.True);
        Assert.That(isSummit, Is.True);
        Assert.That(name, Is.EqualTo(fixture.Tracker.SummitName));
        Assert.That(last, Is.EqualTo(fixture.Summit.position));
    }

    [Test]
    public void Tracker_PutsTheGateBackUpWhenTheRunRestarts()
    {
        Fixture fixture = BuildMission(2);

        foreach (CheckpointVolume volume in fixture.Volumes)
        {
            Activate(fixture.Checkpoints, volume);
        }

        Assert.That(fixture.Gate.activeSelf, Is.False);

        fixture.Checkpoints.ResetProgress();
        fixture.Tracker.ResetObjectives();

        Assert.That(fixture.Gate.activeSelf, Is.True);
        Assert.That(fixture.Tracker.TowerUnlocked, Is.False);
        Assert.That(fixture.Relays[0].Captured, Is.False);
    }

    // ------------------------------------------------------------------ respawn anchors

    [Test]
    public void Respawn_PrefersTheAnchorThePlayerLastStoodInOverTheLastRelay()
    {
        Fixture fixture = BuildSet(2);

        GameObject playerObject = new GameObject("~player", typeof(CharacterController),
            typeof(PlayerFreezeController));
        spawned.Add(playerObject);

        GameObject startObject = new GameObject("~start");
        startObject.transform.position = new Vector3(1f, 2f, 3f);
        spawned.Add(startObject);

        GameObject anchorObject = new GameObject("~anchor", typeof(BoxCollider),
            typeof(RespawnAnchor));
        anchorObject.transform.position = new Vector3(50f, 20f, -10f);
        spawned.Add(anchorObject);

        GameObject managerObject = new GameObject("~respawn");
        spawned.Add(managerObject);
        RespawnManager respawn = managerObject.AddComponent<RespawnManager>();

        SetPrivate(respawn, "player", playerObject.GetComponent<PlayerFreezeController>());
        SetPrivate(respawn, "levelStart", startObject.transform);
        SetPrivate(respawn, "checkpoints", fixture.Checkpoints);

        Activate(fixture.Checkpoints, fixture.Volumes[0]);

        respawn.RespawnAtCheckpoint();
        Assert.That(playerObject.transform.position,
            Is.EqualTo(fixture.Volumes[0].RespawnPosition),
            "With no anchor visited, a set behaves like the course it grew out of.");

        // The trigger is driven directly: an EditMode test has no physics step to walk into it
        // with, and what is under test is the preference, not Unity's collision detection.
        EnterAnchor(respawn, anchorObject.GetComponent<RespawnAnchor>());

        respawn.RespawnAtCheckpoint();
        Assert.That(playerObject.transform.position, Is.EqualTo(anchorObject.transform.position),
            "The anchor is the more recent statement about where the player actually is.");

        respawn.RespawnAtStart();
        respawn.RespawnAtCheckpoint();
        Assert.That(playerObject.transform.position,
            Is.EqualTo(fixture.Volumes[0].RespawnPosition),
            "A restart forgets the anchors, so the next attempt climbs its own way up.");
    }

    // ------------------------------------------------------------------ the compass

    [Test]
    public void Compass_ReadsZeroDeadAheadAndSignsLeftAndRight()
    {
        Vector3 from = new Vector3(10f, 25f, 10f);

        // Facing +Z (yaw 0): a target further along +Z is dead ahead, one at +X is hard right.
        Assert.That(ObjectiveCompass.RelativeBearing(from, 0f, from + Vector3.forward * 30f),
            Is.EqualTo(0f).Within(0.01f));
        Assert.That(ObjectiveCompass.RelativeBearing(from, 0f, from + Vector3.right * 30f),
            Is.EqualTo(90f).Within(0.01f));
        Assert.That(ObjectiveCompass.RelativeBearing(from, 0f, from + Vector3.left * 30f),
            Is.EqualTo(-90f).Within(0.01f));
        Assert.That(Mathf.Abs(ObjectiveCompass.RelativeBearing(from, 0f, from + Vector3.back * 30f)),
            Is.EqualTo(180f).Within(0.01f));

        // Turning the player turns the needle by the same amount, the other way.
        Assert.That(ObjectiveCompass.RelativeBearing(from, 90f, from + Vector3.right * 30f),
            Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void Compass_IgnoresHeightSoALookUpDoesNotSwingTheNeedle()
    {
        Vector3 from = Vector3.zero;
        Vector3 high = new Vector3(0f, 80f, 30f);

        Assert.That(ObjectiveCompass.RelativeBearing(from, 0f, high), Is.EqualTo(0f).Within(0.01f));
        Assert.That(ObjectiveCompass.HorizontalDistance(from, high), Is.EqualTo(30f).Within(0.01f));
    }

    // ------------------------------------------------------------------ fixtures

    private sealed class Fixture
    {
        public CheckpointManager Checkpoints;
        public ObjectiveTracker Tracker;
        public GameObject Gate;
        public Transform Summit;
        public readonly List<CheckpointVolume> Volumes = new List<CheckpointVolume>();
        public readonly List<ObjectiveRelay> Relays = new List<ObjectiveRelay>();
    }

    private Fixture BuildSet(int count) => Build(count, CheckpointRouteOrder.Set);

    private Fixture Build(int count, CheckpointRouteOrder order)
    {
        GameObject root = new GameObject("~ObjectiveSystemTests");
        root.SetActive(false);
        spawned.Add(root);

        Fixture fixture = new Fixture();
        GameObject systems = new GameObject("Systems");
        systems.transform.SetParent(root.transform);
        fixture.Checkpoints = systems.AddComponent<CheckpointManager>();

        List<CheckpointVolume> volumes = new List<CheckpointVolume>();

        for (int i = 0; i < count; i++)
        {
            GameObject go = new GameObject($"Relay_{i}", typeof(BoxCollider));
            go.transform.SetParent(root.transform);

            // Off the origin in Y as well as X: the player under test starts at the origin, and
            // a respawn target that happens to be where they already are proves nothing.
            go.transform.position = new Vector3(i * 40f, 5f, 0f);

            CheckpointVolume volume = go.AddComponent<CheckpointVolume>();
            volumes.Add(volume);
            fixture.Volumes.Add(volume);
        }

        SetPrivate(fixture.Checkpoints, "checkpoints", volumes);
        SetPrivate(fixture.Checkpoints, "order", order);

        root.SetActive(true);
        return fixture;
    }

    private Fixture BuildMission(int count)
    {
        Fixture fixture = Build(count, CheckpointRouteOrder.Set);
        GameObject root = fixture.Checkpoints.transform.parent.gameObject;
        root.SetActive(false);

        for (int i = 0; i < count; i++)
        {
            GameObject go = fixture.Volumes[i].gameObject;
            ObjectiveRelay relay = go.AddComponent<ObjectiveRelay>();
            SetPrivate(relay, "relayId", $"Relay_{i}");
            SetPrivate(relay, "displayName", $"District {i}");
            SetPrivate(relay, "volume", fixture.Volumes[i]);
            fixture.Relays.Add(relay);
        }

        fixture.Gate = new GameObject("TowerGate");
        fixture.Gate.transform.SetParent(root.transform);

        GameObject summit = new GameObject("Summit");
        summit.transform.SetParent(root.transform);
        summit.transform.position = new Vector3(0f, 105f, -194f);
        fixture.Summit = summit.transform;

        ObjectiveTracker tracker = fixture.Checkpoints.gameObject.AddComponent<ObjectiveTracker>();
        SetPrivate(tracker, "checkpoints", fixture.Checkpoints);
        SetPrivate(tracker, "relays", fixture.Relays);
        SetPrivate(tracker, "towerGate", fixture.Gate);
        SetPrivate(tracker, "summit", fixture.Summit);
        fixture.Tracker = tracker;

        root.SetActive(true);
        return fixture;
    }

    // ------------------------------------------------------------------ plumbing

    private static bool Activate(CheckpointManager checkpoints, CheckpointVolume volume)
    {
        MethodInfo method = typeof(CheckpointManager).GetMethod("TryActivate", PrivateInstance);
        Assert.That(method, Is.Not.Null);
        return (bool)method.Invoke(checkpoints, new object[] { volume });
    }

    private static void EnterAnchor(RespawnManager respawn, RespawnAnchor anchor)
    {
        MethodInfo method = typeof(RespawnManager).GetMethod("HandleAnchorEntered", PrivateInstance);
        Assert.That(method, Is.Not.Null);
        method.Invoke(respawn, new object[] { anchor });
    }

    private static void SetPrivate<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
        Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
        field.SetValue(target, value);
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1)
        {
            yield return new List<int>(items);
            yield break;
        }

        for (int i = 0; i < items.Count; i++)
        {
            List<int> rest = new List<int>(items);
            int head = rest[i];
            rest.RemoveAt(i);

            foreach (List<int> tail in Permutations(rest))
            {
                List<int> order = new List<int> { head };
                order.AddRange(tail);
                yield return order;
            }
        }
    }
}
