using UnityEngine;
using UnityEngine.AI;

public class Agent : MonoBehaviour
{
    private NavMeshAgent agent;
    public Transform target;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        if (agent != null && agent.isOnNavMesh && agent.isActiveAndEnabled && target != null)
        {
            agent.SetDestination(target.position);
        }
    }
}
