using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages speed modifiers from multiple sources and combines them into a single multiplier.
/// Attach to any GameObject that needs speed modification support.
/// </summary>
public class SpeedModifierHandler : MonoBehaviour, ISpeedModifiable
{
    private readonly Dictionary<Object, float> speedModifiers = new Dictionary<Object, float>();
    private float currentSpeedMultiplier = 1f;

    /// <summary>
    /// The combined speed multiplier from all sources.
    /// </summary>
    public float CurrentSpeedMultiplier => currentSpeedMultiplier;

    /// <summary>
    /// Event invoked when the speed multiplier changes.
    /// </summary>
    public event System.Action<float> OnSpeedMultiplierChanged;

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

    /// <summary>
    /// Clears all speed modifiers and resets to base speed.
    /// </summary>
    public void ClearAllModifiers()
    {
        speedModifiers.Clear();
        currentSpeedMultiplier = 1f;
        OnSpeedMultiplierChanged?.Invoke(currentSpeedMultiplier);
    }

    private void RecalculateSpeedMultiplier()
    {
        float previousMultiplier = currentSpeedMultiplier;
        currentSpeedMultiplier = 1f;

        foreach (float modifier in speedModifiers.Values)
        {
            currentSpeedMultiplier *= modifier;
        }

        currentSpeedMultiplier = Mathf.Clamp(currentSpeedMultiplier, 0.01f, 10f);

        if (!Mathf.Approximately(previousMultiplier, currentSpeedMultiplier))
        {
            OnSpeedMultiplierChanged?.Invoke(currentSpeedMultiplier);
        }
    }
}
