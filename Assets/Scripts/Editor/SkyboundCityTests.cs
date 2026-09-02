using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase 6B and 6C tests.
///
/// The city plan is a pure function of <see cref="CityDesign"/> and a fixed seed, which is what
/// makes this possible: every dimensional claim the greybox rests on is asserted here, with no
/// scene loaded and nothing built. A change to a movement value, a street width or the storey
/// height that quietly invalidates the layout fails in the test runner rather than in a playtest
/// two phases later.
///
/// Phase 6C put its traversal layer in the same place for the same reason. The links, ascents,
/// relays and the roof graph are all pure functions of the plan, so "every relay is reachable by at
/// least three ways in off the street" is a claim the test runner can settle in milliseconds
/// instead of one somebody has to climb a tower to check.
///
/// Phase 6D's mission follows: the relays, the respawn anchors, the tower gate and the fall rule
/// are all derived from the plan, so "the mission is completable in any relay order" is settled the
/// same way - by walking all 120 orderings across the roof graph. What that cannot reach is whether
/// the components in the scene agree, which is what `ObjectiveSystemTests` is for.
///
/// These tests create no GameObjects and write no files, so they need no teardown.
/// </summary>
public sealed class SkyboundCityTests
{
    private static CityPlanResult Plan => CityPlan.Generate();

    private static TraversalEnvelope.Movement Movement => TraversalEnvelope.Default;

    private static void AssertSameRect(in CityRect actual, in CityRect expected, string message)
    {
        Assert.That(actual.MinX, Is.EqualTo(expected.MinX).Within(0.0001f), message);
        Assert.That(actual.MaxX, Is.EqualTo(expected.MaxX).Within(0.0001f), message);
        Assert.That(actual.MinZ, Is.EqualTo(expected.MinZ).Within(0.0001f), message);
        Assert.That(actual.MaxZ, Is.EqualTo(expected.MaxZ).Within(0.0001f), message);
    }

    private static void AssertPointInside(in Vector3 point, in CityRect rect, string message)
    {
        Assert.That(point.x, Is.InRange(rect.MinX - 0.0001f, rect.MaxX + 0.0001f), message);
        Assert.That(point.z, Is.InRange(rect.MinZ - 0.0001f, rect.MaxZ + 0.0001f), message);
    }

    private static void AssertWalkableConnection(in CityRect landing, in CityRect surface,
        string message)
    {
        float overlapX = Mathf.Min(landing.MaxX, surface.MaxX)
                         - Mathf.Max(landing.MinX, surface.MinX);
        float overlapZ = Mathf.Min(landing.MaxZ, surface.MaxZ)
                         - Mathf.Max(landing.MinZ, surface.MinZ);

        Assert.That(landing.GapTo(surface), Is.EqualTo(0f).Within(0.0001f), message);
        Assert.That(Mathf.Max(overlapX, overlapZ),
            Is.GreaterThanOrEqualTo(CityDesign.StairClearWidth - 0.0001f), message);
    }

    private static AscentPlan TestStair(float riser = 0.20f, float tread = 0.30f,
        float width = 1.80f, float beforeDepth = 1.80f, float afterDepth = 1.80f)
    {
        CityRect before = new CityRect(-0.9f, 0.9f, -1.8f, 0f);
        CityRect after = new CityRect(-0.9f, 0.9f, tread, tread + 1.8f);
        StairFlightPlan flight = new StairFlightPlan("Test_Flight", new Vector3(0f, 0f, 0f),
            Vector3.forward, 1, riser, tread, width, before, after,
            beforeDepth, afterDepth);
        AscentPlan ascent = new AscentPlan
        {
            Name = "Test Stair",
            Kind = AscentKind.Riser,
            Style = AscentTraversalStyle.WalkableStair,
            BottomNode = "Bottom",
            TopNode = "Top",
            BaseY = 0f,
            TopY = riser,
            StepCount = 1,
            StepRise = riser,
            BottomFootprint = before,
            TopFootprint = after,
            FinalLanding = after,
            FinalLandingY = riser
        };

        ascent.Flights.Add(flight);
        ascent.Landings.Add(before);
        ascent.LandingY.Add(0f);
        ascent.Landings.Add(after);
        ascent.LandingY.Add(riser);
        return ascent;
    }

    private static AscentPlan PlanTestStair(float rise)
    {
        System.Reflection.MethodInfo planner = typeof(CityTraversal).GetMethod("PlanStair",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(planner, Is.Not.Null);

        CityRect host = new CityRect(-20f, 20f, -20f, 20f);
        return (AscentPlan)planner.Invoke(null, new object[]
        {
            "Ceiling Boundary Stair", AscentKind.Riser, "Bottom", "Top",
            host, 0f, host, rise, host, Facade.North, 0f
        });
    }

    private static StairFlightPlan GeometryTestFlight(string name, Vector3 start,
        Vector3 direction, CityRect before, CityRect after, int steps = 12)
        => new StairFlightPlan(name, start, direction, steps, 0.20f, 0.30f, 1.80f,
            before, after, 1.80f, 1.80f);

    private static void DestroyStairTestHierarchy(GameObject root)
    {
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.gameObject.name.EndsWith("_Visual") && filter.sharedMesh != null)
            {
                Object.DestroyImmediate(filter.sharedMesh);
            }
        }

