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
/// that phase's exit criterion: does every step of every fire escape, riser, link stair and bridge
/// measure at the tier it was declared at, and is every relay reachable from at least three
/// separate ways in off the street. Both are answered by measuring, not by asserting - which is
/// why a report with a single FAIL in it means Phase 6C is not done.
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
    /// Every step of every stack, measured.
    ///
    /// The rule is not "these look climbable": an ascent step is one mantle, a mantle is ORANGE,
    /// and so no step anywhere in the city may measure harder than ORANGE. A step that grades RED
    /// is a stack whose ledges have drifted apart, and one that grades Unreachable is a ladder with
    /// a rung missing - which is exactly what happened to the Old Quarter's risers the first time
    /// they were authored on the wrong facade.
    /// </summary>
    private static int CheckAscents(StringBuilder sb, CityPlanResult plan)
    {
        int fail = 0;
        int steps = 0;
        System.Collections.Generic.Dictionary<RouteTier, int> histogram =
            new System.Collections.Generic.Dictionary<RouteTier, int>();

        sb.AppendLine("ASCENTS (fire escapes, scaffolds, risers, link stairs)");
        sb.AppendLine("  ascent                              kind          from      to  steps  " +
                      "rise/step  worst");

        foreach (AscentPlan ascent in plan.Traversal.Ascents)
        {
            if (ascent.IsRamped)
            {
                continue;
            }

            int problems = 0;
            int index = 0;

            foreach (AscentStep step in ascent.Steps())
            {
                RouteTier tier = step.Tier;
                histogram.TryGetValue(tier, out int count);
                histogram[tier] = count + 1;
                steps++;

                if (tier > RouteTier.Orange)
                {
                    problems++;
                    sb.AppendLine($"  *** {ascent.Name} step {index}: gap {step.Gap:F2} m, rise " +
                                  $"{step.Rise:F2} m, landing {step.LandingDepth:F2} m grades " +
                                  $"{tier} - a mantle is ORANGE");
                }

                index++;
            }

            if (ascent.StepRise > CityDesign.AscentStepRise + 0.001f)
            {
                problems++;
                sb.AppendLine($"  *** {ascent.Name}: {ascent.StepRise:F2} m per step is past the " +
                              $"{CityDesign.AscentStepRise:F2} m mantle step");
            }

            fail += problems;
            sb.AppendLine($"  {ascent.Name,-35} {ascent.Kind,-12} {ascent.BaseY,7:F2} " +
                          $"{ascent.TopY,7:F2} {ascent.StepCount,6} {ascent.StepRise,10:F2}  " +
                          $"{RoofGraph.WorstStep(ascent),-8}" +
                          (problems == 0 ? string.Empty : "*** see above"));
        }

        sb.AppendLine();
        sb.AppendLine($"  steps measured           {steps,7}");

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

    /// <summary>
    /// The one ascent that is walked rather than mantled. It is graded on its pitch, and on the two
    /// joints at the top: the spiral's corner landings sit diagonally off the shaft's corner and
    /// meet its roof at a single point, so the summit slab that fills that corner has to exist and
    /// has to share a real edge with both.
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
