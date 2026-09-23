using UnityEngine;

/// <summary>
/// 휠의 튜닝 수치를 모아둔 에셋.
/// PlatformTilt와 SphereDriver에 "같은" 에셋을 사용해야 함
/// </summary>
[CreateAssetMenu(fileName = "WheelConfig", menuName = "Wheel/Wheel Config")]
public class WheelConfig : ScriptableObject
{
    // ─────────────────────────────────────────────
    // 보정값: 판 크기가 바뀔 때만 조정.
    // ─────────────────────────────────────────────
    [Header("Calibration (판 크기에 맞추는 값)")]
    [Tooltip("무게중심이 이 거리만큼 벗어나면 최대 기울기에 도달한다.\n" +
                "판의 실제 반지름과 같게 맞추는 것이 기준점. 감도 조절용이 아니다.")]
    public float maxOffsetRadius = 2.5f;

    // ─────────────────────────────────────────────
    // 기본 설정
    // ─────────────────────────────────────────────
    [Header("Feel — 기울기")]
    [Tooltip("판이 기울 수 있는 최대 각도.\n" +
                "되도록 30도를 넘기지 말 것.")]
    [Range(1f, 45f)] public float maxTiltAngle = 18f;

    [Tooltip("목표 기울기로 수렴하는 속도..\n" +
                "높으면 즉각적, 낮으면 관성적.")]
    [Range(0.5f, 30f)] public float tiltResponsiveness = 6f;

    [Header("Feel — 구동")]
    [Tooltip("최대 기울기일 때의 목표 각속도(rad/s).\n" +
                "실제 속도 ≈ 이 값 × 구체 반지름.")]
    public float maxAngularSpeed = 12f;

    [Tooltip("목표 각속도까지 밀어붙이는 힘. '거기까지 가는 시간'을 정함.")]
    public float torqueGain = 8f;

    [Tooltip("기울기가 없을 때 회전을 줄이는 감쇠. 멈출 수 있는지를 결정함.")]
    public float idleDamping = 2f;

    [Header("Safety — 발사 방지")]
    [Tooltip("판이 회전할 수 있는 최대 각속도(도/초). 0이면 제한 없음.\n" +
                "낮추면 안전하지만 반응이 둔해짐. 60~120 사이를 추천.")]
    public float maxTiltAngularSpeed = 90f;

    // ─────────────────────────────────────────────
    // 고급: 대부분 기본값으로 충분하다
    // ─────────────────────────────────────────────
    [Header("Advanced (대개 손대지 않음)")]
    [Tooltip("오프셋 대비 기울기의 응답 곡선.\n" +
                "tiltResponsiveness와 역할이 겹치므로 둘 중 하나만 조절할 것.\n" +
                "가만히 있는데 판이 미세하게 떨릴 때만 시작점을 납작하게 만들 것.")]
    public AnimationCurve tiltResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("공중에 있을 때 토크 배율. 0으로 두면 회전이 유지된 채 착지함.")]
    [Range(0.05f, 1f)] public float airControlFactor = 0.15f;

    private void OnValidate()
    {
        maxOffsetRadius = Mathf.Max(0.01f, maxOffsetRadius);
        maxAngularSpeed = Mathf.Max(0f, maxAngularSpeed);
        torqueGain = Mathf.Max(0f, torqueGain);
        idleDamping = Mathf.Max(0f, idleDamping);
        maxTiltAngularSpeed = Mathf.Max(0f, maxTiltAngularSpeed);
    }
}
