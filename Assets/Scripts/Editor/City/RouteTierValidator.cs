using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Asserts that every authored dimension in Skybound City still agrees with the movement the
/// player actually has.
///
/// This is the other half of the Phase 6A recommendation to build the validators before the
/// geometry. <see cref="CityRouteHarness"/> answers "can the player get there"; this answers "is
/// what we authored still what we think it is" - which is the question that goes stale silently
/// when a movement value is tuned six weeks later.
///
/// It reads the live controller when a city scene is open and falls back to
/// <see cref="TraversalEnvelope.Default"/> otherwise, so the design rules can be checked from a
/// cold project without building anything.
///
/// Phase 6B checked the *street grammar*: the tier of every street width, the avenue rule that
/// makes district boundaries mean something, the storey height against the climb ceiling, and the
/// roof cluster rule that decides which roofs are linkable at all.
///
/// Phase 6C added the layer that has authored jumps in it, and with it the two questions that are
/// that phase's exit criterion: does every jump and every stair or ramp satisfy its declared
/// traversal envelope, and is every relay reachable from at least three separate ways in off the
/// street. Both are answered by measuring, not by asserting - which is why a report with a single
/// FAIL in it means Phase 6C is not done.
/// </summary>
public static class RouteTierValidator
{
    [MenuItem("Tools/Skybound City/D - Validate Route Tiers", priority = 23)]
    public static void Validate()
    {
        TraversalEnvelope.Movement movement = ReadMovement(out string source);
        CityPlanResult plan = CityPlan.Generate();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("SKYBOUND CITY - ROUTE TIER VALIDATION");
        sb.AppendLine($"movement source: {source}");
        sb.AppendLine($"walk={movement.Walk} sprint={movement.Sprint} jump={movement.JumpHeight} " +
                      $"gravity={movement.Gravity} launch={movement.LaunchVelocity:F4}");
        sb.AppendLine();

        RoofGraph graph = RoofGraph.Build(plan);

        int fail = 0;
        fail += CheckClimbCeiling(sb, movement);
        fail += CheckStreetGrammar(sb, movement);
        fail += CheckRoofClusters(sb, plan);
        fail += CheckRoofHops(sb, movement, plan);
        fail += CheckTraversalResolution(sb, plan);
        fail += CheckLinks(sb, plan);
        fail += CheckAscents(sb, plan);
        fail += CheckTowerSpiral(sb, plan);
        fail += CheckRoutes(sb, plan);
        fail += CheckRoofRoutes(sb, graph);
        fail += CheckRelayAccess(sb, plan, graph);

        sb.AppendLine();
        sb.AppendLine(fail == 0
            ? "RESULT: every authored dimension agrees with the current movement envelope."
            : $"RESULT: {fail} rule violation(s).");

        CityRouteHarness.Write("city_tiers.txt", sb, fail);
    }

    // ------------------------------------------------------------------ rules

