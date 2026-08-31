# Phase 6 — Skybound City

Working notes for the third level. Written down because the plan previously existed only in chat
transcripts, which made it expensive to pick up again.

Level 1 (`IndustrialParkour`) and Level 2 (`UIWorldDemo`, shown as NEON DISTRICT) are training
maps and are **not modified by any Phase 6 work**. Skybound City is a new, separate scene.

## Roadmap

| Phase | Scope | Exit criteria | Status |
|---|---|---|---|
| 6A | Inspection and city design | Design report | done |
| 6A.5 | Parkour movement foundation: slide, vault, mantle, wall run, wall jump, frame-rate-independent integrator | 61/61 EditMode tests pass | done |
| 6B | Greybox: `CityKit` + `SkyboundCityBuilder`, all districts as massing, streets, avenues, plaza. `CityRouteHarness` + `RouteTierValidator` built first. | Player can walk the whole 600 × 600 m at street level; massing silhouette reads; harness runs. | done |
| 6C | Traversal: rooftops, fire escapes, skybridges, crane, scaffolding, all 6 inter-district links, every jump tiered | `RouteTierValidator` reports 0 FAIL; every relay reachable by ≥ 3 routes | done |
| 6D | Objectives: set-based `CheckpointManager`, `ObjectiveRelay`, `ObjectiveTracker`, `RespawnAnchor`, `FallImpactDetector`, `ObjectiveCompass`, tower unlock | Mission completable in any relay order | done |
| **6E** | **Environment art: `CityDressing` - facade depth, windows, cornices, rooftop machinery, props, district colour zoning, signage, street furniture, dressed traversal, backdrop ring, dusk lighting, mission HUD. Plus `CityNavigation` / `RouteGuide` world-space route guidance.** | **Reads as architecture, not boxes. 3006 decorative pieces, 0 new colliders, 3465 / 3800 renderers. Every objective routable from all 13 ways up, no leg through a building.** | **done** |
| 6F | Integration: `Level03_SkyboundCity` LevelEntry, build settings, `LevelInfo`, and a menu that presents it as the main run rather than the third of three maps | PLAY launches Skybound City; the two older maps are grouped under TRAINING | **LevelEntry + build settings + menu done**; in-level pause / countdown / death / complete panels still to do |
| 6G | Optimisation: occlusion bake, lighting bake, collider audit, profiling | ≥ 60 fps at 1080p; budgets met | not started |

## The three Phase 6A.5 corrections to the 6A design

All applied in `CityDesign`, each marked in a comment, each guarded by a test.

| # | Was | Now | Why |
|---|---|---|---|
| 1 | Roof cluster tolerance +1.2 m | **+2.0 m** | The mantle makes a 2.0 m step directly climbable, so rooflines can vary instead of being flattened. |
| 2 | Main avenues 12 m | **14 m** | A one-storey drop clears 13.63 m. A 12 m avenue would be crossable from any building one floor taller than its neighbour. |
| 3 | Storey height 3.5 m | **3.6 m** | The airborne mantle raised the absolute climb ceiling to 3.30 m. 3.5 m left only 0.20 m, and Phase 6E's per-floor cornices would have become a ladder. |

## Phase 6C — the traversal layer

Phase 6B's massing is deliberately unclimbable: a storey is taller than the mantle ceiling and an
avenue is wider than a drop-assisted sprint jump, so without designed geometry the roofs are
scenery. 6C is that geometry, and it is authored as data in `CityTraversal` rather than as boxes.

Four primitives, and nothing else:

| | What it is | Grades |
|---|---|---|
| **Ascent** | A stack of ledges 1.8 m apart on a facade — a fire escape from the street, a scaffold on the construction site, a riser between two roof plateaus, or the stair a link needs at its taller end. | Every step is one mantle, so ORANGE |
| **Link** | A deck across an avenue, sitting at the *lower* of the two roofs so one end is always flush and only the other needs a stair. 3.2 m wide, which is above the GREEN landing minimum. | GREEN |
| **Crane** | The same thing above *both* roofs, only as wide as a BLUE landing, climbed to from each side. | BLUE |
| **Spiral** | The way up the shaft. 79.8 m of mantles would be 45 steps, so it is eight walked runs at 21° with a landing at each corner instead. | GREEN |

