using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

public enum Mode { Training, Inference }

[RequireComponent(typeof(NavMeshAgent))]
public class HybridAgent : Agent
{
    private NavMeshAgent agent;
    private bool isReady;

    private Vector3 previousTargetPos;
    private float previousDistanceToTarget;

    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float rotateSpeed = 120f;

    [SerializeField] private Transform target;
    [SerializeField] private DungeonRunner dungeonRunner;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private Mode mode = Mode.Training;

    [SerializeField] private string stickyTag = "Sticky";
    [SerializeField] private string wallTag = "Wall";
    [SerializeField] private string exitTag = "Exit";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float slipperyFloorRayLength = 5f;

    private float cachedMoveInput;
    private float cachedTurnInput;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updatePosition = false;
        agent.updateRotation = false;

        DungeonRunner.OnDungeonReady += () => isReady = true;
    }

    public override void OnEpisodeBegin()
    {
        Debug.Log("New Episode Started");
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
        previousTargetPos = target.position;
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

        // 14 total observations
    }

    // public override void OnActionReceived(ActionBuffers actions)
    // {
    // var i = -1;

    // float sideMovement = actions.ContinuousActions[++i];
    // float forwardMovement = actions.ContinuousActions[++i];
    // Vector3 move = transform.right * sideMovement + transform.forward * forwardMovement;

    // somehow apply movement ke agentnya
    // ganti yang dari agent.setdestination jadi smth else yg bs ad physics
    // biasanya simpen dl di variable terus di FixedUpdate baru dihandle 
    // }

    public override void OnActionReceived(ActionBuffers actions)
    {
        cachedMoveInput = actions.ContinuousActions[0]; // -1..1
        cachedTurnInput = actions.ContinuousActions[1]; // -1..1

        // Rotate
        // transform.Rotate(Vector3.up, turnInput * rotateSpeed * Time.deltaTime);

        // // Move
        // Vector3 move = transform.forward * moveInput * moveSpeed * Time.deltaTime;
        // transform.position += move;

        // check penalty or reward towards target
        float dist = Vector3.Distance(transform.position, target.position);
        float delta = previousDistanceToTarget - dist;

        AddReward(delta * 0.01f);
        previousDistanceToTarget = dist;

        ApplyPredictionReward();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag(wallTag))
        {
            AddReward(-0.2f);
        }
        else if (collision.collider.CompareTag(stickyTag))
        {
            AddReward(-0.3f);
        }
        else if (collision.collider.CompareTag(playerTag))
        {
            Debug.Log("Collided with Player");
            SetReward(1f);
            EndEpisode();
            isReady = false;
            dungeonRunner.Reset();
        }
    }

    // exit without kelarin (for now)
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(exitTag))
        {
            // AddReward(-0.4f);  
            AddReward(-0.1f);
            EndEpisode();
            dungeonRunner.Reset();
        }
    }

    // private void CheckIfReachedTarget()
    // {
    //     if (Vector3.Distance(transform.position, target.position) <= 1f)
    //     {

    //     }
    // }

    private void FixedUpdate()
    {
        if (!isReady) return;

        if (agent != null && !agent.isOnNavMesh)
        {
            AddReward(-0.5f);
            EndEpisode();
            dungeonRunner.Reset();
            return;
        }

        // Rotate
        transform.Rotate(Vector3.up, cachedTurnInput * rotateSpeed * Time.fixedDeltaTime);

        // Move
        Vector3 move = transform.forward * cachedMoveInput * moveSpeed * Time.fixedDeltaTime;
        transform.position += move;

        // Keep navmesh synced (optional)
        agent.nextPosition = transform.position;

        // if (agent != null && agent.isOnNavMesh && target != null)
        // {
        //     agent.SetDestination(target.position);
        // }

        // if (agent != null && !agent.isOnNavMesh)
        // {
        //     AddReward(-0.5f);
        //     EndEpisode();
        //     dungeonRunner.Reset();
        //     return;
        // }

        // CheckIfReachedTarget();
    }

    private void ApplyPredictionReward()
    {
        Vector3 toTarget = (target.position - transform.position).normalized;
        Vector3 forward = transform.forward;

        float alignment = Vector3.Dot(forward, toTarget);

        // Reward facing the target
        AddReward(alignment * 0.002f);

        // Reward actual movement toward the target
        float deltaPos = Vector3.Dot((transform.position - previousTargetPos), toTarget);

        if (deltaPos > 0)
            AddReward(0.001f);
        else
            AddReward(-0.001f);

        previousTargetPos = transform.position;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().x;
        continuousActions[1] = InputSystem.actions.FindAction("Move").ReadValue<Vector2>().y;
    }
}