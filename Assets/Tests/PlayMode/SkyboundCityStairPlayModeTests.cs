using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Traverses production CityKit stair geometry from the project's PlayMode test assembly. The
/// builder lives in Assembly-CSharp-Editor, so reflection keeps this assembly discoverable without
/// forcing the whole legacy Assembly-CSharp project behind new asmdef boundaries.
/// </summary>
public sealed class SkyboundCityStairPlayModeTests
{
    private const float WalkSpeed = 6f;
    private const float SprintSpeed = 9f;
    private const float LandingTolerance = 0.18f;
    private const float FatalDropY = -3f;

    [UnityTest]
    public IEnumerator ShortFlight_WalkSpeed_TraversesUpAndDownWithoutJumping()
        => TraverseShortFlight(WalkSpeed);

    [UnityTest]
    public IEnumerator ShortFlight_SprintSpeed_TraversesUpAndDownWithoutJumping()
        => TraverseShortFlight(SprintSpeed);

    [UnityTest]
    public IEnumerator TallSwitchback_WalkSpeed_TraversesUpAndDownWithoutJumping()
        => TraverseSwitchback(WalkSpeed);

    [UnityTest]
    public IEnumerator TallSwitchback_SprintSpeed_TraversesUpAndDownWithoutJumping()
        => TraverseSwitchback(SprintSpeed);

    private static IEnumerator TraverseShortFlight(float speed)
    {
        GameObject fixture = new GameObject("Short Stair Fixture");
        CharacterController controller = CreateController("Short Stair Controller");

        try
        {
            BuildFlight(fixture.transform, "Fixture_Flight_1", Vector3.zero, Vector3.forward,
                10, Rect(-0.9f, 0.9f, -1.8f, 0f), Rect(-0.9f, 0.9f, 3f, 4.8f));
            controller.transform.position = new Vector3(0f, 0.04f, -0.5f);
            Physics.SyncTransforms();
            yield return null;

            yield return MoveThrough(controller, speed, new[] { new Vector3(0f, 2f, 3.5f) });
            Assert.That(controller.transform.position.y, Is.GreaterThan(1.75f),
                "The controller did not reach the high landing.");

            yield return MoveThrough(controller, speed, new[] { new Vector3(0f, 0f, -0.5f) });
            Assert.That(controller.transform.position.y, Is.LessThan(0.30f),
                "The controller did not descend to the low landing.");
        }
        finally
        {
            DestroyFixture(fixture);
            UnityEngine.Object.Destroy(controller.gameObject);
        }
    }

    private static IEnumerator TraverseSwitchback(float speed)
    {
        GameObject fixture = new GameObject("Tall Stair Fixture");
        CharacterController controller = CreateController("Tall Stair Controller");

        try
        {
            object first = BuildFlight(fixture.transform, "Fixture_Flight_1", Vector3.zero,
                Vector3.forward, 10, Rect(-0.9f, 0.9f, -1.8f, 0f),
                Rect(-0.9f, 2.9f, 3f, 4.8f));
            GameObject turnLanding = ResultObject(first, "LandingAfter");
            BuildFlight(fixture.transform, "Fixture_Flight_2", new Vector3(2f, 2f, 4.8f),
                Vector3.back, 10, Rect(-0.9f, 2.9f, 3f, 4.8f),
                Rect(1.1f, 2.9f, 0f, 1.8f), turnLanding);

            controller.transform.position = new Vector3(0f, 0.04f, -0.5f);
            Physics.SyncTransforms();
            yield return null;

            yield return MoveThrough(controller, speed, new[]
            {
                new Vector3(0f, 2f, 3.7f),
                new Vector3(2f, 2f, 4.8f),
                new Vector3(2f, 4f, 1.3f)
            });
            Assert.That(controller.transform.position.y, Is.GreaterThan(3.70f),
                "The controller did not reach the tall ascent's high landing.");

            yield return MoveThrough(controller, speed, new[]
            {
                new Vector3(2f, 2f, 4.8f),
                new Vector3(0f, 2f, 3.7f),
                new Vector3(0f, 0f, -0.5f)
            });
            Assert.That(controller.transform.position.y, Is.LessThan(0.30f),
                "The controller did not return to the low landing.");
        }
        finally
        {
            DestroyFixture(fixture);
            UnityEngine.Object.Destroy(controller.gameObject);
        }
    }