**The six inter-district links** are the set that touches all six district groups: the City Center
is the hub with four spokes (Residential, Industrial, Corporate, tower), and the Old Quarter — the
one group the hub does not border — is tied in twice, to the Residential block and to the tower.
Ten further links are intra-district and are not decoration: Corporate lots are `Isolated` and sit
39 m apart, the Cut splits the Old Quarter's roofs into halves 36 m apart, and an avenue runs
through the middle of both the Residential and the Industrial districts.

Two pieces exist purely to make a joint work, and both are guarded by a test:

* **Podium wings.** Without them the bridges from the City Center and the Old Quarter would span
  59 m and 50 m to reach the podium face, against 14 m for every other crossing in the city. The
  wings carry the podium roof out to the edge of the landmark superblock instead.
* **The spiral's summit slab.** Every corner landing sits diagonally off the shaft's corner, so the
  last one meets the shaft roof at a single *point*. `CityRect.GapTo` reports that as zero — a free
  step — which is why `CityRect.SharedEdgeWith` exists and why the summit slab fills the corner.

### How the exit criteria are met

`RoofGraph` turns the finished plan into a **directed** graph — reachability is not symmetric, and
height has to be earned — with exactly three kinds of edge: a jump between two roofs graded by the
tier table, stepping on or off a deck it is flush with, and climbing or descending an ascent. Drops
past `CityDesign.SafeDropHeight` are excluded, so the redundancy this phase proves survives 6D
adding `FallImpactDetector`.

* **0 FAIL.** `RouteTierValidator` gained five sections: link geometry, every step of every ascent,
  the spiral, the rooftop routes, and relay access. 267 ascent steps are measured — 265 ORANGE,
  two BLUE, none harder.
* **≥ 3 routes per relay.** A *route* is a distinct way in off the pavement — one fire escape or
  scaffold — from which the relay is reachable in the directed graph. Counting bridges instead
  would let a district claim three ways in that all start at the same stairwell. There are 13 ways
  in, all five relays and the summit are reachable from every one of them, and no surface anywhere
  in the city is stranded.

Rooftop routes are authored as *ends only*: a way in and a relay. The path between them is whatever
`RoofGraph` finds, easiest-first rather than shortest — the fewest-move path between two Industrial
roofs is a 9.9 m corner-to-corner diagonal that grades RED, when two BLUE hops along the street
front get there just as well. What is authored is the tier, and no rooftop route may measure harder
than ORANGE, because every ascent in the city is a mantle.

## Phase 6D — the mission

Phase 6C ended with five relay *sites* and a summit nothing could unlock. 6D is the mission that
makes them mean something: capture five relays in any order, which opens the tower, then climb it.

Four pieces, and one invariant:

| | What it is |
|---|---|
| **Relay** | A trigger on a district's chosen roof, with a plinth and a two-storey mast so it reads from the avenue below. Also a respawn anchor: the strongest one the mission has. |
| **Anchor** | A trigger at the top of every one of the 13 ways in off the street. A death costs the last climb, never the whole mission. |
| **Gate** | A 7.2 m hoarding across the foot of the tower spiral, removed when the set is complete. |
| **Finish** | A volume on the shaft roof, gated on the whole relay set as well as on the gate. |

**The invariant: Phase 6D adds nothing solid to the city except the gate.** Every marker it emits is
decoration with its collider destroyed, and every volume is a trigger. That is what lets the Phase
6B walkability flood fill, the Phase 6C tier measurements and the route harness's surface probes all
still read exactly what they read before — the harnesses ignore triggers, and a plinth a player
cannot stand on cannot change what the roof under it measures. The test named
`Objectives_AddNothingSolidToTheCityExceptTheTowerGate` is that claim, asserted.

### Order-freedom is a property of the city, not of the code

Nothing in `CityObjectives`, `ObjectiveTracker` or `ObjectiveRelay` says which relay is first: the
route is a set, and `CheckpointManager` grew a `CheckpointRouteOrder.Set` mode for it (Levels 1 and
2 stay `Sequential`, which is the default, so neither scene changes). But a set of five objectives
is only order-free if the *city* lets a player go from any one of them to any other, which is a
reachability question — so `RoofGraph.BuildWithStreet` adds the pavement as a node (climbing down a
fire escape and walking two blocks is a route; Phase 6C excluded the street because its question was
how many *separate* ways up there are), and all 120 orderings are walked across it.

