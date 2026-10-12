using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 스폰 즉시 도화선이 타는 폭탄. 들기/던지기/판 캐리는 같은 오브젝트의 HoldableProp이 담당.
/// Host SA: Spawned에서 Fuse 시작, FUN에서 만료 판정 후 폭발.
/// 복제 상태는 Fuse(TickTimer) 하나. 빨개짐/깜빡임은 BombBlinkVisual이 각 피어에서 계산.
/// 폭발은 반경 안 ILaunchable(플레이어, Prop)만 같은 세기로 날림. Transport(판, 구체)는 대상이 아님.
/// 폭발 VFX는 static RPC로 모든 피어에 알림 (폭탄이 같은 틱에 Despawn되어 인스턴스 RPC/복제 상태로는 전달이 보장되지 않음).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Bomb : NetworkBehaviour
{
    [Header("Fuse")]
    [Tooltip("스폰부터 폭발까지 걸리는 시간(초).")]
    [SerializeField] private float _fuseSeconds = 5f;

    [Header("Explosion")]
    [SerializeField] private float _radius = 4f;
    [SerializeField] private LayerMask _mask = ~0;

    [Tooltip("폭탄에서 바깥쪽 수평 속도(m/s). 반경 안이면 거리와 무관하게 같음.")]
    [SerializeField] private float _launchSpeed = 12f;

    [Tooltip("위쪽 속도(m/s).")]
    [SerializeField] private float _launchUpSpeed = 8f;

    [Networked] private TickTimer Fuse { get; set; }

    private readonly Collider[] _overlapBuffer = new Collider[32];
    private readonly HashSet<NetworkId> _launchedIds = new();

    public float FuseSeconds => Mathf.Max(0.01f, _fuseSeconds);

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
            Fuse = TickTimer.CreateFromSeconds(Runner, FuseSeconds);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || !Fuse.Expired(Runner))
            return;

        Fuse = TickTimer.None;
        Explode();
    }

    /// <summary>도화선이 타는 중이면 점화 후 경과 시간(초)을 돌려줌.</summary>
    public bool TryGetFuseElapsed(out float elapsed)
    {
        elapsed = 0f;
        if (Object == null || !Object.IsValid || Runner == null || !Fuse.IsRunning)
            return false;

        float remaining = Fuse.RemainingTime(Runner) ?? 0f;
        elapsed = Mathf.Clamp(FuseSeconds - remaining, 0f, FuseSeconds);
        return true;
    }

    private void Explode()
    {
        Vector3 center = transform.position;
        int count = Physics.OverlapSphereNonAlloc(
            center, _radius, _overlapBuffer, _mask, QueryTriggerInteraction.Ignore);

        _launchedIds.Clear();
        _launchedIds.Add(Object.Id);

        for (int i = 0; i < count; i++)
        {
            var target = _overlapBuffer[i].GetComponentInParent<ILaunchable>();
            if (target == null || target.Object == null || !target.Object.IsValid)
                continue;
            if (!_launchedIds.Add(target.Object.Id))
                continue;

            Vector3 outward = target.Object.transform.position - center;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.zero;

            target.Launch(outward * _launchSpeed + Vector3.up * _launchUpSpeed);
        }

        RPC_PlayExplosionVFX(Runner, center);
        Runner.Despawn(Object);
    }

    [Rpc]
    public static void RPC_PlayExplosionVFX(NetworkRunner runner, Vector3 position)
    {
        var vfx = VFXManager.Instance;
        if (vfx != null)
            vfx.Play(VFXType.Bomb, position);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
