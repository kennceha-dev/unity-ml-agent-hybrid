using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public enum Mode { Training, Inference }

[RequireComponent(typeof(NavMeshAgent))]
public class HybridAgent : Agent
{
    private NavMeshAgent agent;

    [SerializeField] private Transform target;
    [SerializeField] private DungeonRunner dungeonRunner;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private Mode mode = Mode.Training;
    [SerializeField] private string stickyTag = "Sticky";
    [SerializeField] private float slipperyFloorRayLength = 5f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public override void OnEpisodeBegin()
    {
        // do smth pas new episode, kalo perlu
        Debug.Log("New Episode Started");
    }

    private void CheckForStickyFloor(VectorSensor sensor)
    {
        Vector3[] cardinalDirections = {
            transform.forward,
            -transform.forward,
            transform.right,
            -transform.right,
        };
        foreach (var cardinalDir in cardinalDirections)
        {
            Vector3 floorOrigin = transform.position
                                + cardinalDir
                                + Vector3.up;

            Vector3 floorRayDir = (cardinalDir + (Vector3.down * 1.5f)).normalized;

            if (Physics.Raycast(floorOrigin, floorRayDir, out RaycastHit floorHit, slipperyFloorRayLength, floorLayer))
            {
                bool isSticky = floorHit.collider.CompareTag(stickyTag);
                float obs = isSticky ? floorHit.distance : slipperyFloorRayLength;
                sensor.AddObservation(obs);
            }
            else
            {
                sensor.AddObservation(slipperyFloorRayLength);
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // self position
        sensor.AddObservation(transform.position); // 3
        // target position
        sensor.AddObservation(target.position); // 3
        // distance to target
        sensor.AddObservation(Vector3.Distance(transform.position, target.position)); // 1
        // navmesh next position
        sensor.AddObservation(agent.nextPosition); // 3

        CheckForStickyFloor(sensor); // 4
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // var i = -1;

        // float sideMovement = actions.ContinuousActions[++i];
        // float forwardMovement = actions.ContinuousActions[++i];
        // Vector3 move = transform.right * sideMovement + transform.forward * forwardMovement;

        // somehow apply movement ke agentnya, biasanya simpen dl di variable terus di FixedUpdate baru dihandle 
    }

    private void CheckIfReachedTarget()
    {
        // misal nyampe
        if (Vector3.Distance(transform.position, target.position) <= 1f)
        {
            SetReward(1f);
            EndEpisode();
        }
    }

    private void FixedUpdate()
    {
        if (agent != null && agent.isOnNavMesh && agent.isActiveAndEnabled && target != null)
        {
            agent.SetDestination(target.position);
        }

        CheckIfReachedTarget();
    }
}