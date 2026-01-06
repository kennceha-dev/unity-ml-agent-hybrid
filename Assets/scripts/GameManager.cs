using System;
using System.Collections.Generic;
using System.IO;
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

    [Header("Debug")]
    [SerializeField] private bool enableRewardLogging = false;
    public bool EnableRewardLogging => enableRewardLogging;

    [SerializeField] private bool logToFile = true;
    [SerializeField] private bool logToConsole = false;
    [SerializeField] private string logFileName = "reward_log.txt";
    [SerializeField] private int phaseLogInterval = 10;

    private StreamWriter logWriter;
    private string logFilePath;
    private int lastPhaseLogEpisode = 0;

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
        public int ConsecutiveSuccesses;
        public int ConsecutiveFailures;
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

        // Initialize log file
        InitializeLogFile();
    }

    private void OnDestroy()
    {
        CloseLogFile();
    }

    private void OnApplicationQuit()
    {
        CloseLogFile();
    }

    private void InitializeLogFile()
    {
        if (!enableRewardLogging || !logToFile) return;

        try
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = Path.GetFileNameWithoutExtension(logFileName);
            string extension = Path.GetExtension(logFileName);
            if (string.IsNullOrEmpty(extension)) extension = ".txt";

            logFilePath = Path.Combine(Application.dataPath, "Logs", $"{fileName}_{timestamp}{extension}");

            // Ensure directory exists
            string directory = Path.GetDirectoryName(logFilePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            logWriter = new StreamWriter(logFilePath, false) { AutoFlush = true };
            logWriter.WriteLine($"=== Reward Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            logWriter.WriteLine($"Training Phase: {trainingPhase}");
            logWriter.WriteLine(new string('=', 60));

            Debug.Log($"[GameManager] Reward logging to: {logFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameManager] Failed to initialize log file: {e.Message}");
            logWriter = null;
        }
    }

    private void CloseLogFile()
    {
        if (logWriter != null)
        {
            try
            {
                logWriter.WriteLine(new string('=', 60));
                logWriter.WriteLine($"=== Reward Log Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                logWriter.Close();
                logWriter.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Failed to close log file: {e.Message}");
            }
            finally
            {
                logWriter = null;
            }
        }
    }

    public void WriteRewardLog(string message)
    {
        if (!enableRewardLogging) return;

        if (logToConsole)
            Debug.Log(message);

        if (logToFile && logWriter != null)
        {
            try
            {
                logWriter.WriteLine(message);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Failed to write to log: {e.Message}");
            }
        }
    }

    private void WritePhaseDataLog(PhaseData data, int episodeNumber)
    {
        float beatRate = GetSuccessRate(data.BeatBaseResults);
        float threshold = GetPromotionThreshold();

        string separator = new string('-', 60);
        WriteRewardLog(separator);
        WriteRewardLog($"[Phase Summary] Episode {episodeNumber} | Phase: {trainingPhase}");
        WriteRewardLog($"  Seed: {currentSeed}");
        WriteRewardLog($"  Total Successes: {data.HybridTotalSuccesses}");
        WriteRewardLog($"  Beat Base Once: {data.HybridBeatBaseOnce}");
        WriteRewardLog($"  Tracking Enabled: {data.TrackingEnabled}");
        WriteRewardLog($"  Beat Base Rate ({data.BeatBaseResults.Count}): {beatRate:P1}");
        WriteRewardLog($"  Promotion Threshold: {threshold:P0}");
        WriteRewardLog(separator);
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
        {
            data.HybridTotalSuccesses++;
            data.ConsecutiveSuccesses++;
        }
        else
        {
            data.ConsecutiveFailures++;
        }

        if (beatBase && !data.HybridBeatBaseOnce)
        {
            data.HybridBeatBaseOnce = true;
            EnableTracking(data);
        }

        // Log phase data periodically
        if (enableRewardLogging && phaseLogInterval > 0 && episodeNumber - lastPhaseLogEpisode >= phaseLogInterval)
        {
            WritePhaseDataLog(data, episodeNumber);
            lastPhaseLogEpisode = episodeNumber;
        }

        // Special rule for ReachTarget: auto-advance on performanceWindow consecutive successes.
        if (trainingPhase == TrainingPhase.ReachTarget)
        {
            int requiredStreak = Mathf.Max(1, performanceWindow);
            int allowedFailures = Mathf.CeilToInt(requiredStreak * 0.25f); // allow up to 25% failures before reset

            if (data.ConsecutiveFailures > allowedFailures)
            {
                data.ConsecutiveSuccesses = 0;
                data.ConsecutiveFailures = 0;
            }

            if (data.ConsecutiveSuccesses >= requiredStreak)
            {
                if (TryAdvancePhase("success streak"))
                    return;
            }
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

    private float GetPromotionThreshold()
    {
        // Phase 1 (ReachTarget) uses a softer threshold to promote to Phase 2.
        return trainingPhase == TrainingPhase.ReachTarget
            ? minSuccessRateForPromotion * 0.5f
            : minSuccessRateForPromotion;
    }

    private void CheckAutoProgression(PhaseData data)
    {
        if (!data.TrackingEnabled) return;
        if (trainingPhase >= TrainingPhase.MovingTarget) return;
        if (data.BeatBaseResults.Count < minEpisodesForEvaluation) return;

        float beatRate = GetSuccessRate(data.BeatBaseResults);
        float threshold = GetPromotionThreshold();

        if (beatRate < threshold) return;

        TryAdvancePhase("beat-base threshold");
    }

    private bool TryAdvancePhase(string reason)
    {
        var nextPhase = (TrainingPhase)Mathf.Min((int)trainingPhase + 1, (int)TrainingPhase.MovingTarget);
        if (nextPhase == trainingPhase) return false;

        trainingPhase = nextPhase;
        Debug.Log($"Auto-advanced to training phase: {trainingPhase} ({reason})");

        // Reset tracking flags and streaks for the new phase; data is per-phase.
        PhaseData nextData = GetPhaseData(trainingPhase);
        nextData.TrackingEnabled = false;
        nextData.HybridBeatBaseOnce = false;
        nextData.ConsecutiveSuccesses = 0;
        nextData.ConsecutiveFailures = 0;

        // Notify listeners (e.g., DungeonRunner) to regenerate the map
        OnTrainingPhaseChanged?.Invoke(trainingPhase);
        return true;
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
        float threshold = GetPromotionThreshold();
        int requiredStreak = Mathf.Max(1, performanceWindow);
        int allowedFailures = Mathf.CeilToInt(requiredStreak * 0.25f);

        string text =
            $"Phase: {trainingPhase}\n" +
            $"Hybrid beats base ({data.BeatBaseResults.Count}): {beatRate:P1}\n" +
            $"Promote if win rate >= {threshold:P0}\n" +
            $"Hybrid total successes: {data.HybridTotalSuccesses}\n" +
            $"Beat base once (this phase): {data.HybridBeatBaseOnce}";

        if (trainingPhase == TrainingPhase.ReachTarget)
        {
            text += $"\nConsecutive successes: {data.ConsecutiveSuccesses}/{requiredStreak} (auto-promote)";
            text += $"\nAllowed failures before reset: {data.ConsecutiveFailures}/{allowedFailures}";
        }

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
