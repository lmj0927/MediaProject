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

    [Tooltip("물리 엔진이 허용하는 구체 회전 속도 상한(rad/s). 조작으로 내는 속도와는 무관함.\n" +
                "낮으면 해머 등으로 빠르게 밀릴 때 회전이 못 따라가 구르지 않고 미끄러짐. 실제 굴러갈 수 있는 속도 ≈ 이 값 × 반지름.")]
    [Min(0.1f)] public float maxSpinSpeed = 20f;

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

    // ─────────────────────────────────────────────
    // 내려찍기
    // ─────────────────────────────────────────────
    [Header("Stamp — 내려찍기")]
    [Tooltip("판 가장자리에서 내려찍었을 때 휠에 바로 더하는 속도(m/s).\n" +
                "찍은 쪽으로 밀리며, 중심에서 멀수록 커지고 정중앙이면 0. 0이면 사용 안 함.\n" +
                "무게(기울기)와 별개로 들어가므로 소수 쪽 내려찍기도 이동에 반영됨.")]
    public float stampKickSpeed = 2f;

    [Tooltip("연타 방지(초). 같은 사람이 마지막으로 찍은 뒤 이 시간이 지나야 반동이 최대가 됨.\n" +
                "그 전에 찍으면 지난 시간에 비례해 약해짐. 0이면 연타해도 매번 최대.")]
    [Min(0f)] public float stampRecoverTime = 2f;

    [Tooltip("휠이 공중에 있을 때 내려찍기 속도 배율.")]
    [Range(0f, 1f)] public float stampAirFactor = 0.3f;

    // ─────────────────────────────────────────────
    // 강한 충격(해머 등)
    // ─────────────────────────────────────────────
    [Header("Knockback — 강한 충격")]
    [Tooltip("해머 등에 맞은 뒤 조작이 약해지는 시간(초).\n" +
                "조작이 그대로면 땅을 잡는 힘 때문에 옆으로 밀린 속도가 0.5초 만에 사라져 맞아도 티가 안 남.")]
    [Min(0f)] public float knockbackRecoverTime = 0.6f;

    [Tooltip("조작이 약해진 동안의 토크 배율. 0이면 밀리는 동안 전혀 버티지 못함.")]
    [Range(0f, 1f)] public float knockbackControl = 0.1f;

    [Tooltip("조작이 약해진 동안 밀려나는 속도를 줄이는 정도(1/초). 0이면 줄이지 않음.\n" +
                "해머는 닿은 지점의 속도만큼 휠을 밀어 뚫고 들어가지 않게 하므로 처음 속도가 큼. 이 값으로 밀려가는 거리를 줄임.\n" +
                "조작이 약해지는 시간 동안 속도는 e^(-값×시간)배가 됨.")]
    [Min(0f)] public float knockbackDrag = 4f;

    private void OnValidate()
    {
        maxOffsetRadius = Mathf.Max(0.01f, maxOffsetRadius);
        maxAngularSpeed = Mathf.Max(0f, maxAngularSpeed);
        torqueGain = Mathf.Max(0f, torqueGain);
        idleDamping = Mathf.Max(0f, idleDamping);
        maxTiltAngularSpeed = Mathf.Max(0f, maxTiltAngularSpeed);
        stampKickSpeed = Mathf.Max(0f, stampKickSpeed);
        stampRecoverTime = Mathf.Max(0f, stampRecoverTime);
        knockbackRecoverTime = Mathf.Max(0f, knockbackRecoverTime);
        knockbackDrag = Mathf.Max(0f, knockbackDrag);
        maxSpinSpeed = Mathf.Max(0.1f, maxSpinSpeed);
    }
}
