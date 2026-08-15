using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// Automated play-test harness for the parkour course. Not part of the shipped game: it is
/// spawned at runtime by the editor menu "Tools/Parkour/T - Play-test Route" and is never
/// saved into the scene.
///
/// It drives the real BasicFirstPersonController by injecting synthetic Input System keyboard
/// state, so every jump exercises the actual movement code, the actual CharacterController and
/// the actual scene colliders. It does not simulate physics itself.
/// </summary>
public sealed class ParkourAutoPilot : MonoBehaviour
{
    private static readonly string[] Route =
    {
        "Platform_Start", "Plat_S1", "Plat_S2", "Plat_S3", "Plat_S4",
        "Plat_S5", "Plat_S6", "Plat_S7", "Plat_S8_Pillar", "Deck_North",
        "Plat_B1", "Plat_B2", "Plat_B3",
        "Plat_B4", "Plat_B5", "Plat_B6", "Plat_B7_Beam", "Plat_B8_Ledge", "Plat_B9_Ledge", "Deck_South",
        "Plat_K1", "Plat_K2", "Plat_K3", "Plat_K4", "Plat_K5", "Plat_K6", "Plat_K7_TowerShelf",
        "Ledge_W1", "Ledge_W2", "Ledge_W3", "Ledge_N1", "Ledge_N2", "Ledge_N3", "Ledge_E1",
        "Finish_TowerCap"
    };

    // Legs that the design marks as sprint-required.
    private static readonly HashSet<string> SprintFrom = new HashSet<string>
    {
        "Plat_B1", "Plat_B2", "Plat_K1", "Plat_K2", "Plat_K3", "Plat_K4", "Plat_K5",
        "Plat_K7_TowerShelf"
    };

    private BasicFirstPersonController controller;
    private CharacterController cc;
    private Keyboard virtualKeyboard;
    private readonly StringBuilder log = new StringBuilder();
    private int failures;
    private int warnings;

    private void Start()
    {
        // Keep ticking even if the editor window loses focus, otherwise the run stalls.
        Application.runInBackground = true;

        // Force a deterministic 60 Hz step. Unfocused editors can render at an erratic rate, and a
        // large Time.deltaTime would let CharacterController.Move tunnel through thin platforms,
        // producing failures that are artefacts of the harness rather than the level.
        Time.captureDeltaTime = 1f / 60f;
        Debug.Log("[AutoPilot] started (deterministic 60 Hz)");

        controller = FindController();
        if (controller == null)
        {
            Debug.LogError("[AutoPilot] No BasicFirstPersonController found.");
            Finish();
            return;
        }

        cc = controller.GetComponent<CharacterController>();
        virtualKeyboard = InputSystem.AddDevice<Keyboard>("AutoPilotKeyboard");
        StartCoroutine(Run());
    }

    private static BasicFirstPersonController FindController()
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            BasicFirstPersonController c = root.GetComponentInChildren<BasicFirstPersonController>(true);
            if (c != null)
            {
                return c;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ input injection

    private void SetKeys(bool forward, bool sprint, bool jump)
    {
        List<Key> pressed = new List<Key>();
        if (forward) pressed.Add(Key.W);
        if (sprint) pressed.Add(Key.LeftShift);
        if (jump) pressed.Add(Key.Space);
        InputSystem.QueueStateEvent(virtualKeyboard, new KeyboardState(pressed.ToArray()));
    }

    // ------------------------------------------------------------------ geometry helpers

    private static Bounds? TopOf(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
        {
            return null;
        }

        Renderer r = go.GetComponent<Renderer>();
        return r != null ? r.bounds : (Bounds?)null;
    }

    /// <summary>Closest point on the top face of <paramref name="rect"/> to a world point.</summary>
    private static Vector3 ClosestOnTop(Bounds rect, Vector3 to)
    {
        return new Vector3(
            Mathf.Clamp(to.x, rect.min.x, rect.max.x),
            rect.max.y,
            Mathf.Clamp(to.z, rect.min.z, rect.max.z));
    }

    private static Vector3 Inset(Vector3 point, Bounds rect, float amount)
    {
        Vector3 centre = new Vector3(rect.center.x, rect.max.y, rect.center.z);
        Vector3 dir = centre - point;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f)
        {
            return point;
        }

        float move = Mathf.Min(amount, dir.magnitude);
        return point + dir.normalized * move;
    }

    private void Teleport(Vector3 feet)
    {
        cc.enabled = false;
        controller.transform.position = feet + Vector3.up * 0.06f;
        cc.enabled = true;
    }

