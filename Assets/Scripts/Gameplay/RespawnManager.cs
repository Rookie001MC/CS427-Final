using UnityEngine;

/// <summary>
/// Places the player at the level start or at the latest checkpoint. Knows nothing about run
/// state; <see cref="GameManager"/> decides when a respawn happens.
/// </summary>
public sealed class RespawnManager : MonoBehaviour
{
    [SerializeField] private PlayerFreezeController player;
    [SerializeField] private Transform levelStart;
    [SerializeField] private CheckpointManager checkpoints;

    public Transform LevelStart => levelStart;

    /// <summary>Sends the player back to LevelStart.</summary>
    public void RespawnAtStart()
    {
        if (levelStart == null)
        {
            Debug.LogError("[Respawn] No LevelStart assigned.", this);
            return;
        }

        Place(levelStart.position, levelStart.rotation);
    }

    /// <summary>Sends the player to the latest checkpoint, or LevelStart if none reached.</summary>
    public void RespawnAtCheckpoint()
    {
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
