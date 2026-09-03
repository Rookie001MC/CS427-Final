using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The tracking layer: the one thing that listens to gameplay and writes to
/// <see cref="PlayerStatsStore"/>.
///
/// It sits between the two on purpose. Gameplay raises events and knows nothing about statistics;
/// the store holds numbers and knows nothing about scenes; the Player Stats screen reads the store
/// and never touches gameplay. That is what keeps the screen from having to reconstruct a career
/// by searching the scene, and it is why this class is the only one that ever calls
/// <c>FindFirstObjectByType</c> - once per scene load, never in Update.
///
/// It is created automatically rather than placed in a scene, which matters for two reasons: the
/// gameplay scenes are generated, so anything hand-placed in one is lost on the next rebuild, and
/// a component that has to be remembered in three scenes is a statistic that silently stops being
/// recorded in the one where it was forgotten.
/// </summary>
public sealed class PlayerStatsRecorder : MonoBehaviour
{
    /// <summary>The single live recorder, or null before the first scene has loaded.</summary>
    public static PlayerStatsRecorder Active { get; private set; }

    private PlayerStatsStore store;

    private GameManager game;
    private CheckpointManager checkpoints;
    private LevelInfo levelInfo;
    private BasicFirstPersonController movement;
    private PlayerFreezeController freeze;
    private Transform player;

    private MotionSampler sampler;

    // The personal best as it stood when this attempt started, so "was that a PB" is answered the
    // same way whether this or GameplayUIController happens to handle RunFinished first.
    private bool hadBestAtRunStart;
    private float bestAtRunStart;

