using Fusion;
using UnityEngine;

/// <summary>
/// Objects that can be grabbed, carried, released, or thrown by a player.
/// Network ownership of the hold is decided by Host (State Authority).
/// </summary>
public interface IHoldable
{
    NetworkObject Object { get; }

    /// <summary>False when already held by someone.</summary>
    bool CanBeHeld { get; }

    void OnPickedUp(NetworkObject holder);
    void OnReleased();
    void OnThrown(Vector3 worldVelocity);
}