        Object.DestroyImmediate(root);
    }

    // ------------------------------------------------------------------ the grid

    [Test]
    public void Grid_ThreeSuperblocksAndTwoAvenuesFitInsideTheCore()
    {
        Assert.That(CityDesign.GridSpan, Is.EqualTo(568f).Within(0.001f),
            "3 x 180 m superblocks separated by 2 x 14 m avenues.");
        Assert.That(CityDesign.PerimeterMargin, Is.EqualTo(16f).Within(0.001f),
            "The core is 600 m, so 16 m of open ground is left on each side.");
    }

    [Test]
    public void Grid_GapBetweenAdjacentSuperblocksIsExactlyOneAvenue()
    {
        for (int i = 0; i < CityDesign.GridSize - 1; i++)
        {
            CityRect left = CityDesign.CellBounds(i, 1);
            CityRect right = CityDesign.CellBounds(i + 1, 1);

            Assert.That(right.MinX - left.MaxX, Is.EqualTo(CityDesign.AvenueWidth).Within(0.001f),
                $"Superblocks {i} and {i + 1} must be one avenue apart.");
        }
    }

    [Test]
    public void Grid_SuperblocksDoNotOverlapAndStayInsideTheCore()
    {
        CityRect core = CityDesign.CoreBounds;

        for (int i = 0; i < CityDesign.Cells.Length; i++)
        {
            CityRect a = CityDesign.Cells[i].Bounds;

            Assert.That(a.MinX, Is.GreaterThanOrEqualTo(core.MinX));
            Assert.That(a.MaxX, Is.LessThanOrEqualTo(core.MaxX));
            Assert.That(a.MinZ, Is.GreaterThanOrEqualTo(core.MinZ));
            Assert.That(a.MaxZ, Is.LessThanOrEqualTo(core.MaxZ));

            for (int j = i + 1; j < CityDesign.Cells.Length; j++)
            {
                Assert.That(a.Overlaps(CityDesign.Cells[j].Bounds), Is.False,
                    $"{CityDesign.Cells[i].Name} overlaps {CityDesign.Cells[j].Name}.");
            }
        }
    }

    [Test]
    public void Grid_EveryCellOfTheThreeByThreeIsAssignedExactlyOnce()
    {
        HashSet<int> seen = new HashSet<int>();

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            Assert.That(seen.Add(cell.Column * 10 + cell.Row), Is.True,
                $"Two districts claim column {cell.Column}, row {cell.Row}.");
        }

        Assert.That(seen.Count, Is.EqualTo(CityDesign.GridSize * CityDesign.GridSize),
            "All nine superblocks must be assigned.");
    }

    // ------------------------------------------------------------------ the movement envelope

    /// <summary>
    /// Guards the reach formula itself against the figures the Phase 6A report derived the whole
    /// city from. If these move, every street width in the design is wrong.
    /// </summary>
    [Test]
    public void Envelope_ReproducesThePhase6AReachTable()
    {
        Assert.That(Movement.LaunchVelocity, Is.EqualTo(5.196f).Within(0.001f));
        Assert.That(TraversalEnvelope.Reach(Movement, Movement.Sprint, 0f),
            Is.EqualTo(10.392f).Within(0.005f), "Flat sprint reach.");
        Assert.That(TraversalEnvelope.SprintDesignGap(Movement, 0f),
            Is.EqualTo(9.242f).Within(0.005f), "Flat sprint design gap.");
        Assert.That(TraversalEnvelope.WalkDesignGap(Movement, 0f),
            Is.EqualTo(5.778f).Within(0.005f), "Flat walk design gap.");
        Assert.That(TraversalEnvelope.DropAssistedSprintGap(Movement, 2f),
            Is.EqualTo(11.983f).Within(0.005f),
            "The 2 m drop figure that condemned the original 12 m avenue.");
    }

    [Test]
    public void Envelope_NothingIsReachableAtOrAboveTheJumpHeight()
    {
        Assert.That(TraversalEnvelope.TryAirtime(Movement, Movement.JumpHeight, out _), Is.False);
        Assert.That(TraversalEnvelope.SprintDesignGap(Movement, Movement.JumpHeight),
            Is.LessThan(0f), "An unreachable rise must not report a usable gap.");
    }

    /// <summary>PHASE 6A.5 CHANGE 3. A climbable storey would make vertical gain undesignable.</summary>
    [Test]
    public void StoreyHeight_StaysAboveTheMantleAssistedClimbCeiling()
    {
        float ceiling = TraversalEnvelope.MantleAssistedClimb(Movement);

        Assert.That(ceiling, Is.EqualTo(3.30f).Within(0.001f),
            "1.5 m jump apex plus the 1.8 m airborne mantle band.");
        Assert.That(CityDesign.StoreyHeight, Is.GreaterThan(ceiling),
            "A player must not be able to climb a storey unaided.");
        Assert.That(CityDesign.StoreyHeight - ceiling, Is.GreaterThanOrEqualTo(0.25f),
            "Phase 6E adds per-floor cornices; without margin they become a ladder.");
    }

    /// <summary>
    /// PHASE 6A.5 CHANGE 2, and the single most load-bearing dimension in the city: if avenues can
    /// be crossed at roof level, districts stop being separate places.
    /// </summary>
    [Test]
    public void AvenueWidth_IsNotCrossableFromOneStoreyHigher()
    {
        float dropGap = TraversalEnvelope.DropAssistedSprintGap(Movement, CityDesign.StoreyHeight);

        Assert.That(CityDesign.AvenueWidth, Is.GreaterThan(dropGap),
            $"A one-storey drop clears {dropGap:F2} m; the avenue must be wider than that.");
    }

    [Test]
    public void AvenueWidth_WouldNoLongerHoldAtTheOriginalTwelveMetres()
    {
        // A guard on the reasoning behind CHANGE 2 rather than on a dimension. 12 m was only ever
        // 2 cm clear of a plain 2 m drop, and a one-storey drop passes it outright - so if this
        // ever stops holding, the justification for widening the avenues has gone and 14 m should
        // be revisited rather than left as folklore.
        Assert.That(TraversalEnvelope.DropAssistedSprintGap(Movement, 2f),
            Is.EqualTo(11.983f).Within(0.005f), "A 2 m drop leaves 12 m with almost no margin.");

        Assert.That(TraversalEnvelope.DropAssistedSprintGap(Movement, CityDesign.StoreyHeight),
            Is.GreaterThan(12f),
            "A one-storey drop clears more than 12 m, which is what condemned the 12 m avenue.");
    }

    // ------------------------------------------------------------------ tiers

    [Test]
    public void TierTable_IsOrderedEasiestFirst()
    {
        for (int i = 1; i < RouteTiers.Table.Length; i++)
        {
            Assert.That(RouteTiers.Table[i].MaxGap,
                Is.GreaterThanOrEqualTo(RouteTiers.Table[i - 1].MaxGap));
            Assert.That(RouteTiers.Table[i].MinLandingDepth,
                Is.LessThanOrEqualTo(RouteTiers.Table[i - 1].MinLandingDepth));
        }
    }

    [Test]
    public void Tiers_ClassifyEachStreetWidthAsItsDesignIntends()
    {
        Assert.That(RouteTiers.Classify(CityDesign.AlleyWidth, 0f, 99f), Is.EqualTo(RouteTier.Green),
            "An alley is meant to be hopped without sprinting.");
        Assert.That(RouteTiers.Classify(CityDesign.SecondaryStreetWidth, 0f, 99f),
            Is.EqualTo(RouteTier.Blue), "A secondary street is a comfortable sprint jump.");
        Assert.That(RouteTiers.Classify(CityDesign.AvenueWidth, 0f, 99f),
            Is.EqualTo(RouteTier.Unreachable), "An avenue must never be flat-jumpable.");
        Assert.That(RouteTiers.Classify(CityDesign.PlazaSize, 0f, 99f),
            Is.EqualTo(RouteTier.Unreachable), "The plaza is ground-only.");
    }

    [Test]
    public void Tiers_ShallowLandingDemotesAnOtherwiseEasyJump()
    {
        Assert.That(RouteTiers.Classify(3f, 0f, 1.5f), Is.EqualTo(RouteTier.Orange),
            "A 3 m hop onto a 1.5 m ledge is not GREEN however short it is.");
    }

    [Test]
    public void Tiers_MantleStepRaisesTheRiseLimitFromOrangeUpward()
    {
        Assert.That(RouteTiers.Classify(4f, 1.6f, 20f), Is.EqualTo(RouteTier.Orange),
            "A 1.6 m step is above a plain jump's 1.2 m rise but inside the mantle step.");
        Assert.That(RouteTiers.Classify(4f, 2.4f, 20f), Is.EqualTo(RouteTier.Unreachable),
            "Past the 2.0 m mantle step there is nothing left to climb with.");
    }

    [Test]
    public void Tiers_UnderGradingAJumpIsRejectedAndOverGradingIsAllowed()
    {
        Assert.That(RouteTiers.Matches(RouteTier.Green, 7f, 0f, 5f, out string reason), Is.False,
            "A 7 m gap is not a GREEN jump.");
        Assert.That(reason, Is.Not.Empty);

        Assert.That(RouteTiers.Matches(RouteTier.Red, 3f, 0f, 5f, out _), Is.True,
            "A route graded by its hardest jump may contain easy ones.");
    }

    // ------------------------------------------------------------------ the plan

    [Test]
    public void Plan_IsDeterministic()
    {
        CityPlanResult a = CityPlan.Generate();
        CityPlanResult b = CityPlan.Generate();

        Assert.That(b.Buildings.Count, Is.EqualTo(a.Buildings.Count));

        for (int i = 0; i < a.Buildings.Count; i++)
        {
            Assert.That(b.Buildings[i].Name, Is.EqualTo(a.Buildings[i].Name));
            Assert.That(b.Buildings[i].RoofY, Is.EqualTo(a.Buildings[i].RoofY).Within(0.0001f));
            Assert.That(b.Buildings[i].Footprint.MinX,
                Is.EqualTo(a.Buildings[i].Footprint.MinX).Within(0.0001f));
        }

        Assert.That(b.StairFlights.Count, Is.EqualTo(a.StairFlights.Count));

        for (int i = 0; i < a.StairFlights.Count; i++)
        {
            StairFlightPlan expected = a.StairFlights[i];
            StairFlightPlan actual = b.StairFlights[i];

            Assert.That(actual.Start.x, Is.EqualTo(expected.Start.x).Within(0.0001f));
            Assert.That(actual.Start.y, Is.EqualTo(expected.Start.y).Within(0.0001f));
            Assert.That(actual.Start.z, Is.EqualTo(expected.Start.z).Within(0.0001f));
            Assert.That(actual.Direction.x, Is.EqualTo(expected.Direction.x).Within(0.0001f));
            Assert.That(actual.Direction.z, Is.EqualTo(expected.Direction.z).Within(0.0001f));
            Assert.That(actual.StepCount, Is.EqualTo(expected.StepCount));
            Assert.That(actual.RiserHeight, Is.EqualTo(expected.RiserHeight).Within(0.0001f));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.TreadDepth, Is.EqualTo(expected.TreadDepth).Within(0.0001f));
            Assert.That(actual.ClearWidth, Is.EqualTo(expected.ClearWidth).Within(0.0001f));
            Assert.That(actual.LandingBeforeDepth,
                Is.EqualTo(expected.LandingBeforeDepth).Within(0.0001f));
            Assert.That(actual.LandingAfterDepth,
                Is.EqualTo(expected.LandingAfterDepth).Within(0.0001f));
            AssertSameRect(actual.LandingBefore, expected.LandingBefore,
                $"Flight {i} changed its low landing for the same seed.");
            AssertSameRect(actual.LandingAfter, expected.LandingAfter,
                $"Flight {i} changed its high landing for the same seed.");
        }
    }

    [Test]
    public void Plan_BuildsEveryDistrictAndTheLandmark()
    {
        CityPlanResult plan = Plan;

        foreach (DistrictCell cell in CityDesign.Cells)
        {
            int count = 0;

            foreach (BuildingPlan unused in plan.InCell(cell.Name))
            {
                count++;
            }

            if (cell.Group == DistrictGroup.Landmark)
            {
                Assert.That(count, Is.Zero, "The tower cell is massed as blocks, not lots.");
                continue;
            }

            Assert.That(count, Is.GreaterThan(0), $"{cell.Name} has no massing.");
        }

        Assert.That(plan.TallestRoof, Is.EqualTo(CityDesign.TowerTopY).Within(0.001f),
            "The tower must be the tallest thing in the city - it is the orientation anchor.");
    }

    [Test]
    public void Plan_EveryRoofSitsInsideItsDistrictHeightBand()
    {
        foreach (BuildingPlan building in Plan.Buildings)
        {
            DistrictCell cell = CityDesign.Cell(building.CellName);

            Assert.That(building.RoofY, Is.GreaterThanOrEqualTo(cell.MinHeight - 0.001f),
                $"{building.Name} is below its district's band.");
            Assert.That(building.RoofY, Is.LessThanOrEqualTo(cell.MaxHeight + 0.001f),
                $"{building.Name} is above its district's band.");
        }
    }

    [Test]
    public void Plan_NoTwoBuildingsOverlap()
    {
        List<BuildingPlan> buildings = Plan.Buildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            for (int j = i + 1; j < buildings.Count; j++)
            {
                Assert.That(buildings[i].Footprint.Overlaps(buildings[j].Footprint), Is.False,
                    $"{buildings[i].Name} overlaps {buildings[j].Name}.");
            }
        }
    }

    [Test]
    public void Plan_EverythingStaysInsideTheCollidableCore()
    {
        CityRect core = CityDesign.CoreBounds;

        foreach (BuildingPlan building in Plan.Buildings)
        {
            Assert.That(building.Footprint.MinX, Is.GreaterThanOrEqualTo(core.MinX - 0.001f));
            Assert.That(building.Footprint.MaxX, Is.LessThanOrEqualTo(core.MaxX + 0.001f));
            Assert.That(building.Footprint.MinZ, Is.GreaterThanOrEqualTo(core.MinZ - 0.001f));
            Assert.That(building.Footprint.MaxZ, Is.LessThanOrEqualTo(core.MaxZ + 0.001f));
        }
    }

    [Test]
    public void Plan_BuildingsClearTheStreetsTheyAreSeparatedBy()
    {
        // Two buildings in the same district are either separated by at least that district's
        // street width, or they are in different rows and columns and so not neighbours at all.
        foreach (DistrictCell cell in CityDesign.Cells)
        {
            if (cell.Group == DistrictGroup.Landmark)
            {
                continue;
            }

            List<BuildingPlan> buildings = new List<BuildingPlan>(Plan.InCell(cell.Name));

            for (int i = 0; i < buildings.Count; i++)
            {
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    bool sameRow = buildings[i].LotRow == buildings[j].LotRow;
                    bool sameColumn = buildings[i].LotColumn == buildings[j].LotColumn;

                    if (!sameRow && !sameColumn)
                    {
                        continue;
                    }

                    float gap = buildings[i].Footprint.GapTo(buildings[j].Footprint);

                    Assert.That(gap, Is.GreaterThanOrEqualTo(cell.InternalStreetWidth - 0.001f),
                        $"{buildings[i].Name} and {buildings[j].Name} are {gap:F2} m apart, " +
                        $"closer than {cell.Name}'s {cell.InternalStreetWidth:F1} m streets.");
                }
            }
        }
    }

    // ------------------------------------------------------------------ roof clusters

    [Test]
    public void Clusters_RespectTheTolerance()
    {
        foreach (KeyValuePair<int, List<BuildingPlan>> cluster in Clusters())
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;

            foreach (BuildingPlan building in cluster.Value)
            {
                lo = Mathf.Min(lo, building.RoofY);
                hi = Mathf.Max(hi, building.RoofY);
            }

            Assert.That(hi - lo, Is.LessThanOrEqualTo(CityDesign.RoofClusterTolerance + 0.001f),
                $"Cluster {cluster.Key} ({cluster.Value[0].CellName}) spreads too far to be linked.");
        }
    }

    [Test]
    public void Clusters_EveryHopBetweenNeighbouringRoofsIsReachable()
    {
        foreach (KeyValuePair<int, List<BuildingPlan>> cluster in Clusters())
        {
            List<BuildingPlan> members = cluster.Value;
            members.Sort((a, b) => a.LotColumn.CompareTo(b.LotColumn));

            for (int i = 0; i < members.Count - 1; i++)
            {
                BuildingPlan from = members[i];
                BuildingPlan to = members[i + 1];

                float gap = from.Footprint.GapTo(to.Footprint);
                float rise = to.RoofY - from.RoofY;
                float landing = Mathf.Min(to.Footprint.Width, to.Footprint.Depth);

                Assert.That(RouteTiers.Classify(gap, rise, landing),
                    Is.Not.EqualTo(RouteTier.Unreachable),
                    $"{from.Name} -> {to.Name}: gap {gap:F2} m, rise {rise:F2} m. " +
                    "Roofs in one cluster must be linkable, or the cluster is not a cluster.");
            }
        }
    }

    [Test]
    public void Clusters_AreSplitWhereTheRowIsInterrupted()
    {
        // The plaza and the Cut each remove a lot from the middle of a row. The halves left behind
        // are far too far apart to hop, so they must not share a cluster id.
        List<BuildingPlan> centre = new List<BuildingPlan>(Plan.InCell("CityCenter"));
        HashSet<int> middleRowClusters = new HashSet<int>();

        foreach (BuildingPlan building in centre)
        {
            if (building.LotRow == 1)
            {
                middleRowClusters.Add(building.ClusterId);
            }
        }

        Assert.That(middleRowClusters.Count, Is.EqualTo(2),
            "The plaza splits the City Center's middle row into two separate roof clusters.");
    }

    private static Dictionary<int, List<BuildingPlan>> Clusters()
    {
        Dictionary<int, List<BuildingPlan>> clusters = new Dictionary<int, List<BuildingPlan>>();

        foreach (BuildingPlan building in Plan.Buildings)
        {
            if (building.ClusterId < 0)
            {
                continue;
            }

            if (!clusters.TryGetValue(building.ClusterId, out List<BuildingPlan> members))
            {
                members = new List<BuildingPlan>();
                clusters[building.ClusterId] = members;
            }

            members.Add(building);
        }

        return clusters;
    }

    // ------------------------------------------------------------------ the plaza and the start

    [Test]
    public void Plaza_IsOpenGroundAtTheCentreOfTheCityCenter()
    {
        foreach (BuildingPlan building in Plan.Buildings)
        {
            Assert.That(building.Footprint.Overlaps(CityDesign.Plaza), Is.False,
                $"{building.Name} stands on the plaza.");
        }

        Assert.That(CityDesign.Plaza.Width, Is.EqualTo(CityDesign.PlazaSize).Within(0.001f));
        Assert.That(CityDesign.Plaza.CentreX, Is.EqualTo(0f).Within(0.001f));
        Assert.That(CityDesign.Plaza.CentreZ, Is.EqualTo(0f).Within(0.001f));
    }

    [Test]
    public void Spawn_IsOnThePlazaAndNotInsideAnything()
    {
        Vector3 spawn = CityDesign.SpawnPosition;

        Assert.That(CityDesign.Plaza.Inset(-CityDesign.SecondaryStreetWidth)
                .Contains(spawn.x, spawn.z), Is.True,
            "The player must start on or immediately beside the plaza.");

        foreach (BuildingPlan building in Plan.Buildings)
        {
            Assert.That(building.Footprint.Contains(spawn.x, spawn.z), Is.False,
                $"The spawn point is inside {building.Name}.");
        }
    }

    [Test]
    public void PlazaRingStreet_LeavesTheStartAreaWithoutEnteringABuilding()
    {
        // The plaza is enclosed on all four sides; the ring street is the only way off it, and
        // every street-level route depends on that coordinate being clear.
        float ring = CityDesign.PlazaRingStreet;

        foreach (BuildingPlan building in Plan.InCell("CityCenter"))
        {
            Assert.That(building.Footprint.Contains(0f, ring), Is.False,
                $"{building.Name} blocks the plaza's north ring street.");
            Assert.That(building.Footprint.Contains(ring, 0f), Is.False,
                $"{building.Name} blocks the plaza's east ring street.");
        }
    }

    // ------------------------------------------------------------------ the Cut

    [Test]
    public void Cut_SitsAboveTheDeathPlaneAndIsClearOfBuildings()
    {
        Assert.That(CityDesign.CutFloorY, Is.GreaterThan(CityDesign.DeathPlaneY),
            "Dropping into the Cut is a shortcut, not a death.");

        CityRect cut = CityPlan.CutBounds();

        foreach (BuildingPlan building in Plan.Buildings)
        {
            Assert.That(building.Footprint.Overlaps(cut), Is.False,
                $"{building.Name} stands in the Cut.");
        }
    }

    [Test]
    public void Cut_HasAFloorARampAndTwoCrossings()
    {
        CityPlanResult plan = Plan;
        int floors = 0;
        int bridges = 0;

        foreach (SlabPlan slab in plan.Slabs)
        {
            if (slab.GroupName != "THE_CUT")
            {
                continue;
            }

            if (slab.Name.Contains("Floor"))
            {
                floors++;
                Assert.That(slab.SurfaceY, Is.EqualTo(CityDesign.CutFloorY).Within(0.001f));
            }
            else if (slab.Name.Contains("Bridge"))
            {
                bridges++;
                Assert.That(slab.SurfaceY, Is.EqualTo(0f).Within(0.001f),
                    "A crossing has to be at street level to be walked onto.");
            }
        }

        Assert.That(floors, Is.EqualTo(1));
        Assert.That(bridges, Is.EqualTo(2),
            "Without crossings the trench bisects the Old Quarter.");

        // Phase 6C's tower spiral is ramped too, so the Cut's ramp is found by its group rather
        // than by being the only one in the plan.
        List<RampPlan> cutRamps = new List<RampPlan>();

        foreach (RampPlan ramp in plan.Ramps)
        {
            if (ramp.GroupName == "THE_CUT")
            {
                cutRamps.Add(ramp);
            }
        }

        Assert.That(cutRamps.Count, Is.EqualTo(1), "One way down on foot.");

        // The ramp's pitch must stay inside the CharacterController's slope limit, or the only
        // route into the Cut is a fall.
        Assert.That(Mathf.Abs(cutRamps[0].PitchDegrees), Is.LessThan(CityDesign.SlopeLimit));
    }

    // ------------------------------------------------------------------ routes

    [Test]
    public void Routes_EveryStreetLevelRouteIsGraded_Green()
    {
        int streetRoutes = 0;

        foreach (CityRoute route in CityRoutes.All)
        {
            if (!route.StreetLevel)
            {
                continue;
            }

            streetRoutes++;
            Assert.That(route.Tier, Is.EqualTo(RouteTier.Green),
                $"{route.Name}: a walked route is always GREEN.");
            Assert.That(route.Waypoints.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(route.Nodes, Is.Empty,
                $"{route.Name}: a street route is a line on the ground, not a chain of surfaces.");
        }

        Assert.That(streetRoutes, Is.GreaterThan(0));
    }

    [Test]
    public void Routes_StreetLevelRoutesNeverPassThroughABuilding()
    {
        CityPlanResult plan = Plan;

        foreach (CityRoute route in CityRoutes.All)
        {
            if (!route.StreetLevel)
            {
                continue;
            }

            foreach (Vector3 waypoint in route.Waypoints)
            {
                if (waypoint.y < -1f)
                {
                    continue;
                }

                foreach (BuildingPlan building in plan.Buildings)
                {
                    Assert.That(building.Footprint.Contains(waypoint.x, waypoint.z), Is.False,
                        $"{route.Name}: waypoint {waypoint} is inside {building.Name}.");
                }
            }
        }
    }

    [Test]
    public void Routes_ReachEveryAvenueAndThePerimeter()
    {
        HashSet<string> names = new HashSet<string>();

        foreach (CityRoute route in CityRoutes.All)
        {
            names.Add(route.Name);
        }

        Assert.That(names, Contains.Item("Avenue Ring"));
        Assert.That(names, Contains.Item("Perimeter Ring"));
        Assert.That(names, Contains.Item("The Cut (descent)"));
    }

    // ------------------------------------------------------------------ ground

    [Test]
    public void Ground_PavesTheWholeCoreExceptTheCut()
    {
        CityPlanResult plan = Plan;
        float paved = 0f;

        foreach (SlabPlan slab in plan.Slabs)
        {
            if (slab.Kind == CityPieceKind.Ground)
            {
                paved += slab.Footprint.Area;
            }
        }

        float expected = CityDesign.CoreBounds.Area - CityPlan.CutBounds().Area;

        Assert.That(paved, Is.EqualTo(expected).Within(1f),
            "Every square metre of the core is paved except the trench.");
    }

    [Test]
    public void Ground_SlabsDoNotOverlapEachOther()
    {
        List<SlabPlan> ground = new List<SlabPlan>();

        foreach (SlabPlan slab in Plan.Slabs)
        {
            if (slab.Kind == CityPieceKind.Ground)
            {
                ground.Add(slab);
            }
        }

        for (int i = 0; i < ground.Count; i++)
        {
            for (int j = i + 1; j < ground.Count; j++)
            {
                Assert.That(ground[i].Footprint.Overlaps(ground[j].Footprint), Is.False,
                    $"{ground[i].Name} overlaps {ground[j].Name}.");
            }
        }
    }

    // ------------------------------------------------------------------ budgets

    [Test]
    public void Plan_StaysWellInsideThePhase6AColliderBudget()
    {
        // 1100 colliders is the whole-city ceiling. Phase 6E still has to fit inside it, so the
        // greybox and its traversal layer together must not be close to it.
        CityPlanResult plan = Plan;
        int solidBlocks = 0;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Collidable)
            {
                solidBlocks++;
            }
        }

        int expected = plan.Buildings.Count + plan.Slabs.Count + solidBlocks + plan.Ramps.Count
                       + plan.Volumes.Count + plan.StairFlights.Count;
        Assert.That(plan.ColliderCount, Is.EqualTo(expected),
            "Every flight adds exactly one smooth walk-surface collider.");
        Assert.That(plan.ColliderCount, Is.LessThan(1100),
            "The greybox already exceeds the city's total collider budget.");
    }

    // ================================================================== Phase 6C: traversal

    private static RoofGraph Graph(CityPlanResult plan) => RoofGraph.Build(plan);

    [Test]
    public void Traversal_ResolvesEveryAuthoredLinkAscentAndRelay()
    {
        CityPlanResult plan = Plan;

        // Endpoints are authored as lot indices, and the plaza and the Cut each remove lots. A
        // reference that has stopped naming a building is the failure mode this whole layer has,
        // and it has to be loud rather than quietly one crossing short.
        Assert.That(plan.Traversal.Problems, Is.Empty,
            string.Join("; ", plan.Traversal.Problems));

        Assert.That(plan.Traversal.Links.Count, Is.EqualTo(CityTraversal.Links.Length));
        Assert.That(plan.Traversal.Relays.Count, Is.EqualTo(CityTraversal.Relays.Length));
    }

    [Test]
    public void Links_AreSixInterDistrictOnesTouchingAllSixDistrictGroups()
    {
        HashSet<DistrictGroup> touched = new HashSet<DistrictGroup>();
        int inter = 0;

        foreach (DistrictLink link in CityTraversal.Links)
        {
            DistrictGroup from = GroupOf(link.From);
            DistrictGroup to = GroupOf(link.To);

            if (!link.InterDistrict)
            {
                Assert.That(from, Is.EqualTo(to),
                    $"{link.Name} is not marked inter-district but crosses from {from} to {to}.");
                continue;
            }

            inter++;
            Assert.That(from, Is.Not.EqualTo(to),
                $"{link.Name} is marked inter-district but both ends are {from}.");
            touched.Add(from);
            touched.Add(to);
        }

        Assert.That(inter, Is.EqualTo(6), "Phase 6C authors exactly six inter-district links.");
        Assert.That(touched.Count, Is.EqualTo(6),
            "The six must between them touch all six district groups, landmark included.");
    }

    private static DistrictGroup GroupOf(SurfaceRef reference)
        => reference.Kind == SurfaceKind.Platform
            ? DistrictGroup.Landmark
            : CityDesign.Cell(reference.Name).Group;

    [Test]
    public void Links_LandOnARoofRatherThanOnACorner()
    {
        CityPlanResult plan = Plan;

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            Assert.That(link.Bearing, Is.GreaterThanOrEqualTo(CityDesign.SkybridgeMinBearing),
                $"{link.Name}: only {link.Bearing:F2} m of the two roofs face each other.");

            foreach (string end in link.FlushEnds())
            {
                CityRect roof = plan.Traversal.Surfaces[end].Footprint;

                Assert.That(link.Deck.SharedEdgeWith(roof), Is.GreaterThan(1f),
                    $"{link.Name} meets {end} at a corner, which is not a step.");
            }
        }
    }

    [Test]
    public void Links_SitAtTheLowerRoofAndAreClimbedAtTheOther()
    {
        foreach (LinkPlan link in Plan.Traversal.Links)
        {
            if (link.Kind == LinkKind.Crane)
            {
                Assert.That(link.DeckY, Is.GreaterThan(Mathf.Max(link.FromY, link.ToY)),
                    "The crane jib clears both roofs it serves.");
                Assert.That(link.Stairs.Count, Is.EqualTo(2),
                    "A jib above both roofs has to be climbed to from both.");
                continue;
            }

            Assert.That(link.DeckY, Is.EqualTo(Mathf.Min(link.FromY, link.ToY)).Within(0.001f),
                $"{link.Name}: a skybridge sits at the lower roof so one end is always flush.");

            int expected = Mathf.Abs(link.FromY - link.ToY) < 0.01f ? 0 : 1;
            Assert.That(link.Stairs.Count, Is.EqualTo(expected),
                $"{link.Name}: the taller end needs exactly one stair, the level one needs none.");
        }
    }

    [Test]
    public void Crane_JibIsNarrowerThanASkybridgeAndStillABlueLanding()
    {
        Assert.That(CityDesign.CraneDeckWidth, Is.LessThan(CityDesign.SkybridgeWidth));

        Assert.That(RouteTiers.Classify(0f, 0f, CityDesign.CraneDeckWidth),
            Is.EqualTo(RouteTier.Blue), "Stepping onto the jib is a graded jump, not a stroll.");
        Assert.That(RouteTiers.Classify(0f, 0f, CityDesign.SkybridgeWidth),
            Is.EqualTo(RouteTier.Green), "Walking onto a bridge must never grade harder than GREEN.");
    }

    [Test]
    public void Ascents_AllNonTowerRoutesAreWalkableStairs()
    {
        int stairs = 0;

        foreach (AscentPlan ascent in Plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                continue;
            }

            stairs++;
            Assert.That(ascent.Style, Is.EqualTo(AscentTraversalStyle.WalkableStair),
                $"{ascent.Name} is still classified as mantle-only traversal.");
            Assert.That(ascent.Flights, Is.Not.Empty,
                $"{ascent.Name} has no continuous stair flights.");
        }

        Assert.That(stairs, Is.GreaterThan(0));
    }

    [Test]
    public void Ascents_EveryStairFlightUsesSafeStepDimensions()
    {
        int flights = 0;

        foreach (AscentPlan ascent in Plan.Traversal.Ascents)
        {
            if (ascent.Style != AscentTraversalStyle.WalkableStair)
            {
                continue;
            }

            int expectedSteps = Mathf.CeilToInt(
                ascent.Rise / CityDesign.StairMaximumRiserHeight);
            int plannedSteps = 0;

            for (int i = 0; i < ascent.Flights.Count; i++)
            {
                StairFlightPlan flight = ascent.Flights[i];
                flights++;
                plannedSteps += flight.StepCount;

                Assert.That(flight.StepCount, Is.GreaterThan(0),
                    $"{ascent.Name} flight {i} has no visible steps.");
                Assert.That(flight.RiserHeight,
                    Is.GreaterThan(0f).And.LessThanOrEqualTo(
                        CityDesign.StairMaximumRiserHeight + 0.0001f),
                    $"{ascent.Name} flight {i} has a {flight.RiserHeight:F3} m riser.");
                Assert.That(flight.TreadDepth,
                    Is.GreaterThanOrEqualTo(CityDesign.StairPreferredTreadDepth - 0.0001f),
                    $"{ascent.Name} flight {i} has only {flight.TreadDepth:F3} m of tread.");
                Assert.That(flight.ClearWidth,
                    Is.EqualTo(CityDesign.StairClearWidth).Within(0.0001f),
                    $"{ascent.Name} flight {i} does not provide the required clear width.");
            }

            Assert.That(plannedSteps, Is.EqualTo(expectedSteps),
                $"{ascent.Name} must derive one deterministic step count from its full rise.");
            Assert.That(ascent.StepCount, Is.EqualTo(expectedSteps));
            Assert.That(ascent.StepRise,
                Is.EqualTo(ascent.Rise / expectedSteps).Within(0.0001f));
        }

        Assert.That(flights, Is.GreaterThan(0));
    }

    [Test]
    public void StairBuilder_EmitsOneVisualMeshOverOneContinuousWalkSurface()
    {
        GameObject root = new GameObject("Stair Geometry Test");

        try
        {
            CityRect before = new CityRect(-0.9f, 0.9f, -1.8f, 0f);
            CityRect after = new CityRect(-0.9f, 0.9f, 3.6f, 5.4f);
            StairFlightPlan flight = GeometryTestFlight("Test_Flight_1",
                Vector3.zero, Vector3.forward, before, after);

            CityKit.StairFlightBuildResult built = CityKit.BuildWalkableStairs(
                root.transform, flight, null, null);

            Assert.That(built.Visual.name, Is.EqualTo("Test_Flight_1_Visual"));
            Assert.That(built.WalkSurface.name,
                Is.EqualTo("Test_Flight_1_WalkSurface"));
            Assert.That(built.Visual.GetComponentsInChildren<MeshFilter>(true).Length,
                Is.EqualTo(1), "All 12 visible treads must share one mesh.");
            Assert.That(built.Visual.GetComponentsInChildren<MeshRenderer>(true).Length,
                Is.EqualTo(1), "A flight must cost one tread renderer, not one per step.");
            Assert.That(built.Visual.GetComponentsInChildren<Collider>(true), Is.Empty,
                "Visible tread geometry must never carry collision.");

            BoxCollider walk = built.WalkSurface.GetComponent<BoxCollider>();
            Assert.That(walk, Is.Not.Null);
            Assert.That(built.WalkSurface.GetComponents<Collider>().Length, Is.EqualTo(1));
            Assert.That(built.WalkSurface.GetComponent<Renderer>(), Is.Null,
                "The smooth collision surface is intentionally invisible.");

            Vector3 low = built.WalkSurface.transform.TransformPoint(
                walk.center + new Vector3(0f, walk.size.y * 0.5f, -walk.size.z * 0.5f));
            Vector3 high = built.WalkSurface.transform.TransformPoint(
                walk.center + new Vector3(0f, walk.size.y * 0.5f, walk.size.z * 0.5f));

            Assert.That(Vector3.Distance(low, flight.Start), Is.LessThan(0.001f),
                "The walk surface must begin flush with the low landing.");
            Assert.That(Vector3.Distance(high, flight.End), Is.LessThan(0.001f),
                "The walk surface must finish flush with the high landing.");
            Assert.That(Vector3.Angle(built.WalkSurface.transform.up, Vector3.up),
                Is.EqualTo(flight.PitchDegrees).Within(0.001f));
            Assert.That(flight.PitchDegrees, Is.LessThanOrEqualTo(CityDesign.SlopeLimit));
            Assert.That(built.Guards.Length, Is.EqualTo(2));

            foreach (GameObject guard in built.Guards)
            {
                Assert.That(guard.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "Continuous guard rails are decorative and cannot catch the controller.");
                Vector3 guardLowTop = guard.transform.TransformPoint(
                    new Vector3(0f, 0.5f, -0.5f));
                Vector3 guardHighTop = guard.transform.TransformPoint(
                    new Vector3(0f, 0.5f, 0.5f));
                Assert.That(guardLowTop.y - flight.Start.y,
                    Is.EqualTo(CityDesign.StairGuardHeight).Within(0.001f));
                Assert.That(guardHighTop.y - flight.End.y,
                    Is.EqualTo(CityDesign.StairGuardHeight).Within(0.001f));
            }
        }
        finally
        {
            DestroyStairTestHierarchy(root);
        }
    }

    [Test]
    public void StairBuilder_ReusesTurnLandingAndRendererCountDoesNotScaleWithTreads()
    {
        GameObject root = new GameObject("Switchback Geometry Test");

        try
        {
            CityRect bottom = new CityRect(-0.9f, 0.9f, -1.8f, 0f);
            CityRect turn = new CityRect(-0.9f, 2.9f, 3.6f, 5.4f);
            CityRect top = new CityRect(1.1f, 2.9f, 0f, 1.8f);
            StairFlightPlan first = GeometryTestFlight("Test_Flight_1", Vector3.zero,
                Vector3.forward, bottom, turn);
            StairFlightPlan second = GeometryTestFlight("Test_Flight_2",
                new Vector3(2f, 2.4f, 5.4f), Vector3.back, turn, top);

            CityKit.StairFlightBuildResult low = CityKit.BuildWalkableStairs(
                root.transform, first, null, null);
            CityKit.StairFlightBuildResult high = CityKit.BuildWalkableStairs(
                root.transform, second, null, null, low.LandingAfter);

            Assert.That(high.LandingBefore, Is.SameAs(low.LandingAfter),
                "Adjacent flights must share one turn landing collider.");
            Assert.That(root.GetComponentsInChildren<BoxCollider>(true).Length,
                Is.EqualTo(5), "Three unique landings plus one walk surface per flight.");
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length,
                Is.EqualTo(9),
                "Two 12-tread flights stay at two tread renderers plus guards and landings.");
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length,
                Is.LessThan(first.StepCount + second.StepCount));
        }
        finally
        {
            DestroyStairTestHierarchy(root);
        }
    }

    [Test]
    public void CityBuilder_InstantiatesEveryPlannedFlightWithDeterministicNames()
    {
        System.Reflection.MethodInfo build = typeof(SkyboundCityBuilder).GetMethod("BuildStairs",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(build, Is.Not.Null,
            "The scene builder needs a dedicated stair-flight emission pass.");

        GameObject preservedWorld = GameObject.Find(CityKit.WorldRoot);

        if (preservedWorld != null)
        {
            preservedWorld.name = "__SkyboundCityTests_PreservedWorld";
        }

        CityPlanResult plan = Plan;

        try
        {
            build.Invoke(null, new object[] { plan });
            GameObject world = GameObject.Find(CityKit.WorldRoot);
            Assert.That(world, Is.Not.Null);

            int visuals = 0;
            int walkSurfaces = 0;

            foreach (Transform child in world.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.EndsWith("_Visual"))
                {
                    visuals++;
                }

                if (child.name.EndsWith("_WalkSurface"))
                {
                    walkSurfaces++;
                }
            }

            Assert.That(visuals, Is.EqualTo(plan.StairFlights.Count));
            Assert.That(walkSurfaces, Is.EqualTo(plan.StairFlights.Count));
        }
        finally
        {
            GameObject world = GameObject.Find(CityKit.WorldRoot);

            if (world != null)
            {
                DestroyStairTestHierarchy(world);
            }

            if (preservedWorld != null)
            {
                preservedWorld.name = CityKit.WorldRoot;
            }
        }
    }

    [Test]
    public void Ascents_RiseJustAboveAnExactRiserMultipleAddsAnotherStep()
    {
        AscentPlan ascent = PlanTestStair(1.00001f);

        Assert.That(ascent.StepCount, Is.EqualTo(6),
            "ceil(1.00001 / 0.20) is six; tolerance must not under-count safety steps.");
        Assert.That(ascent.StepRise, Is.LessThanOrEqualTo(CityDesign.StairMaximumRiserHeight));

        foreach (StairFlightPlan flight in ascent.Flights)
        {
            Assert.That(flight.RiserHeight,
                Is.LessThanOrEqualTo(CityDesign.StairMaximumRiserHeight));
        }
    }

    [Test]
    public void RoofGraph_RejectsUnsafeWalkableStairFlights()
    {
        AscentPlan valid = TestStair();
        Assert.That(RoofGraph.WorstStep(valid), Is.EqualTo(RouteTier.Green));

        AscentPlan empty = TestStair();
        empty.Flights.Clear();

        foreach (AscentPlan invalid in new[]
                 {
                     empty,
                     TestStair(riser: 0.20001f),
                     TestStair(tread: 0.29999f),
                     TestStair(width: 1.79999f),
                     TestStair(afterDepth: 1.79999f)
                 })
        {
            Assert.That(RoofGraph.WorstStep(invalid), Is.EqualTo(RouteTier.Unreachable),
                $"{invalid.Name} was accepted without safe explicit flight geometry.");
        }
    }

    [Test]
    public void RouteTierValidator_ReportsUnsafeStairFlightDimensions()
    {
        System.Reflection.MethodInfo check = typeof(RouteTierValidator).GetMethod("CheckAscents",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(check, Is.Not.Null);

        AscentPlan empty = TestStair();
        empty.Flights.Clear();
        AscentPlan[] invalid =
        {
            empty,
            TestStair(riser: 0.20001f),
            TestStair(tread: 0.29999f),
            TestStair(width: 1.79999f),
            TestStair(afterDepth: 1.79999f)
        };
        string[] expectedMessages = { "no explicit", "riser", "tread", "clear width", "landing" };

        for (int i = 0; i < invalid.Length; i++)
        {
            CityPlanResult plan = new CityPlanResult { Traversal = new CityTraversalResult() };
            plan.Traversal.Ascents.Add(invalid[i]);
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            int failures = (int)check.Invoke(null, new object[] { report, plan });

            Assert.That(failures, Is.GreaterThan(0));
            Assert.That(report.ToString(), Does.Contain(expectedMessages[i]));
        }
    }

    [Test]
    public void Ascents_StairFlightsJoinThroughFullTurnLandings()
    {
        foreach (AscentPlan ascent in Plan.Traversal.Ascents)
        {
            if (ascent.Style != AscentTraversalStyle.WalkableStair)
            {
                continue;
            }

            for (int i = 1; i < ascent.Flights.Count; i++)
            {
                StairFlightPlan before = ascent.Flights[i - 1];
                StairFlightPlan after = ascent.Flights[i];

                Assert.That(before.LandingAfterDepth,
                    Is.GreaterThanOrEqualTo(CityDesign.StairTurnLandingDepth - 0.0001f));
                Assert.That(after.LandingBeforeDepth,
                    Is.GreaterThanOrEqualTo(CityDesign.StairTurnLandingDepth - 0.0001f));
                Assert.That(after.Start.y, Is.EqualTo(before.End.y).Within(0.0001f),
                    $"{ascent.Name} flights {i - 1} and {i} disagree on landing height.");
                AssertSameRect(before.LandingAfter, after.LandingBefore,
                    $"{ascent.Name} flights {i - 1} and {i} do not share one turn landing.");
                AssertPointInside(before.End, before.LandingAfter,
                    $"{ascent.Name} flight {i - 1} misses its turn landing.");
                AssertPointInside(after.Start, after.LandingBefore,
                    $"{ascent.Name} flight {i} does not start on its turn landing.");
            }
        }
    }

    [Test]
    public void Ascents_StairsReachTheirDeclaredTargetWithoutMantleSteps()
    {
        foreach (AscentPlan ascent in Plan.Traversal.Ascents)
        {
            if (ascent.Style != AscentTraversalStyle.WalkableStair)
            {
                continue;
            }

            StairFlightPlan last = ascent.Flights[ascent.Flights.Count - 1];
            StairFlightPlan first = ascent.Flights[0];

            Assert.That(first.Start.y, Is.EqualTo(ascent.BaseY).Within(0.0001f));
            AssertWalkableConnection(first.LandingBefore, ascent.BottomFootprint,
                $"{ascent.Name} requires a jump to reach its first stair landing.");
            Assert.That(last.End.y, Is.EqualTo(ascent.TopY).Within(0.0001f),
                $"{ascent.Name} does not finish at its declared target height.");
            Assert.That(ascent.FinalLandingY, Is.EqualTo(ascent.TopY).Within(0.0001f));
            AssertSameRect(last.LandingAfter, ascent.FinalLanding,
                $"{ascent.Name} records a different final landing than its last flight.");
            AssertWalkableConnection(ascent.FinalLanding, ascent.TopFootprint,
                $"{ascent.Name} final landing does not connect to its target slab.");

        }
    }

    [Test]
    public void Ascents_NeverStandInsideTheMassingTheyAreHungOn()
    {
        CityPlanResult plan = Plan;

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            for (int i = 0; i < ascent.Landings.Count; i++)
            {
                foreach (BuildingPlan building in plan.Buildings)
                {
                    bool buried = building.Footprint.Overlaps(ascent.Landings[i]) &&
                                  ascent.LandingY[i] < building.RoofY - 0.01f;

                    Assert.That(buried, Is.False,
                        $"{ascent.Name} ledge {i + 1} at {ascent.LandingY[i]:F2} m is inside " +
                        $"{building.Name}.");
                }
            }
        }
    }

    [Test]
    public void Ascents_FromTheStreetNeverBlockAnythingNarrowerThanAnAlley()
    {
        // A ledge is 1.8 m up, which is below the player's standing height, so a stack hung over a
        // narrow street takes that street out of the walkable network Phase 6B proved. Every way in
        // is therefore on a facade that faces an avenue, the perimeter or an open forecourt.
        CityPlanResult plan = Plan;

        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            foreach (CityRect landing in ascent.Landings)
            {
                foreach (BuildingPlan building in plan.Buildings)
                {
                    if (building.Name == ascent.TopNode)
                    {
                        continue;
                    }

                    Assert.That(landing.GapTo(building.Footprint),
                        Is.GreaterThanOrEqualTo(CityDesign.AlleyWidth),
                        $"{ascent.Name} hangs {landing.GapTo(building.Footprint):F2} m from " +
                        $"{building.Name}.");
                }
            }
        }
    }

    [Test]
    public void TowerWings_CarryThePodiumRoofOutToTheAvenues()
    {
        CityRect cell = CityDesign.Cell("TowerPodium").Bounds;
        CityRect podium = CityTraversal.PodiumFootprint;

        foreach (CityRect wing in new[]
                 {
                     CityTraversal.WingNorthFootprint, CityTraversal.WingWestFootprint
                 })
        {
            Assert.That(wing.SharedEdgeWith(podium), Is.EqualTo(CityDesign.TowerWingWidth)
                .Within(0.001f), "A wing has to be walkable straight off the podium roof.");
        }

        Assert.That(CityTraversal.WingNorthFootprint.Depth,
            Is.EqualTo(CityDesign.TowerWingLength).Within(0.001f));
        Assert.That(CityTraversal.WingWestFootprint.Width,
            Is.EqualTo(CityDesign.TowerWingLength).Within(0.001f));

        Assert.That(CityTraversal.WingNorthFootprint.MaxZ, Is.EqualTo(cell.MaxZ).Within(0.001f),
            "The north wing reaches the avenue the City Center bridges across.");
        Assert.That(CityTraversal.WingWestFootprint.MinX, Is.EqualTo(cell.MinX).Within(0.001f),
            "The west wing reaches the avenue the Old Quarter bridges across.");
    }

    [Test]
    public void TowerSpiral_IsWalkedRatherThanMantled()
    {
        AscentPlan spiral = Spiral();

        Assert.That(spiral.Style, Is.EqualTo(AscentTraversalStyle.Ramp));
        Assert.That(spiral.IsRamped, Is.True);
        Assert.That(spiral.PitchDegrees,
            Is.LessThanOrEqualTo(CityDesign.TowerSpiralMaxPitch + 0.001f));
        Assert.That(spiral.PitchDegrees, Is.LessThan(CityDesign.SlopeLimit),
            "A run steeper than the controller's slope limit cannot be walked up at all.");
        Assert.That(spiral.BaseY, Is.EqualTo(CityDesign.TowerPodiumY).Within(0.001f));
        Assert.That(spiral.TopY, Is.EqualTo(CityDesign.TowerShaftTopY).Within(0.001f));
    }

    [Test]
    public void TowerSpiral_ReachesTheShaftRoofOverASharedEdge()
    {
        AscentPlan spiral = Spiral();

        // The corner landings sit diagonally off the shaft's corner, so without the summit slab the
        // last step would meet the roof at a single point - a gap of zero that cannot be walked.
        Assert.That(spiral.FinalLanding.GapTo(CityTraversal.ShaftFootprint),
            Is.EqualTo(0f).Within(0.001f), "The corner does touch, which is exactly the trap.");
        Assert.That(spiral.FinalLanding.SharedEdgeWith(CityTraversal.ShaftFootprint),
            Is.EqualTo(0f).Within(0.001f), "...and touching at a point is not a step.");

        Assert.That(spiral.FinalLanding.SharedEdgeWith(spiral.SummitFootprint),
            Is.GreaterThan(1f));
        Assert.That(spiral.SummitFootprint.SharedEdgeWith(CityTraversal.ShaftFootprint),
            Is.GreaterThan(1f));
    }

    [Test]
    public void TowerSpiral_ClearsTheShaftItClimbs()
    {
        foreach (RampPlan ramp in Plan.Ramps)
        {
            if (ramp.GroupName != CityTraversal.TowerAscentGroup)
            {
                continue;
            }

            float offset = Mathf.Max(Mathf.Abs(ramp.Centre.x - CityTraversal.ShaftFootprint.CentreX),
                Mathf.Abs(ramp.Centre.z - CityTraversal.ShaftFootprint.CentreZ));

            Assert.That(offset - CityDesign.TowerSpiralDeckWidth * 0.5f,
                Is.GreaterThanOrEqualTo(CityDesign.TowerShaftSize * 0.5f - 0.001f),
                $"{ramp.Name} runs through the shaft instead of around it.");
        }
    }

    private static AscentPlan Spiral()
    {
        foreach (AscentPlan ascent in Plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                return ascent;
            }
        }

        Assert.Fail("There is no way up the shaft.");
        return null;
    }

    // ------------------------------------------------------------------ the roof graph

    [Test]
    public void Relays_AreOnePerDistrictAndNoneOnTheLandmark()
    {
        HashSet<DistrictGroup> groups = new HashSet<DistrictGroup>();

        foreach (RelayPlan relay in Plan.Traversal.Relays)
        {
            Assert.That(relay.Group, Is.Not.EqualTo(DistrictGroup.Landmark),
                "The tower is what the relays unlock, so it cannot also be one of them.");
            Assert.That(groups.Add(relay.Group), Is.True,
                $"Two relays are in the {relay.Group} district.");
        }

        Assert.That(groups.Count, Is.EqualTo(5), "One relay per district.");
    }

    /// <summary>
    /// The Phase 6C exit criterion, in the form the roadmap states it.
    ///
    /// A "route" is a distinct way in off the pavement - one fire escape or scaffold - from which
    /// the relay's roof can be reached in the *directed* network. Counting bridges instead would
    /// let a district claim three ways in that all start at the same stairwell, and counting
    /// undirected reachability would count a 40 m drop as a way up.
    /// </summary>
    [Test]
    public void Relays_AreEachReachableByAtLeastThreeWaysInOffTheStreet()
    {
        CityPlanResult plan = Plan;
        RoofGraph graph = Graph(plan);

        foreach (RelayPlan relay in plan.Traversal.Relays)
        {
            List<string> routes = graph.AccessRoutes(relay.Node);

            Assert.That(routes.Count, Is.GreaterThanOrEqualTo(3),
                $"{relay.Name} on {relay.Node} has {routes.Count} way(s) in: " +
                string.Join(", ", routes));
        }
    }

    [Test]
    public void RoofGraph_LeavesNoSurfaceStrandedAboveTheStreet()
    {
        CityPlanResult plan = Plan;
        RoofGraph graph = Graph(plan);

        foreach (string node in graph.Nodes)
        {
            Assert.That(graph.AccessRoutes(node), Is.Not.Empty,
                $"{node} cannot be reached from any way in off the street.");
        }
    }

    [Test]
    public void RoofGraph_DoesNotCountADropTheFallWouldNotSurvive()
    {
        // Phase 6D adds FallImpactDetector. Until it does, RouteTiers.Classify would happily grade
        // a 40 m descent as GREEN, and every redundancy claim this phase makes would evaporate the
        // moment falling started to cost something.
        CityPlanResult plan = Plan;
        RoofGraph graph = Graph(plan);

        foreach (string node in graph.Nodes)
        {
            float from = plan.Traversal.Surfaces[node].SurfaceY;

            foreach (RoofEdge edge in graph.From(node))
            {
                if (edge.Via != "jump")
                {
                    continue;
                }

                float to = plan.Traversal.Surfaces[edge.To].SurfaceY;

                Assert.That(from - to, Is.LessThanOrEqualTo(CityDesign.SafeDropHeight + 0.001f),
                    $"{node} -> {edge.To} counts a {from - to:F1} m drop as a connection.");
            }
        }
    }

    [Test]
    public void RoofRoutes_ConnectTheirEndsAndMeasureNoHarderThanDeclared()
    {
        CityPlanResult plan = Plan;
        RoofGraph graph = Graph(plan);
        int rooftopRoutes = 0;

        foreach (CityRoute route in CityRoutes.All)
        {
            if (route.StreetLevel)
            {
                continue;
            }

            rooftopRoutes++;

            Assert.That(route.Nodes.Length, Is.GreaterThanOrEqualTo(2),
                $"{route.Name}: no route across the roofs connects its ends.");
            Assert.That(route.Waypoints.Length, Is.EqualTo(route.Nodes.Length));

            RouteTier measured = graph.WorstTier(route.Nodes);

            Assert.That(measured, Is.LessThanOrEqualTo(route.Tier),
                $"{route.Name} measures {measured} but is declared {route.Tier}.");
            Assert.That(measured, Is.LessThanOrEqualTo(RouteTier.Orange),
                $"{route.Name} measures {measured}. Planned stairs and the tower ramp are GREEN, " +
                "so a rooftop route that grades RED is a design error rather than a hard route.");
        }

        Assert.That(rooftopRoutes, Is.EqualTo(CityTraversal.RoofRoutes.Length));
    }

    [Test]
    public void RoofRoutes_StandOnSurfacesThePlanActuallyLays()
    {
        CityPlanResult plan = Plan;

        foreach (CityRoute route in CityRoutes.All)
        {
            if (route.StreetLevel)
            {
                continue;
            }

            for (int i = 0; i < route.Nodes.Length; i++)
            {
                Assert.That(plan.Traversal.Surfaces.ContainsKey(route.Nodes[i]), Is.True,
                    $"{route.Name}: {route.Nodes[i]} is not a surface in the plan.");

                Assert.That(route.Waypoints[i].y,
                    Is.EqualTo(plan.Traversal.Surfaces[route.Nodes[i]].SurfaceY).Within(0.001f),
                    $"{route.Name}: waypoint {i} is not on the surface it names.");
            }
        }
    }

    [Test]
    public void RoofRoutes_GiveEveryRelayThreeNamedApproaches()
    {
        Dictionary<string, int> perRelay = new Dictionary<string, int>();

        foreach (RoofRouteSite site in CityTraversal.RoofRoutes)
        {
            perRelay.TryGetValue(site.Target, out int count);
            perRelay[site.Target] = count + 1;
        }

        foreach (RelaySite relay in CityTraversal.Relays)
        {
            perRelay.TryGetValue(relay.Name, out int count);

            Assert.That(count, Is.GreaterThanOrEqualTo(3),
                $"{relay.Name} has {count} named rooftop route(s); Phase 6C authors three.");
        }
    }

    [Test]
    public void RoofRoutes_EachStartOnADifferentWayInOffTheStreet()
    {
        Dictionary<string, HashSet<string>> entries = new Dictionary<string, HashSet<string>>();

        foreach (RoofRouteSite site in CityTraversal.RoofRoutes)
        {
            if (!entries.TryGetValue(site.Target, out HashSet<string> used))
            {
                used = new HashSet<string>();
                entries[site.Target] = used;
            }

            Assert.That(used.Add(site.Entry), Is.True,
                $"Two routes to {site.Target} both start on {site.Entry}, so they are one route.");
        }
    }

    // ================================================================== Phase 6D: the mission

    private static RoofGraph StreetGraph(CityPlanResult plan) => RoofGraph.BuildWithStreet(plan);

    [Test]
    public void Objectives_ResolveARelayForEverySiteAndAnAnchorForEveryWayIn()
    {
        CityPlanResult plan = Plan;
        CityObjectivesResult mission = plan.Objectives;

        Assert.That(mission.Problems, Is.Empty, string.Join("; ", mission.Problems));
        Assert.That(mission.Relays.Count, Is.EqualTo(CityTraversal.Relays.Length));

        int waysIn = 0;

        foreach (AscentPlan unused in plan.Traversal.StreetAscents())
        {
            waysIn++;
        }

        Assert.That(mission.Anchors.Count, Is.EqualTo(waysIn + mission.Relays.Count),
            "One anchor at the top of every way in, and one on every relay.");
        Assert.That(mission.RelayAnchorCount, Is.EqualTo(mission.Relays.Count));
    }

    /// <summary>
    /// The invariant that lets Phase 6D leave every earlier measurement alone: it adds nothing a
    /// player can stand on, walk into or land on, except the one thing whose whole purpose is to be
    /// in the way.
    /// </summary>
    [Test]
    public void Objectives_AddNothingSolidToTheCityExceptTheTowerGate()
    {
        CityPlanResult plan = Plan;
        int gate = 0;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind == CityPieceKind.Objective)
            {
                Assert.That(block.Collidable, Is.False,
                    $"{block.Name} is a marker, and a marker a player can stand on would change " +
                    "what the roof under it measures.");
            }
            else if (block.Kind == CityPieceKind.Gate)
            {
                gate++;
                Assert.That(block.Collidable, Is.True, "A gate with no collider is not a gate.");
            }
        }

        Assert.That(gate, Is.EqualTo(2), "A wall across the spiral's foot, and one along its side.");

        foreach (SlabPlan slab in plan.Slabs)
        {
            Assert.That(slab.Kind, Is.Not.EqualTo(CityPieceKind.Objective));
            Assert.That(slab.Kind, Is.Not.EqualTo(CityPieceKind.Gate));
        }
    }

    [Test]
    public void Objectives_PutEveryTriggerOnTheSurfaceItBelongsTo()
    {
        CityPlanResult plan = Plan;

        Assert.That(plan.Volumes.Count,
            Is.EqualTo(plan.Objectives.Relays.Count + plan.Objectives.Anchors.Count -
                       plan.Objectives.RelayAnchorCount + 1),
            "One volume per relay, one per free-standing anchor, and the summit. A relay's anchor " +
            "shares the relay's volume rather than adding a second one.");

        foreach (VolumePlan volume in plan.Volumes)
        {
            Assert.That(volume.TopY - volume.BottomY,
                Is.GreaterThanOrEqualTo(CityDesign.ObjectiveTriggerHeight - 0.001f),
                $"{volume.Name} is too shallow to catch a player who crosses it mid-jump.");
            Assert.That(volume.Footprint.Width, Is.GreaterThan(0f));
            Assert.That(volume.Footprint.Depth, Is.GreaterThan(0f));
        }
    }

    [Test]
    public void Relays_StandOnTheirOwnRoofWithRoomAroundThePad()
    {
        HashSet<DistrictGroup> districts = new HashSet<DistrictGroup>();

        foreach (RelayObjective relay in Plan.Objectives.Relays)
        {
            Assert.That(relay.Group, Is.Not.EqualTo(DistrictGroup.Landmark),
                "The tower is what the relays unlock.");
            Assert.That(districts.Add(relay.Group), Is.True,
                $"Two relays are in the {relay.Group} district.");

            Assert.That(relay.Pad.CentreX, Is.EqualTo(relay.Roof.CentreX).Within(0.001f));
            Assert.That(relay.Pad.CentreZ, Is.EqualTo(relay.Roof.CentreZ).Within(0.001f));

            float clearance = Mathf.Min(relay.Roof.Width, relay.Roof.Depth) * 0.5f
                              - CityDesign.RelayPadSize * 0.5f;

            Assert.That(clearance, Is.GreaterThanOrEqualTo(CityDesign.AnchorInset),
                $"{relay.Name} has {clearance:F2} m between its pad and the edge of the roof.");
        }

        Assert.That(districts.Count, Is.EqualTo(5), "One relay per district.");
    }

    [Test]
    public void Anchors_StandOnTheRoofTheyName_AndNeverOverTheEdge()
    {
        CityPlanResult plan = Plan;

        foreach (AnchorObjective anchor in plan.Objectives.Anchors)
        {
            Assert.That(plan.Traversal.Surfaces.ContainsKey(anchor.Node), Is.True,
                $"{anchor.Name} stands on {anchor.Node}, which is not a surface in the plan.");

            TraversalSurface host = plan.Traversal.Surfaces[anchor.Node];

            Assert.That(anchor.SurfaceY, Is.EqualTo(host.SurfaceY).Within(0.001f));
            Assert.That(anchor.Pad.MinX, Is.GreaterThanOrEqualTo(host.Footprint.MinX - 0.001f));
            Assert.That(anchor.Pad.MaxX, Is.LessThanOrEqualTo(host.Footprint.MaxX + 0.001f));
            Assert.That(anchor.Pad.MinZ, Is.GreaterThanOrEqualTo(host.Footprint.MinZ - 0.001f));
            Assert.That(anchor.Pad.MaxZ, Is.LessThanOrEqualTo(host.Footprint.MaxZ + 0.001f));
        }
    }

    [Test]
    public void Anchors_CoverTheTopOfEveryWayInOffTheStreet()
    {
        CityPlanResult plan = Plan;
        HashSet<string> anchored = new HashSet<string>();

        foreach (AnchorObjective anchor in plan.Objectives.Anchors)
        {
            if (anchor.Kind == AnchorKind.AscentTop)
            {
                anchored.Add(anchor.Node);
            }
        }

        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            Assert.That(anchored, Contains.Item(ascent.TopNode),
                $"{ascent.Name} tops out on {ascent.TopNode} with no anchor, so a death sends the " +
                "player back down to climb it again.");
        }
    }

    /// <summary>
    /// The promise Phase 6C made when it excluded deep drops from the roof graph: falling would
    /// cost something in 6D, and when it did, not one connection the graph counted would be lost.
    /// </summary>
    [Test]
    public void FallRule_LeavesEveryConnectionTheRoofGraphCountsSurvivable()
    {
        Assert.That(CityDesign.FatalFallHeight, Is.GreaterThan(CityDesign.SafeDropHeight),
            "A fall the roof graph calls a route must not be a fall that kills.");
        Assert.That(CityDesign.FatalFallHeight, Is.GreaterThan(-CityDesign.CutFloorY),
            "Dropping into the Cut is a shortcut Phase 6B authored, not a death.");
        Assert.That(CityDesign.ControllerFallResetY, Is.LessThan(CityDesign.DeathPlaneY),
            "The controller's own reset is the backstop under the run system, not a race with it.");

        CityPlanResult plan = Plan;
        RoofGraph graph = Graph(plan);

        foreach (string node in graph.Nodes)
        {
            float from = plan.Traversal.Surfaces[node].SurfaceY;

            foreach (RoofEdge edge in graph.From(node))
            {
                float drop = from - plan.Traversal.Surfaces[edge.To].SurfaceY;
                AscentPlan ascent = CityTraversal.Ascent(plan.Traversal, edge.Via);

                if (ascent != null)
                {
                    // A stair or a ramp, not a fall. The Center-Industrial link stair descends
                    // 21.6 m, and the player takes it 1.8 m at a time - what has to be inside the
                    // fall rule is one step of it, not the height of the whole flight.
                    if (!ascent.IsRamped)
                    {
                        Assert.That(ascent.StepRise,
                            Is.LessThanOrEqualTo(CityDesign.FatalFallHeight),
                            $"{ascent.Name} descends {ascent.StepRise:F1} m in one move.");
                    }

                    continue;
                }

                Assert.That(drop, Is.LessThanOrEqualTo(CityDesign.FatalFallHeight),
                    $"{node} -> {edge.To} is a {drop:F1} m descent the network counts as a route " +
                    "and the fall rule would kill the player for taking.");
            }
        }
    }

    [Test]
    public void TowerGate_ShutsTheOnlyRunOfTheSpiralAPlayerCanStepOnto()
    {
        CityPlanResult plan = Plan;
        TowerGatePlan gate = plan.Objectives.Gate;
        AscentPlan spiral = Spiral();

        Assert.That(gate, Is.Not.Null);
        Assert.That(gate.BaseY, Is.EqualTo(spiral.BaseY).Within(0.001f),
            "The gate stands on the podium roof the spiral starts from.");

        float climb = TraversalEnvelope.MantleAssistedClimb(TraversalEnvelope.Default);

        Assert.That(gate.Height, Is.GreaterThan(climb),
            "A gate that can be mantled is not a gate.");

        // The first run rises out of reach after climb / tan(pitch) metres; the side wall has to
        // cover at least that much of it, because until then the deck is a mantle away.
        float needed = climb / Mathf.Tan(spiral.PitchDegrees * Mathf.Deg2Rad);
        float sideLength = Mathf.Max(gate.SideWall.Width, gate.SideWall.Depth);

        Assert.That(sideLength, Is.GreaterThanOrEqualTo(needed),
            $"the run is climbable for {needed:F1} m and the wall covers {sideLength:F1} m.");

        float footWidth = Mathf.Max(gate.FootWall.Width, gate.FootWall.Depth);
        float runWidth = Mathf.Min(spiral.FootRun.Width, spiral.FootRun.Depth);

        Assert.That(footWidth, Is.GreaterThanOrEqualTo(runWidth),
            "The wall across the foot has to span the run it shuts.");

        // The inboard side is left open on purpose: the shaft is already there, and what is left
        // between them is narrower than the player.
        Assert.That(spiral.FootRun.GapTo(CityTraversal.ShaftFootprint), Is.LessThan(0.7f),
            "A slot a player fits through is a way round the gate.");

        // And the gate is beside the spiral, never inside it - so taking it away leaves the run
        // exactly as Phase 6C measured it.
        Assert.That(gate.FootWall.Overlaps(spiral.FootRun), Is.False);
        Assert.That(gate.SideWall.Overlaps(spiral.FootRun), Is.False);
    }

    [Test]
    public void Summit_IsTheFinishAndSitsOnTheShaftRoof()
    {
        VolumePlan finish = Plan.Objectives.Finish;

        Assert.That(finish.Kind, Is.EqualTo(ObjectiveVolumeKind.Finish));
        Assert.That(finish.BottomY, Is.EqualTo(CityDesign.TowerShaftTopY).Within(0.001f));
        Assert.That(finish.TopY - finish.BottomY,
            Is.EqualTo(CityDesign.SummitFinishHeight).Within(0.001f));

        CityRect shaft = CityTraversal.ShaftFootprint;

        Assert.That(finish.Footprint.MinX, Is.GreaterThan(shaft.MinX),
            "Inset, so a player falling past the roof does not clip the finish on the way down.");
        Assert.That(finish.Footprint.MaxX, Is.LessThan(shaft.MaxX));
    }

    // ------------------------------------------------------------------ the exit criterion

    /// <summary>
    /// The Phase 6D exit criterion, measured: the mission is completable in any relay order.
    ///
    /// The graph this asks is the roof graph plus the pavement, because a mission is played on the
    /// whole city - climbing down a fire escape and walking two blocks is as legitimate a way to
    /// cross the map as a skybridge is. Phase 6C's graph deliberately excludes the street, since
    /// its question was how many *separate* ways up there are.
    /// </summary>
    [Test]
    public void Mission_IsCompletableInEveryOrderTheRelaysCanBeTakenIn()
    {
        CityPlanResult plan = Plan;
        RoofGraph street = StreetGraph(plan);

        int travellable = CityObjectives.MissionOrders(plan, street, out string problem);
        int total = CityObjectives.Factorial(plan.Objectives.Relays.Count);

        Assert.That(total, Is.EqualTo(120), "Five relays.");
        Assert.That(travellable, Is.EqualTo(total), problem);
        Assert.That(CityObjectives.CanCompleteInAnyOrder(plan, street, out string _), Is.True);
    }

    [Test]
    public void Mission_LeavesEveryStopReachableFromEveryOther()
    {
        CityPlanResult plan = Plan;
        RoofGraph street = StreetGraph(plan);
        List<string> stops = CityObjectives.AllStops(plan);

        Assert.That(stops.Count, Is.EqualTo(plan.Objectives.Relays.Count + 2),
            "The pavement, the five relays, and the summit.");

        foreach (string from in stops)
        {
            foreach (string to in stops)
            {
                if (from == to)
                {
                    continue;
                }

                Assert.That(street.CanReach(from, to), Is.True,
                    $"{to} cannot be reached from {from}, so at least one relay order is a " +
                    "dead end.");
            }
        }
    }

    [Test]
    public void Mission_DoesNotChangeWhatPhase6CMeasured()
    {
        // The Phase 6C graph is built from the same plan the mission now adds to. If a relay pad or
        // the gate had become a surface, a node or an edge, this is where it would show.
        CityPlanResult plan = Plan;
        RoofGraph roofs = Graph(plan);
        RoofGraph street = StreetGraph(plan);

        Assert.That(roofs.Nodes.Count, Is.EqualTo(street.Nodes.Count - 1),
            "The street-augmented graph is the Phase 6C graph plus exactly one node.");
        Assert.That(roofs.Entries.Count, Is.EqualTo(street.Entries.Count));

        foreach (RelayPlan relay in plan.Traversal.Relays)
        {
            Assert.That(roofs.AccessRoutes(relay.Node).Count, Is.GreaterThanOrEqualTo(3),
                $"{relay.Name} lost a way in.");
        }
    }

    // ================================================================== Phase 6E: environment art

    private static List<DetailPlan> Details(CityPlanResult plan, string group)
    {
        List<DetailPlan> found = new List<DetailPlan>();

        foreach (DetailPlan detail in plan.Details)
        {
            if (detail.GroupName == group)
            {
                found.Add(detail);
            }
        }

        return found;
    }

    /// <summary>
    /// The Phase 6E invariant, and a stronger one than Phase 6D's: the art pass adds nothing solid
    /// to the city *at all*, not even one gate.
    ///
    /// It is asserted structurally rather than by inspection, because that is what makes it hold
    /// for art nobody has written yet: decoration lives in its own plan list, that list is the only
    /// thing `SkyboundCityBuilder.BuildDetails` reads, and the only call it can make is
    /// `CityKit.Detail`, which forwards to `Deco`. Every Phase 6B walkability, 6C tier and 6D
    /// reachability measurement therefore still describes the city a player actually runs through.
    /// </summary>
    [Test]
    public void Dressing_AddsNothingSolidToTheCityAtAll()
    {
        CityPlanResult plan = Plan;

        Assert.That(plan.Details.Count, Is.GreaterThan(0), "The city was not dressed at all.");

        int solids = 0;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Collidable)
            {
                solids++;
            }
        }

        Assert.That(plan.ColliderCount,
            Is.EqualTo(plan.Buildings.Count + plan.Slabs.Count + solids + plan.Ramps.Count
                       + plan.Volumes.Count + plan.StairFlights.Count),
            "The collider count is the built massing, triggers, and one walk surface per flight. " +
            "If the art layer had grown a collider it would have to be here.");

        // And nothing the art layer emitted leaked into a list the builder makes solids out of.
        HashSet<string> art = new HashSet<string>();

        foreach (DetailPlan detail in plan.Details)
        {
            art.Add(detail.Name);
        }

        foreach (BlockPlan block in plan.Blocks)
        {
            Assert.That(art.Contains(block.Name), Is.False,
                $"{block.Name} is both a block and a decoration.");
        }

        foreach (SlabPlan slab in plan.Slabs)
        {
            Assert.That(art.Contains(slab.Name), Is.False,
                $"{slab.Name} is both a slab and a decoration.");
        }
    }

    [Test]
    public void Dressing_IsDeterministic()
    {
        CityPlanResult a = CityPlan.Generate();
        CityPlanResult b = CityPlan.Generate();

        Assert.That(b.Details.Count, Is.EqualTo(a.Details.Count));

        for (int i = 0; i < a.Details.Count; i++)
        {
            Assert.That(b.Details[i].Name, Is.EqualTo(a.Details[i].Name));
            Assert.That(b.Details[i].Centre.y, Is.EqualTo(a.Details[i].Centre.y).Within(0.0001f));
            Assert.That(b.Details[i].Surface, Is.EqualTo(a.Details[i].Surface));
        }
    }

    [Test]
    public void Dressing_EveryPieceIsInAGroupTheBuilderKnowsAbout()
    {
        HashSet<string> known = new HashSet<string>(CityDressing.Groups);

        foreach (DetailPlan detail in Plan.Details)
        {
            Assert.That(known.Contains(detail.GroupName), Is.True,
                $"{detail.Name} is in {detail.GroupName}, which is not one of the eight groups " +
                "CityDressing.Groups declares.");

            Assert.That(detail.Size.x, Is.GreaterThan(0f), $"{detail.Name} has no width.");
            Assert.That(detail.Size.y, Is.GreaterThan(0f), $"{detail.Name} has no height.");
            Assert.That(detail.Size.z, Is.GreaterThan(0f), $"{detail.Name} has no depth.");
        }
    }

    /// <summary>
    /// The whole city, against the Phase 6A renderer ceiling.
    ///
    /// This is the test that decides how much art the phase is allowed, and it is why the party
    /// wall rule and the dead-edge rule exist rather than being nice ideas: the first draft of the
    /// dressing layer came to 4256 pieces, which put the scene 915 renderers over the limit.
    /// One-off spending is checked here rather than in the massing report because the report needs
    /// a built scene and this needs nothing.
    /// </summary>
    [Test]
    public void Dressing_KeepsTheWholeCityInsideThePhase6ARendererBudget()
    {
        CityPlanResult plan = Plan;

        int massing = plan.Buildings.Count + plan.Slabs.Count + plan.Blocks.Count
                      + plan.Ramps.Count;
        int uprights = 0;

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.Scaffold && ascent.Landings.Count > 0)
            {
                uprights += 4;
            }
        }

        // One combined tread/riser mesh and two continuous decorative guards per flight. Landing
        // slabs are already represented in plan.Slabs, and walk surfaces are renderer-free.
        int stairRenderers = plan.StairFlights.Count * 3;
        int renderers = massing + uprights + stairRenderers + plan.Details.Count;

        Assert.That(renderers, Is.LessThanOrEqualTo(3800),
            $"{massing} massing + {uprights} scaffold uprights + {stairRenderers} stair parts + " +
            $"{plan.Details.Count} details = {renderers} renderers, against the Phase 6A ceiling " +
            "of 3800.");
    }

    /// <summary>
    /// A cornice may not stand proud of the roof it crowns.
    ///
    /// The reason is traversal, not taste. Every Phase 6C jump in the city is measured from a roof
    /// surface, and a decorative lip standing 0.4 m above one would not change that measurement -
    /// it has no collider - but it would put a visible edge where the player is told to land, which
    /// is worse than a wrong number because it is a lie the harness cannot catch.
    /// </summary>
    [Test]
    public void Dressing_NeverStandsProudOfTheRoofItCrowns()
    {
        CityPlanResult plan = Plan;

        foreach (DetailPlan detail in Details(plan, CityDressing.FacadeGroup))
        {
            foreach (BuildingPlan building in plan.Buildings)
            {
                if (!detail.Name.StartsWith(building.Name + "_"))
                {
                    continue;
                }

                Assert.That(detail.TopY, Is.LessThanOrEqualTo(building.RoofY + 0.001f),
                    $"{detail.Name} reaches {detail.TopY:F2} m on a roof at {building.RoofY:F2} m.");
                break;
            }
        }
    }

    /// <summary>
    /// PHASE 6A.5 CHANGE 3, closed out. The storey was raised to 3.6 m so that Phase 6E's per-floor
    /// cornices would not become a ladder, leaving 0.30 m of margin - which is thin. The art layer
    /// settles it a second way: a band a player cannot touch cannot be climbed whatever its rise,
    /// and every band in the city is decoration.
    /// </summary>
    [Test]
    public void Dressing_FloorBandsAreDecorationRatherThanRungs()
    {
        CityPlanResult plan = Plan;
        int bands = 0;

        foreach (DetailPlan detail in Details(plan, CityDressing.FacadeGroup))
        {
            if (detail.Name.Contains("_Band"))
            {
                bands++;
            }
        }

        Assert.That(bands, Is.GreaterThan(0), "No facade is banded, so no facade reads as storeys.");

        // The design margin the correction bought, still there.
        Assert.That(CityDesign.StoreyHeight,
            Is.GreaterThan(TraversalEnvelope.MantleAssistedClimb(Movement)),
            "Bands are spaced one storey apart, so a storey has to stay above the climb ceiling.");

        // And the structural half: bands are in the list that cannot produce a collider.
        foreach (BlockPlan block in plan.Blocks)
        {
            Assert.That(block.Name.Contains("_Band"), Is.False,
                $"{block.Name} is a floor band that became a block, which can be collidable.");
        }
    }

    /// <summary>
    /// Rooftop plant never stands where a player is going.
    ///
    /// Props carry no collider, so one in the way cannot change a measurement - but a runner
    /// passing through an air handling unit still looks wrong, and this is the rule that stops it:
    /// nothing may stand on a relay pad, a respawn anchor, a bridge deck, an ascent or the apron in
    /// front of one, and nothing may overhang the edge of the roof it is on.
    /// </summary>
    [Test]
    public void Dressing_RooftopPlantKeepsClearOfEveryObjectiveAndEveryWayOnAndOff()
    {
        CityPlanResult plan = Plan;
        List<DetailPlan> props = Details(plan, CityDressing.RoofGroup);

        Assert.That(props.Count, Is.GreaterThan(0), "No roof in the city carries any plant.");

        List<CityRect> keepOut = new List<CityRect>();

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            keepOut.Add(relay.Trigger);
        }

        foreach (AnchorObjective anchor in plan.Objectives.Anchors)
        {
            keepOut.Add(anchor.Trigger);
        }

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            keepOut.Add(link.Deck);
        }

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            foreach (CityRect landing in ascent.Landings)
            {
                keepOut.Add(landing);
            }
        }

        foreach (DetailPlan prop in props)
        {
            CityRect footprint = prop.Footprint;

            foreach (CityRect keep in keepOut)
            {
                Assert.That(footprint.Overlaps(keep), Is.False,
                    $"{prop.Name} stands on something the player uses.");
            }
        }
    }

    [Test]
    public void Dressing_RooftopPlantNeverOverhangsTheRoofItStandsOn()
    {
        CityPlanResult plan = Plan;

        foreach (DetailPlan prop in Details(plan, CityDressing.RoofGroup))
        {
            BuildingPlan? host = null;

            foreach (BuildingPlan building in plan.Buildings)
            {
                if (prop.Name.StartsWith(building.Name + "_"))
                {
                    host = building;
                    break;
                }
            }

            Assert.That(host.HasValue, Is.True, $"{prop.Name} is on no building.");

            CityRect roof = host.Value.Footprint;
            CityRect footprint = prop.Footprint;

            Assert.That(footprint.MinX, Is.GreaterThanOrEqualTo(roof.MinX - 0.001f),
                $"{prop.Name} hangs over the west edge.");
            Assert.That(footprint.MaxX, Is.LessThanOrEqualTo(roof.MaxX + 0.001f),
                $"{prop.Name} hangs over the east edge.");
            Assert.That(footprint.MinZ, Is.GreaterThanOrEqualTo(roof.MinZ - 0.001f),
                $"{prop.Name} hangs over the south edge.");
            Assert.That(footprint.MaxZ, Is.LessThanOrEqualTo(roof.MaxZ + 0.001f),
                $"{prop.Name} hangs over the north edge.");

            Assert.That(prop.Centre.y, Is.GreaterThanOrEqualTo(host.Value.RoofY - 1.5f),
                $"{prop.Name} is below the roof it stands on.");
        }
    }

    /// <summary>
    /// Nothing in the art layer may make the tower taller. The massing report prints the tower's
    /// summit and compares it against <see cref="CityDesign.TowerTopY"/>, every report in the
    /// project quotes 120 m, and an aircraft beacon 1 m above the mast would quietly falsify all
    /// of it.
    /// </summary>
    [Test]
    public void Dressing_NeverRaisesTheCityAboveTheTowerSummit()
    {
        foreach (DetailPlan detail in Plan.Details)
        {
            if (detail.GroupName == CityDressing.BackdropGroup)
            {
                continue;
            }

            Assert.That(detail.TopY, Is.LessThanOrEqualTo(CityDesign.TowerTopY + 0.001f),
                $"{detail.Name} reaches {detail.TopY:F1} m, above the tower's 120 m summit.");
        }
    }

    /// <summary>
    /// The backdrop is outside the city and inside the camera.
    ///
    /// Both halves matter. Inside the core it would be geometry a player could run at and fall
    /// through; past <see cref="CityDesign.CameraFarClip"/> it would pop in and out as they turned,
    /// which is the one artefact fog cannot hide.
    /// </summary>
    [Test]
    public void Dressing_PutsTheBackdropOutsideTheCoreAndInsideTheFarClip()
    {
        CityPlanResult plan = Plan;
        List<DetailPlan> backdrop = Details(plan, CityDressing.BackdropGroup);

        Assert.That(backdrop.Count, Is.GreaterThan(0), "There is no backdrop.");

        float core = CityDesign.CoreExtent * 0.5f;
        float furthest = 0f;

        foreach (DetailPlan block in backdrop)
        {
            float radius = block.IsRotated
                ? 0.5f * Mathf.Sqrt(block.Size.x * block.Size.x + block.Size.z * block.Size.z)
                : 0f;

            if (block.IsRotated)
            {
                float centre = Mathf.Sqrt(block.Centre.x * block.Centre.x
                                          + block.Centre.z * block.Centre.z);

                Assert.That(centre - radius, Is.GreaterThan(core),
                    $"{block.Name} reaches inside the 600 m core.");

                furthest = Mathf.Max(furthest, centre + radius);
                continue;
            }

            // The ground ring, which is laid as four axis-aligned bands outside the paving.
            CityRect footprint = block.Footprint;

            Assert.That(footprint.Overlaps(CityDesign.CoreBounds.Inset(0.5f)), Is.False,
                $"{block.Name} overlaps the paved core.");

            float cornerX = Mathf.Max(Mathf.Abs(footprint.MinX), Mathf.Abs(footprint.MaxX));
            float cornerZ = Mathf.Max(Mathf.Abs(footprint.MinZ), Mathf.Abs(footprint.MaxZ));
            furthest = Mathf.Max(furthest, Mathf.Sqrt(cornerX * cornerX + cornerZ * cornerZ));
        }

        Assert.That(furthest, Is.LessThan(CityDesign.CameraFarClip),
            $"The backdrop reaches {furthest:F0} m against a {CityDesign.CameraFarClip:F0} m clip.");
        Assert.That(CityDesign.FogEnd, Is.GreaterThan(CityDesign.BackdropInnerRadius),
            "Fog closing before the first backdrop ring would erase the skyline outright.");
        Assert.That(CityDesign.FogEnd, Is.LessThan(CityDesign.CameraFarClip),
            "Fog has to close before the clip plane, or the clip plane is visible.");
    }

    /// <summary>
    /// Every district owns a hue nobody else does.
    ///
    /// Colour zoning is the whole reason the palette is data. The Phase 6B greybox separated the
    /// districts by value alone, which fog erases at exactly the distance the information is wanted
    /// - so what is asserted here is separation in hue, which survives it.
    ///
    /// The landmark is deliberately not in the set. It is not a district, it carries no relay, and
    /// its accent is the City Center's cyan washed almost to white on purpose: the tower is the
    /// thing every relay unlocks, so it belongs to the objective colour rather than to a district.
    /// That it reads apart from the City Center anyway is asserted below, on saturation.
    /// </summary>
    [Test]
    public void Dressing_GivesEveryDistrictAnAccentNoOtherDistrictUses()
    {
        DistrictGroup[] groups =
        {
            DistrictGroup.CityCenter, DistrictGroup.Residential, DistrictGroup.Industrial,
            DistrictGroup.Corporate, DistrictGroup.OldQuarter
        };

        for (int i = 0; i < groups.Length; i++)
        {
            CityDesign.DistrictPalette a = CityDesign.Palette(groups[i]);

            Assert.That(a.Trim.grayscale, Is.GreaterThan(a.Massing.grayscale),
                $"{groups[i]}'s trim is not lighter than its massing, so no band would read.");
            Assert.That(a.Glass.grayscale, Is.LessThan(a.Massing.grayscale),
                $"{groups[i]}'s glass is not darker than its massing, so no window would read.");

            for (int j = i + 1; j < groups.Length; j++)
            {
                CityDesign.DistrictPalette b = CityDesign.Palette(groups[j]);

                Color.RGBToHSV(a.Neon, out float hueA, out _, out _);
                Color.RGBToHSV(b.Neon, out float hueB, out _, out _);

                float separation = Mathf.Abs(hueA - hueB);
                separation = Mathf.Min(separation, 1f - separation);

                Assert.That(separation, Is.GreaterThan(0.06f),
                    $"{groups[i]} and {groups[j]} accent within {separation * 360f:F0} degrees of " +
                    "each other, which is not a difference a player reads through fog.");
            }
        }

        Color.RGBToHSV(CityDesign.Palette(DistrictGroup.CityCenter).Neon, out _,
            out float hubSaturation, out _);
        Color.RGBToHSV(CityDesign.Palette(DistrictGroup.Landmark).Neon, out _,
            out float towerSaturation, out _);

        Assert.That(hubSaturation - towerSaturation, Is.GreaterThan(0.25f),
            "The tower shares the hub's hue, so it has to be told apart by being paler. " +
            $"Hub {hubSaturation:F2}, tower {towerSaturation:F2}.");
    }

    /// <summary>
    /// Every bridge deck in the city carries the lit strip that says it is a route.
    ///
    /// The traversal layer is the part of the city a player has to read at speed, and the strip is
    /// the whole of how it is signposted. One missing on one bridge is one crossing that reads as
    /// scenery.
    /// </summary>
    [Test]
    public void Dressing_MarksEveryCrossingAndEveryWayUp()
    {
        CityPlanResult plan = Plan;
        HashSet<string> strips = new HashSet<string>();

        foreach (DetailPlan detail in Details(plan, CityDressing.TraversalGroup))
        {
            if (detail.Surface == DetailSurface.Route)
            {
                strips.Add(detail.Name);
            }
        }

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            Assert.That(strips.Contains($"{link.Name.Replace(' ', '_')}_Route"), Is.True,
                $"{link.Name} has no route strip, so it reads as a ledge.");
        }

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.IsRamped || ascent.Landings.Count == 0)
            {
                continue;
            }

            Assert.That(strips.Contains($"{ascent.Name.Replace(' ', '_')}_RouteFoot"), Is.True,
                $"{ascent.Name} has no marker at its foot.");
        }

        // Every run of the spiral, too - it is the only ramped ascent and the only one whose strips
        // have to follow a rotation.
        int runs = 0;

        foreach (RampPlan ramp in plan.Ramps)
        {
            if (ramp.GroupName == CityTraversal.TowerAscentGroup)
            {
                runs++;
            }
        }

        int marked = 0;

        foreach (string name in strips)
        {
            if (name.StartsWith("TowerSpiral_Route"))
            {
                marked++;
            }
        }

        Assert.That(marked, Is.EqualTo(runs), "Every run of the spiral is lit, or none of it is.");
    }

    /// <summary>
    /// Signage is on facades that face something open, and crowns are on buildings tall enough to
    /// be seen over their neighbours. Both rules are structural rather than authored, so what is
    /// checked is that they produced signs at all and that no sign ended up on an inward facade.
    /// </summary>
    [Test]
    public void Dressing_SignsFaceTheAvenuesRatherThanTheAlleys()
    {
        CityPlanResult plan = Plan;
        int signs = 0;

        foreach (DetailPlan detail in Details(plan, CityDressing.SignGroup))
        {
            if (!detail.Name.EndsWith("_Sign"))
            {
                continue;
            }

            signs++;

            foreach (BuildingPlan building in plan.Buildings)
            {
                if (!detail.Name.StartsWith(building.Name + "_"))
                {
                    continue;
                }

                CityRect cell = CityDesign.Cell(building.CellName).Bounds;
                CityRect f = building.Footprint;

                float nearest = Mathf.Min(
                    Mathf.Min(Mathf.Abs(f.MinX - cell.MinX), Mathf.Abs(f.MaxX - cell.MaxX)),
                    Mathf.Min(Mathf.Abs(f.MinZ - cell.MinZ), Mathf.Abs(f.MaxZ - cell.MaxZ)));

                Assert.That(nearest, Is.LessThanOrEqualTo(CityDesign.AvenueFacingTolerance + 0.001f),
                    $"{detail.Name} is on a building that touches no edge of its superblock.");
                break;
            }
        }

        Assert.That(signs, Is.GreaterThan(20),
            "A city with a handful of signs reads as a city with none.");
    }

    /// <summary>
    /// The crane's cab hangs on its own jib.
    ///
    /// A regression guard on a real mistake rather than on a design rule: the first draft of the
    /// dressing layer looked for a block called "Crane_Jib" to take the jib's height from. There is
    /// no such block - <see cref="CityTraversal"/> emits every deck in the city as a slab, the
    /// crane's included - so the height silently came out as zero and the cab was built on the
    /// pavement 40 m below the crane. Nothing in the plan was wrong and no harness could have
    /// noticed, which is exactly why it is asserted here.
    /// </summary>
    [Test]
    public void Dressing_HangsTheCraneCabOnTheJibRatherThanOnTheGround()
    {
        CityPlanResult plan = Plan;
        LinkPlan crane = null;

        foreach (LinkPlan link in plan.Traversal.Links)
        {
            if (link.Kind == LinkKind.Crane)
            {
                crane = link;
                break;
            }
        }

        Assert.That(crane, Is.Not.Null, "The Industrial crossing has lost its crane.");

        bool sawCab = false;
        bool sawApex = false;

        foreach (DetailPlan detail in Details(plan, CityDressing.TraversalGroup))
        {
            if (detail.Name == "Crane_Cab")
            {
                sawCab = true;
                float bottom = detail.Centre.y - detail.Size.y * 0.5f;

                Assert.That(bottom, Is.EqualTo(crane.DeckY + 0.4f).Within(0.01f),
                    $"The cab sits at {bottom:F1} m and the jib is at {crane.DeckY:F1} m.");
                Assert.That(detail.Footprint.Overlaps(crane.Deck), Is.False,
                    "The cab is over the walkway the crane exists to be.");
            }
            else if (detail.Name == "Crane_Apex")
            {
                sawApex = true;
                Assert.That(detail.Centre.y, Is.GreaterThan(crane.DeckY),
                    "The apex light is below the jib.");
            }
        }

        Assert.That(sawCab, Is.True, "The crane has no cab.");
        Assert.That(sawApex, Is.True, "The crane has no apex light.");
    }

    /// <summary>
    /// Every handrail stands on the ledge it belongs to.
    ///
    /// Which side of a ledge is the outboard one is derived, not authored - it is the side facing
    /// away from the footprint the ascent tops out on - and a sign error there would put 260
    /// handrails in mid-air beside the fire escapes rather than on them. The ledges are 1.6 m deep,
    /// so nothing but the right answer lands inside one.
    /// </summary>
    [Test]
    public void Dressing_PutsEveryHandrailOnTheLedgeItGuards()
    {
        CityPlanResult plan = Plan;
        Dictionary<string, DetailPlan> rails = new Dictionary<string, DetailPlan>();

        foreach (DetailPlan detail in Details(plan, CityDressing.TraversalGroup))
        {
            rails[detail.Name] = detail;
        }

        int checkedRails = 0;

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.IsRamped)
            {
                continue;
            }

            string name = ascent.Name.Replace(' ', '_');

            for (int i = 0; i < ascent.Landings.Count; i++)
            {
                if (!rails.TryGetValue($"{name}_Rail{i}", out DetailPlan rail))
                {
                    Assert.Fail($"{ascent.Name} ledge {i} has no handrail.");
                    continue;
                }

                checkedRails++;
                CityRect ledge = ascent.Landings[i];

                Assert.That(rail.Footprint.MinX, Is.GreaterThanOrEqualTo(ledge.MinX - 0.001f));
                Assert.That(rail.Footprint.MaxX, Is.LessThanOrEqualTo(ledge.MaxX + 0.001f));
                Assert.That(rail.Footprint.MinZ, Is.GreaterThanOrEqualTo(ledge.MinZ - 0.001f));
                Assert.That(rail.Footprint.MaxZ, Is.LessThanOrEqualTo(ledge.MaxZ + 0.001f));

                Assert.That(rail.TopY, Is.EqualTo(ascent.LandingY[i] + CityDesign.RailHeight)
                    .Within(0.001f), $"{name} rail {i} is not at hand height above its ledge.");
            }
        }

        Assert.That(checkedRails, Is.GreaterThan(100),
            "The city has 267 ascent steps; a handful of rails means the loop stopped early.");
    }

    /// <summary>
    /// The gate's dressing is dressing, and it belongs to the gate.
    ///
    /// Two claims, and Harness E now rests on both. The chevrons and beacons have to sit in a group
    /// nested inside <see cref="CityObjectives.GateGroup"/> because `ObjectiveTracker` opens the
    /// tower by deactivating that transform, and anything hung anywhere else would be left in the
    /// air over an opened spiral. And they have to be decoration, because a solid chevron at the
    /// foot of the spiral would be a ledge in the one place in the city whose whole purpose is to
    /// have no way past it.
    ///
    /// Nesting them is also what broke Harness E's old "tower gate pieces 2" rule, which counted
    /// child transforms as a stand-in for counting walls. The validator now counts colliders, and
    /// this is the plan-side half of the same pair of facts.
    /// </summary>
    [Test]
    public void Dressing_HangsTheGateChevronsOnTheGateAndLeavesThemDecoration()
    {
        CityPlanResult plan = Plan;

        Assert.That(CityDressing.GateDetailGroup, Is.Not.EqualTo(CityObjectives.GateGroup),
            "The dressing needs its own group, or deactivating the gate could not tell them apart.");

        List<DetailPlan> dressing = Details(plan, CityDressing.GateDetailGroup);

        Assert.That(dressing.Count, Is.GreaterThan(0), "The tower gate is undressed.");

        int walls = 0;

        foreach (BlockPlan block in plan.Blocks)
        {
            if (block.Kind == CityPieceKind.Gate && block.Collidable)
            {
                walls++;
            }
        }

        Assert.That(walls, Is.EqualTo(2),
            "A wall across the spiral's foot and one along its side - and the number Harness E " +
            "expects to find as colliders under the gate.");

        // Every piece of the dressing stands on the walls it decorates, so deactivating the gate
        // takes all of it and nothing that is not it.
        TowerGatePlan gate = plan.Objectives.Gate;

        foreach (DetailPlan detail in dressing)
        {
            bool onAWall = detail.Footprint.Overlaps(gate.FootWall.Inset(-0.4f))
                           || detail.Footprint.Overlaps(gate.SideWall.Inset(-0.4f));

            Assert.That(onAWall, Is.True,
                $"{detail.Name} is in the gate's group but not on the gate.");
            Assert.That(detail.Centre.y - detail.Size.y * 0.5f,
                Is.GreaterThanOrEqualTo(gate.BaseY - 0.001f),
                $"{detail.Name} hangs below the podium roof the gate stands on.");
            Assert.That(detail.TopY, Is.LessThanOrEqualTo(gate.TopY + 0.001f),
                $"{detail.Name} stands proud of the top of the gate.");
        }
    }

    // ================================================================== Phase 6E: route guidance

    private static CityNavigation.Result Nav(CityPlanResult plan) => CityNavigation.Build(plan);

    private static List<string> TargetIds(CityNavigation.Result nav)
    {
        List<string> ids = new List<string>(nav.Targets.Keys);
        ids.Sort(System.StringComparer.Ordinal);
        return ids;
    }

    [Test]
    public void Guidance_DescribesWalkableAscentsAsStairsNotMantles()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        AscentPlan ascent = null;

        foreach (AscentPlan candidate in plan.Traversal.StreetAscents())
        {
            ascent = candidate;
            break;
        }

        Assert.That(ascent, Is.Not.Null);
        int from = nav.Graph.IndexOf(CityNavigation.FootPrefix + ascent.Name);
        int to = nav.Graph.IndexOf(ascent.TopNode);
        List<int> path = nav.Graph.Path(from, to);
        List<string> description = CityNavigation.Describe(plan, nav, path);
        string text = string.Join("\n", description);

        Assert.That(text, Does.Contain("stair"));
        Assert.That(text, Does.Contain("steps"));
        Assert.That(text, Does.Not.Contain("mantle"));
    }

    [Test]
    public void Guidance_BuildsAGraphOutOfStreetsWaysUpAndRoofsWithNoProblems()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);

        Assert.That(nav.Problems, Is.Empty, string.Join("; ", nav.Problems));

        Assert.That(nav.StreetNodes, Is.GreaterThan(16),
            "The corridor lattice has to be dense enough that the nearest node is in front of the " +
            "player rather than half an avenue behind them.");

        int waysIn = 0;

        foreach (AscentPlan unused in plan.Traversal.StreetAscents())
        {
            waysIn++;
        }

        Assert.That(nav.FootNodes, Is.EqualTo(waysIn),
            "One pavement node at the foot of every way up, or the guidance can send a player to a " +
            "fire escape it has no way to name.");

        Assert.That(nav.SurfaceNodes, Is.EqualTo(RoofGraph.Build(plan).Nodes.Count),
            "The rooftop half of the nav graph is the Phase 6C roof graph, node for node.");

        Assert.That(nav.Targets.Count, Is.EqualTo(plan.Objectives.Relays.Count + 1),
            "Five relays and the summit.");
    }

    /// <summary>
    /// The claim the whole feature rests on: the trail follows the city.
    ///
    /// Every street-level leg of every route from every way up to every objective is checked
    /// against every building footprint and the tower. One leg through a wall would make the
    /// guidance worse than the compass it was built to fix, because a player would follow it.
    /// </summary>
    [Test]
    public void Guidance_NeverRoutesThroughABuilding()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int checkedLegs = 0;

        foreach (NavLink link in graph.Links)
        {
            if (graph.Nodes[link.From].Kind == NavNodeKind.Surface
                || graph.Nodes[link.To].Kind == NavNodeKind.Surface)
            {
                continue;
            }

            checkedLegs++;

            Assert.That(
                CityNavigation.BlockedSegment(plan, graph.Nodes[link.From].Position,
                    graph.Nodes[link.To].Position), Is.False,
                $"{graph.Nodes[link.From].Name} -> {graph.Nodes[link.To].Name} crosses something " +
                "a player cannot walk through.");
        }

        Assert.That(checkedLegs, Is.GreaterThan(100),
            "A graph with almost no street legs would pass this without meaning anything.");
    }

    [Test]
    public void Guidance_ReachesEveryObjectiveFromTheSpawnAndFromEveryWayUp()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        List<string> ids = TargetIds(nav);

        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        Assert.That(graph.Nodes[spawn].Kind, Is.EqualTo(NavNodeKind.Street),
            "A player standing on the plaza is on the street, not on a roof.");

        foreach (string id in ids)
        {
            Assert.That(graph.Path(spawn, graph.IndexOf(nav.Targets[id])), Is.Not.Null,
                $"There is no route from the spawn to {id}.");
        }

        foreach (AscentPlan ascent in plan.Traversal.StreetAscents())
        {
            int foot = graph.IndexOf(CityNavigation.FootPrefix + ascent.Name);

            Assert.That(foot, Is.GreaterThanOrEqualTo(0), $"{ascent.Name} has no foot node.");

            foreach (string id in ids)
            {
                Assert.That(graph.Path(foot, graph.IndexOf(nav.Targets[id])), Is.Not.Null,
                    $"A player at the foot of {ascent.Name} cannot be guided to {id}.");
            }
        }
    }

    /// <summary>
    /// The route is not the straight line the compass already draws.
    ///
    /// Stated as a ratio rather than as "the paths differ", because a route that merely happens to
    /// have nodes along a straight line is the failure this is guarding against. Every objective in
    /// this city is on a roof reached by a fire escape somewhere else, so no honest route to one is
    /// within a few per cent of the crow-flies distance.
    /// </summary>
    [Test]
    public void Guidance_RouteIsNotAStraightLineToTheObjective()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<int> path = graph.Path(spawn, to);

            Assert.That(path, Is.Not.Null);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(3),
                $"{id} is reached in {path.Count} move(s), which is not a route through a city.");

            List<Vector3> line = graph.Waypoints(CityDesign.SpawnPosition, path,
                graph.Nodes[to].Position);

            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            float direct = (graph.Nodes[to].Position - CityDesign.SpawnPosition).magnitude;

            Assert.That(length, Is.GreaterThan(direct * 1.15f),
                $"{id}: the route measures {length:F0} m against {direct:F0} m as the crow flies.");
        }
    }

    /// <summary>
    /// Changing the objective changes the route.
    ///
    /// The behaviour `RouteGuide` depends on when a relay is captured and the tracker starts
    /// pointing somewhere else: the whole route diverges, not merely its last node.
    ///
    /// What is compared is the *route*, not the chevrons that are showing. Those are capped at
    /// <see cref="CityDesign.GuideVisibleRange"/>, and two objectives that lie in the same
    /// direction share the near end of the trail because they genuinely share the near end of the
    /// journey - the Corporate and Industrial relays are both reached by leaving the plaza east,
    /// and 170 m of chevrons cannot tell them apart because at 170 m out there is nothing to tell.
    /// Asserting otherwise would be asserting that the city is different from how it is.
    /// </summary>
    [Test]
    public void Guidance_ChangingTheObjectiveChangesTheTrail()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        Vector3 spawn = CityDesign.SpawnPosition;
        int start = graph.Nearest(spawn);

        List<string> ids = TargetIds(nav);
        Dictionary<string, List<Vector3>> routes = new Dictionary<string, List<Vector3>>();

        foreach (string id in ids)
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<int> path = graph.Path(start, to);

            Assert.That(path, Is.Not.Null);

            routes[id] = graph.Waypoints(spawn, path, graph.Nodes[to].Position);

            Assert.That(CityNavigation.Breadcrumbs(routes[id], null,
                    CityDesign.GuideMarkerCount).Count,
                Is.GreaterThan(0), $"{id} produced no chevrons.");
        }

        for (int i = 0; i < ids.Count; i++)
        {
            for (int j = i + 1; j < ids.Count; j++)
            {
                Assert.That(nav.Targets[ids[i]], Is.Not.EqualTo(nav.Targets[ids[j]]),
                    $"{ids[i]} and {ids[j]} end on the same node.");

                List<Vector3> a = routes[ids[i]];
                List<Vector3> b = routes[ids[j]];

                Assert.That((a[a.Count - 1] - b[b.Count - 1]).magnitude, Is.GreaterThan(1f),
                    $"The routes to {ids[i]} and {ids[j]} finish in the same place.");

                bool diverges = a.Count != b.Count;

                for (int k = 0; !diverges && k < a.Count; k++)
                {
                    diverges = (a[k] - b[k]).sqrMagnitude > 1f;
                }

                Assert.That(diverges, Is.True,
                    $"The route to {ids[i]} and the route to {ids[j]} are the same route.");
            }
        }

        // And the part the cap does not hide: where two routes do diverge inside the visible range,
        // the chevrons diverge with them. Without this the test above could pass on routes that
        // differ only in their last hundred metres.
        int visiblyDifferent = 0;

        for (int i = 0; i < ids.Count; i++)
        {
            for (int j = i + 1; j < ids.Count; j++)
            {
                List<Breadcrumb> a = CityNavigation.Breadcrumbs(routes[ids[i]], null,
                    CityDesign.GuideMarkerCount);
                List<Breadcrumb> b = CityNavigation.Breadcrumbs(routes[ids[j]], null,
                    CityDesign.GuideMarkerCount);

                bool differs = a.Count != b.Count;

                for (int k = 0; !differs && k < a.Count; k++)
                {
                    differs = (a[k].Position - b[k].Position).sqrMagnitude > 1f;
                }

                if (differs)
                {
                    visiblyDifferent++;
                }
            }
        }

        Assert.That(visiblyDifferent, Is.GreaterThanOrEqualTo(ids.Count),
            "Almost every pair of objectives leaves the plaza a different way, so the chevrons " +
            $"should differ for most pairs; only {visiblyDifferent} of " +
            $"{ids.Count * (ids.Count - 1) / 2} did.");
    }

    /// <summary>
    /// A player standing on a roof is guided from that roof.
    ///
    /// <see cref="CityNavGraph.Nearest"/> weights height heavily for one reason: a player on a 25 m
    /// roof is horizontally within a few metres of the pavement below them, and snapping them to
    /// the street would route them down a fire escape they are standing on top of and back up
    /// another one.
    /// </summary>
    [Test]
    public void Guidance_SnapsAPlayerOnARoofToThatRoofRatherThanToTheStreetBelow()
    {
        CityPlanResult plan = Plan;
        CityNavGraph graph = Nav(plan).Graph;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            int node = graph.Nearest(relay.Position);

            Assert.That(graph.Nodes[node].Kind, Is.EqualTo(NavNodeKind.Surface),
                $"A player standing on {relay.Name} snaps to {graph.Nodes[node].Name}.");
            Assert.That(graph.Nodes[node].Name, Is.EqualTo(relay.Node),
                $"A player standing on {relay.Name} snaps to the wrong roof.");
        }
    }

    /// <summary>
    /// Breadcrumbs are spaced to be read, and every corner of the route carries one.
    ///
    /// Even resampling alone is not enough: at a junction the next sample lands past the turn, and
    /// a turn a player cannot see coming is a turn they miss. So corners are kept whatever the
    /// spacing says, and this asserts both halves.
    /// </summary>
    [Test]
    public void Guidance_SpacesBreadcrumbsAndAlwaysMarksATurn()
    {
        // A deliberate right angle, longer on each leg than the spacing, so the corner is not one
        // an even resample would happen to land on.
        List<Vector3> polyline = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 30f),
            new Vector3(45f, 0f, 30f)
        };

        List<Breadcrumb> crumbs = CityNavigation.Breadcrumbs(polyline, null,
            CityDesign.GuideMarkerCount);

        Assert.That(crumbs.Count, Is.GreaterThan(4));
        Assert.That(crumbs.Count, Is.LessThanOrEqualTo(CityDesign.GuideMarkerCount),
            "The pool is fixed, so the trail may never ask for more markers than exist.");

        bool marksTheCorner = false;

        foreach (Breadcrumb crumb in crumbs)
        {
            if ((crumb.Position - polyline[1]).sqrMagnitude < CityDesign.GuideBreadcrumbSpacing
                * CityDesign.GuideBreadcrumbSpacing * 0.25f)
            {
                marksTheCorner = true;
            }
        }

        Assert.That(marksTheCorner, Is.True, "Nothing marks the turn.");

        for (int i = 1; i < crumbs.Count; i++)
        {
            Assert.That((crumbs[i].Position - crumbs[i - 1].Position).magnitude,
                Is.LessThanOrEqualTo(CityDesign.GuideBreadcrumbSpacing + 0.01f),
                $"Chevrons {i - 1} and {i} are further apart than the spacing allows.");
        }
    }

    [Test]
    public void Guidance_NeverAsksForMoreChevronsThanThePoolHolds()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        Vector3 spawn = CityDesign.SpawnPosition;
        int start = graph.Nearest(spawn);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<Vector3> line = graph.Waypoints(spawn, graph.Path(start, to),
                graph.Nodes[to].Position);
            List<Breadcrumb> crumbs = CityNavigation.Breadcrumbs(line, null,
                CityDesign.GuideMarkerCount);

            Assert.That(crumbs.Count, Is.LessThanOrEqualTo(CityDesign.GuideMarkerCount),
                $"{id} wants {crumbs.Count} chevrons and the pool holds " +
                $"{CityDesign.GuideMarkerCount}.");
        }
    }

    /// <summary>
    /// The guidance is graded by the same tier table the traversal layer is, and never sends a
    /// player over a move the city calls harder than a mantle. It cannot: every rooftop edge it
    /// walks is an edge `RoofGraph` built.
    /// </summary>
    [Test]
    public void Guidance_NeverRoutesOverAMoveHarderThanAnAscent()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int start = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            List<int> path = graph.Path(start, graph.IndexOf(nav.Targets[id]));

            Assert.That(graph.WorstTier(path), Is.LessThanOrEqualTo(RouteTier.Orange),
                $"The route to {id} grades {graph.WorstTier(path)}, and every way up this city " +
                "has is a mantle.");
        }
    }

    [Test]
    public void Guidance_IsDeterministic()
    {
        CityNavigation.Result a = CityNavigation.Build(CityPlan.Generate());
        CityNavigation.Result b = CityNavigation.Build(CityPlan.Generate());

        Assert.That(b.Graph.Nodes.Count, Is.EqualTo(a.Graph.Nodes.Count));
        Assert.That(b.Graph.Links.Count, Is.EqualTo(a.Graph.Links.Count));

        for (int i = 0; i < a.Graph.Nodes.Count; i++)
        {
            Assert.That(b.Graph.Nodes[i].Name, Is.EqualTo(a.Graph.Nodes[i].Name));
            Assert.That(b.Graph.Nodes[i].Position.x,
                Is.EqualTo(a.Graph.Nodes[i].Position.x).Within(0.0001f));
        }

        for (int i = 0; i < a.Graph.Links.Count; i++)
        {
            Assert.That(b.Graph.Links[i].From, Is.EqualTo(a.Graph.Links[i].From));
            Assert.That(b.Graph.Links[i].To, Is.EqualTo(a.Graph.Links[i].To));
            Assert.That(b.Graph.Links[i].Cost, Is.EqualTo(a.Graph.Links[i].Cost).Within(0.0001f));
        }
    }

    /// <summary>
    /// The graph survives the round trip through a scene file.
    ///
    /// `SkyboundCityBuilder` bakes the graph into `RouteGuide` as eight flat arrays and the
    /// component rebuilds it in Awake. If those two ever disagree the guide would path over a
    /// different city from the one the tests measure, and nothing would say so.
    /// </summary>
    [Test]
    public void Guidance_SurvivesBeingFlattenedIntoArraysAndRebuilt()
    {
        CityNavigation.Result nav = Nav(Plan);
        CityNavGraph source = nav.Graph;

        int count = source.Nodes.Count;
        string[] names = new string[count];
        int[] kinds = new int[count];
        Vector3[] positions = new Vector3[count];
        Vector3[] extents = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            names[i] = source.Nodes[i].Name;
            kinds[i] = (int)source.Nodes[i].Kind;
            positions[i] = source.Nodes[i].Position;
            extents[i] = source.Nodes[i].Extent;
        }

        int links = source.Links.Count;
        int[] from = new int[links];
        int[] to = new int[links];
        float[] cost = new float[links];
        Vector3[] exit = new Vector3[links];
        int[] tier = new int[links];
        int[] move = new int[links];

        for (int i = 0; i < links; i++)
        {
            from[i] = source.Links[i].From;
            to[i] = source.Links[i].To;
            cost[i] = source.Links[i].Cost;
            exit[i] = source.Links[i].Exit;
            tier[i] = (int)source.Links[i].Tier;
            move[i] = (int)source.Links[i].Move;
        }

        CityNavGraph rebuilt = CityNavGraph.FromArrays(names, kinds, positions, extents, from, to,
            cost, exit, tier, move);

        Assert.That(rebuilt.Nodes.Count, Is.EqualTo(count));
        Assert.That(rebuilt.Links.Count, Is.EqualTo(links));

        int start = source.Nearest(CityDesign.SpawnPosition);

        Assert.That(rebuilt.Nearest(CityDesign.SpawnPosition), Is.EqualTo(start));

        foreach (string id in TargetIds(nav))
        {
            int target = source.IndexOf(nav.Targets[id]);
            List<int> before = source.Path(start, target);
            List<int> after = rebuilt.Path(start, target);

            Assert.That(after, Is.Not.Null, $"The rebuilt graph cannot reach {id}.");
            Assert.That(after.Count, Is.EqualTo(before.Count),
                $"The route to {id} changed length across the round trip.");

            for (int i = 0; i < before.Count; i++)
            {
                Assert.That(after[i], Is.EqualTo(before[i]),
                    $"The route to {id} diverges at leg {i}.");
            }
        }
    }

    // ------------------------------------------------------------------ guidance stability

    /// <summary>
    /// A player standing anywhere on a roof is on *that* roof.
    ///
    /// The first version scored a node by the distance to its centre, and a Corporate roof is 55 m
    /// across: standing near its edge, the middle of the building next door was genuinely closer,
    /// so the guide decided the player was over there and routed them accordingly. Scoring against
    /// the surface's extent instead makes anywhere on a roof score zero for it, which is the
    /// question that was being asked all along.
    /// </summary>
    [Test]
    public void Guidance_SnapsToTheSurfaceThePlayerIsActuallyStandingOn()
    {
        CityPlanResult plan = Plan;
        CityNavGraph graph = Nav(plan).Graph;

        foreach (BuildingPlan building in plan.Buildings)
        {
            CityRect roof = building.Footprint;

            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner & 1) == 0 ? roof.MinX + 1.5f : roof.MaxX - 1.5f;
                float z = (corner & 2) == 0 ? roof.MinZ + 1.5f : roof.MaxZ - 1.5f;

                int node = graph.Nearest(new Vector3(x, building.RoofY, z));

                Assert.That(graph.Nodes[node].Name, Is.EqualTo(building.Name),
                    $"Standing on the corner of {building.Name} snapped to " +
                    $"{graph.Nodes[node].Name}.");
            }
        }
    }

    /// <summary>
    /// The guide does not change its mind while the player is standing still.
    ///
    /// A grounded CharacterController integrates gravity every frame and resolves it against the
    /// floor, so its transform breathes by a centimetre or two even when nothing is pressed. With
    /// height weighted linearly that was a tenth of a metre of score jitter per frame, which is
    /// enough to re-pick a node where two are close - and a re-picked node is a different route and
    /// a trail that jumps. Height inside <see cref="CityDesign.GuideSurfaceBand"/> now costs
    /// nothing, so the jitter cannot move the answer at all.
    /// </summary>
    [Test]
    public void Guidance_IsUnmovedByTheJitterOfAControllerStandingStill()
    {
        CityPlanResult plan = Plan;
        CityNavGraph graph = Nav(plan).Graph;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            TraversalSurface roof = plan.Traversal.Surfaces[relay.Node];
            Vector3 at = new Vector3(roof.Footprint.MinX + 2f, roof.SurfaceY,
                roof.Footprint.MinZ + 2f);

            int expected = graph.Nearest(at);
            int held = expected;

            for (int frame = 0; frame < 240; frame++)
            {
                // A saw-tooth of +/- 2 cm, which is the shape the controller's ground resolution
                // actually has, rather than random noise that might miss the boundary.
                float y = roof.SurfaceY + (frame % 2 == 0 ? 0.02f : -0.02f);
                Vector3 jittered = new Vector3(at.x, y, at.z);

                Assert.That(graph.Nearest(jittered), Is.EqualTo(expected),
                    $"{relay.Node}: the bare snap moved on frame {frame} for 2 cm of height.");

                held = graph.NearestStable(jittered, held, CityDesign.GuideSnapHysteresis);

                Assert.That(held, Is.EqualTo(expected),
                    $"{relay.Node}: the held snap moved on frame {frame}.");
            }
        }
    }

    /// <summary>
    /// Walking a roof does not re-pick the node under the player once per metre.
    ///
    /// Measured over a serpentine walk rather than a raster, because a raster teleports back across
    /// the roof at the end of every column and would count those as flips. Before the surface-extent
    /// fix this walk flipped 52 times on the largest Corporate roof; the assertion is zero, because
    /// the player never leaves the building.
    /// </summary>
    [Test]
    public void Guidance_HoldsOneNodeForAWholeWalkAcrossOneRoof()
    {
        CityPlanResult plan = Plan;
        CityNavGraph graph = Nav(plan).Graph;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            TraversalSurface roof = plan.Traversal.Surfaces[relay.Node];
            int expected = graph.IndexOf(relay.Node);
            int held = -1;
            int flips = 0;
            bool forward = true;

            for (float x = roof.Footprint.MinX + 1f; x <= roof.Footprint.MaxX - 1f; x += 1f)
            {
                float z0 = forward ? roof.Footprint.MinZ + 1f : roof.Footprint.MaxZ - 1f;
                float z1 = forward ? roof.Footprint.MaxZ - 1f : roof.Footprint.MinZ + 1f;
                float dz = forward ? 1f : -1f;
                forward = !forward;

                for (float z = z0; dz > 0f ? z <= z1 : z >= z1; z += dz)
                {
                    int now = graph.NearestStable(new Vector3(x, roof.SurfaceY, z), held,
                        CityDesign.GuideSnapHysteresis);

                    if (held >= 0 && now != held)
                    {
                        flips++;
                    }

                    held = now;
                }
            }

            Assert.That(flips, Is.Zero, $"The snap changed {flips} time(s) crossing {relay.Node}.");
            Assert.That(held, Is.EqualTo(expected));
        }
    }

    /// <summary>
    /// The objective does not chatter on the line where two relays are equidistant.
    ///
    /// This was the loudest of the flickers: the bare "nearest uncaptured relay" rule flips every
    /// frame along that line, and each flip is a new HUD label, a full re-search of the city and an
    /// entirely different trail. Pacing back and forth across it 1120 times switched the objective
    /// 63 times before the stickiness and must not switch at all after it.
    /// </summary>
    [Test]
    public void Guidance_ObjectiveDoesNotChatterOnTheLineBetweenTwoRelays()
    {
        CityPlanResult plan = Plan;
        List<Vector3> positions = new List<Vector3>();
        List<bool> available = new List<bool>();

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            positions.Add(relay.Position);
            available.Add(true);
        }

        Vector3 axis = positions[1] - positions[0];
        axis.y = 0f;
        axis = axis.normalized;
        Vector3 midpoint = (positions[0] + positions[1]) * 0.5f;

        int bare = -1;
        int bareSwitches = 0;
        int sticky = -1;
        int stickySwitches = 0;

        for (int step = 0; step < 1120; step++)
        {
            Vector3 at = midpoint + axis * (Mathf.Sin(step * 0.175f) * 4f);

            int now = ObjectiveFocus.Choose(positions, available, at, -1, 0f);

            if (bare >= 0 && now != bare)
            {
                bareSwitches++;
            }

            bare = now;

            int next = ObjectiveFocus.Choose(positions, available, at, sticky,
                CityDesign.ObjectiveStickiness);

            if (sticky >= 0 && next != sticky)
            {
                stickySwitches++;
            }

            sticky = next;
        }

        Assert.That(bareSwitches, Is.GreaterThan(10),
            "If the bare rule no longer chatters here the test is measuring the wrong place.");
        Assert.That(stickySwitches, Is.Zero,
            $"The objective changed {stickySwitches} time(s) while the player paced one spot.");
    }

    /// <summary>
    /// A captured relay releases the held objective immediately, however far away the next one is.
    ///
    /// The stickiness must never outlive the reason for it. If it did, the compass would go on
    /// pointing at a relay the player has just taken.
    /// </summary>
    [Test]
    public void Guidance_ReleasesTheHeldObjectiveTheMomentItIsCaptured()
    {
        List<Vector3> positions = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(400f, 0f, 0f)
        };

        List<bool> available = new List<bool> { true, true };
        Vector3 at = new Vector3(5f, 0f, 0f);

        int chosen = ObjectiveFocus.Choose(positions, available, at, -1,
            CityDesign.ObjectiveStickiness);

        Assert.That(chosen, Is.Zero, "The nearest one, with nothing held.");

        available[0] = false;
        chosen = ObjectiveFocus.Choose(positions, available, at, chosen,
            CityDesign.ObjectiveStickiness);

        Assert.That(chosen, Is.EqualTo(1),
            "A captured relay is not a relay the mission may keep pointing at.");
    }

    /// <summary>
    /// The chevrons are nailed to the route, not to the player.
    ///
    /// A re-search used to resample the trail from wherever the player happened to be, so every
    /// search slid all twenty-six markers by up to a spacing at once - 7 m of the whole world
    /// moving. Laying them at fixed arc lengths from the start of the route means a re-search of the
    /// same route puts every marker back exactly where it was, and the player sees nothing happen.
    /// </summary>
    [Test]
    public void Guidance_LaysTheSameChevronsWhereverTheSearchStartedFrom()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        int from = graph.IndexOf("CityCenter_B12");
        int to = graph.IndexOf(nav.Targets["Relay_Industrial"]);
        List<NavMove> moves = new List<NavMove>();
        List<Vector3> polyline = graph.Waypoints(graph.Nodes[from].Position, graph.Path(from, to),
            graph.Nodes[to].Position, moves);

        List<Breadcrumb> reference = CityNavigation.Breadcrumbs(polyline, moves, 64);

        Assert.That(reference.Count, Is.GreaterThan(8));

        // Every later view of the same route puts its markers exactly where the first one did.
        // Only the overlapping range is compared: the drawn window slides forward with the player,
        // so a later view legitimately reaches further down the route than the first one could.
        float covered = reference[reference.Count - 1].Along;

        for (float advanced = 0f; advanced < 60f; advanced += 1f)
        {
            List<Breadcrumb> later = CityNavigation.Breadcrumbs(polyline, moves, 64, advanced);

            foreach (Breadcrumb crumb in later)
            {
                if (crumb.Along > covered)
                {
                    continue;
                }

                bool matched = false;

                foreach (Breadcrumb original in reference)
                {
                    if ((original.Position - crumb.Position).sqrMagnitude < 0.0001f)
                    {
                        matched = true;
                        break;
                    }
                }

                Assert.That(matched, Is.True,
                    $"A chevron at {crumb.Position} appeared only after the player had run " +
                    $"{advanced:F0} m, so the trail moved under them.");
            }
        }
    }

    /// <summary>
    /// Progress along the route only ever goes forwards, and does not move at all for a player who
    /// is standing still and looking around.
    ///
    /// Which chevrons are showing is decided by this number, so a value that wobbled would show and
    /// hide markers on alternating frames - which is what "flickers while rotating the camera"
    /// looks like from the inside.
    /// </summary>
    [Test]
    public void Guidance_ProgressAlongTheRouteOnlyAdvancesAndHoldsStillWhenThePlayerDoes()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        int from = graph.IndexOf("CityCenter_B12");
        int to = graph.IndexOf(nav.Targets["Relay_Industrial"]);
        List<Vector3> polyline = graph.Waypoints(graph.Nodes[from].Position, graph.Path(from, to),
            graph.Nodes[to].Position);

        float along = 0f;

        for (float travelled = 0f; travelled < 180f; travelled += 0.4f)
        {
            Vector3 at = PointAlong(polyline, travelled);
            float next = CityNavigation.Advance(polyline, at, along,
                CityDesign.GuideProjectionWindow);

            Assert.That(next, Is.GreaterThanOrEqualTo(along - 0.0001f),
                $"Progress went backwards at {travelled:F0} m along the route.");
            Assert.That(next, Is.EqualTo(travelled).Within(1.5f),
                "Progress should track the player's real position along the route.");

            along = next;
        }

        // Standing still: 240 frames, nothing may move.
        Vector3 still = PointAlong(polyline, 60f);
        float held = CityNavigation.Advance(polyline, still, 55f, CityDesign.GuideProjectionWindow);

        for (int frame = 0; frame < 240; frame++)
        {
            float now = CityNavigation.Advance(polyline, still, held,
                CityDesign.GuideProjectionWindow);

            Assert.That(now, Is.EqualTo(held).Within(0.0001f),
                $"Progress moved on frame {frame} for a player who did not.");

            held = now;
        }
    }

    private static Vector3 PointAlong(List<Vector3> polyline, float along)
    {
        float travelled = 0f;

        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Vector3 step = polyline[i + 1] - polyline[i];
            float length = step.magnitude;

            if (travelled + length >= along)
            {
                return polyline[i] + step * ((along - travelled) / Mathf.Max(0.001f, length));
            }

            travelled += length;
        }

        return polyline[polyline.Count - 1];
    }

    // ------------------------------------------------------------------ guidance clarity

    /// <summary>
    /// Every point on a route where the player has to stop running and do something carries a
    /// marker that says so.
    ///
    /// This is the difference between a trail that points at the objective and a trail a player who
    /// has never seen the city can follow: an arrow towards a wall is useless, and an arrow towards
    /// a wall with an upright marker standing on the fire escape at the bottom of it is not.
    /// </summary>
    [Test]
    public void Guidance_MarksEveryPointWhereTheRouteStopsBeingARun()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        int totalTransitions = 0;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            foreach (string id in TargetIds(nav))
            {
                int from = graph.Nearest(relay.Position);
                int to = graph.IndexOf(nav.Targets[id]);

                if (from == to)
                {
                    continue;
                }

                List<int> path = graph.Path(from, to);

                Assert.That(path, Is.Not.Null);

                List<NavMove> moves = new List<NavMove>();
                List<Vector3> polyline = graph.Waypoints(graph.Nodes[from].Position, path,
                    graph.Nodes[to].Position, moves);

                // Everything the route asks for that is not simply running.
                //
                // `Waypoints` stores a link's exit point and its move at the same index, and the
                // exit point is where the player is standing when they make the move - the roof
                // edge they jump from, the pavement at the foot of the fire escape. So the place a
                // marker has to stand is polyline[i] for moves[i], not the point before it.
                List<Vector3> transitions = new List<Vector3>();

                for (int i = 1; i < moves.Count && i < polyline.Count; i++)
                {
                    if (moves[i] != NavMove.Walk)
                    {
                        transitions.Add(polyline[i]);
                    }
                }

                totalTransitions += transitions.Count;

                // The trail is only drawn GuideVisibleRange ahead, so the window is walked forward
                // the way a player walks it. What is asserted is the thing that matters: by the
                // time the player gets there, something is standing on every transition.
                float length = 0f;

                for (int i = 0; i < polyline.Count - 1; i++)
                {
                    length += (polyline[i + 1] - polyline[i]).magnitude;
                }

                List<Breadcrumb> crumbs = new List<Breadcrumb>();

                for (float window = 0f; window <= length + 20f;
                     window += CityDesign.GuideVisibleRange * 0.5f)
                {
                    crumbs.AddRange(CityNavigation.Breadcrumbs(polyline, moves, 512, window));
                }

                foreach (Vector3 transition in transitions)
                {
                    bool marked = false;

                    foreach (Breadcrumb crumb in crumbs)
                    {
                        if (crumb.Move != NavMove.Walk
                            && (crumb.Position - transition).sqrMagnitude
                            < CityDesign.GuideBreadcrumbSpacing * CityDesign.GuideBreadcrumbSpacing)
                        {
                            marked = true;
                            break;
                        }
                    }

                    Assert.That(marked, Is.True,
                        $"{relay.Name} -> {id}: nothing tells the player what to do at " +
                        $"{transition}.");
                }
            }
        }

        Assert.That(totalTransitions, Is.GreaterThan(40),
            "A city whose routes have almost no transitions would pass this without meaning much.");
    }

    /// <summary>
    /// Every route between two objectives can be written down as a sequence of moves a player can
    /// actually perform, and every jump on one is inside the movement envelope.
    ///
    /// The second half is the important one: the guidance is only as good as its claim that the
    /// route is walkable, and the tier grading it inherits from `RoofGraph` is a table lookup. This
    /// re-derives each jump from `TraversalEnvelope` - the same formula the whole city was sized
    /// against - so a route that graded GREEN but needed a 12 m leap could not pass.
    ///
    /// The *horizontal* reach is checked at a rise of zero rather than at the jump's own rise, and
    /// that is not a loosening. PHASE 6A.5 gave the player an airborne mantle, and the tier table
    /// spends it: from ORANGE upward a jump may rise `RouteTiers.MantleStepRise` by catching the far
    /// edge rather than by clearing it. So what the arc has to buy is the distance, and what the
    /// mantle buys is the last two metres of height - which is why they are asserted separately.
    /// </summary>
    [Test]
    public void Guidance_OnlyEverRoutesOverJumpsThePlayerCanActuallyMake()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int jumps = 0;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            foreach (string id in TargetIds(nav))
            {
                int from = graph.Nearest(relay.Position);
                int to = graph.IndexOf(nav.Targets[id]);

                if (from == to)
                {
                    continue;
                }

                List<int> path = graph.Path(from, to);
                List<string> described = CityNavigation.Describe(plan, nav, path);

                Assert.That(described.Count, Is.EqualTo(path.Count + 1),
                    $"{relay.Name} -> {id}: the route cannot be written down move by move.");

                foreach (int link in path)
                {
                    NavLink edge = graph.Links[link];

                    if (edge.Move != NavMove.Jump)
                    {
                        continue;
                    }

                    string a = graph.Nodes[edge.From].Name;
                    string b = graph.Nodes[edge.To].Name;

                    if (!plan.Traversal.Surfaces.TryGetValue(a, out TraversalSurface sa)
                        || !plan.Traversal.Surfaces.TryGetValue(b, out TraversalSurface sb))
                    {
                        continue;
                    }

                    jumps++;

                    float gap = sa.Footprint.GapTo(sb.Footprint);
                    float rise = sb.SurfaceY - sa.SurfaceY;
                    float landing = Mathf.Min(sb.Footprint.Width, sb.Footprint.Depth);
                    float reach = TraversalEnvelope.SprintDesignGap(Movement, 0f);
                    RouteTier tier = RouteTiers.Classify(gap, rise, landing);

                    Assert.That(gap, Is.LessThanOrEqualTo(reach + 0.001f),
                        $"{relay.Name} -> {id}: the route jumps {gap:F2} m and a sprinting player " +
                        $"reaches {reach:F2} m.");
                    Assert.That(rise, Is.LessThanOrEqualTo(RouteTiers.MantleStepRise + 0.001f),
                        $"{relay.Name} -> {id}: the route steps up {rise:F2} m in one move, past " +
                        "the mantle.");
                    Assert.That(-rise, Is.LessThanOrEqualTo(CityDesign.FatalFallHeight + 0.001f),
                        $"{relay.Name} -> {id}: the route drops {-rise:F2} m, which is fatal.");
                    Assert.That(tier, Is.LessThanOrEqualTo(RouteTier.Orange),
                        $"{relay.Name} -> {id}: a {gap:F2} m / {rise:F2} m jump onto a " +
                        $"{landing:F1} m landing grades {tier}.");
                }
            }
        }

        Assert.That(jumps, Is.GreaterThan(10), "No route in the city jumps, which cannot be right.");
    }

    /// <summary>
    /// The one route the player asked about, pinned: from the relay the compass sends them to first
    /// to the Industrial relay, there is a route, it is all inside the move set, and it is not a
    /// straight line.
    /// </summary>
    [Test]
    public void Guidance_ConnectsTheFirstObjectiveToTheIndustrialRelay()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        List<Vector3> positions = new List<Vector3>();
        List<bool> available = new List<bool>();

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            positions.Add(relay.Position);
            available.Add(true);
        }

        int first = ObjectiveFocus.Choose(positions, available, CityDesign.SpawnPosition, -1,
            CityDesign.ObjectiveStickiness);

        Assert.That(first, Is.GreaterThanOrEqualTo(0));

        RelayObjective start = plan.Objectives.Relays[first];
        RelayObjective industrial = plan.Objectives.Relay("Relay_Industrial");

        Assert.That(industrial, Is.Not.Null);
        Assert.That(start.Name, Is.Not.EqualTo(industrial.Name),
            "The first objective cannot also be the Industrial relay, or there is nothing to route.");

        List<int> path = graph.Path(graph.Nearest(start.Position),
            graph.IndexOf(industrial.Node));

        Assert.That(path, Is.Not.Null, "There is no route from the first relay to the Industrial one.");
        Assert.That(path.Count, Is.GreaterThanOrEqualTo(4),
            "A route across two districts is not four moves long.");
        Assert.That(graph.WorstTier(path), Is.LessThanOrEqualTo(RouteTier.Orange));

        // It uses a way down, the street and a way back up - the shape a cross-district route has
        // in this city, and the reason the trail has to say more than "that way".
        bool descends = false;
        bool climbs = false;
        bool runsOnTheStreet = false;

        foreach (int link in path)
        {
            NavLink edge = graph.Links[link];
            descends |= edge.Move == NavMove.Descend;
            climbs |= edge.Move == NavMove.Climb;
            runsOnTheStreet |= graph.Nodes[edge.From].Kind == NavNodeKind.Street
                               && graph.Nodes[edge.To].Kind == NavNodeKind.Street;
        }

        Assert.That(descends && climbs && runsOnTheStreet, Is.True,
            "The route to the Industrial relay goes down to the street and back up, so the " +
            "guidance has to be able to say so.");
    }
    // ------------------------------------------------------------------ guidance: the trail holds

    /// <summary>
    /// A player standing on a rooftop does not make the guide search the city.
    ///
    /// This was the remaining flicker, and it was not subtle once it was measured: the guide asked
    /// "am I still on my route" as a distance from the drawn polyline, the polyline is anchored at
    /// the *centre* of the node it starts from, and a roof node in this city is up to 88 m across.
    /// Standing anywhere on an Industrial roof but its middle read as 51 m off a route that starts
    /// under the player's feet, against a 9 m threshold - so the guide ran a fresh Dijkstra, took a
    /// fresh start node and re-anchored the whole trail on <b>every single frame</b>. 600 searches
    /// in 600 frames of standing perfectly still.
    ///
    /// The assertion is one search, which is the first one.
    /// </summary>
    [Test]
    public void Guidance_DoesNotSearchAgainWhileThePlayerStandsOnARoof()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            TraversalSurface roof = plan.Traversal.Surfaces[relay.Node];

            foreach (string id in TargetIds(nav))
            {
                int to = graph.IndexOf(nav.Targets[id]);

                if (nav.Targets[id] == relay.Node)
                {
                    continue;
                }

                // The far corner of the roof, which is the worst case and also where a player
                // lining up a jump actually stands.
                Vector3 at = new Vector3(roof.Footprint.MaxX - 2f, roof.SurfaceY,
                    roof.Footprint.MaxZ - 2f);

                RouteTrail trail = new RouteTrail(graph, Budget);

                for (int frame = 0; frame < 600; frame++)
                {
                    // The saw-tooth a grounded CharacterController really has, so the height band
                    // is exercised rather than assumed away.
                    float y = at.y + (frame % 2 == 0 ? 0.02f : -0.02f);

                    trail.Step(new Vector3(at.x, y, at.z), id, to, graph.Nodes[to].Position);
                }

                Assert.That(trail.HasRoute, Is.True,
                    $"No route from {relay.Node} to {id}, so this proves nothing.");
                Assert.That(trail.Searches, Is.EqualTo(1),
                    $"Standing on {relay.Node} heading for {id} searched the city " +
                    $"{trail.Searches} time(s) in 600 frames.");
            }
        }
    }

    /// <summary>
    /// Nor does walking the whole of a roof, right out to its edges.
    ///
    /// The circle runs to within two metres of the roof's nearest edge, which crosses the boundary
    /// where a rival node scores close, passes the point the route leaves by, and doubles back -
    /// every shape that used to re-pick the start node. Before the fix this walk searched the city
    /// between 568 and 790 times in 900 frames.
    /// </summary>
    [Test]
    public void Guidance_HoldsOneRouteForAWholeWalkAroundOneRoof()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            TraversalSurface roof = plan.Traversal.Surfaces[relay.Node];
            float cx = (roof.Footprint.MinX + roof.Footprint.MaxX) * 0.5f;
            float cz = (roof.Footprint.MinZ + roof.Footprint.MaxZ) * 0.5f;
            float radius = Mathf.Max(2f,
                Mathf.Min(roof.Footprint.MaxX - cx, roof.Footprint.MaxZ - cz) - 2f);

            foreach (string id in TargetIds(nav))
            {
                if (nav.Targets[id] == relay.Node)
                {
                    continue;
                }

                int to = graph.IndexOf(nav.Targets[id]);
                RouteTrail trail = new RouteTrail(graph, Budget);
                int held = -1;

                for (int frame = 0; frame < 900; frame++)
                {
                    float angle = frame * 0.012f;

                    trail.Step(new Vector3(cx + Mathf.Cos(angle) * radius, roof.SurfaceY,
                        cz + Mathf.Sin(angle) * radius), id, to, graph.Nodes[to].Position);

                    if (frame == 0)
                    {
                        held = trail.StandingOn;
                    }

                    Assert.That(trail.StandingOn, Is.EqualTo(held),
                        $"The node under the player changed on frame {frame} of a walk that " +
                        $"never leaves {relay.Node}.");
                }

                Assert.That(trail.Searches, Is.EqualTo(1),
                    $"Walking {relay.Node} heading for {id} searched the city " +
                    $"{trail.Searches} time(s) in 900 frames.");
            }
        }
    }

    /// <summary>
    /// Running the route is what the guidance is for, and it must cost almost nothing.
    ///
    /// The same route is run three times at a walk, a run and a sprint. Two claims, and the second
    /// one is the stronger:
    ///
    ///   * a handful of searches over the whole length of the route, not one per frame;
    ///   * <b>the same handful at every speed</b>. Everything the guide decides is a function of
    ///     where the player is, never of how many frames have gone by, so a route run at 10 m/s
    ///     must produce exactly the churn it produces at 1.5 m/s. A count that moved with the
    ///     frame rate would mean something in here was still being recomputed per frame, which is
    ///     the whole family of faults this is guarding against.
    /// </summary>
    [Test]
    public void Guidance_SearchesTheSameFewTimesWhateverSpeedTheRouteIsRunAt()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                graph.Nodes[to].Position);
            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            int searchesAtAWalk = -1;
            int laysAtAWalk = -1;

            // A walk, a run and a sprint, as metres per 1/60 s frame.
            foreach (float step in new[] { 1.5f / 60f, 6f / 60f, 10f / 60f })
            {
                RouteTrail trail = new RouteTrail(graph, Budget);
                int frames = 0;

                for (float travelled = 0f; travelled < length; travelled += step)
                {
                    trail.Step(PointAlong(line, travelled), id, to, graph.Nodes[to].Position);
                    frames++;
                }

                Assert.That(trail.Searches, Is.EqualTo(1),
                    $"{id} at {step * 60f:F1} m/s: {trail.Searches} searches over {frames} " +
                    "frames of running the route the guide itself drew. One is the first one; " +
                    "every other is the trail re-anchoring under a player who did nothing wrong.");

                if (searchesAtAWalk < 0)
                {
                    searchesAtAWalk = trail.Searches;
                    laysAtAWalk = trail.Lays;
                    continue;
                }

                Assert.That(trail.Searches, Is.EqualTo(searchesAtAWalk),
                    $"{id}: running at {step * 60f:F1} m/s searched {trail.Searches} times " +
                    $"against {searchesAtAWalk} at a walk, so something is per-frame.");
                Assert.That(trail.Lays, Is.EqualTo(laysAtAWalk),
                    $"{id}: running at {step * 60f:F1} m/s laid the trail {trail.Lays} times " +
                    $"against {laysAtAWalk} at a walk.");
            }
        }
    }

    /// <summary>
    /// The markers do not churn: what is drawn only loses at the near end and gains at the far one.
    ///
    /// This is what "flicker" means from the pool's side. A chevron may leave the set because the
    /// player has run past it, and one may join because the trail has been laid further down the
    /// route; anything else - a marker dropping out of the middle, or one appearing behind the far
    /// end after the trail had already reached past it - is the trail moving under the player, and
    /// it is what they see.
    ///
    /// Compared by <see cref="Breadcrumb.Along"/>, which is the marker's exact arc position and is
    /// what makes the claim precise: re-deriving it from the position would be a global
    /// closest-point search, and this city's routes double back on themselves.
    ///
    /// Counted over the whole of every route, at a sprint, with a metre of side-to-side wander so
    /// the player is not standing exactly on the polyline they are being shown.
    /// </summary>
    [Test]
    public void Guidance_DrawsTheSameMarkersFromOneFrameToTheNext()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        List<Breadcrumb> chevrons = new List<Breadcrumb>();
        List<Breadcrumb> actions = new List<Breadcrumb>();
        List<float> before = new List<float>();

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                graph.Nodes[to].Position);
            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            RouteTrail trail = new RouteTrail(graph, Budget);
            before.Clear();

            int frame = 0;
            int searches = 0;
            int droppedFromTheMiddle = 0;
            int appearedBehindTheEnd = 0;

            for (float travelled = 0f; travelled < length; travelled += 10f / 60f, frame++)
            {
                Vector3 at = PointAlong(line, travelled);
                at.x += Mathf.Sin(frame * 0.19f);
                at.z += Mathf.Cos(frame * 0.23f);

                // And off the ground. A player crossing a gap or coming up a fire escape spends a
                // second or so above the surface the route is drawn on, which is exactly where a
                // guide that snapped to the nearest node by height used to change its mind.
                at.y += Mathf.Max(0f, Mathf.Sin(travelled * 0.21f)) * 1.8f;

                trail.Step(at, id, to, graph.Nodes[to].Position);
                trail.Visible(CityDesign.GuideMarkerCount, CityDesign.GuideActionMarkerCount,
                    chevrons, actions);

                // A search legitimately replaces the whole trail - it is a different route. That it
                // almost never happens is the subject of the tests above; what happens on the
                // frame it does is not this one's business.
                bool searched = trail.Searches != searches;
                searches = trail.Searches;

                if (before.Count > 0 && chevrons.Count > 0 && !searched)
                {
                    float nearest = chevrons[0].Along;
                    float furthest = before[before.Count - 1];

                    foreach (float was in before)
                    {
                        if (was >= nearest - 0.001f && !HoldsAlong(chevrons, was))
                        {
                            droppedFromTheMiddle++;
                        }
                    }

                    foreach (Breadcrumb crumb in chevrons)
                    {
                        if (crumb.Along < furthest - 0.001f && !before.Contains(crumb.Along))
                        {
                            appearedBehindTheEnd++;
                        }
                    }
                }

                before.Clear();

                foreach (Breadcrumb crumb in chevrons)
                {
                    before.Add(crumb.Along);
                }

                // The upright markers come off the same laid trail as the chevrons, so they
                // cannot churn on their own - but say so, because a pool that showed four and then
                // two would read as a flicker whatever the chevrons did.
                Assert.That(actions.Count,
                    Is.LessThanOrEqualTo(CityDesign.GuideActionMarkerCount));

                // And they are the *only* marker on their spot. This assertion used to be its own
                // inverse - it required a ground chevron under every upright marker - which is what
                // stood a three-metre post with an arrowhead on top through the middle of a flat
                // chevron lying on the floor. Two solids sharing an origin is a z-fight, and a
                // z-fight is what flickers as the camera turns.
                foreach (Breadcrumb action in actions)
                {
                    Assert.That(HoldsAlong(chevrons, action.Along), Is.False,
                        $"{id}: a ground chevron is being drawn inside the upright marker at " +
                        $"{action.Along:F1} m, so the two pools are fighting over the same pixels.");

                    foreach (Breadcrumb chevron in chevrons)
                    {
                        Assert.That((chevron.Position - action.Position).magnitude,
                            Is.GreaterThanOrEqualTo(CityDesign.GuideMarkerClearGap - 0.001f),
                            $"{id}: a chevron stands {(chevron.Position - action.Position).magnitude:F2} m " +
                            $"from the upright marker at {action.Along:F1} m.");
                    }
                }
            }

            Assert.That(frame, Is.GreaterThan(500), $"{id}: too short a run to mean anything.");
            Assert.That(trail.Searches, Is.LessThanOrEqualTo(3),
                $"{id}: running the route with a metre of wander and 1.8 m of hop searched the " +
                $"city {trail.Searches} time(s) over {frame} frames.");
            Assert.That(droppedFromTheMiddle, Is.Zero,
                $"{id}: a chevron the player had not yet reached stopped being drawn " +
                $"{droppedFromTheMiddle} time(s) over {frame} frames.");
            Assert.That(appearedBehindTheEnd, Is.Zero,
                $"{id}: a chevron appeared behind the far end of the trail " +
                $"{appearedBehindTheEnd} time(s), so the trail moved under the player.");
        }
    }

    /// <summary>
    /// Searching again for a route the guide is already drawing changes nothing at all.
    ///
    /// The last defence, and the one that makes the rest cheap to be wrong about: however a search
    /// comes to be run, if it finds the same route then the arc position and every marker stay
    /// exactly as they were. There is no frame on which a redundant search can be seen.
    /// </summary>
    [Test]
    public void Guidance_ARedundantSearchChangesNothing()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                graph.Nodes[to].Position);

            RouteTrail trail = new RouteTrail(graph, Budget);
            Vector3 at = PointAlong(line, 40f);

            // Settle, then make it search again by pointing it somewhere else and straight back.
            trail.Step(at, id, to, graph.Nodes[to].Position);

            float along = trail.Along;
            List<Vector3> drawn = new List<Vector3>();

            foreach (Breadcrumb crumb in trail.Crumbs)
            {
                drawn.Add(crumb.Position);
            }

            Assert.That(drawn, Is.Not.Empty);

            int searches = trail.Searches;

            for (int frame = 0; frame < 30; frame++)
            {
                trail.Step(at, id, to, graph.Nodes[to].Position);
            }

            Assert.That(trail.Searches, Is.EqualTo(searches),
                $"{id}: standing still searched again.");
            Assert.That(trail.Along, Is.EqualTo(along).Within(0.0001f),
                $"{id}: the arc position moved for a player who did not.");
            Assert.That(trail.Crumbs.Count, Is.EqualTo(drawn.Count));

            for (int i = 0; i < drawn.Count; i++)
            {
                Assert.That((trail.Crumbs[i].Position - drawn[i]).magnitude,
                    Is.LessThan(0.0001f), $"{id}: chevron {i} moved while the player stood still.");
            }
        }
    }

    /// <summary>
    /// A player who has arrived stops the guidance dead.
    ///
    /// Standing on the objective is not a moment, it is the rest of the run - the tracker keeps
    /// pointing there until the relay is captured, and a player lining one up can stand on it for a
    /// while. The end of a route is where both of the old rebuild rules degenerated: the trail
    /// could not grow, so "is the trail long enough" was false on every frame, and the route had
    /// run out, so "have I reached the end with somewhere still to go" wanted a fresh Dijkstra.
    /// Between them that was a search and a marker lay-out sixty times a second, for ever.
    ///
    /// One of each, and nothing drawn, because there is nothing left to say.
    /// </summary>
    [Test]
    public void Guidance_StopsWorkingEntirelyOnceThePlayerIsStandingOnTheObjective()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            Vector3 destination = graph.Nodes[to].Position;
            RouteTrail trail = new RouteTrail(graph, Budget);

            List<Breadcrumb> chevrons = new List<Breadcrumb>();
            List<Breadcrumb> actions = new List<Breadcrumb>();

            for (int frame = 0; frame < 600; frame++)
            {
                float y = destination.y + (frame % 2 == 0 ? 0.02f : -0.02f);

                trail.Step(new Vector3(destination.x, y, destination.z), id, to, destination);
                trail.Visible(CityDesign.GuideMarkerCount, CityDesign.GuideActionMarkerCount,
                    chevrons, actions);

                Assert.That(chevrons, Is.Empty,
                    $"{id}: a chevron is being drawn on frame {frame} for a player standing on " +
                    "the objective it leads to.");
                Assert.That(actions, Is.Empty);
            }

            Assert.That(trail.Searches, Is.EqualTo(1),
                $"{id}: standing on the objective searched the city {trail.Searches} time(s).");
            Assert.That(trail.Lays, Is.EqualTo(1),
                $"{id}: standing on the objective laid the markers out {trail.Lays} time(s).");
        }
    }

    /// <summary>
    /// No two markers stand on the same patch of ground.
    ///
    /// A resampled chevron and the corner marker for the same turn used to be laid on top of each
    /// other - 47 of 862 across this city's routes, the closest 0.22 m apart. Two flat chevrons at
    /// the same height, on the same plane, pointing almost the same way is a z-fight, and a z-fight
    /// is two surfaces swapping which one is in front as the camera turns. That is a marker that
    /// flickers while the player stands perfectly still, which is the symptom this pass was chasing.
    /// </summary>
    [Test]
    public void Guidance_NeverLaysTwoMarkersOnTopOfEachOther()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        List<int> starts = new List<int> { graph.Nearest(CityDesign.SpawnPosition) };

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            starts.Add(graph.IndexOf(relay.Node));
        }

        int checkedCrumbs = 0;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);

            foreach (int from in starts)
            {
                List<int> path = graph.Path(from, to);

                if (path == null || from == to)
                {
                    continue;
                }

                List<NavMove> moves = new List<NavMove>();
                List<Vector3> line = graph.Waypoints(graph.Nodes[from].Position, path,
                    graph.Nodes[to].Position, moves);
                List<Breadcrumb> crumbs = CityNavigation.Breadcrumbs(line, moves, 64);

                for (int i = 1; i < crumbs.Count; i++)
                {
                    checkedCrumbs++;
                    float gap = (crumbs[i].Position - crumbs[i - 1].Position).magnitude;

                    Assert.That(gap,
                        Is.GreaterThanOrEqualTo(CityDesign.GuideMarkerClearGap - 0.001f),
                        $"{id} from {graph.Nodes[from].Name}: markers {i - 1} and {i} are " +
                        $"{gap:F2} m apart, inside the {CityDesign.GuideMarkerClearGap:F2} m at " +
                        "which two chevrons start sharing pixels.");
                }
            }
        }

        Assert.That(checkedCrumbs, Is.GreaterThan(400),
            "Too few markers measured for this to mean anything.");
    }

    /// <summary>
    /// Every marker has a heading, and it is the route's rather than the pool's.
    ///
    /// A chevron lies flat and can only say which way round it is. A leg that goes straight up a
    /// fire escape has no horizontal direction to give one, and the answer used to be "whatever
    /// rotation the pool object drawing it was last left in" - which is a different answer on
    /// different frames for the same spot on the ground, because which slot draws which marker
    /// shifts every time the player runs past one.
    /// </summary>
    [Test]
    public void Guidance_EveryMarkerHasAHeadingTakenFromTheRoute()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<NavMove> moves = new List<NavMove>();
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                graph.Nodes[to].Position, moves);

            foreach (Breadcrumb crumb in CityNavigation.Breadcrumbs(line, moves, 64))
            {
                Assert.That(crumb.Forward.y, Is.EqualTo(0f).Within(0.0001f),
                    $"{id}: a chevron at {crumb.Along:F0} m is tilted out of the ground plane.");
                Assert.That(crumb.Forward.magnitude, Is.EqualTo(1f).Within(0.001f),
                    $"{id}: the chevron at {crumb.Along:F0} m has no heading at all, so it would " +
                    "point whichever way the pool object drawing it was last left facing.");
            }
        }
    }

    // ------------------------------------------------------------------ guidance: which way round

    /// <summary>
    /// The point of the arrow is at the front.
    ///
    /// Every cyan chevron in the city pointed <b>exactly backwards</b> along the player's route,
    /// for as long as the feature existed, and every test passed the whole time. They could: the
    /// route was right, <see cref="Breadcrumb.Forward"/> was right, and `RouteGuide` aimed the
    /// marker's local +Z along it correctly. What was wrong was a metre of local offset inside the
    /// builder - the two arms were centred *behind* the origin and splayed outwards in front of it,
    /// so they met at -Z and opened towards +Z. That is an arrowhead aimed at where the player has
    /// just come from, and nothing in the code it was built from could tell.
    ///
    /// So this is asserted where it can be: the arms are data now, and the point they meet at is
    /// arithmetic.
    /// </summary>
    [Test]
    public void Chevron_ArmsMeetInFrontOfTheMarkerAndOpenOutBehindIt()
    {
        float s = CityDesign.GuideMarkerSize;

        Vector3 leftTip = GuideChevron.ArmTip(s, -1f, 0f);
        Vector3 rightTip = GuideChevron.ArmTip(s, 1f, 0f);
        Vector3 leftTail = GuideChevron.ArmTail(s, -1f, 0f);
        Vector3 rightTail = GuideChevron.ArmTail(s, 1f, 0f);

        Assert.That(GuideChevron.ArmForward, Is.GreaterThan(0f),
            "The arms have to be pushed forward of the origin, or they converge behind it.");

        Assert.That(leftTip.z, Is.GreaterThan(0f),
            $"The arms meet at z = {leftTip.z:F2}, which is behind the marker's origin: the " +
            "chevron is an arrowhead pointing backwards.");
        Assert.That(rightTip.z, Is.EqualTo(leftTip.z).Within(0.0001f),
            "The two arms have to meet each other.");

        float met = Mathf.Abs(rightTip.x - leftTip.x);
        float opened = Mathf.Abs(rightTail.x - leftTail.x);

        Assert.That(met, Is.LessThan(opened * 0.35f),
            $"The arms are {met:F2} m apart at z = {leftTip.z:F2} and {opened:F2} m apart at " +
            $"z = {leftTail.z:F2}, which is not a point.");
        Assert.That(leftTip.z, Is.GreaterThan(leftTail.z),
            "The end the arms meet at has to be the forward end.");
    }

    /// <summary>
    /// And the point ends up downstream: on every marker of every route, the visible arrowhead sits
    /// between the route point it stands on and the next one.
    ///
    /// The claim the player made and the one they can check by looking. Asserted against the same
    /// basis `RouteGuide` builds with `Quaternion.LookRotation(crumb.Forward, Vector3.up)`, over
    /// every chevron of every route from the spawn and from every relay - the plain resampled ones,
    /// the corner ones, the transition ones the upright markers stand on, and the last one before
    /// the objective.
    /// </summary>
    [Test]
    public void Chevron_PointsFromEachRoutePointTowardsTheNextOne()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        float s = CityDesign.GuideMarkerSize;

        List<int> starts = new List<int> { graph.Nearest(CityDesign.SpawnPosition) };

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            starts.Add(graph.IndexOf(relay.Node));
        }

        int corners = 0;
        int transitions = 0;
        int finals = 0;
        int total = 0;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);

            foreach (int from in starts)
            {
                List<int> path = graph.Path(from, to);

                if (path == null || from == to)
                {
                    continue;
                }

                List<NavMove> moves = new List<NavMove>();
                List<Vector3> line = graph.Waypoints(graph.Nodes[from].Position, path,
                    graph.Nodes[to].Position, moves);
                List<Breadcrumb> crumbs = CityNavigation.Breadcrumbs(line, moves, 64);

                for (int i = 0; i < crumbs.Count; i++)
                {
                    Breadcrumb crumb = crumbs[i];
                    total++;

                    // Where the point of the arrow actually lands, once the marker is standing on
                    // this crumb and aimed the way RouteGuide aims it.
                    Vector3 apex = GuideChevron.Apex(crumb.Position, crumb.Forward, s);
                    Vector3 shown = apex - crumb.Position;
                    shown.y = 0f;

                    Assert.That(shown.magnitude, Is.GreaterThan(0.1f),
                        $"{id}: the chevron at {crumb.Along:F0} m has no visible point.");

                    // The direction the player is next asked to move: this route point to the one
                    // after it, taken from the polyline rather than from the marker.
                    Vector3 onward = NextRoutePoint(line, crumb.Position) - crumb.Position;
                    onward.y = 0f;

                    if (onward.magnitude < 0.1f)
                    {
                        continue;
                    }

                    float agreement = Vector3.Dot(shown.normalized, onward.normalized);

                    Assert.That(agreement, Is.GreaterThan(0.9f),
                        $"{id} from {graph.Nodes[from].Name}: the chevron at " +
                        $"{crumb.Along:F0} m ({crumb.Move}) shows its point towards " +
                        $"{shown.normalized} while the route goes {onward.normalized} " +
                        $"(agreement {agreement:F2}; -1 is exactly backwards).");

                    if (crumb.IsTransition)
                    {
                        corners++;

                        if (crumb.Move != NavMove.Walk)
                        {
                            transitions++;
                        }
                    }

                    if (i == crumbs.Count - 1)
                    {
                        finals++;
                    }
                }
            }
        }

        // Every kind of marker the player can see is in the sample above, not merely the easy ones.
        Assert.That(total, Is.GreaterThan(400), "Too few chevrons measured.");
        Assert.That(corners, Is.GreaterThan(20), "No corner chevrons were measured.");
        Assert.That(transitions, Is.GreaterThan(10),
            "No transition chevrons - the ones the upright markers stand on - were measured.");
        Assert.That(finals, Is.GreaterThan(5), "No final chevrons were measured.");
    }

    /// <summary>
    /// The arithmetic above is the engine's arithmetic.
    ///
    /// <see cref="GuideChevron.Apex"/> writes out the basis `Quaternion.LookRotation` builds so the
    /// orientation can be asserted without a scene. If the two ever disagreed, every test that
    /// rests on the first one would be measuring something the player never sees.
    /// </summary>
    [Test]
    public void Chevron_TheApexArithmeticAgreesWithLookRotation()
    {
        float s = CityDesign.GuideMarkerSize;
        Vector3 at = new Vector3(12f, 25.2f, -37f);

        for (int degrees = 0; degrees < 360; degrees += 13)
        {
            float radians = degrees * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));

            Vector3 engine = at + Quaternion.LookRotation(forward, Vector3.up)
                * new Vector3(0f, 0f, GuideChevron.ArmTip(s, 1f, 0f).z);

            Assert.That((GuideChevron.Apex(at, forward, s) - engine).magnitude,
                Is.LessThan(0.001f),
                $"At {degrees} degrees the written-out basis and LookRotation disagree.");
        }
    }

    // ------------------------------------------------------------------ persistent route state

    /// <summary>
    /// Two searches that agree about a stretch of route put their markers in the same places on it.
    ///
    /// This is the fault that was left, and it is the one a player on a rooftop sees. A marker's
    /// place on the route was measured from the start of the route, and the start of the route is
    /// the node the player is standing on - so stepping across a boundary between two roof nodes
    /// re-anchored every chevron in the city, including the ones two hundred metres away on a
    /// stretch both searches completely agreed about. Measured over this city's twenty pairs of
    /// routes that share a tail, 114 of 116 markers on the shared part moved.
    ///
    /// The fix is in <see cref="CityNavigation"/>: markers are resampled at whole spacings measured
    /// <b>backwards from the objective</b>, so a shared stretch is the same distance from the end in
    /// both routes and therefore gets the same markers. This is the test that says so, and it is
    /// deliberately about the marker geometry rather than about a frame count: a re-search that
    /// changes nothing visible is a re-search nobody has to prevent.
    /// </summary>
    [Test]
    public void Guidance_TwoRoutesThatShareAStretchLayTheSameMarkersOnIt()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        int pairs = 0;
        int matched = 0;
        int moved = 0;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<int> starts = new List<int> { graph.Nearest(CityDesign.SpawnPosition) };

            foreach (RelayObjective relay in plan.Objectives.Relays)
            {
                int node = graph.IndexOf(relay.Node);

                if (node >= 0 && node != to)
                {
                    starts.Add(node);
                }
            }

            for (int a = 0; a < starts.Count; a++)
            {
                for (int b = a + 1; b < starts.Count; b++)
                {
                    List<NavMove> movesA = new List<NavMove>();
                    List<NavMove> movesB = new List<NavMove>();
                    List<int> pathA = graph.Path(starts[a], to);
                    List<int> pathB = graph.Path(starts[b], to);

                    if (pathA == null || pathB == null)
                    {
                        continue;
                    }

                    Vector3 destination = graph.Nodes[to].Position;
                    List<Vector3> lineA = graph.Waypoints(graph.Nodes[starts[a]].Position, pathA,
                        destination, movesA);
                    List<Vector3> lineB = graph.Waypoints(graph.Nodes[starts[b]].Position, pathB,
                        destination, movesB);

                    // How much of the two routes is literally the same line, walked back from the
                    // objective. Only that part is being claimed about: where the routes differ the
                    // markers are supposed to differ.
                    int shared = 0;

                    while (shared < lineA.Count && shared < lineB.Count
                           && (lineA[lineA.Count - 1 - shared]
                               - lineB[lineB.Count - 1 - shared]).sqrMagnitude < 0.01f)
                    {
                        shared++;
                    }

                    if (shared < 3)
                    {
                        continue;
                    }

                    pairs++;

                    List<Breadcrumb> crumbsA = new List<Breadcrumb>();
                    List<Breadcrumb> crumbsB = new List<Breadcrumb>();
                    CityNavigation.LayRoute(lineA, movesA, crumbsA);
                    CityNavigation.LayRoute(lineB, movesB, crumbsB);

                    // How long the shared part is, measured back from the objective. A marker is on
                    // it if it is nearer the end than that - which is exact, where "is this point
                    // geometrically on that line" is not: a route that comes back down a street it
                    // already ran would answer yes for a marker on the outward leg.
                    float tail = 0f;

                    for (int i = lineA.Count - shared; i < lineA.Count - 1; i++)
                    {
                        tail += (lineA[i + 1] - lineA[i]).magnitude;
                    }

                    foreach (Breadcrumb crumb in crumbsA)
                    {
                        if (crumb.Remaining > tail - 0.001f)
                        {
                            continue;
                        }

                        bool found = false;

                        foreach (Breadcrumb other in crumbsB)
                        {
                            if ((crumb.Position - other.Position).sqrMagnitude < 0.01f)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (found)
                        {
                            matched++;
                        }
                        else
                        {
                            moved++;
                            Assert.That(found, Is.True,
                                $"{id}: the marker at {crumb.Position} is on a stretch of route " +
                                $"that the search from {graph.Nodes[starts[a]].Name} and the one " +
                                $"from {graph.Nodes[starts[b]].Name} completely agree about, but " +
                                "only one of them draws it there.");
                        }
                    }
                }
            }
        }

        Assert.That(pairs, Is.GreaterThan(10),
            $"Only {pairs} pairs of routes share a tail, so this proves nothing.");
        Assert.That(matched, Is.GreaterThan(200),
            $"Only {matched} markers measured on shared route, so this proves nothing.");
        Assert.That(moved, Is.Zero);
    }

    /// <summary>
    /// A marker is laid backwards from the objective, so it is a whole number of spacings from it.
    ///
    /// The property the test above rests on, stated on its own so that a regression in the phase
    /// says which of the two it is.
    /// </summary>
    [Test]
    public void Guidance_EveryResampledMarkerIsAWholeSpacingFromTheObjective()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);
        int measured = 0;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            List<NavMove> moves = new List<NavMove>();
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                graph.Nodes[to].Position, moves);

            List<Breadcrumb> crumbs = new List<Breadcrumb>();
            CityNavigation.LayRoute(line, moves, crumbs);

            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            Assert.That(crumbs, Is.Not.Empty);

            foreach (Breadcrumb crumb in crumbs)
            {
                Assert.That(crumb.Remaining, Is.EqualTo(length - crumb.Along).Within(0.01f),
                    $"{id}: a marker's Remaining does not agree with its Along.");

                if (crumb.IsTransition)
                {
                    // A corner is kept wherever it falls, which is the whole point of it.
                    continue;
                }

                measured++;
                float spacings = crumb.Remaining / CityDesign.GuideBreadcrumbSpacing;

                Assert.That(Mathf.Abs(spacings - Mathf.Round(spacings)), Is.LessThan(0.001f),
                    $"{id}: a resampled marker is {crumb.Remaining:F2} m from the objective, " +
                    $"which is not a whole {CityDesign.GuideBreadcrumbSpacing:F0} m spacing - so " +
                    "a route found from somewhere else would put its markers elsewhere.");
            }
        }

        Assert.That(measured, Is.GreaterThan(100));
    }

    /// <summary>
    /// A pool object stays on the marker it is drawing, for as long as the trail draws it.
    ///
    /// The last of the flicker, and the only one that a still frame cannot show. The pool used to
    /// give slot i the i-th visible marker, so running past the nearest chevron shifted every marker
    /// behind it down a slot: over this 400 m run twenty-six live objects were teleported - up to
    /// 30 m - and re-aimed, some of them through 90 degrees, 513 times, while 36 markers genuinely
    /// came into view. Thirteen unnecessary discontinuous moves for every real one, sixty times a
    /// second, in front of the camera.
    ///
    /// The claim is exact rather than statistical: the number of times an object is put on a marker
    /// it was not already on must equal the number of markers that have entered the drawn window,
    /// and no more.
    /// </summary>
    [Test]
    public void Guidance_APoolObjectStaysOnTheMarkerItIsDrawing()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        int spawn = graph.Nearest(CityDesign.SpawnPosition);

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            Vector3 destination = graph.Nodes[to].Position;
            List<Vector3> line = graph.Waypoints(graph.Nodes[spawn].Position, graph.Path(spawn, to),
                destination);

            float length = 0f;

            for (int i = 0; i < line.Count - 1; i++)
            {
                length += (line[i + 1] - line[i]).magnitude;
            }

            RouteTrail trail = new RouteTrail(graph, Budget);
            GuideMarkerPool pool = new GuideMarkerPool(CityDesign.GuideMarkerCount);
            GuideMarkerPool uprights = new GuideMarkerPool(CityDesign.GuideActionMarkerCount);

            List<Breadcrumb> chevrons = new List<Breadcrumb>();
            List<Breadcrumb> actions = new List<Breadcrumb>();
            List<int> slots = new List<int>();
            List<bool> fresh = new List<bool>();
            List<int> release = new List<int>();

            // What each slot is standing on, as the view would have written it.
            Vector3[] standing = new Vector3[CityDesign.GuideMarkerCount];
            Vector3[] aimed = new Vector3[CityDesign.GuideMarkerCount];
            bool[] on = new bool[CityDesign.GuideMarkerCount];

            HashSet<long> showing = new HashSet<long>();
            int entered = 0;
            int moved = 0;
            int reaimed = 0;
            int frames = 0;

            for (float travelled = 0f; travelled < length; travelled += 8f / 60f)
            {
                Vector3 at = PointAlong(line, travelled);
                at.x += Mathf.Sin(frames * 0.19f);
                at.z += Mathf.Cos(frames * 0.23f);
                frames++;

                trail.Step(at, id, to, destination);
                trail.Visible(CityDesign.GuideMarkerCount, CityDesign.GuideActionMarkerCount,
                    chevrons, actions);

                // How many markers genuinely came into view this frame - the floor on how much
                // work the pool can possibly be asked to do.
                HashSet<long> now = new HashSet<long>();

                foreach (Breadcrumb crumb in chevrons)
                {
                    long key = GuideMarkerPool.Key(crumb);
                    now.Add(key);

                    if (!showing.Contains(key))
                    {
                        entered++;
                    }
                }

                showing = now;

                uprights.Bind(actions, slots, fresh, release);
                pool.Bind(chevrons, slots, fresh, release);

                foreach (int slot in release)
                {
                    on[slot] = false;
                }

                for (int i = 0; i < chevrons.Count; i++)
                {
                    Assert.That(slots[i], Is.GreaterThanOrEqualTo(0),
                        $"{id}: the pool had no object for a marker it is being asked to draw.");

                    int slot = slots[i];

                    if (on[slot])
                    {
                        if ((standing[slot] - chevrons[i].Position).magnitude > 0.001f)
                        {
                            moved++;
                        }

                        if (Vector3.Angle(aimed[slot], chevrons[i].Forward) > 0.01f)
                        {
                            reaimed++;
                        }
                    }

                    on[slot] = true;
                    standing[slot] = chevrons[i].Position;
                    aimed[slot] = chevrons[i].Forward;
                }
            }

            Assert.That(frames, Is.GreaterThan(500), $"{id}: too short a run to mean anything.");
            Assert.That(moved, Is.Zero,
                $"{id}: a pool object was moved to a different patch of ground {moved} time(s) " +
                "without being rebound, over a run in which nothing it was drawing had moved.");
            Assert.That(reaimed, Is.Zero,
                $"{id}: a pool object was turned to face a different way {reaimed} time(s) " +
                "while it was standing on the same marker.");
            Assert.That(pool.Rebinds, Is.EqualTo(entered),
                $"{id}: the pool put an object on a new marker {pool.Rebinds} time(s) over " +
                $"{frames} frames, against {entered} marker(s) that actually came into view.");
            Assert.That(pool.Toggles, Is.LessThanOrEqualTo(entered * 2),
                $"{id}: {pool.Toggles} enables and disables for {entered} markers.");
        }
    }

    /// <summary>
    /// A player who has arrived and is walking about on the objective does not search the city.
    ///
    /// `NeedsSearch` has a clause for "the route has run out and the objective is still somewhere
    /// else", which is right, and which a player standing twelve metres from the pad they are being
    /// sent to satisfies on every frame: 938 Dijkstras in 1200 frames, every one of them returning
    /// the route already being drawn. That is not visible, and it is a per-frame graph search in a
    /// component whose entire purpose is not to do one.
    ///
    /// It is fixed by arithmetic rather than by a timer: a search is a pure function of the node it
    /// starts from, the node it ends on and the objective's position, so a search with all three
    /// unchanged since the last one cannot say anything new.
    /// </summary>
    [Test]
    public void Guidance_DoesNotSearchWhileMillingAboutOnTheObjective()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;

        foreach (string id in TargetIds(nav))
        {
            int to = graph.IndexOf(nav.Targets[id]);
            Vector3 destination = graph.Nodes[to].Position;
            RouteTrail trail = new RouteTrail(graph, Budget);

            for (int frame = 0; frame < 1200; frame++)
            {
                Vector3 at = destination + new Vector3(Mathf.Sin(frame * 0.04f) * 12f, 1f,
                    Mathf.Cos(frame * 0.031f) * 12f);

                trail.Step(at, id, to, destination);
            }

            Assert.That(trail.Searches, Is.LessThanOrEqualTo(2),
                $"{id}: walking a twelve-metre circle round the objective searched the city " +
                $"{trail.Searches} time(s) in 1200 frames.");
            Assert.That(trail.Lays, Is.LessThanOrEqualTo(2),
                $"{id}: the same walk laid the markers out {trail.Lays} time(s).");
        }
    }

    /// <summary>
    /// Walking every square metre of a rooftop searches once and lays the markers out once.
    ///
    /// The other roof tests walk a circle or a line; this one walks the whole surface on a one-metre
    /// grid, out to two metres from every edge, which is 4988 positions on the largest Industrial
    /// roof. It is the strongest form of the claim the guide's stability rests on: the route is a
    /// property of the graph, and a roof is one node of it however big it is.
    /// </summary>
    [Test]
    public void Guidance_SweepingAWholeRoofSearchesOnceAndLaysOutOnce()
    {
        CityPlanResult plan = Plan;
        CityNavigation.Result nav = Nav(plan);
        CityNavGraph graph = nav.Graph;
        string id = null;

        foreach (string candidate in TargetIds(nav))
        {
            id = candidate;
            break;
        }

        int to = graph.IndexOf(nav.Targets[id]);
        Vector3 destination = graph.Nodes[to].Position;
        int swept = 0;

        foreach (RelayObjective relay in plan.Objectives.Relays)
        {
            int node = graph.IndexOf(relay.Node);

            if (node < 0 || node == to)
            {
                continue;
            }

            NavNode surface = graph.Nodes[node];
            RouteTrail trail = new RouteTrail(graph, Budget);
            int positions = 0;
            int flips = 0;
            int held = -2;

            for (float z = -surface.Extent.z + 1f; z <= surface.Extent.z - 1f; z += 1f)
            {
                for (float x = -surface.Extent.x + 1f; x <= surface.Extent.x - 1f; x += 1f)
                {
                    trail.Step(new Vector3(surface.Position.x + x, surface.Position.y + 1f,
                        surface.Position.z + z), id, to, destination);

                    positions++;

                    if (held != -2 && trail.StandingOn != held)
                    {
                        flips++;
                    }

                    held = trail.StandingOn;
                }
            }

            Assert.That(positions, Is.GreaterThan(300),
                $"{relay.Node} is too small a roof for this to prove anything.");
            Assert.That(flips, Is.Zero,
                $"{relay.Node}: the node under the player changed {flips} time(s) while they " +
                "walked one roof.");
            Assert.That(trail.Searches, Is.EqualTo(1),
                $"{relay.Node}: sweeping the whole roof searched the city {trail.Searches} " +
                $"time(s) over {positions} positions.");
            Assert.That(trail.Lays, Is.EqualTo(1),
                $"{relay.Node}: sweeping the whole roof laid the markers out {trail.Lays} time(s).");

            swept++;
        }

        Assert.That(swept, Is.GreaterThan(2));
    }

    /// <summary>
    /// The pool frees the slots it is about to need before it hands any out.
    ///
    /// A pool of twenty-six drawing twenty-six markers has nothing spare, so a frame in which one
    /// marker leaves the window and another enters it can only be served if the leaving one is
    /// released first. Allocating first leaves the new marker undrawn for a frame - which is the far
    /// end of the trail blinking off and on again, once every seven metres, for the whole run.
    /// </summary>
    [Test]
    public void Guidance_ThePoolReleasesBeforeItAllocates()
    {
        List<Breadcrumb> wanted = new List<Breadcrumb>();
        GuideMarkerPool pool = new GuideMarkerPool(4);
        List<int> slots = new List<int>();
        List<bool> fresh = new List<bool>();
        List<int> release = new List<int>();

        for (int i = 0; i < 4; i++)
        {
            wanted.Add(new Breadcrumb(new Vector3(i * 10f, 0f, 0f), Vector3.forward, i * 10f,
                NavMove.Walk, false, 100f - i * 10f));
        }

        pool.Bind(wanted, slots, fresh, release);

        Assert.That(pool.Rebinds, Is.EqualTo(4));
        Assert.That(release, Is.Empty);

        for (int step = 0; step < 20; step++)
        {
            // The window slides by one: the nearest marker goes, one more arrives at the far end.
            wanted.RemoveAt(0);
            wanted.Add(new Breadcrumb(new Vector3((step + 4) * 10f, 0f, 0f), Vector3.forward,
                (step + 4) * 10f, NavMove.Walk, false, 0f));

            int before = pool.Rebinds;
            pool.Bind(wanted, slots, fresh, release);

            Assert.That(release.Count, Is.EqualTo(1),
                $"Step {step}: {release.Count} slot(s) released for one marker leaving the window.");
            Assert.That(pool.Rebinds - before, Is.EqualTo(1),
                $"Step {step}: {pool.Rebinds - before} object(s) put on a new marker for one " +
                "marker entering the window.");

            for (int i = 0; i < wanted.Count; i++)
            {
                Assert.That(slots[i], Is.GreaterThanOrEqualTo(0),
                    $"Step {step}: marker {i} was left undrawn, so it blinks.");
            }

            int freshCount = 0;

            foreach (bool f in fresh)
            {
                if (f)
                {
                    freshCount++;
                }
            }

            Assert.That(freshCount, Is.EqualTo(1),
                $"Step {step}: {freshCount} marker(s) had to be moved and re-aimed.");
        }

        // Two markers can never share a slot, and a slot can never hold two markers.
        HashSet<int> distinct = new HashSet<int>(slots);
        Assert.That(distinct.Count, Is.EqualTo(slots.Count));
    }

    // ------------------------------------------------------------------ guidance helpers

    /// <summary>The trail's marker budget, as `RouteGuide` sizes it: the pool plus a spare pool.</summary>
    private static int Budget => CityDesign.GuideMarkerCount + CityDesign.GuideMarkerCount;

    private static bool HoldsAlong(List<Breadcrumb> crumbs, float along)
    {
        foreach (Breadcrumb crumb in crumbs)
        {
            if (Mathf.Abs(crumb.Along - along) < 0.001f)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How far along a polyline a point sits, for ordering what is drawn.</summary>
    private static float Along(List<Vector3> line, Vector3 point)
    {
        float travelled = 0f;
        float best = 0f;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < line.Count - 1; i++)
        {
            Vector3 from = line[i];
            Vector3 step = line[i + 1] - from;
            float length = step.magnitude;

            if (length < 0.001f)
            {
                continue;
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - from, step) / (length * length));
            float distance = (from + step * t - point).sqrMagnitude;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = travelled + length * t;
            }

            travelled += length;
        }

        return best;
    }

    /// <summary>The next vertex of the route after a marker, which is where it has to be pointing.</summary>
    private static Vector3 NextRoutePoint(List<Vector3> line, Vector3 from)
    {
        float at = Along(line, from);
        float travelled = 0f;

        for (int i = 0; i < line.Count - 1; i++)
        {
            float length = (line[i + 1] - line[i]).magnitude;
            travelled += length;

            if (travelled > at + 0.05f)
            {
                return line[i + 1];
            }
        }

        return line[line.Count - 1];
    }
}
