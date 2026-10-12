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
/// Host State Authority에서만 GetCarryDelta/EndTick을 적용한다.
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

    [Tooltip("공중에서도 판의 위아래 이동을 따라감. 점프대로 휠이 뜨거나 떨어지는 중에 점프해도 판과 함께 움직임.\n" +
                "플레이어의 점프는 판 기준으로 계산됨. 평지에서는 판의 위아래 이동이 거의 없어 체감 차이가 없음.")]
    [SerializeField] private bool carryVerticalWhenAirborne = true;

    [Tooltip("이 거리 이상 순간 이동했으면 기준점을 버림. 들기, 던지기, 네트워크 보정 대비.")]
    [SerializeField] private float teleportThreshold = 0.5f;

    [Header("Ground snap")]
    [Tooltip("착지 상태에서 매 틱 아래로 눌러 주는 거리.\n" +
                "CharacterController는 마지막 이동에서 아래에 닿아야 착지로 치므로, " +
                "판이 움직이며 몇 mm만 떠도 착지가 풀림. 이를 막음.\n" +
                "바닥에 닿으면 거기서 멈추므로 파고들지 않음. 0이면 사용 안 함.")]
    [SerializeField] private float groundSnapDistance = 0.08f;

    [Header("Deck support")]
    [Tooltip("발밑 이 거리 안에 무언가 있으면 판 위에 서 있는 것으로 봄.")]
    [SerializeField] private float supportDistance = 0.2f;

    [Tooltip("발보다 이만큼 위에서부터 검사함. 판이 발을 파고든 상태도 서 있는 것으로 잡기 위함.")]
    [SerializeField] private float supportLift = 0.3f;

    [Tooltip("한 틱에 판에서 이보다 많이 멀어지면 떠오르는 중으로 보고 서 있지 않은 것으로 봄. 점프 직후 재점프 방지.")]
    [SerializeField] private float supportRiseTolerance = 0.04f;

    [Tooltip("발밑 검사 레이어.")]
    [SerializeField] private LayerMask supportMask = ~0;

    private static readonly List<PlatformTilt> platforms = new();
    private static readonly Dictionary<PlatformTilt, Rigidbody> bodies = new();
    private static float nextRefreshTime;
    private const float RefreshInterval = 1f;

    private bool hasAnchor;
    private PlatformTilt anchorPlatform;
    private Vector3 anchorLocal;          // 판 기준 로컬 좌표
    private Vector3 anchorPlatformPosition;
    private Vector3 lastFinalPosition;

    private CharacterController characterController;
    private readonly RaycastHit[] supportHits = new RaycastHit[8];
    private float lastSupportGap;
    private bool hasLastSupportGap;

    /// <summary>현재 실려 가고 있는 판. 없으면 null.</summary>
    public PlatformTilt CurrentPlatform { get; private set; }

    /// <summary>
    /// 판 위(판에 실린 물체 위 포함)에 서 있는지. 직전 EndTick 기준.
    /// 판이 움직이는 중에도 끊기지 않으므로 점프 조건과 애니 착지에 씀.
    /// </summary>
    public bool IsStandingOnDeck { get; private set; }

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

        Vector3 delta = Vector3.zero;

        bool teleported = hasAnchor &&
            (position - lastFinalPosition).sqrMagnitude > teleportThreshold * teleportThreshold;

        if (hasAnchor && anchorPlatform == platform && !teleported &&
            GetDeckPose(platform, out Vector3 deckPosition, out Quaternion deckRotation))
        {
            if (grounded && carryRotationWhenGrounded)
            {
                // 지난 틱에 서 있던 판 위 지점이 이번 틱에 어디로 가는지
                Vector3 carried = deckPosition + deckRotation * anchorLocal;
                delta = carried - position;
            }
            else
            {
                // 공중에서는 판의 이동만 따라감. 회전은 무시함.
                delta = deckPosition - anchorPlatformPosition;
                if (!carryVerticalWhenAirborne)
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

        if (CurrentPlatform == null ||
            !GetDeckPose(CurrentPlatform, out Vector3 deckPosition, out Quaternion deckRotation))
        {
            hasAnchor = false;
            ClearDeckSupport();
            return;
        }

        anchorPlatform = CurrentPlatform;
        anchorLocal = Quaternion.Inverse(deckRotation) * (finalPosition - deckPosition);
        anchorPlatformPosition = deckPosition;
        hasAnchor = true;

        UpdateDeckSupport();
    }

    /// <summary>
    /// 발밑을 직접 검사해 판 위에 서 있는지 판정함.
    /// 발보다 조금 위에서 쏘아 판이 발을 파고든 상태도 잡음.
    /// 판에서 멀어지는 중이면 서 있지 않은 것으로 봄.
    /// </summary>
    private void UpdateDeckSupport()
    {
        if (characterController == null) characterController = GetComponent<CharacterController>();

        if (CurrentPlatform == null || characterController == null || !characterController.enabled)
        {
            ClearDeckSupport();
            return;
        }

        Vector3 s = transform.lossyScale;
        float radius = characterController.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) * 0.9f;
        Vector3 center = transform.TransformPoint(characterController.center);
        Vector3 foot = center + Vector3.down * (characterController.height * 0.5f * Mathf.Abs(s.y));

        Vector3 origin = foot + Vector3.up * (radius + supportLift);
        float distance = supportLift + supportDistance;

        int count = Physics.SphereCastNonAlloc(
            origin, radius, Vector3.down, supportHits, distance, supportMask, QueryTriggerInteraction.Ignore);

        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var col = supportHits[i].collider;
            if (col == null || col.transform.IsChildOf(transform)) continue;

            // 시작부터 겹친 대상은 distance가 0으로 오므로 판이 발을 파고든 경우도 포함됨
            best = Mathf.Min(best, supportHits[i].distance);
        }

        if (float.IsPositiveInfinity(best))
        {
            ClearDeckSupport();
            return;
        }

        float gap = best - supportLift;
        bool rising = hasLastSupportGap && gap - lastSupportGap > supportRiseTolerance;

        IsStandingOnDeck = gap <= supportDistance && !rising;
        lastSupportGap = gap;
        hasLastSupportGap = true;
    }

    private void ClearDeckSupport()
    {
        IsStandingOnDeck = false;
        hasLastSupportGap = false;
    }

    /// <summary>기준이 되는 판 포즈. 현재 충돌체 위치.</summary>
    private bool GetDeckPose(PlatformTilt platform, out Vector3 position, out Quaternion rotation)
    {
        var body = GetBody(platform);
        if (body == null)
        {
            position = platform.transform.position;
            rotation = platform.transform.rotation;
            return false;
        }

        position = body.position;
        rotation = body.rotation;
        return true;
    }

    /// <summary>기준점을 버림. 강제로 위치를 옮긴 직후 호출.</summary>
    public void ResetAnchor()
    {
        hasAnchor = false;
        ClearDeckSupport();
    }

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