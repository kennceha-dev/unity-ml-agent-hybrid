using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NavMeshAgent))]
public class HybridAgent : Agent, ISpeedModifiable
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float rotationSpeed = 180f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float jumpTimeToApex = 0.28f;
    [SerializeField] private float fallGravityMultiplier = 2.5f;
    [SerializeField] private float groundedGravity = -5f;
    [SerializeField] private float terminalVelocity = -45f;

    [Header("References")]
    [SerializeField] private Transform target;
    [SerializeField] private DungeonRunner dungeonRunner;
    [SerializeField] private LayerMask floorLayer;
    [SerializeField] private LayerMask wallLayer;

    private CharacterController characterController;
    private NavMeshAgent navAgent;
    private bool isReady;
    private float baseMoveSpeed;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new();

    private float previousDistanceToTarget;
    private float previousDistanceToSteeringTarget;
    private Vector3 lastSignificantPosition;
    private int stuckCounter;
    private int episode;

    private float cachedRotationInput;
    private float cachedForwardInput;
    private bool cachedJumpInput;
    private float verticalVelocity;
    private float gravity;
    private float initialJumpVelocity;
    private bool isOnSticky;
    private bool isOnWall;

    #region Unity Lifecycle

    private void Start()
    {
        characterController = GetComponent<CharacterController>();
        navAgent = GetComponent<NavMeshAgent>();

        if (navAgent != null)
        {
            baseMoveSpeed = navAgent.speed;
            navAgent.updatePosition = false;
            navAgent.updateRotation = false;
        }

        RecalculateJumpValues();
        DungeonRunner.OnDungeonReady += () => isReady = true;
    }

    private void OnValidate() => RecalculateJumpValues();

    private void FixedUpdate()
    {
        if (!isReady) return;

        if (navAgent != null && !navAgent.isOnNavMesh)
        {
            AddReward(-0.5f);
            HandleEpisodeEnd();
            return;
        }

        ApplyRotation();
        ApplyMovement();
        SyncNavMeshAgent();
    }

    #endregion

    #region ML-Agents Overrides

    public override void OnEpisodeBegin()
    {
        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
        previousDistanceToSteeringTarget = 0f;
        lastSignificantPosition = transform.position;
        stuckCounter = 0;
        isOnSticky = false;
        isOnWall = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 steeringTarget = GetSteeringTarget();
        Vector3 dirToSteeringTarget = (steeringTarget - transform.position).normalized;

        sensor.AddObservation(transform.position);
        sensor.AddObservation(target.position);
        sensor.AddObservation(Vector3.Distance(transform.position, target.position));
        sensor.AddObservation(steeringTarget);
        sensor.AddObservation(dirToSteeringTarget);
        sensor.AddObservation(Vector3.Distance(transform.position, steeringTarget));
        sensor.AddObservation(characterController.isGrounded);
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(isOnSticky);
        sensor.AddObservation(isOnWall);

        CollectWallObservations(sensor);
        CollectFloorObservations(sensor);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        ProcessActions(actions);

        Vector3 steeringTarget = GetSteeringTarget();
        Vector3 desiredDir = (steeringTarget - transform.position).normalized;
        float facingAlignment = Vector3.Dot(transform.forward, desiredDir);

        RewardSteeringProgress(steeringTarget);
        RewardRotation(desiredDir);
        RewardAlignment(facingAlignment);
        PenalizeWallProximity();
        RewardTargetProgress();
        PenalizeStuck();

        AddReward(-GameManager.Instance.TimePenaltyPerStep);
    }

    #endregion

    #region Action Processing

    private void ProcessActions(ActionBuffers actions)
    {
        cachedRotationInput = actions.ContinuousActions[0];
        cachedForwardInput = (actions.ContinuousActions[1] + 1f) * 0.5f;
        cachedJumpInput = (actions.DiscreteActions[0] == 1) && GameManager.Instance.CanJump;
    }

    #endregion

    #region Rewards

    private void RewardSteeringProgress(Vector3 steeringTarget)
    {
        float distToSteering = Vector3.Distance(transform.position, steeringTarget);

        if (previousDistanceToSteeringTarget > 0f)
        {
            float delta = previousDistanceToSteeringTarget - distToSteering;
            AddReward(delta * 0.1f);
        }

        previousDistanceToSteeringTarget = distToSteering;
    }

    private void RewardRotation(Vector3 desiredDir)
    {
        float turnDirection = Vector3.Cross(transform.forward, desiredDir).y;

        if (Mathf.Abs(turnDirection) > 0.1f)
        {
            float correctTurn = Mathf.Sign(turnDirection) * cachedRotationInput;
            if (correctTurn > 0)
                AddReward(0.01f * Mathf.Abs(cachedRotationInput));
        }
    }

    private void RewardAlignment(float facingAlignment)
    {
        if (cachedForwardInput > 0.1f)
            AddReward(facingAlignment * 0.03f * cachedForwardInput);

        if (facingAlignment < 0f && cachedForwardInput > 0.3f)
            AddReward(-0.02f);
    }

    private void PenalizeWallProximity()
    {
        if (!GameManager.Instance.ShouldPenalizeWalls) return;

        float minWallDist = GetMinWallDistance();

        if (minWallDist < 2.0f)
        {
            float normalizedDist = minWallDist / 2.0f;
            float penalty = (1f - normalizedDist) * (1f - normalizedDist) * 0.05f;
            AddReward(-penalty);
        }

        if (minWallDist > 2.5f)
            AddReward(0.005f);
    }

    private void RewardTargetProgress()
    {
        float dist = Vector3.Distance(transform.position, target.position);
        float delta = previousDistanceToTarget - dist;
        AddReward(delta * 0.05f);

        if (previousDistanceToTarget <= GameManager.Instance.CloseDistanceThreshold)
        {
            if (dist > previousDistanceToTarget)
                AddReward(GameManager.Instance.CloseMoveAwayPenalty);
            else if (dist < previousDistanceToTarget)
                AddReward(GameManager.Instance.CloseMoveCloserReward);
        }

        previousDistanceToTarget = dist;
    }

    private void PenalizeStuck()
    {
        float dist = Vector3.Distance(transform.position, lastSignificantPosition);

        if (dist < 0.5f)
        {
            stuckCounter++;
            if (stuckCounter > 50)
                AddReward(-0.05f);
        }
        else
        {
            stuckCounter = 0;
            lastSignificantPosition = transform.position;
            AddReward(0.01f);
        }
    }

    #endregion

    #region Observations

    private void CollectWallObservations(VectorSensor sensor)
    {
        Vector3[] directions = { transform.forward, -transform.forward, transform.right, -transform.right };

        foreach (var dir in directions)
        {
            if (!GameManager.Instance.ShouldPenalizeWalls)
            {
                sensor.AddObservation(GameManager.Instance.WallRayLength);
                continue;
            }

            bool hit = Physics.Raycast(transform.position, dir, out RaycastHit hitInfo, GameManager.Instance.WallRayLength, wallLayer);
            sensor.AddObservation(hit ? hitInfo.distance : GameManager.Instance.WallRayLength);
            Debug.DrawLine(transform.position, transform.position + dir * GameManager.Instance.WallRayLength, Color.red);
        }
    }

    private void CollectFloorObservations(VectorSensor sensor)
    {
        Vector3[] directions = { transform.forward, -transform.forward, transform.right, -transform.right };

        foreach (var dir in directions)
        {
            if (!GameManager.Instance.ShouldPenalizeSlime)
            {
                sensor.AddObservation(GameManager.Instance.SlipperyFloorRayLength);
                continue;
            }

            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 rayDir = (dir + Vector3.down * 1.5f).normalized;

            bool hit = Physics.Raycast(origin, rayDir, out RaycastHit hitInfo, GameManager.Instance.SlipperyFloorRayLength, floorLayer);

            if (hit && hitInfo.collider.CompareTag(GameManager.Instance.StickyTag))
                sensor.AddObservation(hitInfo.distance);
            else
                sensor.AddObservation(GameManager.Instance.SlipperyFloorRayLength);

            Debug.DrawLine(origin, origin + rayDir * GameManager.Instance.SlipperyFloorRayLength, Color.blue);
        }
    }

    #endregion

    #region Movement

    private void ApplyRotation()
    {
        float amount = cachedRotationInput * rotationSpeed * Time.fixedDeltaTime;
        transform.Rotate(0f, amount, 0f);
    }

    private void ApplyMovement()
    {
        Vector3 move = cachedForwardInput * currentSpeedMultiplier * moveSpeed * transform.forward;

        if (characterController.isGrounded)
        {
            verticalVelocity = groundedGravity;

            if (cachedJumpInput)
            {
                verticalVelocity = initialJumpVelocity;
                cachedJumpInput = false;
            }
        }
        else
        {
            float appliedGravity = verticalVelocity < 0f ? gravity * fallGravityMultiplier : gravity;
            verticalVelocity -= appliedGravity * Time.fixedDeltaTime;
            verticalVelocity = Mathf.Max(verticalVelocity, terminalVelocity);
        }

        move.y = verticalVelocity;
        characterController.Move(move * Time.fixedDeltaTime);
    }

    private void SyncNavMeshAgent()
    {
        if (navAgent == null || !navAgent.isOnNavMesh) return;

        navAgent.Warp(transform.position);

        if (target != null)
            navAgent.SetDestination(target.position);
    }

    private void RecalculateJumpValues()
    {
        jumpHeight = Mathf.Max(0.1f, jumpHeight);
        jumpTimeToApex = Mathf.Max(0.05f, jumpTimeToApex);
        gravity = 2f * jumpHeight / (jumpTimeToApex * jumpTimeToApex);
        initialJumpVelocity = gravity * jumpTimeToApex;
    }

    #endregion

    #region Collision

    private void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.CompareTag(GameManager.Instance.WallTag))
        {
            HandleWallEnter();
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.StickyTag))
        {
            HandleStickyEnter();
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.PlayerTag))
        {
            HandlePlayerCaught();
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.ExitTag))
        {
            AddReward(-0.1f);
            HandleEpisodeEnd();
        }
    }

    private void OnCollisionStay(Collision other)
    {
        if (other.gameObject.CompareTag(GameManager.Instance.WallTag) && GameManager.Instance.ShouldPenalizeWalls)
        {
            AddReward(-0.05f);

            if (cachedForwardInput > 0.2f)
                AddReward(-0.03f);
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.StickyTag) && GameManager.Instance.ShouldPenalizeSlime)
        {
            AddReward(-0.02f);
        }
    }

    private void OnCollisionExit(Collision other)
    {
        if (other.gameObject.CompareTag(GameManager.Instance.WallTag))
        {
            isOnWall = false;
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.StickyTag))
        {
            isOnSticky = false;
        }
    }

    private void HandleWallEnter()
    {
        if (GameManager.Instance.ShouldPenalizeWalls)
            AddReward(-0.4f);

        isOnWall = true;
    }

    private void HandleStickyEnter()
    {
        if (GameManager.Instance.ShouldPenalizeSlime)
            AddReward(-0.3f);

        isOnSticky = true;
    }

    private void HandlePlayerCaught()
    {
        // SetReward(1f);
        AddReward(5f); // coba add?
        HandleEpisodeEnd(true);
    }

    #endregion

    #region Utility

    private Vector3 GetSteeringTarget() =>
        navAgent != null && navAgent.hasPath ? navAgent.steeringTarget : target.position;

    private float GetMinWallDistance()
    {
        float minDist = GameManager.Instance.WallRayLength;
        Vector3[] directions = { transform.forward, -transform.forward, transform.right, -transform.right };

        foreach (var dir in directions)
        {
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, GameManager.Instance.WallRayLength, wallLayer))
                minDist = Mathf.Min(minDist, hit.distance);
        }

        return minDist;
    }

    private void HandleEpisodeEnd(bool setNotReady = false)
    {
        EndEpisode();

        if (setNotReady)
            isReady = false;

        GameManager.Instance.IncrementSeed();

        if (dungeonRunner != null)
        {
            dungeonRunner.SetSeed(GameManager.Instance.CurrentSeed);
            dungeonRunner.Reset();
        }
    }

    #endregion

    #region Speed Modifiers

    public void ApplySpeedMultiplier(Object source, float multiplier)
    {
        if (source == null) return;

        speedModifiers[source] = Mathf.Clamp(multiplier, 0.01f, 10f);
        RecalculateSpeedMultiplier();
    }

    public void RemoveSpeedMultiplier(Object source)
    {
        if (source == null) return;

        if (speedModifiers.Remove(source))
            RecalculateSpeedMultiplier();
    }

    private void RecalculateSpeedMultiplier()
    {
        currentSpeedMultiplier = 1f;

        foreach (float modifier in speedModifiers.Values)
            currentSpeedMultiplier *= modifier;

        currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 0.01f, 10f);
        UpdateAgentSpeed();
    }

    private void UpdateAgentSpeed()
    {
        if (navAgent == null) return;

        if (baseMoveSpeed <= 0f)
            baseMoveSpeed = navAgent.speed;

        navAgent.speed = baseMoveSpeed * currentSpeedMultiplier;
    }

    #endregion
}