using UnityEngine;

/// <summary>
/// Places the player at the level start, at the latest respawn anchor, or at the latest
/// checkpoint. Knows nothing about run state; <see cref="GameManager"/> decides when a respawn
/// happens.
///
/// The anchor is Phase 6D's addition and takes precedence when there is one, because it is always
/// the more recent statement about where the player actually is: on a course the last checkpoint
/// crossed and the last place stood are the same thing, but in an open city with an order-free set
/// of objectives they come apart completely. A level with no <see cref="RespawnAnchor"/> in it
/// never sees one, so Levels 1 and 2 behave exactly as they did.
/// </summary>
public sealed class RespawnManager : MonoBehaviour
{
    [SerializeField] private PlayerFreezeController player;
    [SerializeField] private Transform levelStart;
    [SerializeField] private CheckpointManager checkpoints;

    /// <summary>The most recent anchor the player stood in, or null in a level that has none.</summary>
    private RespawnAnchor latestAnchor;

    public Transform LevelStart => levelStart;

    public RespawnAnchor LatestAnchor => latestAnchor;

    private void OnEnable() => RespawnAnchor.PlayerEntered += HandleAnchorEntered;

    private void OnDisable() => RespawnAnchor.PlayerEntered -= HandleAnchorEntered;

    private void HandleAnchorEntered(RespawnAnchor anchor) => latestAnchor = anchor;

    /// <summary>
    /// Sends the player back to LevelStart, and forgets the anchors. A run that restarts starts
    /// from the start; carrying an anchor over from the previous attempt would silently skip the
    /// first climb of the next one.
    /// </summary>
    public void RespawnAtStart()
    {
        latestAnchor = null;

        if (levelStart == null)
        {
            Debug.LogError("[Respawn] No LevelStart assigned.", this);
            return;
        }

        Place(levelStart.position, levelStart.rotation);
    }

    /// <summary>
    /// Sends the player to the most recent respawn anchor, or failing that the latest checkpoint,
    /// or failing that LevelStart.
    /// </summary>
    public void RespawnAtCheckpoint()
    {
        if (latestAnchor != null)
        {
            Place(latestAnchor.RespawnPosition, latestAnchor.RespawnRotation);
            return;
        }

        CheckpointVolume current = checkpoints != null ? checkpoints.Current : null;
        if (current == null)
        {
            RespawnAtStart();
            return;
        }

        Place(current.RespawnPosition, current.RespawnRotation);
    }

    private void Place(Vector3 position, Quaternion rotation)
    {
        if (player == null)
        {
            Debug.LogError("[Respawn] No player assigned.", this);
            return;
        }

        player.Teleport(position, rotation);
    }

    private void OnDrawGizmos()
    {
        if (levelStart == null)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.85f, 0.2f, 1f);
        Gizmos.DrawWireSphere(levelStart.position, 0.5f);
        Gizmos.DrawRay(levelStart.position + Vector3.up * 1.6f, levelStart.rotation * Vector3.forward * 2f);
    }
}
