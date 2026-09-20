using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 휠의 튜닝 수치를 모아둔 에셋.
    /// PlatformTilt와 SphereDriver에 "같은" 에셋을 사용해야 함.
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
                 "30도를 넘기지 않도록 주의.")]
        [Range(1f, 45f)] public float maxTiltAngle = 18f;

        [Tooltip("목표 기울기로 수렴하는 속도.\n" +
                 "높을수록 즉각적, 낮을수록 관성적.")]
        [Range(0.5f, 30f)] public float tiltResponsiveness = 6f;

        [Header("Feel — 구동")]
        [Tooltip("최대 기울기일 때의 목표 각속도(rad/s).\n" +
                 "실제 속도 ≈ 해당 수치 × 구체 반지름. 맵 크기 맞춰 조정.")]
        public float maxAngularSpeed = 12f;

        [Tooltip("목표 각속도까지 밀어붙이는 힘. 도달 시간을 결정.")]
        public float torqueGain = 8f;

        [Tooltip("기울기가 없을 때 회전을 줄이는 감쇠값.")]
        public float idleDamping = 2f;

        // ─────────────────────────────────────────────
        // 고급 설정(특별한 경우에만 수정)
        // ─────────────────────────────────────────────
        [Header("Advanced")]
        [Tooltip("오프셋 대비 기울기의 응답 곡선.\n" +
                 "tiltResponsiveness와 유사 역할.\n" +
                 "부동 상태에서 판이 미세하게 떨릴 때만 시작점을 납작하게.")]
        public AnimationCurve tiltResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("공중에 있을 때 토크 배율. 0으로 두면 회전이 유지된 채 착지.")]
        [Range(0.05f, 1f)] public float airControlFactor = 0.15f;

        private void OnValidate()
        {
            maxOffsetRadius = Mathf.Max(0.01f, maxOffsetRadius);
            maxAngularSpeed = Mathf.Max(0f, maxAngularSpeed);
            torqueGain = Mathf.Max(0f, torqueGain);
            idleDamping = Mathf.Max(0f, idleDamping);
        }
    }
}