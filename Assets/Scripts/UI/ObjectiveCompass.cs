using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The mission readout: which relay to head for, how far away it is, which way it is, and how many
/// are left.
///
/// A compass is what makes an order-free mission playable rather than merely possible. Levels 1 and
/// 2 are corridors and the next checkpoint is wherever the level is pointing; Skybound City is
/// 600 x 600 m of rooftops with five objectives that may be taken in any order, and without a
/// bearing the freedom reads as being lost. It points at the *nearest* uncaptured relay, so it never
/// implies an order the level does not have, and at the tower once the set is complete.
///
/// A view, and nothing but: every decision belongs to <see cref="ObjectiveTracker"/>, and the one
/// piece of arithmetic here is <see cref="RelativeBearing"/>, which is static and pure so it can be
/// tested without a scene.
/// </summary>
public sealed class ObjectiveCompass : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private ObjectiveTracker tracker;

    [Tooltip("The player's yaw source - the body transform, not the camera pivot, which only pitches.")]
    [SerializeField] private Transform playerBody;

    [Header("Views")]
    [Tooltip("Rotated about Z. Points straight up when the target is dead ahead.")]
    [SerializeField] private RectTransform needle;

    [SerializeField] private TMP_Text targetLabel;
    [SerializeField] private TMP_Text distanceLabel;
    [SerializeField] private TMP_Text counterLabel;
    [SerializeField] private TMP_Text statusLabel;

    [Tooltip("One chip per relay, lit left to right as the set fills. Optional.")]
    [SerializeField] private Graphic[] relayPips;

    [Header("Wording")]
    [SerializeField] private string lockedStatus = "TOWER LOCKED";
    [SerializeField] private string unlockedStatus = "TOWER UNLOCKED";

    /// <summary>Signed degrees off the player's facing. Negative is left, positive is right.</summary>
    public float Bearing { get; private set; }

    /// <summary>Horizontal metres to the target. Vertical distance is deliberately excluded.</summary>
    public float Distance { get; private set; }

    /// <summary>What the compass is currently pointing at.</summary>
    public string TargetName { get; private set; } = string.Empty;

    /// <summary>
    /// Where <paramref name="to"/> lies relative to a player at <paramref name="from"/> facing
    /// <paramref name="yawDegrees"/>: 0 is dead ahead, +90 is hard right, +/-180 is behind.
    ///
    /// Horizontal only. A relay forty metres above the player is still "that way", and folding the
    /// height in would swing the needle towards the tower's base every time they looked up.
    /// </summary>
    public static float RelativeBearing(Vector3 from, float yawDegrees, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;

        if (delta.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        float absolute = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        return Mathf.DeltaAngle(yawDegrees, absolute);
    }

    /// <summary>Horizontal distance between two points.</summary>
    public static float HorizontalDistance(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        return delta.magnitude;
    }

    private void Update()
    {
        if (tracker == null || playerBody == null)
        {
            return;
        }

        Vector3 at = playerBody.position;

        if (!tracker.TryGetTarget(at, out Vector3 target, out string label, out bool isSummit))
        {
            TargetName = label;
            SetText(targetLabel, label);
            SetText(distanceLabel, "--");
            RefreshCounters();
            return;
        }

        TargetName = label;
        Bearing = RelativeBearing(at, playerBody.eulerAngles.y, target);
        Distance = HorizontalDistance(at, target);

        if (needle != null)
        {
            // Screen space turns anticlockwise, the world clockwise, so the sign flips here.
            needle.localRotation = Quaternion.Euler(0f, 0f, -Bearing);
        }

        SetText(targetLabel, isSummit ? label.ToUpperInvariant() : label.ToUpperInvariant() + " RELAY");
        SetText(distanceLabel, $"{Distance:0} m");
        RefreshCounters();
    }

    private void RefreshCounters()
    {
        SetText(counterLabel, $"{tracker.Captured} / {tracker.Total}");
        SetText(statusLabel, tracker.TowerUnlocked ? unlockedStatus : lockedStatus);

        if (statusLabel != null)
        {
            statusLabel.color = tracker.TowerUnlocked ? UITheme.Green : UITheme.Orange;
        }

        RefreshPips();
    }

    /// <summary>
    /// The relay chips. They count, they do not name: the mission is a set, so which five have been
    /// taken is not information the player needs and showing it would imply an order the level does
    /// not have. Filling left to right is the same reading the "3 / 5" above them gives, in a form
    /// that can be taken in without reading a number.
    /// </summary>
    private void RefreshPips()
    {
        if (relayPips == null)
        {
            return;
        }

        for (int i = 0; i < relayPips.Length; i++)
        {
            if (relayPips[i] == null)
            {
                continue;
            }

            Color wanted = i < tracker.Captured
                ? (tracker.TowerUnlocked ? UITheme.Green : UITheme.Cyan)
                : UITheme.PanelBorder;

            if (relayPips[i].color != wanted)
            {
                relayPips[i].color = wanted;
            }
        }
    }

    private static void SetText(TMP_Text field, string value)
    {
        if (field != null && field.text != value)
        {
            field.text = value;
        }
    }
}
