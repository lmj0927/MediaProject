using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CharacterController로 움직이는 대상이 휠과 함께 실려 가도록 이동량을 계산함.
/// Rigidbody 대상은 WheelRider를 쓰고, 이쪽은 CharacterController 전용.
///
/// 속도 × 틱 간격으로 추정하지 않고, 지난 틱에 대상이 판의 어느 지점에 있었는지를 기억해 두었다가
/// 그 지점이 지금 어디로 갔는지를 보고 옮김.
///
/// 사용 순서 (플레이어 이동 코드 안에서)
///   1. GetCarryDelta로 이동량을 받아 자체 이동보다 먼저 적용
///   2. 자체 이동
///   3. EndTick으로 최종 위치를 기록
///
/// 주의: 지난 틱의 기준점을 들고 있으므로 네트워크 재시뮬레이션에 안전하지 않음.
/// 호스트에서는 문제없지만, 클라이언트 예측을 켤 경우 기준점을 네트워크 변수로 옮겨야 함.
/// </summary>
[RequireComponent(typeof(WeightSource))]
[DisallowMultipleComponent]
public class WheelCarrier : MonoBehaviour
{
[Header("Volume")]
[Tooltip("판 반경에 곱하는 값. 이 범위 안이면 휠에 실린 것으로 봄.\n" +
            "1보다 살짝 크게 두면 가장자리에서 끊기는 느낌이 줄어듦.")]
[Range(0.5f, 1.5f)][SerializeField] private float radiusScale = 1.05f;

[Tooltip("데크 위로 이 높이까지 실어 줌. 점프 최고점보다 높게 둘 것.\n" +
            "낮으면 점프 정점에서 휠이 먼저 가 버려 판 밖으로 떨어짐.")]
[SerializeField] private float heightAbove = 4f;

[Tooltip("데크 아래로 허용하는 여유. 판이 기울어 발 위치가 데크보다 낮아지는 경우 대비.")]
[SerializeField] private float heightBelow = 0.6f;

[Header("Carry")]
[Tooltip("착지 상태에서는 판의 회전까지 따라감. 판이 기울 때 발이 표면에서 떨어지지 않음.\n" +
            "공중에서는 항상 판의 이동만 따라감. 회전까지 따라가면 높이 떠 있을수록 지렛대처럼 크게 휩쓸림.")]
[SerializeField] private bool carryRotationWhenGrounded = true;

[Tooltip("이 거리 이상 순간 이동했으면 기준점을 버림. 들기, 던지기, 네트워크 보정 대비.")]
[SerializeField] private float teleportThreshold = 0.5f;

[Header("Ground snap")]
[Tooltip("착지 상태에서 매 틱 아래로 눌러 주는 거리.\n" +
            "CharacterController는 마지막 이동에서 아래에 닿아야 착지로 치므로, " +
            "판이 움직이며 몇 mm만 떠도 착지가 풀림. 이를 막음.\n" +
            "바닥에 닿으면 거기서 멈추므로 파고들지 않음. 0이면 사용 안 함.")]
[SerializeField] private float groundSnapDistance = 0.08f;

private static readonly List<PlatformTilt> platforms = new();
private static readonly Dictionary<PlatformTilt, Rigidbody> bodies = new();
private static float nextRefreshTime;
private const float RefreshInterval = 1f;

private bool hasAnchor;
private PlatformTilt anchorPlatform;
private Vector3 anchorLocal;          // 판 기준 로컬 좌표
private Vector3 anchorPlatformPosition;
private Vector3 lastFinalPosition;

/// <summary>현재 실려 가고 있는 판. 없으면 null.</summary>
public PlatformTilt CurrentPlatform { get; private set; }

/// <summary>
/// 이번 틱에 적용할 이동량을 구함. 자체 이동보다 먼저 적용할 것.
/// </summary>
/// <param name="position">대상의 현재 위치(발 기준).</param>
/// <param name="grounded">착지 여부.</param>
/// <param name="snapToGround">지면 흡착 허용 여부. 점프 중에는 false.</param>
public Vector3 GetCarryDelta(Vector3 position, bool grounded, bool snapToGround)
{
    var platform = FindContainingPlatform(position);
    CurrentPlatform = platform;

    if (platform == null)
    {
        hasAnchor = false;
        return Vector3.zero;
    }

    var body = GetBody(platform);
    Vector3 delta = Vector3.zero;

    bool teleported = hasAnchor &&
        (position - lastFinalPosition).sqrMagnitude > teleportThreshold * teleportThreshold;

    if (hasAnchor && anchorPlatform == platform && !teleported && body != null)
    {
        if (grounded && carryRotationWhenGrounded)
        {
            // 지난 틱에 서 있던 판 위 지점이 지금 어디에 있는지
            Vector3 carried = body.position + body.rotation * anchorLocal;
            delta = carried - position;
        }
        else
        {
            // 공중에서는 판의 이동만 따라감
            delta = body.position - anchorPlatformPosition;
            delta.y = 0f;
        }
    }

    if (grounded && snapToGround && groundSnapDistance > 0f)
        delta.y -= groundSnapDistance;

    return delta;
}

/// <summary>
/// 이번 틱의 모든 이동이 끝난 뒤 최종 위치를 기록함.
/// 다음 틱은 이 지점이 판과 함께 어디로 갔는지를 기준으로 옮김.
/// </summary>
public void EndTick(Vector3 finalPosition)
{
    lastFinalPosition = finalPosition;

    var body = CurrentPlatform != null ? GetBody(CurrentPlatform) : null;
    if (body == null)
    {
        hasAnchor = false;
        return;
    }

    anchorPlatform = CurrentPlatform;
    anchorLocal = Quaternion.Inverse(body.rotation) * (finalPosition - body.position);
    anchorPlatformPosition = body.position;
    hasAnchor = true;
}

/// <summary>기준점을 버림. 강제로 위치를 옮긴 직후 호출.</summary>
public void ResetAnchor() => hasAnchor = false;

/// <summary>대상이 들어가 있는 판을 찾음. 여러 개면 가장 가까운 것.</summary>
private PlatformTilt FindContainingPlatform(Vector3 position)
{
    RefreshPlatformsIfNeeded();

    PlatformTilt best = null;
    float bestDistance = float.PositiveInfinity;

    for (int i = 0; i < platforms.Count; i++)
    {
        var p = platforms[i];
        if (p == null || !p.isActiveAndEnabled) continue;

        var body = GetBody(p);
        Vector3 deck = body != null ? body.position : p.transform.position;

        float dy = position.y - deck.y;
        if (dy < -heightBelow || dy > heightAbove) continue;

        Vector3 flat = position - deck;
        flat.y = 0f;
        float distance = flat.magnitude;
        if (distance > p.PlatformRadius * radiusScale) continue;

        if (distance < bestDistance)
        {
            bestDistance = distance;
            best = p;
        }
    }

    return best;
}

private static Rigidbody GetBody(PlatformTilt platform)
{
    if (bodies.TryGetValue(platform, out var rb) && rb != null) return rb;
    rb = platform.GetComponent<Rigidbody>();
    bodies[platform] = rb;
    return rb;
}

/// <summary>
/// 씬의 판 목록을 주기적으로 갱신함.
/// </summary>
private static void RefreshPlatformsIfNeeded()
{
    bool hasMissing = false;
    for (int i = 0; i < platforms.Count; i++)
    {
        if (platforms[i] == null) { hasMissing = true; break; }
    }

    if (!hasMissing && platforms.Count > 0 && Time.time < nextRefreshTime) return;

    platforms.Clear();
    platforms.AddRange(Object.FindObjectsByType<PlatformTilt>(FindObjectsSortMode.None));
    bodies.Clear();
    nextRefreshTime = Time.time + RefreshInterval;
}

#if UNITY_EDITOR
private void OnDrawGizmosSelected()
{
    if (!Application.isPlaying || !hasAnchor || anchorPlatform == null) return;

    var body = GetBody(anchorPlatform);
    if (body == null) return;

    Gizmos.color = Color.cyan;
    Gizmos.DrawWireSphere(body.position + body.rotation * anchorLocal, 0.12f);
}
#endif
}
