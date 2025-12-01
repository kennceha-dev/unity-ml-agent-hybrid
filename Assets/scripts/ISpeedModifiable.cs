using UnityEngine;

/// <summary>
/// Implemented by any behaviour that exposes its movement speed to external modifiers.
/// Sources can register/unregister multipliers that are combined to produce the final speed.
/// </summary>
public interface ISpeedModifiable
{
    /// <summary>
    /// Registers a multiplier coming from the given source. Pass values between 0-1 to slow down.
    /// </summary>
    /// <param name="source">The component generating the modifier (ex: a SlimeZone instance).</param>
    /// <param name="multiplier">Value multiplied into the target's base speed.</param>
    void ApplySpeedMultiplier(Object source, float multiplier);

    /// <summary>
    /// Clears the multiplier registered previously by the same source.
    /// </summary>
    /// <param name="source">The same source reference passed to ApplySpeedMultiplier.</param>
    void RemoveSpeedMultiplier(Object source);
}
