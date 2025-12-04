using UnityEngine;

/// <summary>
/// Training phases that control what obstacles/penalties are active.
/// </summary>
public enum TrainingPhase
{
    BasePathfinding,  // Phase 1: No slimes, no wall penalties - just learn to follow path
    AvoidWalls,       // Phase 2: Add wall collision penalties
    AvoidSlime,       // Phase 3: Add slime penalties and jumping
    MovingTarget      // Phase 4: Target moves (placeholder for later)
}

/// <summary>
/// Centralized game state manager. Attach to an empty GameObject in the scene.
/// Other scripts reference this singleton to access shared training settings,
/// dungeon configuration, and runtime state.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Training Settings")]
    [SerializeField] private TrainingPhase trainingPhase = TrainingPhase.BasePathfinding;
    public TrainingPhase CurrentTrainingPhase => trainingPhase;

    [SerializeField] private bool trainingMode = false;
    /// <summary>
    /// When true, the dungeon generator creates a minimal training map (2 rooms).
    /// </summary>
    public bool IsTrainingMode => trainingMode;

    [Header("Dungeon Seed")]
    [SerializeField] private int initialSeed = 12345;
    private int currentSeed;
    public int CurrentSeed => currentSeed;

    [Header("Timing")]
    [SerializeField] private float timePenaltyPerStep = 0.001f;
    public float TimePenaltyPerStep => timePenaltyPerStep;

    [Header("Close-Range Feedback")]
    [Tooltip("Distance (world units) considered 'close' to the target.")]
    [SerializeField] private float closeDistanceThreshold = 2f;
    public float CloseDistanceThreshold => closeDistanceThreshold;

    [Tooltip("Penalty applied when agent was close and moves away (negative value).")]
    [SerializeField] private float closeMoveAwayPenalty = -0.25f;
    public float CloseMoveAwayPenalty => closeMoveAwayPenalty;

    [Tooltip("Reward applied when agent was close and moves closer.")]
    [SerializeField] private float closeMoveCloserReward = 0.05f;
    public float CloseMoveCloserReward => closeMoveCloserReward;

    [Header("Detection Settings")]
    [SerializeField] private float wallRayLength = 5f;
    public float WallRayLength => wallRayLength;

    [SerializeField] private float slipperyFloorRayLength = 5f;
    public float SlipperyFloorRayLength => slipperyFloorRayLength;

    [Header("Tags")]
    [SerializeField] private string stickyTag = "Sticky";
    public string StickyTag => stickyTag;

    [SerializeField] private string wallTag = "Wall";
    public string WallTag => wallTag;

    [SerializeField] private string exitTag = "Exit";
    public string ExitTag => exitTag;

    [SerializeField] private string playerTag = "Player";
    public string PlayerTag => playerTag;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Initialize seed
        currentSeed = initialSeed;
    }

    /// <summary>
    /// Increment the seed for the next dungeon generation.
    /// </summary>
    public void IncrementSeed()
    {
        currentSeed++;
    }

    /// <summary>
    /// Reset the seed to the initial value.
    /// </summary>
    public void ResetSeed()
    {
        currentSeed = initialSeed;
    }

    /// <summary>
    /// Set a specific seed value.
    /// </summary>
    public void SetSeed(int seed)
    {
        currentSeed = seed;
    }

    /// <summary>
    /// Check if slime spawning should be enabled based on training phase.
    /// </summary>
    public bool ShouldSpawnSlime => trainingPhase >= TrainingPhase.AvoidSlime;

    /// <summary>
    /// Check if wall penalties should be active based on training phase.
    /// </summary>
    public bool ShouldPenalizeWalls => trainingPhase >= TrainingPhase.AvoidWalls;

    /// <summary>
    /// Check if slime penalties should be active based on training phase.
    /// </summary>
    public bool ShouldPenalizeSlime => trainingPhase >= TrainingPhase.AvoidSlime;

    /// <summary>
    /// Check if jumping should be allowed based on training phase.
    /// </summary>
    public bool CanJump => trainingPhase >= TrainingPhase.AvoidSlime;
}