    private string GroundUnderPlayer()
    {
        Vector3 origin = controller.transform.position + Vector3.up * 0.4f;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            return hit.collider.gameObject.name;
        }

        return "<none>";
    }

    // ------------------------------------------------------------------ main run

    private IEnumerator Run()
    {
        log.AppendLine("=== PARKOUR PLAY-TEST ===");
        log.AppendLine($"controller: walk/sprint/jumpHeight/gravity read from live component");
        log.AppendLine();

        yield return null;
        yield return null;

        yield return ClearanceSweep();
        yield return RespawnTest("initial spawn (no checkpoint crossed)", new Vector3(0f, 0.55f, -4.5f));
        yield return JumpTests();

        log.AppendLine();
        log.AppendLine($"=== RESULT: {failures} failure(s), {warnings} warning(s) ===");
        Finish();
    }

    // ------------------------------------------------------------------ clearance sweep

    /// <summary>
    /// Samples the player capsule across every walkable surface to find spots where the player
    /// would be blocked or embedded in geometry (obstructions, low overhangs, stuck pockets).
    /// </summary>
    private IEnumerator ClearanceSweep()
    {
        log.AppendLine("--- capsule clearance sweep (player radius 0.35, height 2.0) ---");
        int blockedTotal = 0;

        foreach (string name in Route)
        {
            Bounds? maybe = TopOf(name);
            if (maybe == null)
            {
                log.AppendLine($"  {name}: MISSING");
                failures++;
                continue;
            }

            Bounds b = maybe.Value;
            int blocked = 0;
            int samples = 0;
            List<string> blockers = new List<string>();

            // sample on a grid, staying 0.35 in from the edges (where the player can stand)
            float xLo = b.min.x + 0.35f;
            float xHi = b.max.x - 0.35f;
            float zLo = b.min.z + 0.35f;
            float zHi = b.max.z - 0.35f;
            int nx = Mathf.Max(1, Mathf.CeilToInt((xHi - xLo) / 0.6f) + 1);
            int nz = Mathf.Max(1, Mathf.CeilToInt((zHi - zLo) / 0.6f) + 1);

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float x = nx == 1 ? b.center.x : Mathf.Lerp(xLo, xHi, ix / (float)(nx - 1));
                    float z = nz == 1 ? b.center.z : Mathf.Lerp(zLo, zHi, iz / (float)(nz - 1));
                    Vector3 feet = new Vector3(x, b.max.y + 0.06f, z);
                    samples++;

                    // shrink slightly so touching the floor/wall flush is not a false positive
                    const float r = 0.33f;
                    Vector3 p1 = feet + Vector3.up * 0.37f;
                    Vector3 p2 = feet + Vector3.up * 1.63f;
                    Collider[] hits = Physics.OverlapCapsule(p1, p2, r, ~0, QueryTriggerInteraction.Ignore);

                    foreach (Collider c in hits)
                    {
                        if (c == cc)
                        {
                            continue;
                        }

                        blocked++;
                        if (!blockers.Contains(c.gameObject.name))
                        {
                            blockers.Add(c.gameObject.name);
                        }

                        break;
                    }
                }
            }

            if (blocked > 0)
            {
                blockedTotal += blocked;
                log.AppendLine($"  {name}: {blocked}/{samples} stance samples BLOCKED by [{string.Join(", ", blockers)}]");
                warnings++;
            }
        }

        log.AppendLine(blockedTotal == 0
            ? "  all walkable surfaces clear"
            : $"  {blockedTotal} blocked stance sample(s) - review");
        log.AppendLine();
        yield return null;
    }

    // ------------------------------------------------------------------ respawn test

    private IEnumerator RespawnTest(string label, Vector3 expected)
    {
        SetKeys(false, false, false);
        Vector3 shove = new Vector3(controller.transform.position.x, -20f, controller.transform.position.z);
        cc.enabled = false;
        controller.transform.position = shove;
        cc.enabled = true;

        // the controller's Update performs the reset when y < fallResetHeight
        for (int i = 0; i < 6; i++)
        {
            yield return null;
        }

        Vector3 got = controller.transform.position;
        float error = Vector3.Distance(got, expected);
        bool ok = error < 0.35f;
        log.AppendLine($"respawn [{label}]: expected {expected} got {got} error {error:F3} -> {(ok ? "OK" : "FAIL")}");
        if (!ok)
        {
            failures++;
        }

        yield return null;
    }

    // ------------------------------------------------------------------ jump tests

    private IEnumerator JumpTests()
    {
        log.AppendLine("--- jump tests (real controller, real colliders) ---");
        log.AppendLine("leg                                          speed   result");

        for (int i = 0; i < Route.Length - 1; i++)
        {
            string fromName = Route[i];
            string toName = Route[i + 1];

            Bounds? fromMaybe = TopOf(fromName);
            Bounds? toMaybe = TopOf(toName);
            if (fromMaybe == null || toMaybe == null)
            {
                log.AppendLine($"{fromName} -> {toName}: MISSING GEOMETRY");
                failures++;
                continue;
            }

            Bounds from = fromMaybe.Value;
            Bounds to = toMaybe.Value;

            Vector3 toCentre = new Vector3(to.center.x, to.max.y, to.center.z);
            Vector3 takeoff = Inset(ClosestOnTop(from, toCentre), from, 0.3f);
            Vector3 fromCentre = new Vector3(from.center.x, from.max.y, from.center.z);
            Vector3 landing = Inset(ClosestOnTop(to, fromCentre), to, 0.6f);

            bool sprint = SprintFrom.Contains(fromName);

            Teleport(takeoff);
            Vector3 face = landing - takeoff;
            face.y = 0f;
            if (face.sqrMagnitude > 1e-4f)
            {
                controller.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
            }

            // settle on the ground so isGrounded is true before jumping
            SetKeys(false, false, false);
            for (int f = 0; f < 5; f++)
            {
                yield return null;
            }

            float startY = controller.transform.position.y;
            bool airborne = false;
            bool landed = false;
            bool fell = false;
            float peak = startY;
            float timer = 0f;

            while (timer < 6f)
            {
                // Steer toward the landing pad and stop pushing forward once over it. Holding W
                // for the whole flight carries the player speed*airtime (up to 10.4 m), which
                // overshoots every short gap - that is what a competent player avoids. This
                // mirrors ParkourRouteHarness, which models the same intended traversal.
                Vector3 flat = landing - controller.transform.position;
                flat.y = 0f;
                bool pushForward = flat.magnitude > 0.15f;
                if (pushForward)
                {
                    controller.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
                }

                bool jumpKey = timer < 0.06f;    // brief tap, then release so it cannot auto-hop
                SetKeys(pushForward, sprint && pushForward, jumpKey);

                yield return null;
                timer += Time.deltaTime;

                Vector3 p = controller.transform.position;
                peak = Mathf.Max(peak, p.y);

                if (!cc.isGrounded)
                {
                    airborne = true;
                }
                else if (airborne)
                {
                    landed = true;
                    break;
                }

                if (p.y < -11f)
                {
                    fell = true;
                    break;
                }
            }

            SetKeys(false, false, false);
            yield return null;

            string standing = GroundUnderPlayer();
            string verdict;

            if (fell)
            {
                verdict = $"FAIL fell (peak rise {peak - startY:F2})";
                failures++;
            }
            else if (!landed)
            {
                verdict = $"FAIL never landed (timeout, y={controller.transform.position.y:F2})";
                failures++;
            }
            else if (standing == toName)
            {
                verdict = $"OK  (rise used {peak - startY:F2} m)";
            }
            else if (standing == fromName)
            {
                verdict = "FAIL landed back on takeoff platform";
                failures++;
            }
            else
            {
                verdict = $"WARN landed on '{standing}' instead of '{toName}'";
                warnings++;
            }

            log.AppendLine($"{fromName,-20} -> {toName,-20} {(sprint ? "sprint" : "walk  ")}  {verdict}");
            Debug.Log($"[AutoPilot] leg {i + 1}/{Route.Length - 1} {fromName}->{toName}: {verdict}");

            // checkpoint + respawn checks at the two decks
            if (toName == "Deck_North" && standing == "Deck_North")
            {
                log.AppendLine();
                yield return RespawnTest("after Deck_North checkpoint", new Vector3(0f, 4.65f, 78.75f));
                log.AppendLine();
            }
            else if (toName == "Deck_South" && standing == "Deck_South")
            {
                log.AppendLine();
                yield return RespawnTest("after Deck_South checkpoint", new Vector3(8.5f, 11.05f, 5f));
                log.AppendLine();
            }
        }

        yield return null;
    }

    // ------------------------------------------------------------------ teardown

    private void Finish()
    {
        Time.captureDeltaTime = 0f;
        string dir = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).FullName, "SceneBackups");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "playtest_report.txt"), log.ToString());
        Debug.Log($"[AutoPilot] done: {failures} failure(s), {warnings} warning(s)");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
