using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(DungeonGenerator))]
[RequireComponent(typeof(NavMeshSurface))]
public class DungeonRunner : MonoBehaviour
{
    public static event Action OnDungeonReady;

    [SerializeField] private Transform basicAgent;
    [SerializeField] private Transform agent;
    [SerializeField] private Transform target;

    private DungeonGenerator generator;
    private NavMeshSurface surface;

    /// <summary>
    /// Tracks whether a dungeon has been generated at least once.
    /// Used to decide if we can skip regeneration during training.
    /// </summary>
    private bool dungeonGenerated = false;

    /// <summary>
    /// Guards against multiple simultaneous reset/regeneration calls.
    /// </summary>
    private bool isResetting = false;
    private float lastResetTime = 0f;
    private const float MIN_RESET_INTERVAL = 0.5f;

    void Awake()
    {
        generator = GetComponent<DungeonGenerator>();
        surface = GetComponent<NavMeshSurface>();

        if (surface != null)
        {
            surface.collectObjects = CollectObjects.All;

            int floorLayer = LayerMask.NameToLayer("Floor");
            if (floorLayer != -1)
            {
                surface.layerMask = 1 << floorLayer;
            }
            else
            {
                Debug.LogWarning("Floor layer not found. Please create a 'Floor' layer in Unity's Layer settings.");
            }
        }
    }

    void Start()
    {
        StartCoroutine(GenerateAndSpawn());
        GameManager.OnTrainingPhaseChanged += OnPhaseChanged;
    }

    void OnDestroy()
    {
        GameManager.OnTrainingPhaseChanged -= OnPhaseChanged;
    }

    private void OnPhaseChanged(TrainingPhase newPhase)
    {
        Debug.Log($"Training phase changed to {newPhase}, regenerating dungeon...");
        GameManager.Instance.IncrementSeed();
        SetSeed(GameManager.Instance.CurrentSeed);
        ForceRegenerate();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            GameManager.Instance.IncrementSeed();
            SetSeed(GameManager.Instance.CurrentSeed);
            ForceRegenerate();
        }
    }

    public IEnumerator GenerateAndSpawn()
    {
        if (isResetting)
        {
            Debug.Log("GenerateAndSpawn skipped - already resetting");
            yield break;
        }

        if (generator == null)
        {
            Debug.LogError("DungeonGenerator component not found!");
            yield break;
        }

        isResetting = true;
        lastResetTime = Time.time;

        generator.Generate();
        dungeonGenerated = true;

        yield return null;

        if (surface != null)
        {
            surface.BuildNavMesh();
        }
        else
        {
            Debug.LogWarning("NavMeshSurface component not found. NavMesh will not be built.");
        }

        SpawnAgentAndTarget();
        isResetting = false;
    }

    void SpawnAgentAndTarget()
    {
        var rooms = generator.GetRooms();

        Room agentRoom = generator.GetRandomRoom();
        Room targetRoom = generator.GetRandomRoom();

        while (targetRoom == agentRoom && rooms.Count > 1)
            targetRoom = generator.GetRandomRoom();

        Vector3 agentPosition = Vector3.zero;
        Vector3 targetPosition = Vector3.zero;

        // Minimum distance to prevent spawning on top of each other
        const float minDistance = 2.0f;
        const int maxAttempts = 20;

        if (agent != null)
        {
            agentPosition = generator.GetRandomPositionInRoom(agentRoom);
        }

        if (target != null)
        {
            // Try to find a target position that's far enough from the agent
            for (int i = 0; i < maxAttempts; i++)
            {
                targetPosition = generator.GetRandomPositionInRoom(targetRoom);
                float distance = Vector3.Distance(agentPosition, targetPosition);
                if (distance >= minDistance)
                    break;

                // If same room and still too close, try a different position
                if (i == maxAttempts - 1 && distance < minDistance)
                {
                    Debug.LogWarning($"Could not find spawn positions with minimum distance after {maxAttempts} attempts. Forcing dungeon regeneration.");
                    StartCoroutine(GenerateAndSpawn());
                    return;
                }
            }
            target.position = targetPosition;
        }

        if (agent != null)
        {
            if (agent.TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = false;
                agent.position = agentPosition;
                cc.enabled = true;
            }
            else
            {
                agent.position = agentPosition;
            }

            if (basicAgent != null && basicAgent.TryGetComponent<NavMeshAgent>(out var basicNavAgent))
            {
                basicNavAgent.Warp(agentPosition);
                if (basicNavAgent.isOnNavMesh)
                    basicNavAgent.ResetPath();
            }
            else if (basicAgent != null)
            {
                basicAgent.position = agentPosition;
            }
        }

        OnDungeonReady?.Invoke();
    }

    /// <summary>
    /// Set the seed on the internal DungeonGenerator (for deterministic generation).
    /// </summary>
    public void SetSeed(int seed)
    {
        if (generator != null)
            generator.SetSeed(seed);
    }

    /// <summary>
    /// Reset the dungeon. During training phases, only respawn agent and target
    /// without regenerating the entire dungeon map.
    /// </summary>
    public void Reset()
    {
        // Prevent rapid successive resets
        if (isResetting || Time.time - lastResetTime < MIN_RESET_INTERVAL)
        {
            Debug.Log($"Reset skipped - too soon since last reset ({Time.time - lastResetTime:F2}s)");
            return;
        }

        lastResetTime = Time.time;
        StartCoroutine(GenerateAndSpawn());
    }

    /// <summary>
    /// Force full dungeon regeneration regardless of training phase.
    /// </summary>
    public void ForceRegenerate()
    {
        // Prevent rapid successive regenerations
        if (isResetting)
        {
            Debug.Log("ForceRegenerate skipped - already resetting");
            return;
        }
        StartCoroutine(GenerateAndSpawn());
    }
}
