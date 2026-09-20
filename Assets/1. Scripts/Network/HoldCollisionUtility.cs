using UnityEngine;

/// <summary>
/// Ignores physics collisions between a holdable's colliders and a holder GameObject
/// (CharacterController + child colliders) without disabling world collision.
/// </summary>
public static class HoldCollisionUtility
{
    public static void SetIgnoreCollisions(Collider[] holdableColliders, GameObject holder, bool ignore)
    {
        if (holdableColliders == null || holder == null)
            return;

        var holderColliders = holder.GetComponentsInChildren<Collider>();
        for (var i = 0; i < holdableColliders.Length; i++)
        {
            var held = holdableColliders[i];
            if (held == null)
                continue;

            for (var j = 0; j < holderColliders.Length; j++)
            {
                var holderCollider = holderColliders[j];
                if (holderCollider == null || holderCollider == held)
                    continue;

                Physics.IgnoreCollision(held, holderCollider, ignore);
            }
        }
    }
}