### Falling now costs something

`CityDesign.FatalFallHeight` is **derived**, not chosen: `SafeDropHeight + StoreyHeight` = 14.4 m.
Phase 6C refused to count a descent of more than three storeys as a connection precisely so that its
redundancy would survive this, and the test asserts the two in that order. One subtlety the first
draft got wrong: a *stair* is not a fall. The Center-Industrial link stair descends 21.6 m and the
player takes it 1.8 m at a time, so the rule is applied to ascent edges by their step and to
everything else by its whole drop.

`FallImpactDetector` measures from the apex of the fall, not the ledge — a player who jumps up off a
roof has fallen from the top of the jump — and in metres rather than landing speed, because every
other dimension in the city is in metres. The controller's own fall reset moved one storey below the
death plane so it is the backstop it was always meant to be rather than a race with `FallDetector`.

### What the scene now carries

The builder stands up the same run systems the other two levels use — `GameManager`, `RunTimer`,
`CheckpointManager`, `RespawnManager`, `FallDetector` — plus `FallImpactDetector` and
`ObjectiveTracker`, and a mission HUD that is the compass, the relay count and the tower's state and
nothing else. The pause, countdown, death and level-complete panels are Phase 6F's, built by
`GameplayUIBuilder` against the very same components.

## Phase 6E — the environment art

Phase 6D finished a city that played correctly and looked like a greybox, because that is what it
was. 6E is the art pass, authored the same way every layer before it was: `CityDressing` is a pure
function of the finished plan, every dimension it uses is in `CityDesign`, and `SkyboundCityBuilder`
instantiates exactly what it is handed.

**The invariant, and it is stronger than Phase 6D's: Phase 6E adds nothing collidable to the city at
all.** Not "nothing except one gate" — nothing. Every piece goes into a fifth plan list,
`CityPlanResult.Details`, which the builder can only turn into `CityKit.Deco`, and which
`ColliderCount` does not count because there is nothing to count. That is what lets an art pass of
3006 objects land on a city whose traversal has already been measured without re-measuring any of
it: the Phase 6B walkability fill, the Phase 6C tier tables, the Phase 6D route probes and all 120
mission orderings are arithmetic over the massing and the traversal layer, and this phase touches
neither. `Dressing_AddsNothingSolidToTheCityAtAll` asserts it structurally rather than by
inspection, so it holds for art nobody has written yet.

### The layer, by group

| Group | What it is | Pieces |
|---|---|---|
| `DETAIL_FACADES` | Plinth, cornice, floor bands, glazing, corner piers | 1427 |
| `DETAIL_TRAVERSAL` | Handrails, ledge soles, bridge trusses and posts, the crane's cab and ties, the spiral's rails, and the route strip | 606 |
| `DETAIL_ROOFS` | Water tanks, industrial stacks, antenna masts, air handling units, vent cowls, stair bulkheads | 510 |
| `DETAIL_SIGNS` | Blade signs on avenue-facing facades, lit crowns on anything over ten storeys | 148 |
| `DETAIL_STREET` | Kerbs, centre lines, street lamps, the plaza's pylons and planters, the Cut's hazard edge, the perimeter wall | 132 |
| `BACKDROP` | Four rings of unreachable skyline, and the ground under them | 108 |
| `DETAIL_TOWER` | Shaft fins, podium banding and glazing, lit bands, the crown, the beacon, the wings | 34 |
| `DETAIL_OBJECTIVES` | A halo on every pad, a dish and a beacon on every relay mast | 33 |
| `TOWER_GATE_DETAIL` | Chevrons and warning beacons on the hoarding | 8 |

All nine are **siblings** of the massing groups, never children of them. That is load bearing: the
Phase 6B massing report measures a district's height band from the bounds of every renderer under
that district's transform, so a plinth parented to `ResidentialNorth` would report the district as
reaching 1.4 m and fail the band check. The gate's group is the one exception and is nested *inside*
`TOWER_GATE` on purpose — `ObjectiveTracker` opens the tower by deactivating that transform, and
chevrons anywhere else would be left hanging in the air over an opened spiral.

