/// <summary>
/// Lifecycle of a single parkour run. Owned by <see cref="GameManager"/>.
/// </summary>
public enum RunState
{
    Idle,
    Countdown,
    Running,
    Paused,
    Dead,
    Finished
}
