using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 센서가 준 목록으로 무게중심을 구하고, 축(구체 중심) 기준 수평 오프셋을 낸다.
    /// 출력은 월드 공간 XZ 벡터. 판의 현재 기울기에 의존하지 않으므로
    /// 기울기 → 무게중심 → 기울기로 되먹임되는 발진이 생기지 않는다.
    /// </summary>
    [RequireComponent(typeof(PlatformOccupancySensor))]
    public class CenterOfMassSolver : MonoBehaviour
    {
        [Tooltip("오프셋을 재는 기준점. 보통 구체(축)의 Transform.")]
        [SerializeField] private Transform pivot;

        [Tooltip("계산 결과를 매끄럽게 만드는 시간. 0이면 즉시 반영. 플레이어가 순간이동하듯 튈 때 완충.")]
        [SerializeField] private float smoothTime = 0.08f;

        [Tooltip("외부에서 무게중심을 인위적으로 밀어내는 값(아이템, 바람, 돌풍 등).")]
        [SerializeField] private Vector3 externalBias = Vector3.zero;

        private PlatformOccupancySensor sensor;
        private Vector3 smoothedOffset;
        private Vector3 smoothVelocity;

        /// <summary>축 기준 수평 오프셋(월드 XZ). 길이가 0이면 균형 상태.</summary>
        public Vector3 WorldOffset => smoothedOffset;

        /// <summary>무게중심의 월드 위치. 디버그 표시용.</summary>
        public Vector3 WorldCenterOfMass => PivotPosition + smoothedOffset;

        /// <summary>현재 판 위 총 무게. 0이면 아무도 없는 상태.</summary>
        public float TotalWeight { get; private set; }

        public bool HasOccupants => TotalWeight > Mathf.Epsilon;

        private Vector3 PivotPosition => pivot != null ? pivot.position : transform.position;

        private void Awake()
        {
            sensor = GetComponent<PlatformOccupancySensor>();
            if (pivot == null) Debug.LogWarning($"{name}: pivot이 비어 있습니다. 구체의 Transform을 지정하세요.", this);
        }

        private void FixedUpdate()
        {
            Vector3 target = CalculateRawOffset() + externalBias;

            if (smoothTime <= 0f)
            {
                smoothedOffset = target;
                smoothVelocity = Vector3.zero;
            }
            else
            {
                smoothedOffset = Vector3.SmoothDamp(
                    smoothedOffset, target, ref smoothVelocity, smoothTime, Mathf.Infinity, Time.fixedDeltaTime);
            }
        }

        private Vector3 CalculateRawOffset()
        {
            Vector3 weightedSum = Vector3.zero;
            float total = 0f;

            var list = sensor.Occupants;
            for (int i = 0; i < list.Count; i++)
            {
                var source = list[i];
                if (source == null || !source.Contributes) continue;

                float w = source.Weight;
                if (w <= 0f) continue;

                weightedSum += source.WorldPosition * w;
                total += w;
            }

            TotalWeight = total;
            if (total <= Mathf.Epsilon) return Vector3.zero;

            Vector3 com = weightedSum / total;
            Vector3 offset = com - PivotPosition;
            offset.y = 0f;   // 수평 성분만 기울기에 기여한다
            return offset;
        }

        /// <summary>외부 시스템이 무게중심을 밀어낼 때 사용.</summary>
        public void SetExternalBias(Vector3 bias)
        {
            bias.y = 0f;
            externalBias = bias;
        }
    }
}
