using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

public enum Mode { Training, Inference }

[RequireComponent(typeof(CharacterController))]
public class HybridAgent : Agent, ISpeedModifiable
{
    private CharacterController characterController;
    private NavMeshAgent agent;
    private bool isReady;
    private float baseMoveSpeed;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new Dictionary<Object, float>();

    private Vector3 previousTargetPos;
    private float previousDistanceToTarget;

    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float groundedGravity = -5f;

    [SerializeField] private Transform target;
    [SerializeField] private DungeonRunner dungeonRunner;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private Mode mode = Mode.Training;

    [SerializeField] private string stickyTag = "Sticky";
    [SerializeField] private string wallTag = "Wall";
    [SerializeField] private string exitTag = "Exit";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float slipperyFloorRayLength = 5f;

    private float cachedHorizontalInput; // left/right
    private float cachedVerticalInput;   // forward/backward
    private float verticalVelocity = 0f;
    private bool isOnSticky = false;
    private bool isOnWall = false;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            baseMoveSpeed = agent.speed;
            agent.updatePosition = false;
            agent.updateRotation = false;
        }

        DungeonRunner.OnDungeonReady += () => isReady = true;
    }

    public override void OnEpisodeBegin()
    {
        Debug.Log("New Episode Started");
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
        previousTargetPos = target.position;
        isOnSticky = false;
        isOnWall = false;
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
        cachedHorizontalInput = actions.ContinuousActions[0]; // left/right (-1..1)
        cachedVerticalInput = actions.ContinuousActions[1];   // forward/backward (-1..1)

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
            if (!isOnWall)
            {
                AddReward(-0.2f);
                isOnWall = true;
            }
        }
        else if (collision.collider.CompareTag(stickyTag))
        {
            if (!isOnSticky)
            {
                AddReward(-0.3f);
                isOnSticky = true;
            }
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

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider.CompareTag(wallTag))
        {
            isOnWall = false;
        }
        else if (collision.collider.CompareTag(stickyTag))
        {
            isOnSticky = false;
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

        // Move using CharacterController (left/right + forward/backward)
        Vector3 move = transform.right * cachedHorizontalInput + transform.forward * cachedVerticalInput;
        move *= moveSpeed * currentSpeedMultiplier;

        // Apply gravity
        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;
        }
        move.y = verticalVelocity;

        characterController.Move(move * Time.fixedDeltaTime);

        // Keep navmesh synced (optional)
        if (agent != null)
        {
            agent.nextPosition = transform.position;
        }

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
        Vector2 moveInput = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
        continuousActions[0] = moveInput.x; // left/right
        continuousActions[1] = moveInput.y; // forward/backward
    }

    public void ApplySpeedMultiplier(Object source, float multiplier)
    {
        if (source == null)
        {
            return;
        }

        speedModifiers[source] = Mathf.Clamp(multiplier, 0.01f, 10f);
        RecalculateSpeedMultiplier();
    }

    public void RemoveSpeedMultiplier(Object source)
    {
        if (source == null)
        {
            return;
        }

        if (speedModifiers.Remove(source))
        {
            RecalculateSpeedMultiplier();
        }
    }

    private void RecalculateSpeedMultiplier()
    {
        currentSpeedMultiplier = 1f;
        foreach (float modifier in speedModifiers.Values)
        {
            currentSpeedMultiplier *= modifier;
        }
        currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 0.01f, 10f);
        UpdateAgentSpeed();
    }

    private void UpdateAgentSpeed()
    {
        if (agent == null)
        {
            return;
        }

        if (baseMoveSpeed <= 0f)
        {
            baseMoveSpeed = agent.speed;
        }

        agent.speed = baseMoveSpeed * currentSpeedMultiplier;
    }
}