That nesting cost one rule in Harness E, and it is worth writing down because the rule was wrong
rather than the geometry. `ObjectiveValidator` checked the gate by counting the child transforms of
`TOWER_GATE` and expecting **2** — a stand-in for "both walls are in the scene" that held only while
nothing else was ever parented there. The dressing group made it 3 and the harness failed a scene
that was correct. It now counts **colliders** under the gate against the number of collidable
`Gate` blocks the plan holds, which is the thing the rule always meant, and adds a second rule the
old count could not have asked at all: everything Phase 6E hung on the gate must carry no collider,
because a solid chevron at the foot of the spiral would be a ledge in the one place in the city
whose whole purpose is to have no way past it.

### Two rules did most of the work

Neither is a style choice; both are answers to a problem the greybox did not have.

**The dead-edge rule.** A rooftop prop with no collider cannot change what a roof measures, but a
player running through an air-conditioning unit still looks wrong. So plant only stands on roof
edges *nothing in the game can arrive at or leave from*: an edge is live if any surface sitting at
or above `roof − SafeDropHeight` lies within `DeadEdgeReach` = 13 m of it, which is above the flat
sprint reach of 10.39 m with margin. 54 roofs are dressed and 54 are left bare, and the bare ones
are bare because a runner uses them. 64 further props were planned and dropped for standing too
close to a relay pad, an anchor, a bridge deck or an ascent.

**The party-wall rule.** A Residential superblock is five lots by five separated by 4 m alleys, so
nine of its twenty-five buildings have neighbours on all four sides. A facade 4 m from the wall
opposite cannot be read from anywhere, and glazing it costs four boxes for nothing. Facades with
less than 5 m of clear air get one window bay and no recessed panel; open facades get the full
grammar. Real dense fabric leaves those walls blank, so this does too — and it is what brought the
layer from 4256 pieces back to 3006, against a Phase 6A ceiling of 3800 for the whole city.

### District colour zoning

`CityDesign.Palette` is a six-entry table: massing, trim, panel, glass, one saturated accent, and
whether the district glazes in continuous bands or punched bays. The massing materials come from it
too, so a district cannot end up wearing a cornice that does not belong to it.

| District | Fabric | Accent | Glazing |
|---|---|---|---|
| City Center | Cool poured concrete | Cyan | Curtain wall |
| Residential | Warm brick | Amber | Punched bays |
| Industrial | Olive steel | Hazard lime | Punched bays |
| Corporate | Blue glass | Electric blue | Curtain wall |
| Old Quarter | Red masonry | Magenta | Punched bays |
| The tower | Pale precast | Cold white-cyan | Curtain wall |

The Phase 6B greybox separated its districts by *value*, which fog erases at exactly the distance
the information is wanted. `Dressing_GivesEveryDistrictAnAccentNoOtherDistrictUses` asserts hue
separation instead, which survives it. The landmark is deliberately outside that set: it is not a
district, carries no relay, and its accent is the City Center's cyan washed almost to white, because
the tower belongs to the objective colour rather than to a district — so it is held apart on
saturation instead.

The one city-wide, district-blind colour is the **route strip**: the cyan line down every bridge
deck, every spiral run, the foot of every ascent and around every relay pad. It is the same cyan
`ObjectiveRelay` uses for an uncaptured relay and the same cyan the HUD is drawn in, so "cyan means
go there" is one rule across the geometry, the objectives and the interface.

### Lighting

The single cheapest change in the phase and close to the most effective. The greybox lit the city
from 46°, which put much the same light on every face of every box and is most of why a city of
boxes read as boxes. `SunPitch` is now **24°**, which throws a facade's shadow across the avenue in
front of it, separates the four sides of every building and picks the cornices and floor bands out
as lines rather than as tone. Three further changes come with it: a procedural sky tinted to the
same dusk as the fog, dusk trilight ambient, and `FogEnd` raised from 450 m to **700 m** so the
backdrop ring reads through it instead of being erased.

Realtime lights go from one to **two** — a warm shadow-casting key and a cool unshadowed fill —
against a budget of ten. Every lit window, sign, beacon, lamp head and route strip in the city is
emissive geometry, not a light.

