using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BasicAgent : Agent, ISpeedModifiable
{
    [SerializeField] private Transform target;

    private NavMeshAgent agent;
    private float baseMoveSpeed;
    private float currentSpeedMultiplier = 1f;
    private readonly Dictionary<Object, float> speedModifiers = new();

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        baseMoveSpeed = agent.speed;
    }

    void Update()
    {
        if (target != null)
        {
            agent.SetDestination(target.position);
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
}
