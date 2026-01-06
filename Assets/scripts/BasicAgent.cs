using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BasicAgent : MonoBehaviour, ISpeedModifiable
{
    [SerializeField] private Transform target;
    [SerializeField] private float targetReachedThreshold = 1.5f;

    private NavMeshAgent agent;
    private float baseMoveSpeed;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new();

    /// <summary>
    /// Prevents firing OnBasicAgentReachedTarget multiple times per episode.
    /// Reset when dungeon regenerates via OnDungeonReady event.
    /// </summary>
    private bool hasReachedTargetThisEpisode = false;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        baseMoveSpeed = agent.speed;
        DungeonRunner.OnDungeonReady += ResetReachedFlag;
    }

    void OnDestroy()
    {
        DungeonRunner.OnDungeonReady -= ResetReachedFlag;
    }

    private void ResetReachedFlag()
    {
        hasReachedTargetThisEpisode = false;
    }

    void Update()
    {
        if (target != null && agent != null && agent.isOnNavMesh)
        {
            // Only move if we haven't reached the target yet
            if (!hasReachedTargetThisEpisode)
            {
                agent.SetDestination(target.position);

                // Check if we've reached the target
                float distanceToTarget = Vector3.Distance(transform.position, target.position);
                if (distanceToTarget <= targetReachedThreshold)
                {
                    hasReachedTargetThisEpisode = true;
                    agent.ResetPath(); // Stop moving
                    GameManager.Instance.OnBasicAgentReachedTarget();
                }
            }
        }
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
        if (agent == null) return;

        if (baseMoveSpeed <= 0f)
            baseMoveSpeed = agent.speed;

        agent.speed = baseMoveSpeed * currentSpeedMultiplier;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(agent.destination, 0.2f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(agent.steeringTarget, 0.25f);
    }
#endif
}
