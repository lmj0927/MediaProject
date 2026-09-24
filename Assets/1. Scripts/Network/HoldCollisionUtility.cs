using UnityEngine;

/// <summary>
/// 잡은 대상과 홀더(캐릭터) 사이 물리 충돌만 무시한다.
/// 바닥·다른 오브젝트와의 충돌은 유지한다.
/// </summary>
public static class HoldCollisionUtility
{
    /// <summary>
    /// holdable 콜라이더들과 holder(및 자식) 콜라이더 쌍에 IgnoreCollision을 설정한다.
    /// </summary>
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
