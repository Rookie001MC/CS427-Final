using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which pool object is standing on which marker, and for how long.
///
/// This is the last piece of the route guide that flickered, and the only one whose fault was
/// invisible in a still frame. <see cref="RouteTrail"/> decides which square metres of the city want
/// a marker; the pool used to answer "which object draws them" with `slot i draws the i-th one`,
/// which is correct as a picture and wrong as a scene. Running past the nearest chevron drops it out
/// of the list, so every marker behind it shifts down a slot, so on that one frame twenty-odd live
/// GameObjects are teleported - up to 30 m - and re-aimed, some of them through 90 degrees, and the
/// object at the end of the pool is switched off. Over a 400 m run that is 535 discontinuous moves
/// where 36 markers genuinely came into view: a renderer moved like that has no motion vector and no
/// temporal history, and the pool's tail blinks.
///
/// So a marker has an identity - <see cref="Key"/>, the five-centimetre cell of the city it stands
/// on - and a slot holds one until the trail stops asking for that cell. Identity by ground rather
/// than by arc length is deliberate: arc length is measured from the node the player is standing on,
/// which changes as they cross a roof, and two searches that agree about a piece of ground must
/// agree about which object is standing on it. The ground is the only thing they both name.
///
/// Pure, and in the City layer rather than in <see cref="RouteGuide"/>, for the same reason
/// `RouteTrail` is: a binding that is stable over one frame is not a claim anyone can check, and a
/// rule that lives in a `MonoBehaviour.Update` cannot be walked for ten thousand frames by a test.
/// </summary>
public sealed class GuideMarkerPool
{
    private readonly long[] binding;
    private readonly bool[] claimed;

    // This frame's wanted keys, so the identity of a marker is computed once rather than once per
    // slot it is compared against. Grows to fit and is never shrunk.
    private long[] keys = new long[0];

    /// <summary>How many times an object has been put on a marker it was not already on.</summary>
    public int Rebinds { get; private set; }

    /// <summary>How many times an object has been switched on or off.</summary>
    public int Toggles { get; private set; }

    public GuideMarkerPool(int slots)
    {
        binding = new long[Mathf.Max(0, slots)];
        claimed = new bool[binding.Length];
    }

    public int Slots => binding.Length;

    /// <summary>The marker a slot is standing on, or 0 where it is switched off.</summary>
    public long BoundTo(int slot) => binding[slot];

    /// <summary>Whether a slot is currently showing something.</summary>
    public bool IsOn(int slot) => binding[slot] != 0L;

    /// <summary>
    /// A marker's identity: the five-centimetre cell of the city it stands on, and which way round
    /// it is standing there to the nearest degree.
    ///
    /// Never zero, because zero is what a free slot holds.
    /// <see cref="CityDesign.GuideMarkerClearGap"/> is enforced when the trail is laid, so two
    /// markers of one route can never land in the same cell. The heading is in the identity because
    /// an object that keeps its marker is never re-aimed, so a marker that somehow kept its ground
    /// and changed its direction - a route re-found the other way up the same street - has to be a
    /// different marker rather than a silently stale rotation.
    /// </summary>
    public static long Key(Vector3 position, Vector3 forward)
    {
        long x = (long)Mathf.Round(position.x * 20f) & 0x1FFFFF;
        long y = (long)Mathf.Round(position.y * 20f) & 0x1FFFFF;
        long z = (long)Mathf.Round(position.z * 20f) & 0x1FFFFF;
        long yaw = (long)Mathf.Round(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg) & 0x3FF;

        long key = (x << 42) ^ (y << 21) ^ z ^ unchecked(yaw * -7046029254386353131L);
        return key == 0L ? 1L : key;
    }

    /// <summary>The identity of one marker.</summary>
    public static long Key(Breadcrumb crumb) => Key(crumb.Position, crumb.Forward);

    /// <summary>
    /// One frame of binding.
    ///
    /// <paramref name="slots"/> comes back parallel with <paramref name="wanted"/> - the slot each
    /// marker is drawn by, or -1 where the pool had nothing spare. <paramref name="fresh"/> is true
    /// on the markers whose slot has only just taken them, which are the only ones an object has to
    /// be moved or aimed for. <paramref name="release"/> is the slots to switch off.
    ///
    /// Releasing happens first, so a marker that has just left the window frees its object for one
    /// that has just entered it: without that the pool runs out one frame early and the far end of
    /// the trail blinks off and on again every seven metres.
    /// </summary>
    public void Bind(List<Breadcrumb> wanted, List<int> slots, List<bool> fresh, List<int> release)
    {
        slots.Clear();
        fresh.Clear();
        release.Clear();

        if (keys.Length < wanted.Count)
        {
            keys = new long[wanted.Count];
        }

        for (int c = 0; c < wanted.Count; c++)
        {
            keys[c] = Key(wanted[c]);
            slots.Add(-1);
            fresh.Add(false);
        }

        for (int i = 0; i < claimed.Length; i++)
        {
            claimed[i] = false;
        }

        for (int i = 0; i < binding.Length; i++)
        {
            if (binding[i] == 0L || Wanted(keys, wanted.Count, binding[i]))
            {
                continue;
            }

            binding[i] = 0L;
            release.Add(i);
            Toggles++;
        }

        // Everything the trail still wants that already has an object on it. Held first and as a
        // whole pass, so a marker can never be handed a free slot while its own object is sitting
        // further down the pool.
        for (int c = 0; c < wanted.Count; c++)
        {
            for (int i = 0; i < binding.Length; i++)
            {
                if (binding[i] != keys[c] || claimed[i])
                {
                    continue;
                }

                claimed[i] = true;
                slots[c] = i;
                break;
            }
        }

        // And the ones that are new, onto whatever is spare.
        for (int c = 0; c < wanted.Count; c++)
        {
            if (slots[c] >= 0)
            {
                continue;
            }

            for (int i = 0; i < binding.Length; i++)
            {
                if (claimed[i] || binding[i] != 0L)
                {
                    continue;
                }

                claimed[i] = true;
                binding[i] = keys[c];
                slots[c] = i;
                fresh[c] = true;
                Rebinds++;
                Toggles++;
                break;
            }
        }
    }

    /// <summary>Everything off - no route, or the guide has been switched off.</summary>
    public void Clear(List<int> release)
    {
        release.Clear();

        for (int i = 0; i < binding.Length; i++)
        {
            if (binding[i] == 0L)
            {
                continue;
            }

            binding[i] = 0L;
            release.Add(i);
            Toggles++;
        }
    }

    private static bool Wanted(long[] keys, int count, long key)
    {
        for (int i = 0; i < count; i++)
        {
            if (keys[i] == key)
            {
                return true;
            }
        }

        return false;
    }
}
