using System;
using UnityEngine;

/// <summary>
/// A place a death returns the player to.
///
/// Levels 1 and 2 need nothing like this: their checkpoints are the course, so the last checkpoint
/// crossed is by definition the right place to come back to. Skybound City is 600 x 600 m with five
/// objectives that may be taken in any order, and there the two questions come apart - the last
/// relay captured says how far through the mission the player is, and says nothing about whether
/// they are currently on a roof forty storeys away from it.
///
/// So an anchor is progress of a different kind: it does not count towards anything, it does not
/// gate the finish, and it only moves where a respawn puts you. The builder places one at the top
/// of every way in off the street, which is exactly the climb a player should not have to repeat,
/// and one on every relay, which is the strongest anchor the mission has.
///
/// Reports through a static event in the <see cref="KillZone"/> idiom, so a new anchor needs no
/// wiring at all - <see cref="RespawnManager"/> subscribes once and remembers the latest.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class RespawnAnchor : MonoBehaviour
{
    [SerializeField] private string anchorName = "Anchor";

    [Tooltip("Where the player is placed. Defaults to this transform.")]
    [SerializeField] private Transform respawnPoint;

    [SerializeField] private bool logOnActivate = true;

    /// <summary>Raised whenever the player enters any anchor. Payload is the anchor entered.</summary>
    public static event Action<RespawnAnchor> PlayerEntered;

    public string AnchorName => anchorName;

    public Vector3 RespawnPosition => respawnPoint != null ? respawnPoint.position : transform.position;

    public Quaternion RespawnRotation => respawnPoint != null ? respawnPoint.rotation : transform.rotation;

    /// <summary>True once the player has stood in this anchor at least once during the run.</summary>
    public bool Visited { get; private set; }

    private void Reset()
    {
        Collider attached = GetComponent<Collider>();

        if (attached != null)
        {
            attached.isTrigger = true;
        }
    }

    /// <summary>Clears the visited flag. Used by RESTART RUN.</summary>
    public void ResetActivation() => Visited = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<BasicFirstPersonController>() == null)
        {
            return;
        }

        // Deliberately fires on every entry rather than once: an anchor is not a collectable, it
        // is a statement about where the player is standing right now.
        bool first = !Visited;
        Visited = true;

        PlayerEntered?.Invoke(this);

        if (logOnActivate && first)
        {
            Debug.Log($"[Respawn] anchor set: {anchorName}", this);
        }
    }

    private void OnDrawGizmos()
    {
        Color accent = Visited
            ? new Color(0.30f, 1f, 0.55f, 1f)
            : new Color(1f, 0.85f, 0.2f, 1f);

        Collider attached = GetComponent<Collider>();

        if (attached != null)
        {
            Gizmos.color = new Color(accent.r, accent.g, accent.b, 0.10f);
            Gizmos.DrawCube(attached.bounds.center, attached.bounds.size);
            Gizmos.color = accent;
            Gizmos.DrawWireCube(attached.bounds.center, attached.bounds.size);
        }

        Vector3 target = RespawnPosition;
        Gizmos.color = accent;
        Gizmos.DrawWireSphere(target, 0.4f);
        Gizmos.DrawRay(target + Vector3.up * 1.6f, RespawnRotation * Vector3.forward * 2f);
    }
}