    private string levelKey = string.Empty;
    private LevelTrack track = LevelTrack.Training;
    private string displayName = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Active != null)
        {
            return;
        }

        GameObject host = new GameObject(nameof(PlayerStatsRecorder));
        Object.DontDestroyOnLoad(host);
        host.AddComponent<PlayerStatsRecorder>();
    }

    private void Awake()
    {
        // A second recorder would double every count. The first one wins and the rest leave.
        if (Active != null && Active != this)
        {
            Destroy(gameObject);
            return;
        }

        Active = this;
        store = PlayerStatsStore.Default;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        Bind();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Unbind();

        if (Active == this)
        {
            Active = null;
        }
    }

    private void OnApplicationQuit() => store?.Flush();

    private void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            store?.Flush();
        }
    }

    // ---------------------------------------------------------------- binding

    /// <summary>
    /// Rebinds to whatever run systems the newly loaded scene has, and banks whatever the last
    /// one produced. Returning to the main menu is a save point for exactly this reason: the
    /// action counters are not flushed per action, so this is where a run's jumps are committed.
    /// </summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        store?.Flush();
        Bind();
    }

    /// <summary>
    /// Finds the run systems in the active scene and subscribes to them.
    ///
    /// Every subscription is preceded by the matching unsubscribe in <see cref="Unbind"/>, and
    /// binding is only ever driven from OnEnable and a scene load, so a scene reload cannot leave
    /// two live subscriptions counting the same jump twice.
    /// </summary>
    private void Bind()
    {
        Unbind();

        game = Object.FindFirstObjectByType<GameManager>();
        checkpoints = Object.FindFirstObjectByType<CheckpointManager>();
        levelInfo = Object.FindFirstObjectByType<LevelInfo>();
        movement = Object.FindFirstObjectByType<BasicFirstPersonController>();
        freeze = Object.FindFirstObjectByType<PlayerFreezeController>();
        player = movement != null ? movement.transform : null;

        levelKey = ResolveLevelKey();
        track = levelInfo != null ? levelInfo.Track : LevelTrack.Training;
        displayName = levelInfo != null ? levelInfo.DisplayName : levelKey;

        // A fresh scene is a fresh origin: the spawn placement must never read as travel.
        sampler.Discontinuity();

        if (game != null)
        {
            game.RunStarted += HandleRunStarted;
            game.RunFinished += HandleRunFinished;
            game.PlayerDied += HandlePlayerDied;
            game.StateChanged += HandleStateChanged;
        }

        if (checkpoints != null)
        {
            checkpoints.CheckpointReached += HandleCheckpointReached;
        }

        if (movement != null)
        {
            movement.ActionPerformed += HandleAction;
            movement.Teleported += HandleTeleported;
        }

        if (freeze != null)
        {
            freeze.Teleported += HandleTeleported;
        }
    }

    private void Unbind()
    {
        if (game != null)
        {
            game.RunStarted -= HandleRunStarted;
            game.RunFinished -= HandleRunFinished;
            game.PlayerDied -= HandlePlayerDied;
            game.StateChanged -= HandleStateChanged;
        }

        if (checkpoints != null)
        {
            checkpoints.CheckpointReached -= HandleCheckpointReached;
        }

        if (movement != null)
        {
            movement.ActionPerformed -= HandleAction;
            movement.Teleported -= HandleTeleported;
        }

        if (freeze != null)
        {
            freeze.Teleported -= HandleTeleported;
        }

        game = null;
        checkpoints = null;
        levelInfo = null;
        movement = null;
        freeze = null;
        player = null;
    }

    /// <summary>
    /// The level's record key. <see cref="LevelInfo"/> first, because it reads the catalogue asset
    /// the menu also reads; then the selection the loader established; then the scene name, which
    /// is what <see cref="LevelInfo"/> itself falls back to.
    /// </summary>
    private string ResolveLevelKey()
    {
        if (levelInfo != null && !string.IsNullOrWhiteSpace(levelInfo.RecordKey))
        {
            return levelInfo.RecordKey;
        }

        if (RunSession.HasSelection && !string.IsNullOrWhiteSpace(RunSession.ActiveRecordKey))
        {
            return RunSession.ActiveRecordKey;
        }

        return SceneManager.GetActiveScene().name;
    }

    // ---------------------------------------------------------------- run lifecycle

    /// <summary>
    /// A run attempt. Raised by <see cref="GameManager.RunStarted"/> once the countdown has
    /// finished and the player has control, which is the definition this system uses: the run
    /// setup is complete and the clock is moving.
    /// </summary>
    private void HandleRunStarted()
    {
        hadBestAtRunStart = RunRecordStore.Default.TryGetBest(levelKey, ModeOfRun(),
            out bestAtRunStart);

        sampler.Discontinuity();
        store.RecordRunStarted(levelKey, track);
    }

    private void HandleRunFinished(float finishTime)
    {
        bool personalBest = !hadBestAtRunStart || finishTime < bestAtRunStart;

        store.RecordRunFinished(levelKey, displayName, track, ModeOfRun(), finishTime,
            personalBest);
    }

    private void HandlePlayerDied(int deaths)
    {
        store.RecordDeath();

        // A death in No-Checkpoint Mode ends the attempt by the mode's own rule, so it is a
        // failed run and can be recorded as one. A death in Checkpoint Mode is not: the same
        // attempt continues from the last checkpoint, and calling it a failure would mean the
        // failure count no longer counted runs.
        if (game != null && game.Rules.DeathAction == DeathRecoveryAction.AwaitPlayerDecision)
        {
            float lasted = game.Timer != null ? game.Timer.ElapsedSeconds : 0f;
            store.RecordRunFailed(levelKey, displayName, track, ModeOfRun(), lasted);
        }
    }

    /// <summary>
    /// Anything other than a live run invalidates the motion origin.
    ///
    /// Recovering, Paused, Countdown and Finished all involve the player being held, moved or
    /// released somewhere; the first frame back in <see cref="RunState.Running"/> must not be
    /// measured from wherever they were when they left it.
    /// </summary>
    private void HandleStateChanged(RunState state)
    {
        if (state != RunState.Running)
        {
            sampler.Discontinuity();
            store.Flush();
        }
    }

    private void HandleCheckpointReached(CheckpointVolume volume, int reached, int total,
        float split, float cumulative)
    {
        store.RecordCheckpoint(levelKey, track);
    }

    private void HandleTeleported() => sampler.Discontinuity();

    /// <summary>
    /// One parkour action, counted once.
    ///
    /// The controller has already decided that the action happened; all this adds is that it only
    /// counts while a run is actually running, which keeps the countdown, the recovery pause and
    /// a scene opened directly in the editor with no run in it out of the career totals.
    /// </summary>
    private void HandleAction(ParkourAction action)
    {
        if (game == null || game.State != RunState.Running)
        {
            return;
        }

        store.RecordAction(action);
    }

    /// <summary>The mode this run is being played in, as the session established it.</summary>
    private GameMode ModeOfRun() => game != null ? game.Mode : RunSession.ActiveMode;

    // ---------------------------------------------------------------- sampling

    private void Update()
    {
        if (game == null || game.State != RunState.Running)
        {
            return;
        }

        float dt = Time.deltaTime;
        if (dt <= 0f)
        {
            return;
        }

        // Active run time: this branch is only reached while the run is running, so a pause, a
        // loading screen, a menu and a countdown all contribute nothing by construction.
        store.AddRunTime(dt);

        if (player == null)
        {
            return;
        }

        if (!sampler.TryAdvance(player.position, dt, out float metres))
        {
            // Rejected or first frame: no travel, and no speed either. A frame whose displacement
            // could not be believed is not a frame whose speed can be.
            return;
        }

        store.AddDistance(metres);

        if (movement != null)
        {
            store.ReportSpeed(movement.CurrentHorizontalSpeed);
        }
    }
}