The scene also gets its own `Volume` and its own profile at `Assets/City/SkyboundCity_PostFX.asset`
(bloom, neutral tonemapping, a little contrast and saturation, a light vignette), and the camera
gets `renderPostProcessing` turned on, because URP leaves it off. Both are deliberately scene-local:
the project's shared `DefaultVolumeProfile` carries a Bloom override with its intensity set to
**zero**, which is right for Levels 1 and 2 and is not something this phase is allowed to change —
but without an override every emissive surface 6E added would render as a flat bright box and none
of them would read as a light. The bloom threshold sits at 1.05, above white: `EnsureEmissive`
drives emission past 1.0 and nothing else in the city reaches it, so a lit sign glows and a pale
cornice in full sun does not.

### The mission HUD

Phase 6D's HUD was four centred labels stacked down the middle of the screen over a plain square:
correct, and unmistakably a debug readout. 6E rebuilt the presentation of the same five numbers and
changed none of them. It is now one instrument panel drawn to `UITheme` — the same fills, borders,
type scale, tracking and three-family font set as the pause menu, the level-complete panel and the
main menu — with a ticked compass dial, an `OBJECTIVE` eyebrow over the target name, the distance in
mono, a divided counter block, and five relay chips that fill left to right. The chips *count*, they
do not name: the mission is a set, and showing which five had been taken would imply an order the
level does not have.

`ObjectiveCompass` grew one optional serialized field for the chips and nothing else. Every decision
still belongs to `ObjectiveTracker`.

`SkyboundCity.unity` is now on `UIRebuildAll.AllUIScenes`, so `Tools ▸ Parkour UI ▸ Audit UI
Typography` covers the mission HUD. It is audited but deliberately **not** rebuilt: the HUD is not a
`GameplayUI` root, it is built by `SkyboundCityBuilder` along with the city, and running
`GameplayUIBuilder` against it would drop a second HUD into a level that does not have one yet.

### Route guidance

The compass answers "where is it". In a 600 x 600 m city of solid blocks that is not enough on its
own: a bearing straight through a superblock tells the player to go somewhere they cannot go and
gives them no idea which way round it. `CityNavigation` answers the other question — which way to
run — and `RouteGuide` draws the answer on the ground as a trail of cyan chevrons.

The graph is built out of the three things the city already has, and derives nothing new:

| Layer | Nodes | Where it comes from |
|---|---|---|
| Street corridors | 140 | The four avenue centrelines, the perimeter ring and the two plaza ring streets, as lines, intersected into a lattice and stepped every 40 m |
| Ways up | 13 | One pavement node at the foot of each street ascent, joined to the corridor by the shortest connector that does not cross a building |
| Rooftops | 128 | `RoofGraph.Build` unchanged — same edges, same tier grading, same refusal to count a drop the fall rule would kill for |

294 nodes and 949 links in total. The search is Dijkstra weighted by `CityNavigation.TierWeight`,
so a long stroll and a short awkward hop are genuinely comparable — which is the difference between
this and `RoofGraph.Path`, whose easiest-first breadth-first search answers a validator's question
rather than a player's.

Two details do most of the work:

* **Every link carries an `Exit` point.** A node's position is the middle of a roof, but a player
  does not run to the middle of a roof and teleport — they run to the edge facing where they are
  going and jump from there. The trail is drawn through those exits, which is why it bends round
  corners instead of cutting them.
* **Breadcrumbs keep every corner.** Points are laid at 7 m — about one a second at sprint — but a
  turn is always marked whatever the spacing says, because a turn the player cannot see coming is a
  turn they miss. Even resampling alone slides the marker past the junction.

The trail stops at 170 m: past that it is not guidance, it is clutter in front of the thing the
player is trying to look at. A consequence worth naming rather than treating as a bug — two
objectives that lie in the same direction share the near end of their trails, because at 170 m out
there is genuinely nothing yet to tell them apart. `Guidance_ChangingTheObjectiveChangesTheTrail`
compares the whole route for that reason, and separately checks that most pairs do differ inside the
visible range.

Over the destination stands a 120 m pillar in the objective cyan, tall enough to clear the tower,
which is the tallest thing that can ever stand between a player and a relay. The Phase 6E relay
mast, halo and beacon are unchanged and still flip to green on capture.

