using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;

[RequireComponent(typeof(DungeonGenerator))]
[RequireComponent(typeof(NavMeshSurface))]
public class DungeonRunner : MonoBehaviour
{
    public static event Action OnDungeonReady;

    [SerializeField] private Transform agent;
    [SerializeField] private Transform target;

    private DungeonGenerator generator;
    private NavMeshSurface surface;

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
            StartCoroutine(GenerateAndSpawn());
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
            CharacterController cc = agent.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false; // IMPORTANT
                agent.position = generator.GetRandomPositionInRoom(agentRoom);
                cc.enabled = true;
            }
            else
            {
                agent.position = generator.GetRandomPositionInRoom(agentRoom);
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

    public void Reset() => StartCoroutine(GenerateAndSpawn());
}
