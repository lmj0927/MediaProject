using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 얼음 장판. 판 기울기에 대한 휠의 반응이 늦어짐.
/// 매 틱 수평 속도 변화를 진행 방향 기준으로 나눠 따로 다룸.
///   진행 방향으로 빨라짐: 그대로 허용함. 기울기와 진행 방향이 같으면 평소처럼 가속함.
///   진행 방향으로 느려짐: 크게 줄임. 반대로 기울인 정도에 비례해서만 제동이 살아남.
///   옆으로 꺾임: 줄임. 방향 전환이 늦어짐.
/// 저속에서는 유지할 방향이 없으므로 얼음 효과를 서서히 끔. 얼음 위에서 출발하지 못하는 일을 막음.
/// </summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class IcePad : WheelPadBase
{
    [Header("Ice")]
    [Tooltip("옆으로 꺾이는 변화의 허용 비율. 낮을수록 방향 전환이 늦음.")]
    [Range(0.02f, 1f)][SerializeField] private float turnGrip = 0.2f;

    [Tooltip("진행 방향으로 빨라지는 변화의 허용 비율. 1이면 기울기가 진행 방향과 같을 때 평소처럼 가속함.")]
    [Range(0.02f, 1f)][SerializeField] private float accelGrip = 1f;

    [Header("Brake")]
    [Tooltip("반대로 기울이지 않을 때의 제동 비율. 무게를 중앙에 두거나 진행 방향으로 기울인 채 속도가 넘칠 때.\n" +
             "낮을수록 잘 안 멈춤.")]
    [FormerlySerializedAs("brakeGrip")]
    [Range(0.01f, 1f)][SerializeField] private float passiveBrakeGrip = 0.1f;

    [Tooltip("진행 방향의 반대로 최대한 기울였을 때의 제동 비율.\n" +
             "반대로 기울인 정도에 비례해 passiveBrakeGrip에서 이 값까지 올라감.")]
    [Range(0.01f, 1f)][SerializeField] private float activeBrakeGrip = 0.4f;

    [Header("Low speed")]
    [Tooltip("이 속도(m/s) 이하에서는 얼음 효과가 없음. 두 배 속도에서 최대가 됨.\n" +
             "느릴 때는 유지할 방향이 없으므로, 출발과 저속 조작은 평소처럼 함.")]
    [Min(0.1f)][SerializeField] private float lowSpeed = 2f;

    [Header("Safety")]
    [Tooltip("한 틱에 이보다 크게 바뀐 수평 속도(m/s)는 충돌로 보고 그대로 둠.")]
    [Min(0.1f)][SerializeField] private float collisionThreshold = 2f;

    [Tooltip("회전을 진행 방향에 맞춤.\n" +
             "비활성화시 얼음 위에서 바퀴가 헛돌고, 벗어나는 순간 돌던 방향으로 급발진함.")]
    [SerializeField] private bool matchSpin = true;

    private struct Sample
    {
        public Vector3 velocity;
        public int tick;
    }

    private readonly Dictionary<Rigidbody, Sample> samples = new();
    private readonly Dictionary<Rigidbody, PlatformTilt> platforms = new();
    private readonly List<Rigidbody> stale = new();

    protected override void Affect(Rigidbody rb, float dt)
    {
        int tick = Runner.Tick.Raw;
        Vector3 v = rb.linearVelocity;

        if (samples.TryGetValue(rb, out var prev) && prev.tick == tick - 1)
        {
            Vector3 prevH = new Vector3(prev.velocity.x, 0f, prev.velocity.z);
            Vector3 curH = new Vector3(v.x, 0f, v.z);
            Vector3 change = curH - prevH;

            float speed = prevH.magnitude;
            float ice = IceAmount(speed);

            if (ice > 0f && change.magnitude < collisionThreshold)
            {
                Vector3 heading = prevH / speed;
                float brakeGrip = BrakeGrip(rb, heading);

                curH = prevH + FilterChange(change, heading, ice, brakeGrip);
                v = new Vector3(curH.x, v.y, curH.z);
                rb.linearVelocity = v;

                if (matchSpin) MatchSpin(rb, v);
            }
        }

        samples[rb] = new Sample { velocity = v, tick = tick };

        if (samples.Count > 8) PruneStale(tick);
    }

    /// <summary>저속에서 0, lowSpeed의 두 배 이상에서 1.</summary>
    private float IceAmount(float speed)
    {
        float t = Mathf.InverseLerp(lowSpeed, lowSpeed * 2f, speed);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// 이번 틱의 제동 비율. 진행 방향의 반대로 기울인 정도에 비례함.
    /// 판을 찾지 못하면 반대로 기울이지 않은 것으로 봄.
    /// </summary>
    private float BrakeGrip(Rigidbody rb, Vector3 heading)
    {
        var platform = FindPlatform(rb);
        if (platform == null) return passiveBrakeGrip;

        // 내리막 방향이 진행 방향의 반대일수록 1에 가까움. 기울기 세기를 곱해 무게 쏠림에 비례시킴.
        float counter = -Vector3.Dot(platform.DownhillDirection, heading) * platform.NormalizedTilt;
        return Mathf.Lerp(passiveBrakeGrip, activeBrakeGrip, Mathf.Clamp01(counter));
    }

    /// <summary>휠과 같은 부모 아래의 판을 찾아 캐시함.</summary>
    private PlatformTilt FindPlatform(Rigidbody rb)
    {
        if (platforms.TryGetValue(rb, out var cached) && cached != null) return cached;

        var parent = rb.transform.parent;
        var found = parent != null ? parent.GetComponentInChildren<PlatformTilt>() : null;
        platforms[rb] = found;
        return found;
    }

    /// <summary>속도 변화를 진행 방향 성분과 옆 성분으로 나눠 각각 허용 비율을 적용함.</summary>
    private Vector3 FilterChange(Vector3 change, Vector3 heading, float ice, float brakeGrip)
    {
        float along = Vector3.Dot(change, heading);
        Vector3 lateral = change - heading * along;

        float alongGrip = along >= 0f ? accelGrip : brakeGrip;

        return heading * (along * Mathf.Lerp(1f, alongGrip, ice))
             + lateral * Mathf.Lerp(1f, turnGrip, ice);
    }

    /// <summary>구르는 회전을 수평 속도에 맞춤. SphereDriver와 같은 축 규칙을 따름.</summary>
    private static void MatchSpin(Rigidbody rb, Vector3 velocity)
    {
        float radius = SphereRadiusOf(rb);
        if (radius <= 0.01f) return;

        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 yaw = Vector3.Project(rb.angularVelocity, Vector3.up);
        rb.angularVelocity = Vector3.Cross(horizontal, Vector3.up) / radius + yaw;
    }

    /// <summary>얼음을 떠난 대상의 기록을 정리함.</summary>
    private void PruneStale(int tick)
    {
        stale.Clear();
        foreach (var kvp in samples)
        {
            if (kvp.Key == null || tick - kvp.Value.tick > 1)
                stale.Add(kvp.Key);
        }
        foreach (var rb in stale)
        {
            samples.Remove(rb);
            platforms.Remove(rb);
        }
    }
}