`RouteGuide` owns no mission state — it reads `ObjectiveTracker.TryGetTarget` and nothing else, so a
guide that failed would cost directions and nothing more. It instantiates nothing: 26 chevrons and
one beacon are a fixed pool the builder creates, and a search happens when the objective changes or
the player has moved 9 m, not per frame. The pool lives under a `ROUTE_GUIDE` root **outside**
`WORLD`, because WORLD is the city — everything under it is static, batched, occluded and counted by
the Phase 6A budgets, and all four of those are wrong for objects that move every frame. So the
guide sits beside the HUD canvas rather than inside the city, and Harness B still measures the city
rather than the interface.

Harness E gained a `ROUTE GUIDANCE` section that measures it: the route to every objective from the
spawn with its length against the crow-flies distance, whether all 13 ways up reach all 6 objectives
(78 pairs), whether any street leg of any of those routes crosses a building, and — separately —
whether the graph *baked into the open scene* matches the plan's and can still route to everything.
A guide wired to an empty array draws nothing and reports no error, which is exactly the failure
that has to be loud.

#### Making it hold still

The first version of the guidance recomputed everything it drew from the player's position every
frame, and it flickered. Four separate causes, each measured before it was fixed:

| Cause | Measured | Fix |
|---|---|---|
| **The objective chattered.** `ObjectiveTracker` points at the nearest uncaptured relay, evaluated fresh each frame. 113 of 5041 sampled street positions sit within 3 m of the line where two are equidistant, and pacing that line switched the target **63 times**. Every switch is a new HUD label *and* a full re-search *and* a completely different trail. | 63 switches → **0** | `ObjectiveFocus.Choose`: the same rule, applied with 20 m of stickiness. Lifted out of the tracker as a pure function so it can be measured without a scene. |
| **The snap chattered.** The node under the player was scored by distance to its *centre*. A Corporate roof is 55 m across, so near its edge the middle of the next building is genuinely closer — the guide decided the player was over there. A serpentine walk flipped **52 times** on one roof. | 52 flips → **0** | `NavNode.Extent`: score against the surface's footprint, not its centre. Anywhere on a roof scores zero for that roof. Plus `NearestStable`, which keeps its answer unless something beats it by 6 m. |
| **Height jitter moved the score.** A grounded `CharacterController` integrates gravity every frame, so its transform breathes by a centimetre or two standing still. Weighted linearly at ×6 that was 0.1 of score per frame — enough to re-pick a node where two were close. | — | `GuideSurfaceBand`: height inside 2 m costs nothing at all. |
| **The trail slid, and the visible set oscillated.** Markers were resampled from the player's position — so every search moved all 26 at once by up to a spacing — and which of them showed was a distance test with a hard 2.83 m threshold, so a player hovering on that radius shifted the whole trail one step *per frame*. | 0 drift, 0 stationary changes | Markers are laid at fixed arc lengths from the start of the **route**; the visible window is a projection of the player onto that same arc, searched inside a ±30 m window and never allowed to go backwards. |

Two smaller ones came with them: a marker's bob was keyed to its slot in the pool rather than to its
place on the route, so a marker retiring made every other one pop; and the last chevron took its
facing from the player, so it swivelled when the camera turned. Both now come from the route.

The search itself is no longer triggered by distance travelled. Running *along* the route is the
normal case and must cost nothing, so a re-search happens when the objective changes, when there is
no route, when the player is more than 9 m from the route, or when they reach the end of what is
drawn — and that last one is suppressed while they are standing on the objective, or arriving would
mean a Dijkstra per frame for a trail with nothing left in it.

#### Saying what to do, not just which way

An arrow pointing at a wall is not guidance. Every link carries a `NavMove` — walk, climb, descend,
jump, cross — read back from `RoofGraph`'s own `Via` string rather than re-derived, so the guide
cannot disagree with the traversal layer about what a move is. A breadcrumb inherits the move that
gets the player off the end of the segment it stands on, and a route vertex that is not a plain walk
**always** gets a marker, whatever the spacing rule says. Four upright amber posts stand on the next
four of those: at the foot of the fire escape, at the mouth of the skybridge, on the roof edge the
jump leaves from.

`Tools ▸ Skybound City ▸ E` prints the five legs of the mission in English — every surface, every
direction, every named fire escape and span, and the hardest move on each leg — because "the graph
connects them" and "a person could follow it" are different claims and only the second one is the
feature.

### The budget

| | Renderers |
|---|---|
| Massing, traversal and objectives (6B–6D) | 455 |
| Scaffold uprights | 4 |
| Phase 6E art | 3006 |
| **Total** | **3465** / 3800 |