    private static void DestroyFixture(GameObject fixture)
    {
        foreach (MeshFilter filter in fixture.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.gameObject.name.EndsWith("_Visual") && filter.sharedMesh != null)
            {
                UnityEngine.Object.Destroy(filter.sharedMesh);
            }
        }

        UnityEngine.Object.Destroy(fixture);
    }

    private static CharacterController CreateController(string name)
    {
        GameObject player = new GameObject(name);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.35f;
        controller.center = new Vector3(0f, 1f, 0f);
        controller.slopeLimit = 50f;
        controller.stepOffset = 0.3f;
        controller.skinWidth = 0.04f;
        controller.minMoveDistance = 0.001f;
        return controller;
    }

    private static IEnumerator MoveThrough(CharacterController controller, float speed,
        Vector3[] waypoints)
    {
        foreach (Vector3 waypoint in waypoints)
        {
            float timeout = 8f;
            float bestDistance = float.MaxValue;
            float stuckFor = 0f;

            while (!ReachedLanding(controller, waypoint))
            {
                float delta = Mathf.Max(Time.deltaTime, 0.001f);
                Vector3 flat = waypoint - controller.transform.position;
                flat.y = 0f;
                Vector3 direction = flat.magnitude > LandingTolerance
                    ? flat.normalized
                    : Vector3.zero;
                // Match the normal controller: planar input follows the flight, then a separate
                // downward sweep keeps the capsule on the smooth walk surface while it settles.
                controller.Move(direction * Mathf.Min(speed * delta, flat.magnitude));
                controller.Move(Vector3.down * 3f * delta);

                float remaining = Vector3.Distance(controller.transform.position, waypoint);
                stuckFor = remaining < bestDistance - 0.01f ? 0f : stuckFor + delta;
                bestDistance = Mathf.Min(bestDistance, remaining);
                timeout -= delta;

                Assert.That(controller.transform.position.y, Is.GreaterThan(FatalDropY),
                    "Traversal suffered a fatal vertical drop.");
                Assert.That(stuckFor, Is.LessThan(1.5f),
                    $"Controller became stuck {remaining:F2} m from a stair waypoint.");
                Assert.That(timeout, Is.GreaterThan(0f),
                    $"Timed out moving to stair waypoint {waypoint}.");
                yield return null;
            }
        }
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
        => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private static bool ReachedLanding(CharacterController controller, Vector3 landing)
    {
        Vector3 position = controller.transform.position;
        return controller.isGrounded
               && HorizontalDistance(position, landing) <= LandingTolerance
               && Mathf.Abs(position.y - landing.y) <= LandingTolerance;
    }

    private static object Rect(float minX, float maxX, float minZ, float maxZ)
    {
        Type type = RequireType("CityRect, Assembly-CSharp");
        return Activator.CreateInstance(type, minX, maxX, minZ, maxZ);
    }

    private static object BuildFlight(Transform parent, string name, Vector3 start,
        Vector3 direction, int steps, object before, object after,
        GameObject existingLowLanding = null)
    {
        Type planType = RequireType("StairFlightPlan, Assembly-CSharp");
        object plan = Activator.CreateInstance(planType, name, start, direction, steps,
            0.20f, 0.30f, 1.80f, before, after, 1.80f, 1.80f);
        Type kitType = RequireType("CityKit, Assembly-CSharp-Editor");
        MethodInfo builder = kitType.GetMethod("BuildWalkableStairs",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(builder, Is.Not.Null);
        return builder.Invoke(null,
            new object[] { parent, plan, null, null, existingLowLanding });
    }

    private static GameObject ResultObject(object result, string field)
    {
        FieldInfo value = result.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
        Assert.That(value, Is.Not.Null);
        return (GameObject)value.GetValue(result);
    }

    private static Type RequireType(string qualifiedName)
    {
        Type type = Type.GetType(qualifiedName);
        Assert.That(type, Is.Not.Null, $"Could not load production type {qualifiedName}.");
        return type;
    }
}
