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

    [Header("Timeout")]
    [SerializeField] private float catchUpTimeout = 60f;
    [SerializeField] private float progressThreshold = 0.05f;
    [SerializeField] private float regressionThreshold = 1f;
    [SerializeField] private float baseCatchTolerance = 5f;
    [SerializeField] private float wallContactTimeout = 5f;

    [Header("Reward Timeout")]
    [SerializeField] private float rewardTimeoutThreshold = -2f;

    [Header("Action Filtering")]
    [SerializeField, Range(0f, 1f)] private float actionSmoothing = 0.2f;
    [SerializeField, Range(0f, 1f)] private float actionDeadZone = 0.1f;
    private enum ProgressState { Unknown, Progressing, NoProgress, Regressing }
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

    private float cachedForwardInput;
    private float cachedStrafeInput;
    private bool cachedJumpInput;
    private float verticalVelocity;
    private float gravity;
    private float initialJumpVelocity;
    private bool isOnSticky;
    private bool isOnWall;
    private float timeoutTimer;
    private bool isInTimeout;
    private float wallContactTimer;
    private float baseCatchElapsed;
    private float previousPathRemainingDistance = -1f;
    private float previousSteeringDistance = -1f;
    private float lastEpisodeReward;
    private float bestEpisodeReward = float.MinValue;
    private Vector2 smoothedMove;

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();

        // Disable NavMeshAgent rotation as early as possible
        navAgent = GetComponent<NavMeshAgent>();
        if (navAgent != null)
        {
            navAgent.updateRotation = false;
            navAgent.updatePosition = false;
        }
    }

    private void Start()
    {
        characterController = GetComponent<CharacterController>();

        if (navAgent != null)
        {
            baseMoveSpeed = navAgent.speed;
        }

        RecalculateJumpValues();
        DungeonRunner.OnDungeonReady += OnDungeonReady;
        DungeonRunner.OnDungeonRegenerating += OnDungeonRegenerating;
        GameManager.OnBasicAgentReachedTargetEvent += OnBasicAgentReachedTarget;
    }

    private void OnDestroy()
    {
        DungeonRunner.OnDungeonReady -= OnDungeonReady;
        DungeonRunner.OnDungeonRegenerating -= OnDungeonRegenerating;
        GameManager.OnBasicAgentReachedTargetEvent -= OnBasicAgentReachedTarget;
    }

    private void OnDungeonReady()
    {
        isReady = true;
    }

    private void OnDungeonRegenerating()
    {
        isReady = false;
    }

    private void OnBasicAgentReachedTarget()
    {
        // BasicAgent reached target - start timeout for RL agent to catch up
        if (!isInTimeout)
        {
            isInTimeout = true;
            timeoutTimer = catchUpTimeout;
            baseCatchElapsed = 0f;
            previousPathRemainingDistance = -1f;
            previousSteeringDistance = -1f;
        }
    }

    private void OnValidate() => RecalculateJumpValues();

    private void FixedUpdate()
    {
        if (!isReady) return;

        if (navAgent != null && !navAgent.isOnNavMesh)
        {
            LoggedAddReward(-0.5f, "Off NavMesh");
            HandleEpisodeEnd(false, false, false);
            return;
        }

        if (HasRewardDroppedBelowThreshold())
        {
            LoggedAddReward(-1f, "Reward timeout");
            HandleEpisodeEnd(false, false, false);
            return;
        }

        ApplyMovement();
        SyncNavMeshAgent();

        if (isInTimeout)
        {
            baseCatchElapsed += Time.fixedDeltaTime;
            if (HandleProgressBasedTimeout())
                return;
        }
        else
        {
            baseCatchElapsed = 0f;
        }
    }

    #endregion

    #region ML-Agents Overrides

    public override void OnEpisodeBegin()
    {
        episode++;
        transform.rotation = Quaternion.identity;

        previousDistanceToTarget = Vector3.Distance(transform.position, target.position);
        previousDistanceToSteeringTarget = 0f;
        lastSignificantPosition = transform.position;
        stuckCounter = 0;
        isOnSticky = false;
        isOnWall = false;
        isInTimeout = false;
        wallContactTimer = 0f;
        timeoutTimer = catchUpTimeout;
        baseCatchElapsed = 0f;
        previousPathRemainingDistance = -1f;
        previousSteeringDistance = -1f;
        smoothedMove = Vector2.zero;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Pause observation gathering while the dungeon/map is resetting
        if (!isReady)
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
            sensor.AddObservation(false);
            sensor.AddObservation(false);
            sensor.AddObservation(false);

            for (int i = 0; i < 4; i++) sensor.AddObservation(0f);
            for (int i = 0; i < 4; i++) sensor.AddObservation(0f);
            return;
        }

        Vector3 steeringTarget = GetSteeringTarget();
        Vector3 dirToSteeringTarget = (steeringTarget - transform.position).normalized;

        sensor.AddObservation(transform.position);
        sensor.AddObservation(steeringTarget);
        sensor.AddObservation(dirToSteeringTarget);
        sensor.AddObservation(Vector3.Distance(transform.position, steeringTarget));
        sensor.AddObservation(characterController.isGrounded);
        sensor.AddObservation(isOnSticky);
        sensor.AddObservation(isOnWall);

        CollectWallObservations(sensor);
        CollectFloorObservations(sensor);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Ignore actions while the environment is not ready to avoid training on invalid states
        if (!isReady)
            return;

        ProcessActions(actions);

        Vector3 steeringTarget = GetSteeringTarget();
        Vector3 desiredDir = (steeringTarget - transform.position).normalized;

        RewardSteeringProgress(steeringTarget);
        RewardMovementAlignment(desiredDir);
        PenalizeWallProximity();
        RewardTargetProgress();
        PenalizeStuck();

        LoggedAddReward(-GameManager.Instance.TimePenaltyPerStep, "Time penalty");
    }

    #endregion

    #region Action Processing

    private void ProcessActions(ActionBuffers actions)
    {
        Vector2 raw = new Vector2(actions.ContinuousActions[1], actions.ContinuousActions[0]); // x = strafe, y = forward

        if (raw.magnitude < actionDeadZone)
            raw = Vector2.zero;

        float smoothing = Mathf.Clamp01(actionSmoothing);
        smoothedMove = Vector2.Lerp(smoothedMove, raw, smoothing);
        smoothedMove = Vector2.ClampMagnitude(smoothedMove, 1f);

        cachedStrafeInput = smoothedMove.x;
        cachedForwardInput = smoothedMove.y;
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
            LoggedAddReward(delta * 0.1f, "Steering progress");
        }

        previousDistanceToSteeringTarget = distToSteering;
    }

    private void RewardMovementAlignment(Vector3 desiredDir)
    {
        // Use actual velocity instead of input to avoid rewarding wall-ramming
        Vector3 actualVelocity = characterController.velocity;
        Vector3 horizontalVelocity = new Vector3(actualVelocity.x, 0f, actualVelocity.z);

        float speed = horizontalVelocity.magnitude;
        if (speed > 0.1f)
        {
            Vector3 moveDir = horizontalVelocity.normalized;

            // Reward moving in the direction of the target
            float alignment = Vector3.Dot(moveDir, desiredDir);
            LoggedAddReward(alignment * 0.05f, "Movement alignment");

            // Additional small reward for any forward movement to encourage exploration
            float normalizedSpeed = Mathf.Clamp01(speed / moveSpeed);
            LoggedAddReward(normalizedSpeed * 0.005f, "Forward movement");
        }
    }

    private void PenalizeWallProximity()
    {
        if (!GameManager.Instance.ShouldPenalizeWalls) return;

        float minWallDist = GetMinWallDistance();
        bool isTouchingWall = minWallDist < 0.6f;

        // Only penalize when very close to walls (about to collide)
        if (minWallDist < 0.6f)
        {
            float normalizedDist = minWallDist / 0.6f;
            float penalty = (1f - normalizedDist) * 0.02f;
            LoggedAddReward(-penalty, "Wall proximity");
        }

        // Track wall contact time and end episode if too long
        if (isTouchingWall)
        {
            wallContactTimer += Time.fixedDeltaTime;
            if (wallContactTimer >= wallContactTimeout)
            {
                LoggedAddReward(-1f, "Wall contact timeout");
                HandleEpisodeEnd(false, false, false);
            }
        }
        else
        {
            wallContactTimer = 0f;
        }
    }

    private void RewardTargetProgress()
    {
        float dist = Vector3.Distance(transform.position, target.position);
        float delta = previousDistanceToTarget - dist;
        LoggedAddReward(delta * 0.05f, "Target progress");

        if (previousDistanceToTarget <= GameManager.Instance.CloseDistanceThreshold)
        {
            if (dist > previousDistanceToTarget)
                LoggedAddReward(GameManager.Instance.CloseMoveAwayPenalty, "Close move away");
            else if (dist < previousDistanceToTarget)
                LoggedAddReward(GameManager.Instance.CloseMoveCloserReward, "Close move closer");
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
                LoggedAddReward(-0.05f, "Stuck penalty");
        }
        else
        {
            stuckCounter = 0;
            lastSignificantPosition = transform.position;
            LoggedAddReward(0.01f, "Movement reward");
        }
    }

    #endregion

    #region Observations

    private void CollectWallObservations(VectorSensor sensor)
    {
        // Use world-space directions since agent no longer rotates
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

        foreach (var dir in directions)
        {
            if (!GameManager.Instance.ShouldPenalizeWalls)
            {
                sensor.AddObservation(GameManager.Instance.WallRayLength);
                continue;
            }

            bool hit = Physics.Raycast(transform.position, dir, out RaycastHit hitInfo, GameManager.Instance.WallRayLength, wallLayer);
            sensor.AddObservation(hit ? hitInfo.distance : GameManager.Instance.WallRayLength);
            Debug.DrawLine(transform.position, transform.position + dir * GameManager.Instance.WallRayLength, Color.green);
        }
    }

    private void CollectFloorObservations(VectorSensor sensor)
    {
        // Use world-space directions since agent no longer rotates
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

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

    private void ApplyMovement()
    {
        // Calculate movement in world space (forward = +Z, right = +X)
        Vector3 move = currentSpeedMultiplier * moveSpeed * new Vector3(cachedStrafeInput, 0f, cachedForwardInput);

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

        // Check again after Warp - agent may have been moved off NavMesh
        if (target != null && navAgent.isOnNavMesh)
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

    #region Timeout Management

    private bool HandleProgressBasedTimeout()
    {
        float remainingDistance = GetRemainingDistance();
        float steeringDistance = Vector3.Distance(transform.position, GetSteeringTarget());
        ProgressState state = EvaluateProgressState(remainingDistance, steeringDistance);

        previousPathRemainingDistance = remainingDistance;
        previousSteeringDistance = steeringDistance;

        switch (state)
        {
            case ProgressState.Progressing:
                timeoutTimer = catchUpTimeout;
                return false;
            case ProgressState.NoProgress:
                timeoutTimer -= Time.fixedDeltaTime;
                if (timeoutTimer <= 0f)
                {
                    LoggedAddReward(-1f, "No progress timeout");
                    HandleEpisodeEnd(false, false, false);
                    return true;
                }
                return false;
            case ProgressState.Regressing:
                LoggedAddReward(-1f, "Regressing");
                HandleEpisodeEnd(false, false, false);
                return true;
            default:
                return false;
        }
    }

    private bool HasRewardDroppedBelowThreshold()
    {
        return GetCumulativeReward() <= rewardTimeoutThreshold;
    }

    private ProgressState EvaluateProgressState(float remainingDistance, float steeringDistance)
    {
        if (previousPathRemainingDistance < 0f || previousSteeringDistance < 0f)
            return ProgressState.Unknown;

        float pathDelta = previousPathRemainingDistance - remainingDistance;
        float steeringDelta = previousSteeringDistance - steeringDistance;

        if (pathDelta < -regressionThreshold || steeringDelta < -regressionThreshold)
            return ProgressState.Regressing;

        bool closingTarget = pathDelta > progressThreshold;
        bool followingPath = steeringDelta > -progressThreshold;

        if (closingTarget && followingPath)
            return ProgressState.Progressing;

        if (Mathf.Abs(pathDelta) <= progressThreshold && Mathf.Abs(steeringDelta) <= progressThreshold)
            return ProgressState.NoProgress;

        return ProgressState.NoProgress;
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
            LoggedAddReward(-0.1f, "Exit collision");
            HandleEpisodeEnd(false, false, false);
        }
    }

    private void OnCollisionStay(Collision other)
    {
        if (other.gameObject.CompareTag(GameManager.Instance.WallTag) && GameManager.Instance.ShouldPenalizeWalls)
        {
            LoggedAddReward(-0.05f, "Wall collision");

            if (cachedForwardInput > 0.2f)
                LoggedAddReward(-0.03f, "Pushing into wall");
        }
        else if (other.gameObject.CompareTag(GameManager.Instance.StickyTag) && GameManager.Instance.ShouldPenalizeSlime)
        {
            LoggedAddReward(-0.02f, "Sticky collision");
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
            LoggedAddReward(-0.4f, "Wall enter");

        isOnWall = true;
    }

    private void HandleStickyEnter()
    {
        if (GameManager.Instance.ShouldPenalizeSlime)
            LoggedAddReward(-0.3f, "Sticky enter");

        isOnSticky = true;
    }

    private void HandlePlayerCaught()
    {
        LoggedSetReward(1f, "Player caught");
        bool beatBase = !isInTimeout || baseCatchElapsed <= baseCatchTolerance;
        HandleEpisodeEnd(true, true, beatBase);
    }

    #endregion

    #region Utility

    public int Episode => episode;
    public float LastEpisodeReward => lastEpisodeReward;
    public float BestEpisodeReward => bestEpisodeReward;
    public float CurrentReward => GetCumulativeReward();

    private Vector3 GetSteeringTarget()
    {
        Vector3 steeringTarget = navAgent != null && navAgent.hasPath ? navAgent.steeringTarget : target.position;
        // Debug.Log("Steering target: " + steeringTarget);
        return steeringTarget;
    }

    private float GetRemainingDistance()
    {
        if (navAgent != null && navAgent.isOnNavMesh && navAgent.hasPath)
            return navAgent.remainingDistance;

        return Vector3.Distance(transform.position, target.position);
    }

    private float GetMinWallDistance()
    {
        float minDist = GameManager.Instance.WallRayLength;
        Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };

        foreach (var dir in directions)
        {
            if (Physics.Raycast(transform.position, dir, out RaycastHit hit, GameManager.Instance.WallRayLength, wallLayer))
                minDist = Mathf.Min(minDist, hit.distance);
        }

        return minDist;
    }

    private void HandleEpisodeEnd(bool setNotReady = false, bool isSuccess = false, bool beatBase = false)
    {
        float finalReward = GetCumulativeReward();
        lastEpisodeReward = finalReward;
        if (finalReward > bestEpisodeReward)
            bestEpisodeReward = finalReward;

        GameManager.Instance.ReportHybridEpisodeResult(isSuccess, beatBase, episode);
        EndEpisode();

        if (setNotReady)
            isReady = false;

        // Use exact same pattern as 'G' key reset to avoid crashes
        GameManager.Instance.IncrementSeed();

        if (dungeonRunner != null)
        {
            dungeonRunner.SetSeed(GameManager.Instance.CurrentSeed);
            dungeonRunner.ForceRegenerate();
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

    #region Reward Logging

    private void LoggedAddReward(float reward, string reason)
    {
        if (GameManager.Instance.EnableRewardLogging && reward != 0f)
        {
            string message = $"[Reward] Episode {episode} | {reason}: {reward:+0.000;-0.000} | Cumulative: {GetCumulativeReward():F3}";
            GameManager.Instance.WriteRewardLog(message);
        }
        AddReward(reward);
    }

    private void LoggedSetReward(float reward, string reason)
    {
        if (GameManager.Instance.EnableRewardLogging)
        {
            string message = $"[Reward] Episode {episode} | {reason}: SET to {reward:F3}";
            GameManager.Instance.WriteRewardLog(message);
        }
        SetReward(reward);
    }

    #endregion

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (navAgent == null)
            navAgent = GetComponent<NavMeshAgent>();

        if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(navAgent.destination, 0.2f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(navAgent.steeringTarget, 0.25f);
    }
#endif
}