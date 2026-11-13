using Fusion;
using UnityEngine;
using UnityEngine.Serialization;
/// <summary>
/// 점프대. 올라온 대상을 쏘아 올림.
/// 기존 속도에 패드의 로컬 Y축 방향 속도 변화를 더함.
///
/// 이미 쏘아 올려진 상태면 다시 쏘지 않음. 상태 없이 현재 속도만 보고 판단함.
/// 대상 탐색과 네트워크 처리는 WheelPadBase가 담당함.
/// </summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class JumpPad : WheelPadBase
{
    [Header("Visual")]
    [SerializeField] private JumpPadVisual visual = new JumpPadVisual();

    [Networked] private int LaunchCount { get; set; }
    private int shownLaunchCount;
    internal event System.Action<JumpPad, Rigidbody> Launched;
    [Header("Jump")]
    [Tooltip("패드의 로컬 Y축 방향으로 더하는 속도 변화(m/s).")]
    [FormerlySerializedAs("jumpHeight")]
    [Min(0f)][SerializeField] private float jumpForce = 6f;

    [Tooltip("패드 방향 속도가 발사 속도의 이 비율보다 낮을 때만 다시 쏨.")]
    [Range(0.1f, 0.95f)][SerializeField] private float rearmFraction = 0.5f;

    public override void Spawned()
    {
        shownLaunchCount = LaunchCount;
        visual.Reset();
    }

    public override void Render()
    {
        if (shownLaunchCount != LaunchCount)
        {
            shownLaunchCount = LaunchCount;
            visual.Play();
        }

        visual.Render(Time.deltaTime);
    }

    protected override void Affect(Rigidbody rb, float dt)
    {
        if (Vector3.Dot(rb.linearVelocity, transform.up) >= jumpForce * rearmFraction) return;

        rb.AddForce(transform.up * jumpForce, ForceMode.VelocityChange);
        LaunchCount++;
        Launched?.Invoke(this, rb);
    }

#if UNITY_EDITOR
    private static readonly Color GizmoColor = new Color(1f, 0.75f, 0.2f, 0.95f);

    private void OnDrawGizmos()
    {
        // 쏘는 방향. 항상 표시함.
        Vector3 dir = transform.up;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir.Normalize();

        Vector3 from = transform.position;
        Vector3 to = from + dir * 2.5f;

        Gizmos.color = GizmoColor;
        Gizmos.DrawLine(from, to);

        Vector3 a = Vector3.Cross(dir, Vector3.right);
        if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(dir, Vector3.forward);
        a = a.normalized * 0.35f;
        Vector3 b = Vector3.Cross(dir, a).normalized * 0.35f;

        Gizmos.DrawLine(to, to - dir * 0.6f + a);
        Gizmos.DrawLine(to, to - dir * 0.6f - a);
        Gizmos.DrawLine(to, to - dir * 0.6f + b);
        Gizmos.DrawLine(to, to - dir * 0.6f - b);
    }

#endif
}

/// <summary>발사 이벤트만 받아 물리와 분리된 모델에 애니메이션을 재생한다.</summary>
[System.Serializable]
public class JumpPadVisual
{
    [SerializeField] private GameObject model;
    [SerializeField] private AnimationClip clip;

    private float time;
    private bool playing;

    public void Reset()
    {
        playing = false;
        if (model != null && clip != null)
            clip.SampleAnimation(model, 0f);
    }

    public void Play()
    {
        time = 0f;
        playing = true;
    }

    public void Render(float deltaTime)
    {
        if (!playing || model == null || clip == null) return;

        clip.SampleAnimation(model, time);
        time += deltaTime;
        if (time >= clip.length)
            Reset();
    }
}
