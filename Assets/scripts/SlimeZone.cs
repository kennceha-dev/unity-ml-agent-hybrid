using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SlimeZone : MonoBehaviour
{
    [SerializeField, Range(0.1f, 1f)] private float slowMultiplier = 0.5f;


    private readonly HashSet<ISpeedModifiable> slowedTargets = new HashSet<ISpeedModifiable>();

    private void Reset()
    {
        SetColliderAsTrigger();
    }

    private void Awake()
    {
        SetColliderAsTrigger();
    }

    private void SetColliderAsTrigger()
    {
        if (TryGetComponent<Collider>(out var zoneCollider))
        {
            zoneCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        ISpeedModifiable target = other.GetComponentInParent<ISpeedModifiable>();
        if (target == null || slowedTargets.Contains(target))
        {
            return;
        }

        slowedTargets.Add(target);
        target.ApplySpeedMultiplier(this, slowMultiplier);
    }

    private void OnTriggerExit(Collider other)
    {
        ISpeedModifiable target = other.GetComponentInParent<ISpeedModifiable>();
        if (target == null)
        {
            return;
        }

        if (slowedTargets.Remove(target))
        {
            target.RemoveSpeedMultiplier(this);
        }
    }

    private void OnDisable()
    {
        foreach (var target in slowedTargets)
        {
            target.RemoveSpeedMultiplier(this);
        }

        slowedTargets.Clear();
    }
}
