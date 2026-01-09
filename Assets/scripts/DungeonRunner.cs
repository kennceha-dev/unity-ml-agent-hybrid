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
    public static event Action OnDungeonRegenerating;

    [SerializeField] private Transform basicAgent;
    [SerializeField] private Transform agent;
    [SerializeField] private Transform target;

    private DungeonGenerator generator;
    private NavMeshSurface surface;

    /// <summary>
    /// Guards against multiple simultaneous reset/regeneration calls.
    /// </summary>
    private bool isResetting = false;
    private int lastResetFrame = 0;
    private const int MIN_RESET_FRAME_INTERVAL = 30;  // ~0.5 sec at 60fps to prevent rapid regenerations

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
        lastResetFrame = Time.frameCount;

        // Notify listeners that regeneration is starting (agents should pause)
        OnDungeonRegenerating?.Invoke();

        // Disable NavMeshAgents before regeneration to prevent "no valid NavMesh" warnings
        DisableNavMeshAgents();

        generator.Generate();

        yield return null;

        if (surface != null)
        {
            surface.BuildNavMesh();
        }
        else
        {
            Debug.LogWarning("NavMeshSurface component not found. NavMesh will not be built.");
        }

        // Wait a frame for NavMesh to fully initialize
        yield return null;

        SpawnAgentAndTarget();
        isResetting = false;
    }

    /// <summary>
    /// Disable all NavMeshAgents before NavMesh rebuild to prevent warnings.
    /// </summary>
    private void DisableNavMeshAgents()
    {
        if (basicAgent != null && basicAgent.TryGetComponent<NavMeshAgent>(out var basicNavAgent))
        {
            basicNavAgent.enabled = false;
        }

        if (agent != null && agent.TryGetComponent<NavMeshAgent>(out var agentNavAgent))
        {
            agentNavAgent.enabled = false;
        }
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
        // Use smaller distance if forced into same room (only 1 room exists)
        bool sameRoom = (targetRoom == agentRoom);
        float minDistance = sameRoom ? 0.5f : 2.0f;
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

                // If still too close after all attempts, offset the target position
                if (i == maxAttempts - 1 && distance < minDistance)
                {
                    // Apply a small offset instead of regenerating the dungeon
                    Vector3 offset = new Vector3(1.5f, 0f, 1.5f);
                    targetPosition = agentPosition + offset;
                    Debug.Log($"Spawning target with offset due to small room (distance: {distance:F2})");
                }
            }
            target.position = targetPosition;
        }

        if (agent != null)
        {
            // Disable CharacterController before moving to prevent collision issues
            if (agent.TryGetComponent<CharacterController>(out var cc))
                cc.enabled = false;

            // Reset PhysicsMovement velocity
            if (agent.TryGetComponent<PhysicsMovement>(out var pm))
                pm.ResetVelocity();

            // Find valid NavMesh position and move agent
            if (agent.TryGetComponent<NavMeshAgent>(out var agentNavAgent))
            {
                if (NavMesh.SamplePosition(agentPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.position = hit.position;
                    agentNavAgent.enabled = true;
                    agentNavAgent.Warp(hit.position);
                }
                else
                {
                    agent.position = agentPosition;
                    agentNavAgent.enabled = true;
                }
            }
            else
            {
                agent.position = agentPosition;
            }

            // Re-enable CharacterController after positioning
            if (cc != null)
                cc.enabled = true;
        }

        // Spawn BasicAgent at same position as HybridAgent
        if (basicAgent != null)
        {
            // Disable CharacterController before moving
            if (basicAgent.TryGetComponent<CharacterController>(out var basicCC))
                basicCC.enabled = false;

            // Reset PhysicsMovement velocity
            if (basicAgent.TryGetComponent<PhysicsMovement>(out var basicPM))
                basicPM.ResetVelocity();

            // Find valid NavMesh position and move agent
            if (basicAgent.TryGetComponent<NavMeshAgent>(out var basicNavAgent))
            {
                if (NavMesh.SamplePosition(agentPosition, out NavMeshHit hitBasic, 2f, NavMesh.AllAreas))
                {
                    basicAgent.position = hitBasic.position;
                    basicNavAgent.enabled = true;
                    basicNavAgent.Warp(hitBasic.position);
                    if (basicNavAgent.isOnNavMesh)
                        basicNavAgent.ResetPath();
                }
                else
                {
                    basicAgent.position = agentPosition;
                    basicNavAgent.enabled = true;
                }
            }
            else
            {
                basicAgent.position = agentPosition;
            }

            // Re-enable CharacterController after positioning
            if (basicCC != null)
                basicCC.enabled = true;
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
        if (isResetting || Time.frameCount - lastResetFrame < MIN_RESET_FRAME_INTERVAL)
        {
            Debug.Log($"Reset skipped - too soon since last reset ({Time.frameCount - lastResetFrame} frames)");
            return;
        }

        lastResetFrame = Time.frameCount;
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
