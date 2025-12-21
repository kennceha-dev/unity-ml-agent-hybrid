using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Training phases that control what obstacles/penalties are active.
/// </summary>
public enum TrainingPhase
{
    ReachTarget,       // Initial phase: just reach the target, 1 Room
    BasePathfinding,  // Phase 1: No slimes - just learn to follow path, 2 Room
    FullPathfinding,  // Phase 2: No slimes, full map
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

    /// <summary>
    /// Event fired when BasicAgent reaches the target. HybridAgent subscribes to end its episode.
    /// </summary>
    public static event Action OnBasicAgentReachedTargetEvent;

    /// <summary>
    /// Event fired when training phase changes. DungeonRunner subscribes to regenerate the map.
    /// </summary>
    public static event Action<TrainingPhase> OnTrainingPhaseChanged;

    [Header("Training Settings")]
    [SerializeField] private TrainingPhase trainingPhase = TrainingPhase.BasePathfinding;
    public TrainingPhase CurrentTrainingPhase => trainingPhase;

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

    [Header("Auto Progression")]
    [SerializeField] private int performanceWindow = 50;
    [SerializeField] private int minEpisodesForEvaluation = 15;
    [SerializeField] private float minSuccessRateForPromotion = 0.6f;
    [SerializeField] private float successMargin = 0.05f;

    private class PhaseData
    {
        public readonly Queue<bool> BeatBaseResults = new();
        public bool HybridBeatBaseOnce;
        public bool TrackingEnabled;
        public int HybridTotalSuccesses;
        public int LastHybridEpisodeReported = -1;
    }

    private readonly Dictionary<TrainingPhase, PhaseData> phaseData = new();

#if UNITY_EDITOR
    [Header("Scene HUD")]
    [SerializeField] private bool showTrainingHud = true;
    [SerializeField] private Vector2 trainingHudPosition = new Vector2(10f, 110f);
    [SerializeField] private Vector2 trainingHudSize = new Vector2(280f, 180f);
    [SerializeField] private HybridAgent hudAgent;
#endif

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

    public void ReportHybridEpisodeResult(bool success, bool beatBase, int episodeNumber)
    {
        PhaseData data = GetPhaseData(trainingPhase);

        if (episodeNumber == data.LastHybridEpisodeReported)
            return;
        data.LastHybridEpisodeReported = episodeNumber;

        if (success)
            data.HybridTotalSuccesses++;

        if (beatBase && !data.HybridBeatBaseOnce)
        {
            data.HybridBeatBaseOnce = true;
            EnableTracking(data);
        }

        if (!data.TrackingEnabled) return;

        RecordResult(data.BeatBaseResults, beatBase);
        CheckAutoProgression(data);
    }

    public void ReportBasicEpisodeResult(bool success)
    {
        // Base result is implied via beatBase flag passed from HybridAgent; nothing to track per-seed here.
    }

    private void EnableTracking(PhaseData data)
    {
        data.TrackingEnabled = true;
        data.BeatBaseResults.Clear();
    }

    private PhaseData GetPhaseData(TrainingPhase phase)
    {
        if (!phaseData.TryGetValue(phase, out PhaseData data))
        {
            data = new PhaseData();
            phaseData[phase] = data;
        }

        return data;
    }

    private void RecordResult(Queue<bool> queue, bool success)
    {
        queue.Enqueue(success);

        int window = Mathf.Max(1, performanceWindow);
        while (queue.Count > window)
            queue.Dequeue();
    }

    private float GetSuccessRate(Queue<bool> queue)
    {
        if (queue.Count == 0) return 0f;

        int successCount = 0;
        foreach (bool result in queue)
        {
            if (result) successCount++;
        }

        return (float)successCount / queue.Count;
    }

    private void CheckAutoProgression(PhaseData data)
    {
        if (!data.TrackingEnabled) return;
        if (trainingPhase >= TrainingPhase.MovingTarget) return;
        if (data.BeatBaseResults.Count < minEpisodesForEvaluation) return;

        float beatRate = GetSuccessRate(data.BeatBaseResults);

        if (beatRate < minSuccessRateForPromotion) return;

        var nextPhase = (TrainingPhase)Mathf.Min((int)trainingPhase + 1, (int)TrainingPhase.MovingTarget);
        if (nextPhase != trainingPhase)
        {
            trainingPhase = nextPhase;
            Debug.Log($"Auto-advanced to training phase: {trainingPhase}");
            // Reset tracking flags for the new phase; data will be re-fetched per phase
            PhaseData nextData = GetPhaseData(trainingPhase);
            nextData.TrackingEnabled = false;
            nextData.HybridBeatBaseOnce = false;

            // Notify listeners (e.g., DungeonRunner) to regenerate the map
            OnTrainingPhaseChanged?.Invoke(trainingPhase);
        }
    }

    /// <summary>
    /// Check if slime spawning should be enabled based on training phase.
    /// </summary>
    public bool ShouldSpawnSlime => trainingPhase >= TrainingPhase.AvoidSlime;

    /// <summary>
    /// Check if wall penalties should be active based on training phase.
    /// </summary>
    public bool ShouldPenalizeWalls => trainingPhase >= TrainingPhase.ReachTarget;

    /// <summary>
    /// Check if slime penalties should be active based on training phase.
    /// </summary>
    public bool ShouldPenalizeSlime => trainingPhase >= TrainingPhase.AvoidSlime;

    /// <summary>
    /// Check if jumping should be allowed based on training phase.
    /// </summary>
    public bool CanJump => trainingPhase >= TrainingPhase.AvoidSlime;

    /// <summary>
    /// Called by BasicAgent when it reaches the target.
    /// Fires event to notify HybridAgent to end episode and reset.
    /// </summary>
    public void OnBasicAgentReachedTarget()
    {
        ReportBasicEpisodeResult(true);
        OnBasicAgentReachedTargetEvent?.Invoke();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !showTrainingHud) return;

        PhaseData data = GetPhaseData(trainingPhase);

        float beatRate = GetSuccessRate(data.BeatBaseResults);

        string text =
            $"Phase: {trainingPhase}\n" +
            $"Hybrid beats base ({data.BeatBaseResults.Count}): {beatRate:P1}\n" +
            $"Promote if win rate >= {minSuccessRateForPromotion:P0}\n" +
            $"Hybrid total successes: {data.HybridTotalSuccesses}\n" +
            $"Beat base once (this phase): {data.HybridBeatBaseOnce}";

        if (hudAgent != null)
        {
            text +=
                $"\n\nAgent Ep: {hudAgent.Episode}\n" +
                $"Current: {hudAgent.CurrentReward:F2}\n" +
                $"Last: {hudAgent.LastEpisodeReward:F2}\n" +
                $"Best: {hudAgent.BestEpisodeReward:F2}";
        }

        if (!data.HybridBeatBaseOnce)
            text += "\n\nTracking starts once hybrid beats base first.";

        Handles.BeginGUI();
        GUI.Label(new Rect(trainingHudPosition.x, trainingHudPosition.y, trainingHudSize.x, trainingHudSize.y), text, GetHudStyle());
        Handles.EndGUI();
    }

    private GUIStyle GetHudStyle()
    {
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        style.normal.textColor = Color.white;
        return style;
    }
#endif
}
