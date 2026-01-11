using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(PhysicsMovement))]
[RequireComponent(typeof(NavMeshAgent))]
public class BasicAgent : MonoBehaviour, ISpeedModifiable
{
    [Header("References")]
    [SerializeField] private Transform target;
    [SerializeField] private float targetReachedThreshold = 1.5f;

    private PhysicsMovement physicsMovement;
    private NavMeshAgent navAgent;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new();

    /// <summary>
    /// Prevents firing OnBasicAgentReachedTarget multiple times per episode.
    /// Reset when dungeon regenerates via OnDungeonReady event.
    /// </summary>
    private bool hasReachedTargetThisEpisode = false;

    // Eval mode tracking
    private float evalStartTime;
    private bool hasFinishedEval = false;

    /// <summary>
    /// Check if this agent is enabled in GameManager.
    /// </summary>
    private bool IsAgentEnabled => GameManager.Instance == null || GameManager.Instance.EnableBaseAgent;

    void Start()
    {
        physicsMovement = GetComponent<PhysicsMovement>();
        navAgent = GetComponent<NavMeshAgent>();

        // Configure NavMeshAgent for pathfinding only (not movement)
        if (navAgent != null)
        {
            navAgent.updatePosition = false;
            navAgent.updateRotation = false;
        }

        DungeonRunner.OnDungeonReady += ResetReachedFlag;
    }

    void OnDestroy()
    {
        DungeonRunner.OnDungeonReady -= ResetReachedFlag;
    }

    private void ResetReachedFlag()
    {
        hasReachedTargetThisEpisode = false;
        hasFinishedEval = false;

        // Reset eval start time when dungeon is ready
        if (GameManager.Instance != null && !GameManager.Instance.IsTraining)
        {
            evalStartTime = Time.time;
        }

        if (physicsMovement != null)
            physicsMovement.ResetVelocity();
    }

    void FixedUpdate()
    {
        if (target == null || navAgent == null || !navAgent.isOnNavMesh)
            return;

        // Skip if agent is disabled in GameManager
        if (!IsAgentEnabled)
        {
            if (physicsMovement != null)
                physicsMovement.Move(Vector3.zero, useFixedDelta: true);
            return;
        }

        // Sync NavMeshAgent position
        SyncNavMeshAgent();

        // Update speed multiplier
        if (physicsMovement != null)
            physicsMovement.SetSpeedMultiplier(currentSpeedMultiplier);

        // Only move if we haven't reached the target yet
        if (!hasReachedTargetThisEpisode)
        {
            // Get movement direction from NavMesh pathfinding
            Vector3 inputDirection = GetNavMeshDirection();

            // Apply physics-based movement
            if (physicsMovement != null)
                physicsMovement.Move(inputDirection, useFixedDelta: true);

            // Check if we've reached the target
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            if (distanceToTarget <= targetReachedThreshold)
            {
                hasReachedTargetThisEpisode = true;
                navAgent.ResetPath();

                // Handle differently based on training vs eval mode
                if (GameManager.Instance.IsTraining)
                {
                    GameManager.Instance.OnBasicAgentReachedTarget();
                }
                else
                {
                    // Eval mode - log and notify
                    if (!hasFinishedEval)
                    {
                        hasFinishedEval = true;
                        GameManager.Instance.OnBasicAgentFinishedEval();
                    }
                }
            }
        }
        else
        {
            // Stop moving - apply zero input for deceleration and gravity
            if (physicsMovement != null)
                physicsMovement.Move(Vector3.zero, useFixedDelta: true);
        }
    }

    private Vector3 GetNavMeshDirection()
    {
        if (navAgent == null || !navAgent.isOnNavMesh || target == null)
            return Vector3.zero;

        navAgent.SetDestination(target.position);

        if (!navAgent.hasPath || navAgent.pathPending)
            return Vector3.zero;

        // Get direction to steering target (next waypoint)
        Vector3 steeringTarget = navAgent.steeringTarget;
        Vector3 direction = steeringTarget - transform.position;
        direction.y = 0f;

        if (direction.magnitude < 0.1f)
            return Vector3.zero;

        return direction.normalized;
    }

    private void SyncNavMeshAgent()
    {
        if (navAgent == null || !navAgent.isOnNavMesh) return;
        navAgent.nextPosition = transform.position;
    }

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
        // Speed is now managed by PhysicsMovement component
    }

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
