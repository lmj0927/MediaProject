using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 휠의 모든 튜닝 수치를 모아둔 에셋.
    /// </summary>
    [CreateAssetMenu(fileName = "WheelConfig", menuName = "Wheel/Wheel Config")]
    public class WheelConfig : ScriptableObject
    {
        [Header("Tilt — 판 기울기")]
        [Tooltip("무게중심이 이 거리만큼 벗어나면 최대 기울기에 도달한다. 판 반지름 정도로 시작.")]
        public float maxOffsetRadius = 2.5f;

        [Tooltip("판이 기울 수 있는 최대 각도. 너무 크면 플레이어가 전부 미끄러져 떨어진다.")]
        [Range(1f, 45f)] public float maxTiltAngle = 18f;

        [Tooltip("목표 기울기로 수렴하는 속도. 높을수록 즉각적이고 뻣뻣하다.")]
        [Range(0.5f, 30f)] public float tiltResponsiveness = 6f;

        [Tooltip("오프셋 대비 기울기 응답 곡선. 중앙 근처를 완만하게 만들면 조작이 안정된다.")]
        public AnimationCurve tiltResponseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Roll — 구체 구동")]
        [Tooltip("최대 기울기일 때의 목표 각속도(rad/s). 구체 반지름을 곱하면 대략적인 최고 속도.")]
        public float maxAngularSpeed = 12f;

        [Tooltip("목표 각속도로 수렴시키는 토크 세기. 낮으면 관성감, 높으면 즉각 반응.")]
        public float torqueGain = 8f;

        [Tooltip("공중에 있을 때 토크 배율. 0이면 공중 조작 불가.")]
        [Range(0f, 1f)] public float airControlFactor = 0.15f;

        [Tooltip("기울기가 없을 때 회전을 줄이는 감쇠. 0이면 계속 굴러간다.")]
        public float idleDamping = 2f;

        private void OnValidate()
        {
            maxOffsetRadius = Mathf.Max(0.01f, maxOffsetRadius);
            maxAngularSpeed = Mathf.Max(0f, maxAngularSpeed);
            torqueGain = Mathf.Max(0f, torqueGain);
            idleDamping = Mathf.Max(0f, idleDamping);
        }
    }
}
