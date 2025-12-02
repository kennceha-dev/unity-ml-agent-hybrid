using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine.InputSystem;

public enum Mode { Training, Inference }

public enum TrainingPhase
{
    BasePathfinding,  // Phase 1: No slimes, no wall penalties - just learn to follow path
    AvoidWalls,       // Phase 2: Add wall collision penalties
    AvoidSlime,       // Phase 3: Add slime penalties and jumping
    MovingTarget      // Phase 4: Target moves (placeholder for later)
}

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
    private float previousDistanceToSteeringTarget;

    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 180f; // degrees per second
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    [SerializeField] private Transform target;
    [SerializeField] private DungeonRunner dungeonRunner;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private Mode mode = Mode.Training;

    [Header("Training Settings")]
    [SerializeField] private TrainingPhase trainingPhase = TrainingPhase.BasePathfinding;
    [SerializeField] private float timePenaltyPerStep = 0.001f; // Negative reward over time to encourage progress

    [SerializeField] private string stickyTag = "Sticky";
    [SerializeField] private string wallTag = "Wall";
    [SerializeField] private string exitTag = "Exit";
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float slipperyFloorRayLength = 5f;

    private float cachedRotationInput;  // rotate left/right (-1..1)
    private float cachedForwardInput;    // forward/backward (-1..1)
    private bool cachedJumpInput = false;
    private float verticalVelocity = 0f;
    private float gravity;
    private float initialJumpVelocity;
    private bool isOnSticky = false;
    private bool isOnWall = false;

    private int episode = 0;

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

        RecalculateJumpValues();
        DungeonRunner.OnDungeonReady += () => isReady = true;
    }

    void OnValidate()
    {
        RecalculateJumpValues();
    }

    private void RecalculateJumpValues()
    {
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        jumpTimeToApex = Mathf.Max(0.05f, jumpTimeToApex);
        gravity = 2f * jumpHeight / (jumpTimeToApex * jumpTimeToApex);
        initialJumpVelocity = gravity * jumpTimeToApex;
    }

    public override void OnEpisodeBegin()
    {
        episode++;
        Debug.Log("New Episode Started");
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
        previousTargetPos = transform.position;
        previousDistanceToSteeringTarget = 0f;
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
            // In phases before AvoidSlime, don't report slime (treat all floor as normal)
            if (trainingPhase < TrainingPhase.AvoidSlime)
            {
                sensor.AddObservation(slipperyFloorRayLength);
                continue;
            }

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

        // NavMesh steering target (next waypoint on the path)
        Vector3 steeringTarget = agent != null && agent.hasPath ? agent.steeringTarget : target.position;
        sensor.AddObservation(steeringTarget); // 3

        // Direction to steering target (normalized) - this tells the agent WHERE to go
        Vector3 dirToSteeringTarget = (steeringTarget - transform.position).normalized;
        sensor.AddObservation(dirToSteeringTarget); // 3

        // Distance to steering target
        sensor.AddObservation(Vector3.Distance(transform.position, steeringTarget)); // 1

        // Is grounded (so agent knows when it can jump)
        sensor.AddObservation(characterController.isGrounded); // 1

        // Current forward direction (so agent knows its orientation)
        sensor.AddObservation(transform.forward); // 3

        CheckForStickyFloor(sensor); // 4

        // 22 total observations
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        cachedRotationInput = actions.ContinuousActions[0];  // rotate left/right (-1..1)
        cachedForwardInput = actions.ContinuousActions[1];   // forward/backward (-1..1)

        // Discrete action for jump: 0 = no jump, 1 = jump
        // Only allow jumping in AvoidSlime phase and above
        int jumpAction = actions.DiscreteActions[0];
        cachedJumpInput = (jumpAction == 1) && (trainingPhase >= TrainingPhase.AvoidSlime);

        // Reward for following the NavMesh path (steering target)
        Vector3 steeringTarget = agent != null && agent.hasPath ? agent.steeringTarget : target.position;
        float distToSteering = Vector3.Distance(transform.position, steeringTarget);

        if (previousDistanceToSteeringTarget > 0f)
        {
            float steeringDelta = previousDistanceToSteeringTarget - distToSteering;
            AddReward(steeringDelta * (0.05f / Mathf.Max(episode / 10, 1))); // Reward for moving closer to the next waypoint
        }
        previousDistanceToSteeringTarget = distToSteering;

        // Reward for facing the right direction (alignment between forward and desired direction)
        Vector3 desiredDir = (steeringTarget - transform.position).normalized;
        float facingAlignment = Vector3.Dot(transform.forward, desiredDir);
        AddReward(facingAlignment * 0.01f); // Small reward for facing the right direction

        // check penalty or reward towards final target
        float dist = Vector3.Distance(transform.position, target.position);
        float delta = previousDistanceToTarget - dist;

        AddReward(delta * 0.01f);
        previousDistanceToTarget = dist;

        // Time penalty to encourage faster completion
        AddReward(-timePenaltyPerStep);

        ApplyPredictionReward();
    }

    // private void OnCollisionEnter(Collision collision)
    // {
    //     if (collision.collider.CompareTag(wallTag))
    //     {
    //         if (!isOnWall)
    //         {
    //             AddReward(-0.2f);
    //             isOnWall = true;
    //         }
    //     }
    //     else if (collision.collider.CompareTag(stickyTag))
    //     {
    //         if (!isOnSticky)
    //         {
    //             AddReward(-0.3f);
    //             isOnSticky = true;
    //         }
    //     }
    // }

    // private void OnCollisionExit(Collision collision)
    // {
    //     if (collision.collider.CompareTag(wallTag))
    //     {
    //         isOnWall = false;
    //     }
    //     else if (collision.collider.CompareTag(stickyTag))
    //     {
    //         isOnSticky = false;
    //     }
    // }

    // exit without kelarin (for now)
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(wallTag))
        {
            isOnWall = false;
        }
        else if (other.CompareTag(stickyTag))
        {
            isOnSticky = false;
        }
        else if (other.CompareTag(exitTag))
        {
            // AddReward(-0.4f);  
            AddReward(-0.1f);
            EndEpisode();
            dungeonRunner.Reset();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(wallTag))
        {
            if (!isOnWall)
            {
                // Only penalize wall hits in Phase 2+ (AvoidWalls and above)
                if (trainingPhase >= TrainingPhase.AvoidWalls)
                {
                    AddReward(-0.2f);
                }
                isOnWall = true;
            }
        }
        else if (other.CompareTag(stickyTag))
        {
            if (!isOnSticky)
            {
                // Only penalize slime in Phase 3+ (AvoidSlime and above)
                if (trainingPhase >= TrainingPhase.AvoidSlime)
                {
                    AddReward(-0.3f);
                }
                isOnSticky = true;
            }
        }
        else if (other.CompareTag(playerTag))
        {
            Debug.Log("Caught the Player!");
            SetReward(1f);
            EndEpisode();
            isReady = false;
            dungeonRunner.Reset();
        }
    }

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

        // Rotate the agent based on rotation input
        float rotationAmount = cachedRotationInput * rotationSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, rotationAmount, 0f);

        // Move forward/backward based on forward input
        Vector3 move = transform.forward * cachedForwardInput;
        move *= moveSpeed * currentSpeedMultiplier;

        // Apply gravity and handle jumping (matching Player physics)
        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            // Jump if requested while grounded
            if (cachedJumpInput)
            {
                verticalVelocity = initialJumpVelocity;
                cachedJumpInput = false;
            }
        }
        else
        {
            float appliedGravity = gravity;

            // Fall faster when descending
            if (verticalVelocity < 0f)
            {
                appliedGravity *= fallGravityMultiplier;
            }

            verticalVelocity -= appliedGravity * Time.fixedDeltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
        }
        move.y = verticalVelocity;

        characterController.Move(move * Time.fixedDeltaTime);

        // Sync NavMesh agent position and update path to moving target (for guidance only)
        if (agent != null && agent.isOnNavMesh)
        {
            // Warp the NavMeshAgent to match CharacterController position
            agent.Warp(transform.position);

            // Recalculate path to target every frame so steeringTarget stays current
            if (target != null)
            {
                agent.SetDestination(target.position);
            }
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
        Vector3 movement = transform.position - previousTargetPos;
        float deltaPos = Vector3.Dot(movement, toTarget);

        if (deltaPos > 0)
            AddReward(0.001f);
        else
            AddReward(-0.001f);

        previousTargetPos = transform.position;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        var discreteActions = actionsOut.DiscreteActions;

        Vector2 moveInput = InputSystem.actions.FindAction("Move").ReadValue<Vector2>();
        continuousActions[0] = moveInput.x; // rotate left/right
        continuousActions[1] = moveInput.y; // forward/backward

        // Jump input (space bar or gamepad south button)
        bool jumpPressed = InputSystem.actions.FindAction("Jump").IsPressed();
        discreteActions[0] = jumpPressed ? 1 : 0;
    }

    public void ApplySpeedMultiplier(Object source, float multiplier)
    {
        if (source == null)
        {
            return;
        }

        // // Only apply slow effects in AvoidSlime phase and above
        // if (trainingPhase < TrainingPhase.AvoidSlime && multiplier < 1f)
        // {
        //     currentSpeedMultiplier = 1f;
        //     return;
        // }

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