Colliders are **unchanged at 450**, against a ceiling of 1100. Report B now itemises the art layer
by group with its collider count beside it, so "the decoration is decoration" is checked against the
built scene and not only against the plan — `CityDressing` promising it emits no colliders and
`SkyboundCityBuilder` actually emitting none are two different claims. Report B's ASCII skyline also
now skips renderers outside the core, or the 108 backdrop blocks would be clamped into its two edge
columns and turn the one picture in that report into two black bars.

### What Phase 6E deliberately did not do

* No pause, countdown, death or level-complete panels — 6F. **This is the one thing still missing
  from a Skybound City reached through PLAY: the level runs, but ESC has no pause panel, so there
  is no in-level route back to the menu.** `GameplayUIBuilder.Build()` against this scene would
  supply all four, and is deliberately not called yet because it would put a second HUD canvas over
  the mission readout `SkyboundCityBuilder` already builds.
* ~~No LevelEntry asset, build-settings registration or menu card~~ — done. `Level03_SkyboundCity`
  is in `Assets/Data`, `SkyboundCity.unity` is in Build Settings, the scene carries a `LevelInfo`
  pointing at that asset, and the main menu's PLAY screen is built from it.
* No occlusion or lighting bake, and no LODs — 6G. Everything emitted is marked static, so all
  three are available to it.
* No change to any collider, footprint, roof height, link, ascent, relay, anchor or trigger.

## Architecture

The design is data, and the tools consume it. Nothing hard-codes a dimension.

```
Assets/Scripts/City/            (runtime assembly - no UnityEditor, so tests can reach it)
  CityRect.cs                   footprint maths, named by the axes it uses
  TraversalEnvelope.cs          the ONE copy of the reach formulas
  RouteTier.cs                  GREEN/BLUE/ORANGE/RED table and the classifier
  CityDesign.cs                 every authored dimension: grid, streets, storeys, districts, 6C
  CityPlan.cs                   deterministic plan: what boxes exist, where, how tall
  CityTraversal.cs         6C   links, ascents, relays and rooftop routes; resolves and emits them
  RoofGraph.cs             6C   the rooftop network as a directed graph, and reachability
  CityObjectives.cs        6D   relays, respawn anchors, the tower gate, the summit finish, and
                                the 120-ordering completability check
  CityDressing.cs          6E   the art layer: facades, rooftops, signage, street furniture, the
                                dressed traversal layer and the backdrop ring. Emits into a fifth
                                plan list that cannot produce a collider
  CityNavigation.cs        6E   the navigable city: street corridors + ways up + RoofGraph, as one
                                weighted graph, with the shortest-path search, the arc-anchored
                                breadcrumb layout, the sticky objective rule (`ObjectiveFocus`) and
                                the plain-English route describer
  CityRoutes.cs                 named routes: street level walked, rooftop routes pathed

Assets/Scripts/Gameplay/        (the mission, as components)
  CheckpointManager.cs          Sequential or Set. 6D added the Set; the default is unchanged
  ObjectiveRelay.cs        6D   one relay's identity and its captured/uncaptured face
  ObjectiveTracker.cs      6D   the seam: which relay a counted crossing was, and the tower gate
  RespawnAnchor.cs         6D   a place a death returns to, reported through a static event
  FallImpactDetector.cs    6D   kills for a fall measured from its apex, in metres
  RespawnManager.cs             anchor first, then checkpoint, then LevelStart
  RouteGuide.cs            6E   the world-space trail: searches the baked nav graph when the
                                objective changes, drives a fixed pool of chevrons and one beacon

Assets/Scripts/UI/
  ObjectiveCompass.cs      6D   nearest uncaptured relay: bearing, distance, count, tower state
                           6E   plus the relay chips, which count rather than name

Assets/Scripts/Editor/City/
  CityKit.cs                    part factory; Solid keeps its collider, Deco destroys it
  SkyboundCityBuilder.cs        instantiates the plan into a scene, and wires the mission
  CityRouteHarness.cs           A walkability · B massing · C routes (street walked, roofs probed)
  RouteTierValidator.cs         D tier / avenue / cluster / link / ascent / relay rules
  ObjectiveValidator.cs    6D   E fall rule / relays / anchors / gate / 120 orderings / the scene

Assets/Scripts/Editor/SkyboundCityTests.cs     104 EditMode tests over the plan, the network, the
                                                art layer and the route guidance (67 before Phase
                                                6E; 16 for the art, 21 for the guidance, ten of
                                                those on its stability)
Assets/Scripts/Editor/ObjectiveSystemTests.cs   13 EditMode tests over the mission components
```