    /// <summary>
    /// A storey must be taller than the highest thing a player can climb unaided, or vertical gain
    /// stops being something the level designer controls.
    /// </summary>
    private static int CheckClimbCeiling(StringBuilder sb, in TraversalEnvelope.Movement movement)
    {
        float ceiling = TraversalEnvelope.MantleAssistedClimb(movement);
        float margin = CityDesign.StoreyHeight - ceiling;
        bool ok = margin > 0f;

        sb.AppendLine("CLIMB CEILING");
        sb.AppendLine($"  unassisted jump          {TraversalEnvelope.UnassistedClimb(movement),7:F2} m");
        sb.AppendLine($"  airborne mantle ceiling  {ceiling,7:F2} m");
        sb.AppendLine($"  storey height            {CityDesign.StoreyHeight,7:F2} m");
        sb.AppendLine($"  margin                   {margin,7:F2} m   " +
                      (ok ? "ok" : "*** a storey is climbable - vertical gain is no longer designed"));
        sb.AppendLine();
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Each street width has a job. Alleys are meant to be hopped, secondary streets to be jumped
    /// with a run-up, and avenues never to be crossed at roof level except as a deliberate
    /// high-to-low drop. The avenue rule is the load-bearing one: it is what turns the city into a
    /// route-finding problem instead of an open jumping field.
    /// </summary>
    private static int CheckStreetGrammar(StringBuilder sb, in TraversalEnvelope.Movement movement)
    {
        int fail = 0;

        sb.AppendLine("STREET GRAMMAR (flat roof-to-roof crossing)");
        sb.AppendLine("  street            width   flat tier     intended");

        fail += Street(sb, "alley", CityDesign.AlleyWidth, RouteTier.Green);
        fail += Street(sb, "secondary", CityDesign.SecondaryStreetWidth, RouteTier.Blue);
        fail += Street(sb, "avenue", CityDesign.AvenueWidth, RouteTier.Unreachable);
        fail += Street(sb, "plaza", CityDesign.PlazaSize, RouteTier.Unreachable);

        // The avenue must also survive the far side being one storey lower, which is the case the
        // Phase 6A.5 report showed a 12 m avenue losing.
        float dropGap = TraversalEnvelope.DropAssistedSprintGap(movement, CityDesign.StoreyHeight);
        float margin = CityDesign.AvenueWidth - dropGap;
        bool ok = margin > 0f;

        if (!ok)
        {
            fail++;
        }

        sb.AppendLine();
        sb.AppendLine("AVENUE RULE (drop of one storey onto the far side)");
        sb.AppendLine($"  drop-assisted design gap {dropGap,7:F3} m");
        sb.AppendLine($"  avenue width             {CityDesign.AvenueWidth,7:F3} m");
        sb.AppendLine($"  margin                   {margin,7:F3} m   " +
                      (ok ? "ok" : "*** avenues are crossable - district boundaries mean nothing"));
        sb.AppendLine();
        return fail;
    }

    private static int Street(StringBuilder sb, string name, float width, RouteTier intended)
    {
        // Landing depth is not what limits a street crossing - the far roof is always deep - so it
        // is set past every tier's minimum and only the gap and rise decide.
        RouteTier actual = RouteTiers.Classify(width, 0f, 99f);
        bool ok = actual == intended;
        sb.AppendLine($"  {name,-16} {width,6:F1}   {actual,-12}  {intended,-12} " +
                      (ok ? "ok" : "*** MISMATCH"));
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Roofs in one cluster must stay inside the tolerance, or the cluster is not a cluster and
    /// the rooftop network 6C is about to author on top of it has a hole in it.
    /// </summary>
    private static int CheckRoofClusters(StringBuilder sb, CityPlanResult plan)
    {
        Dictionary<int, List<BuildingPlan>> clusters = new Dictionary<int, List<BuildingPlan>>();

        foreach (BuildingPlan building in plan.Buildings)
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

        int fail = 0;
        float worst = 0f;
        string worstName = "-";

        foreach (KeyValuePair<int, List<BuildingPlan>> entry in clusters)
        {
            float lo = float.MaxValue;
            float hi = float.MinValue;

            foreach (BuildingPlan b in entry.Value)
            {
                lo = Mathf.Min(lo, b.RoofY);
                hi = Mathf.Max(hi, b.RoofY);
            }

            float spread = hi - lo;

            if (spread > worst)
            {
                worst = spread;
                worstName = $"{entry.Value[0].CellName} row {entry.Value[0].LotRow}";
            }

            if (spread > CityDesign.RoofClusterTolerance + 1e-3f)
            {
                fail++;
                sb.AppendLine($"  *** cluster {entry.Key} ({entry.Value[0].CellName}) spreads " +
                              $"{spread:F2} m, over the {CityDesign.RoofClusterTolerance:F1} m tolerance");
            }
        }

        sb.AppendLine("ROOF CLUSTERS");
        sb.AppendLine($"  clusters                 {clusters.Count,7}");
        sb.AppendLine($"  tolerance                {CityDesign.RoofClusterTolerance,7:F2} m");
        sb.AppendLine($"  worst spread             {worst,7:F2} m   ({worstName})");
        sb.AppendLine($"  violations               {fail,7}");
        sb.AppendLine();
        return fail;
    }

    /// <summary>
    /// Measures every hop between neighbouring roofs in a cluster. Phase 6B does not declare a
    /// tier per hop - that is 6C's job once the rooftop network exists - so the rule here is the
    /// weaker but still meaningful one: no hop inside a cluster may be unreachable, and any that
    /// grades RED must keep its slack.
    /// </summary>
    private static int CheckRoofHops(StringBuilder sb, in TraversalEnvelope.Movement movement,
        CityPlanResult plan)
    {
        Dictionary<int, List<BuildingPlan>> clusters = new Dictionary<int, List<BuildingPlan>>();

        foreach (BuildingPlan building in plan.Buildings)
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

        Dictionary<RouteTier, int> histogram = new Dictionary<RouteTier, int>();
        int fail = 0;
        int hops = 0;

        foreach (List<BuildingPlan> members in clusters.Values)
        {
            members.Sort((a, b) => a.LotColumn.CompareTo(b.LotColumn));

            for (int i = 0; i < members.Count - 1; i++)
            {
                BuildingPlan from = members[i];
                BuildingPlan to = members[i + 1];

                float gap = from.Footprint.GapTo(to.Footprint);
                float rise = to.RoofY - from.RoofY;
                float landing = Mathf.Min(to.Footprint.Width, to.Footprint.Depth);

                RouteTier tier = RouteTiers.Classify(gap, rise, landing);
                histogram.TryGetValue(tier, out int count);
                histogram[tier] = count + 1;
                hops++;

                if (tier == RouteTier.Unreachable)
                {
                    fail++;
                    sb.AppendLine($"  *** {from.Name} -> {to.Name} unreachable: " +
                                  $"gap {gap:F2} m, rise {rise:F2} m, landing {landing:F2} m");
                    continue;
                }

                // Slack is only meaningful for a plain jump. A hop that clears its rise by
                // mantling has no closed-form reach, and grading it on one would be nonsense.
                bool mantleAssisted = rise > RouteTiers.Spec(tier).MaxRise;

                if (tier != RouteTier.Red || mantleAssisted)
                {
                    continue;
                }

                float slack = RouteTiers.Slack(movement, tier, gap, rise);

                if (slack < RouteTiers.RedMinimumSlack)
                {
                    fail++;
                    sb.AppendLine($"  *** {from.Name} -> {to.Name} is RED with only {slack:F2} m " +
                                  $"slack (minimum {RouteTiers.RedMinimumSlack:F2} m)");
                }
            }
        }

        sb.AppendLine("ROOFTOP HOPS INSIDE CLUSTERS");
        sb.AppendLine($"  hops measured            {hops,7}");

        foreach (RouteTier tier in new[]
                 {
                     RouteTier.Green, RouteTier.Blue, RouteTier.Orange, RouteTier.Red,
                     RouteTier.Unreachable
                 })
        {
            histogram.TryGetValue(tier, out int count);
            sb.AppendLine($"  {tier,-24} {count,7}");
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ the traversal layer

    /// <summary>
    /// Every link, ascent and relay is authored as lot indices and resolved against the plan. A
    /// reference that no longer names a building - because the plaza or the Cut removed that lot,
    /// or because a district was re-subdivided - has to be loud, or the network silently loses a
    /// crossing and everything downstream still passes.
    /// </summary>
    private static int CheckTraversalResolution(StringBuilder sb, CityPlanResult plan)
    {
        CityTraversalResult traversal = plan.Traversal;

        sb.AppendLine("TRAVERSAL NETWORK");
        sb.AppendLine($"  links                    {traversal.Links.Count,7}   " +
                      $"(authored {CityTraversal.Links.Length})");
        sb.AppendLine($"  inter-district links     {traversal.InterDistrictLinkCount,7}   " +
                      "(Phase 6C asks for 6)");
        sb.AppendLine($"  ascents                  {traversal.Ascents.Count,7}");
        sb.AppendLine($"  street ways in           {Count(traversal.StreetAscents()),7}");
        sb.AppendLine($"  relay sites              {traversal.Relays.Count,7}   " +
                      $"(authored {CityTraversal.Relays.Length})");

        int fail = traversal.Problems.Count;

        foreach (string problem in traversal.Problems)
        {
            sb.AppendLine($"  *** {problem}");
        }

        if (traversal.InterDistrictLinkCount != 6)
        {
            fail++;
            sb.AppendLine("  *** Phase 6C authors exactly six inter-district links, one per pair " +
                          "that ties all six district groups together");
        }

        sb.AppendLine();
        return fail;
    }

    private static int Count<T>(System.Collections.Generic.IEnumerable<T> items)
    {
        int count = 0;

        foreach (T unused in items)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// A link has to do three things: land on enough of the far roof to be a landing rather than a
    /// corner, be flush with at least one of its two ends, and grade no harder than declared.
    /// </summary>
    private static int CheckLinks(StringBuilder sb, CityPlanResult plan)
    {
        CityTraversalResult traversal = plan.Traversal;
        int fail = 0;

        sb.AppendLine("LINKS");
        sb.AppendLine("  link                          kind        span  bearing   deckY  step   " +
                      "declared  stairs");

        foreach (LinkPlan link in traversal.Links)
        {
            RouteTier step = RouteTiers.Classify(0f, 0f, link.DeckWidth);
            int problems = 0;

            if (link.Bearing < CityDesign.SkybridgeMinBearing)
            {
                problems++;
                sb.AppendLine($"  *** {link.Name}: only {link.Bearing:F2} m of the two roofs face " +
                              $"each other, under the {CityDesign.SkybridgeMinBearing:F1} m minimum");
            }

            if (step > link.Tier)
            {
                problems++;
                sb.AppendLine($"  *** {link.Name}: a {link.DeckWidth:F1} m deck grades {step}, " +
                              $"declared {link.Tier}");
            }

            int flush = 0;

            foreach (string end in link.FlushEnds())
            {
                flush++;
                float shared = link.Deck.SharedEdgeWith(traversal.Surfaces[end].Footprint);

                if (shared < 1f)
                {
                    problems++;
                    sb.AppendLine($"  *** {link.Name}: the deck meets {end} over only " +
                                  $"{shared:F2} m - that is a corner, not a step");
                }
            }

            // A skybridge sits at the lower roof, so it is flush with one end and needs a stair at
            // the other. The crane clears both, so it is flush with neither and needs two.
            int expectedStairs = link.Kind == LinkKind.Crane ? 2 : (flush == 2 ? 0 : 1);

            if (link.Stairs.Count != expectedStairs)
            {
                problems++;
                sb.AppendLine($"  *** {link.Name}: {link.Stairs.Count} stair(s), expected " +
                              $"{expectedStairs} - one end cannot be climbed to");
            }

            fail += problems;
            sb.AppendLine($"  {link.Name,-28} {link.Kind,-10} {link.Span,6:F1} {link.Bearing,8:F1} " +
                          $"{link.DeckY,7:F2}  {step,-6} {link.Tier,-9} {link.Stairs.Count,5}" +
                          (problems == 0 ? string.Empty : "   *** see above"));
        }

        sb.AppendLine();
        return fail;
    }

    /// <summary>
    /// Every explicit flight of every normal stair, measured. A stair is traversable only when its
    /// risers, treads, clear width, landings, and joins all satisfy the authored walking envelope.
    /// </summary>
    private static int CheckAscents(StringBuilder sb, CityPlanResult plan)
    {
        int fail = 0;
        int steps = 0;
        int flights = 0;

        sb.AppendLine("ASCENTS (fire escapes, scaffolds, risers, link stairs)");
        sb.AppendLine("  ascent                              kind          from      to flights " +
                      "steps  riser  status");

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                continue;
            }

            int problems = 0;
            int plannedSteps = 0;
            float plannedRise = 0f;

            if (ascent.Style != AscentTraversalStyle.WalkableStair)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: normal ascents must use explicit " +
                              "WalkableStair flights");
            }

            if (ascent.Flights.Count == 0)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: no explicit stair flights were planned");
            }

            for (int i = 0; i < ascent.Flights.Count; i++)
            {
                StairFlightPlan flight = ascent.Flights[i];
                flights++;
                steps += flight.StepCount;
                plannedSteps += flight.StepCount;
                plannedRise += flight.Rise;

                problems += StairRule(sb, ascent.Name, i, "step count", flight.StepCount,
                    flight.StepCount > 0, "must be positive");
                problems += StairRule(sb, ascent.Name, i, "riser", flight.RiserHeight,
                    flight.RiserHeight > 0f
                    && flight.RiserHeight <= CityDesign.StairMaximumRiserHeight,
                    $"must be > 0 and <= {CityDesign.StairMaximumRiserHeight:F2} m");
                problems += StairRule(sb, ascent.Name, i, "tread", flight.TreadDepth,
                    flight.TreadDepth >= CityDesign.StairPreferredTreadDepth,
                    $"must be >= {CityDesign.StairPreferredTreadDepth:F2} m");
                problems += StairRule(sb, ascent.Name, i, "clear width", flight.ClearWidth,
                    flight.ClearWidth >= CityDesign.StairClearWidth,
                    $"must be >= {CityDesign.StairClearWidth:F2} m");
                problems += StairRule(sb, ascent.Name, i, "landing before",
                    flight.LandingBeforeDepth,
                    flight.LandingBeforeDepth >= CityDesign.StairTurnLandingDepth,
                    $"must be >= {CityDesign.StairTurnLandingDepth:F2} m");
                problems += StairRule(sb, ascent.Name, i, "landing after",
                    flight.LandingAfterDepth,
                    flight.LandingAfterDepth >= CityDesign.StairTurnLandingDepth,
                    $"must be >= {CityDesign.StairTurnLandingDepth:F2} m");

                if (i == 0)
                {
                    problems += StairRule(sb, ascent.Name, i, "start elevation", flight.Start.y,
                        Near(flight.Start.y, ascent.BaseY),
                        $"must equal ascent base {ascent.BaseY:F2} m");
                }
                else
                {
                    StairFlightPlan previous = ascent.Flights[i - 1];
                    problems += StairRule(sb, ascent.Name, i, "join elevation", flight.Start.y,
                        Near(flight.Start.y, previous.End.y),
                        $"must equal prior flight end {previous.End.y:F2} m");

                    if (!SameRect(flight.LandingBefore, previous.LandingAfter))
                    {
                        problems++;
                        sb.AppendLine($"  *** {ascent.Name} flight {i}: landing before does not " +
                                      "match the preceding flight's landing after");
                    }
                }
            }

            if (plannedSteps != ascent.StepCount)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: flights contain {plannedSteps} steps but " +
                              $"the ascent declares {ascent.StepCount}");
            }

