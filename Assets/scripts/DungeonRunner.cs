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
        if (generator == null)
        {
            Debug.LogError("DungeonGenerator component not found!");
            yield break;
        }

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
    }

    // void SpawnAgentAndTarget()
    // {
    //     var rooms = generator.GetRooms();
    //     if (rooms.Count < 2)
    //     {
    //         Debug.Log("Not enough rooms to spawn agent and target separately.");
    //         return;
    //     }

    //     Room agentRoom = generator.GetRandomRoom();
    //     Room targetRoom = generator.GetRandomRoom();

    //     while (targetRoom == agentRoom && rooms.Count > 1)
    //     {
    //         targetRoom = generator.GetRandomRoom();
    //     }

    //     if (agent != null)
    //     {
    //         agent.position = generator.GetRandomPositionInRoom(agentRoom);
    //     }

    //     if (target != null)
    //     {
    //         target.position = generator.GetRandomPositionInRoom(targetRoom);
    //     }

    //     OnDungeonReady?.Invoke();
    // }

    void SpawnAgentAndTarget()
    {
        var rooms = generator.GetRooms();

        Room agentRoom = generator.GetRandomRoom();
        Room targetRoom = generator.GetRandomRoom();

        while (targetRoom == agentRoom && rooms.Count > 1)
            targetRoom = generator.GetRandomRoom();

        if (agent != null)
        {
            Vector3 position = generator.GetRandomPositionInRoom(agentRoom);
            if (agent.TryGetComponent<CharacterController>(out var cc))
            {
                cc.enabled = false;
                agent.position = position;
                cc.enabled = true;
            }
            else
            {
                agent.position = position;
            }

            if (basicAgent != null && basicAgent.TryGetComponent<NavMeshAgent>(out var basicNavAgent))
            {
                basicNavAgent.Warp(position);
                basicNavAgent.ResetPath();
            }
            else if (basicAgent != null)
            {
                basicAgent.position = position;
            }
        }

        if (target != null)
            target.position = generator.GetRandomPositionInRoom(targetRoom);

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
        // During training, if dungeon already exists, just respawn agent/target
        if (dungeonGenerated && IsTrainingPhase())
        {
            SpawnAgentAndTarget();
        }
        else
        {
            StartCoroutine(GenerateAndSpawn());
        }
    }

    /// <summary>
    /// Force full dungeon regeneration regardless of training phase.
    /// </summary>
    public void ForceRegenerate()
    {
        StartCoroutine(GenerateAndSpawn());
    }

    /// <summary>
    /// Check if we're currently in a training phase where we want to keep the same map.
    /// </summary>
    private bool IsTrainingPhase()
    {
        if (GameManager.Instance == null)
            return false;

        // All phases except MovingTarget are considered training phases where we keep the map
        var phase = GameManager.Instance.CurrentTrainingPhase;
        return phase == TrainingPhase.ReachTarget ||
               phase == TrainingPhase.BasePathfinding ||
               phase == TrainingPhase.FullPathfinding ||
               phase == TrainingPhase.AvoidSlime;
    }
}