`CityTraversal` is called at the end of `CityPlan.Generate`, and puts its geometry into the same
four plan lists the massing uses. That is why the builder only had to learn four new piece kinds,
and why the collider budget, the determinism test and the massing report all cover 6C for free.

`CityDressing` is called after both of them and breaks that pattern on purpose: it emits into
`CityPlanResult.Details` rather than into the four geometry lists, because a `BlockPlan` may be
collidable and a `DetailPlan` may not. Keeping them in separate lists is what makes "the art pass
adds nothing solid" a property of the type system rather than a promise kept by review.

`CityPlan` is a pure function of `CityDesign` and a fixed seed (`xorshift32`, not `System.Random`,
whose sequence is not stable across runtimes). That is what lets the tests assert every dimensional
claim without opening a scene.

## Menu items

| Menu | Does |
|---|---|
| `Tools ▸ Skybound City ▸ Build Greybox` | Rebuilds `Assets/Scenes/SkyboundCity.unity` from scratch |
| `Tools ▸ Skybound City ▸ A - Validate Street Walkability` | Floods the street network from the spawn → `SceneBackups/city_walkability.txt` |
| `Tools ▸ Skybound City ▸ B - Massing Report` | Counts vs the 6A budgets, height bands, the Phase 6E art layer itemised by group with its collider count, skyline → `SceneBackups/city_massing.txt` |
| `Tools ▸ Skybound City ▸ C - Run Named Routes` | Walks the street routes with the real CharacterController and probes every surface the rooftop routes stand on → `SceneBackups/city_routes.txt` |
| `Tools ▸ Skybound City ▸ D - Validate Route Tiers` | Street grammar, avenue rule, cluster rule, links, ascent steps, the spiral, rooftop routes, relay access → `SceneBackups/city_tiers.txt` |
| `Tools ▸ Skybound City ▸ E - Validate Objectives` | The fall rule, the relays, the anchors, the tower gate, all 120 relay orderings, the route guidance (graph, routes from the spawn and from all 13 ways up, no leg through a building, and the graph baked into the scene), the five mission legs written out in English, and whether the open scene is actually wired up → `SceneBackups/city_objectives.txt` |

Rooftop routes are probed rather than walked. Their legs are mantles, bridges and jumps, and the
route runner has no input to press, so walking one would only prove that a player who never jumps
cannot climb a fire escape. What C proves about them — and what no amount of plan arithmetic can —
is that every surface the plan says they stand on is really in the scene, at the planned height,
with room to stand. Whether each move between those surfaces is inside the move set is D's job.

The builder is the single source of truth. Hand edits to the scene are destroyed on the next run.

## Verifying without the editor

The Unity Editor usually holds the project lock, so `-batchmode` is unavailable. The city code is
deliberately free of `UnityEditor` references, so it can be compiled and run against
`UnityEngine.CoreModule.dll` from a plain `dotnet` console project — including the NUnit tests,
via reflection over `[Test]` methods. `SceneBackups/city_plan_offline.txt`,
`SceneBackups/city_traversal_offline.txt` and `SceneBackups/city_objectives_offline.txt` are that
output — the last two are the plan-side halves of reports D and E, produced without the editor.

`SkyboundCityTests` compiles offline unchanged, so all 104 of its tests run that way — Phase 6E's
15 included, which is why `CityDressing` computes the tower spiral's rail offsets with explicit
trigonometry instead of `Quaternion.Euler`. That method is an engine ECall and throws outside Unity,
so one call to it would have taken the whole offline half of this project's verification with it.
`SceneBackups/city_dressing_offline.txt` is the Phase 6E report produced that way.

`ObjectiveSystemTests` does not compile offline: it builds components, which needs the real engine,
so those 13 are Unity-only.

Assembly compile check, which is what the project has always used:

```
dotnet build Assembly-CSharp.csproj -t:Rebuild
dotnet build Assembly-CSharp-Editor.csproj -t:Rebuild
```