            if (ascent.StepRise <= 0f
                || ascent.StepRise > CityDesign.StairMaximumRiserHeight)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: declared riser {ascent.StepRise:F5} m must " +
                              $"be > 0 and <= {CityDesign.StairMaximumRiserHeight:F2} m");
            }

            if (!Near(plannedRise, ascent.Rise))
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: flight rise totals {plannedRise:F2} m but " +
                              $"the ascent rises {ascent.Rise:F2} m");
            }

            if (ascent.Flights.Count > 0)
            {
                StairFlightPlan last = ascent.Flights[ascent.Flights.Count - 1];

                if (!Near(last.End.y, ascent.TopY)
                    || !Near(ascent.FinalLandingY, ascent.TopY)
                    || !SameRect(last.LandingAfter, ascent.FinalLanding))
                {
                    problems++;
                    sb.AppendLine($"  *** {ascent.Name}: final flight and landing do not reach " +
                                  $"the declared top at {ascent.TopY:F2} m");
                }
            }

            if (problems == 0 && RoofGraph.WorstStep(ascent) != RouteTier.Green)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: explicit stair geometry is not continuous");
            }

            fail += problems;
            sb.AppendLine($"  {ascent.Name,-35} {ascent.Kind,-12} {ascent.BaseY,7:F2} " +
                          $"{ascent.TopY,7:F2} {ascent.Flights.Count,7} " +
                          $"{ascent.StepCount,5} {ascent.StepRise,6:F2}  " +
                          $"{RoofGraph.WorstStep(ascent),-11}" +
                          (problems == 0 ? string.Empty : "*** see above"));
        }

        sb.AppendLine();
        sb.AppendLine($"  stair flights measured   {flights,7}");
        sb.AppendLine($"  visible steps measured   {steps,7}");
        sb.AppendLine();
        return fail;
    }

    private static int StairRule(StringBuilder sb, string ascent, int flight, string dimension,
        float value, bool ok, string requirement)
    {
        if (ok)
        {
            return 0;
        }

        sb.AppendLine($"  *** {ascent} flight {flight}: {dimension} {value:F5} {requirement}");
        return 1;
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) <= 0.0001f;

    private static bool SameRect(in CityRect a, in CityRect b)
        => Near(a.MinX, b.MinX)
           && Near(a.MaxX, b.MaxX)
           && Near(a.MinZ, b.MinZ)
           && Near(a.MaxZ, b.MaxZ);

    /// <summary>
    /// The sole ramped ascent. It is graded on its style and pitch, and on the two joints at the
    /// top: the spiral's corner landings sit diagonally off the shaft's corner and meet its roof at
    /// a single point, so the summit slab that fills that corner has to exist and has to share a
    /// real edge with both.
    /// </summary>
    private static int CheckTowerSpiral(StringBuilder sb, CityPlanResult plan)
    {
        AscentPlan spiral = null;

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.Kind == AscentKind.TowerSpiral)
            {
                spiral = ascent;
            }
        }

        sb.AppendLine("TOWER SPIRAL");

        if (spiral == null)
        {
            sb.AppendLine("  *** there is no way up the shaft");
            sb.AppendLine();
            return 1;
        }

        float toLanding = spiral.FinalLanding.SharedEdgeWith(spiral.SummitFootprint);
        float toShaft = spiral.SummitFootprint.SharedEdgeWith(spiral.TopFootprint);

        int fail = 0;
        fail += Rule(sb, "ramp style", (float)spiral.Style,
            spiral.Style == AscentTraversalStyle.Ramp);
        fail += Rule(sb, "runs", spiral.StepCount, spiral.StepCount > 0);
        fail += Rule(sb, "rise per run     m", spiral.StepRise, true);
        fail += Rule(sb, "pitch          deg", spiral.PitchDegrees,
            spiral.PitchDegrees <= CityDesign.TowerSpiralMaxPitch + 0.001f);
        fail += Rule(sb, "slope limit    deg", CityDesign.SlopeLimit,
            spiral.PitchDegrees < CityDesign.SlopeLimit);
        fail += Rule(sb, "landing to summit m", toLanding, toLanding > 1f);
        fail += Rule(sb, "summit to shaft   m", toShaft, toShaft > 1f);

        sb.AppendLine();
        return fail;
    }

    private static int Rule(StringBuilder sb, string label, float value, bool ok)
    {
        sb.AppendLine($"  {label,-24} {value,7:F2}   {(ok ? "ok" : "*** FAIL")}");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Street-level routes are walked, not jumped, so every consecutive pair of waypoints must be
    /// on paving the plan actually lays. A waypoint inside a building footprint is authored into
    /// a wall, and the route runner would only discover it after a minute of stepping physics.
    ///
    /// Rooftop routes are the opposite case - their waypoints are *supposed* to be over buildings,
    /// because they stand on them - so they are checked by <see cref="CheckRoofRoutes"/> instead.
    /// </summary>
    private static int CheckRoutes(StringBuilder sb, CityPlanResult plan)
    {
        int fail = 0;

        sb.AppendLine("STREET ROUTE DEFINITIONS");
        sb.AppendLine("  route                            tier    legs   length   waypoints");

        foreach (CityRoute route in CityRoutes.All)
        {
            if (!route.StreetLevel)
            {
                continue;
            }

            int inside = 0;

            foreach (Vector3 waypoint in route.Waypoints)
            {
                // Only street-level waypoints can be tested this way; the Cut's floor waypoints
                // are below the massing and cannot collide with it.
                if (waypoint.y < -1f)
                {
                    continue;
                }

                foreach (BuildingPlan building in plan.Buildings)
                {
                    if (building.Footprint.Contains(waypoint.x, waypoint.z))
                    {
                        inside++;
                        sb.AppendLine($"  *** {route.Name}: waypoint {waypoint} is inside " +
                                      $"{building.Name}");
                        break;
                    }
                }
            }

            if (route.Tier != RouteTier.Green)
            {
                inside++;
                sb.AppendLine($"  *** {route.Name} is street level but graded {route.Tier}; " +
                              "a walked route is always GREEN");
            }

            fail += inside;
            sb.AppendLine($"  {route.Name,-32} {route.Tier,-7} {route.Waypoints.Length - 1,4} " +
                          $"{route.TotalLength,8:F1} {route.Waypoints.Length,8}" +
                          (inside == 0 ? string.Empty : "   *** see above"));
        }

        sb.AppendLine();
        return fail;
    }

    /// <summary>
    /// A rooftop route is a chain of surfaces, so what is measured is the move between each pair,
    /// not the distance between two waypoints. Only the two ends and the tier are authored: the
    /// path itself is whatever the network offers, so this is the check that notices when the
    /// network stops offering one.
    /// </summary>
    private static int CheckRoofRoutes(StringBuilder sb, RoofGraph graph)
    {
        int fail = 0;

        sb.AppendLine("ROOFTOP ROUTE DEFINITIONS");
        sb.AppendLine("  route                            declared  hops   measured");

        foreach (CityRoute route in CityRoutes.All)
        {
            if (route.StreetLevel)
            {
                continue;
            }

            if (route.Nodes.Length < 2)
            {
                fail++;
                sb.AppendLine($"  *** {route.Name}: no route across the roofs connects its ends");
                continue;
            }

            RouteTier measured = graph.WorstTier(route.Nodes);
            bool ok = measured <= route.Tier;

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"  {route.Name,-32} {route.Tier,-9} {route.Nodes.Length - 1,4}   " +
                          $"{measured,-12}" + (ok ? string.Empty : "*** harder than declared"));
        }

        sb.AppendLine();
        return fail;
    }

    /// <summary>
    /// The Phase 6C exit criterion: every relay reachable by at least three routes.
    ///
    /// A "route" here is a distinct way in off the pavement - one fire escape or scaffold - from
    /// which the relay's roof can actually be reached in the directed network. Counting bridges
    /// instead would let a district claim three ways in that all start from the same stairwell,
    /// and counting undirected reachability would count a 40 m drop as a way up.
    /// </summary>
    private static int CheckRelayAccess(StringBuilder sb, CityPlanResult plan, RoofGraph graph)
    {
        const int minimum = 3;
        int fail = 0;

        sb.AppendLine("RELAY ACCESS");
        sb.AppendLine($"  ways in off the street: {graph.Entries.Count}, minimum per relay: {minimum}");
        sb.AppendLine("  relay                    host                          surface  routes");

        foreach (RelayPlan relay in plan.Traversal.Relays)
        {
            System.Collections.Generic.List<string> routes = graph.AccessRoutes(relay.Node);
            bool ok = routes.Count >= minimum;

            if (!ok)
            {
                fail++;
            }

            sb.AppendLine($"  {relay.Name,-24} {relay.Node,-28} {relay.SurfaceY,7:F2} " +
                          $"{routes.Count,7}" + (ok ? string.Empty : "   *** UNDER THE MINIMUM"));

            foreach (string route in routes)
            {
                sb.AppendLine($"      via {route}");
            }
        }

        System.Collections.Generic.List<string> summit =
            graph.AccessRoutes(CityTraversal.ShaftRoofNode);
        sb.AppendLine($"  {"the summit",-24} {CityTraversal.ShaftRoofNode,-28} " +
                      $"{CityDesign.TowerShaftTopY,7:F2} {summit.Count,7}");

        if (summit.Count == 0)
        {
            fail++;
            sb.AppendLine("  *** the tower cannot be climbed, so Phase 6D has nothing to unlock");
        }

        sb.AppendLine();
        return fail;
    }

    // ------------------------------------------------------------------ plumbing

    private static TraversalEnvelope.Movement ReadMovement(out string source)
    {
        BasicFirstPersonController move = Object.FindFirstObjectByType<BasicFirstPersonController>();

        if (move == null)
        {
            source = "CityDesign default (no player in the open scene)";
            return TraversalEnvelope.Default;
        }

        source = $"live controller on '{move.gameObject.name}'";
        return new TraversalEnvelope.Movement(move.WalkSpeed, move.SprintSpeed, move.JumpHeight,
            move.Gravity);
    }
}
