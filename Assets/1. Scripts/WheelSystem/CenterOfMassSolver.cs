using System.Collections.Generic;
using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 판 위에 올라온 대상을 감지하고, 그들의 무게중심을 구해 축(구체 중심) 기준 수평 오프셋을 냄. 
    /// 판에 연결함.
    ///
    /// 감지에는 isTrigger가 켜진 콜라이더가 추가로 필요함.
    /// </summary>
    public class CenterOfMassSolver : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("오프셋을 재는 기준점. 보통 구체(축)의 Transform.")]
        [SerializeField] private Transform pivot;

        [Header("Detection")]
        [Tooltip("이 레이어에 속한 것만 감지함.")]
        [SerializeField] private LayerMask detectionMask = ~0;

        [Tooltip("판을 벗어난 뒤 목록에서 제거하기까지의 유예. " +
                 "0이면 점프할 때마다 무게중심이 깜빡여 판이 떨림.")]
        [SerializeField] private float exitGracePeriod = 0.25f;

        [Header("Bias")]
        [Tooltip("무게중심을 인위적으로 밀어내는 값(아이템, 돌풍 등).")]
        [SerializeField] private Vector3 externalBias = Vector3.zero;

        [Header("Advanced")]
        [Tooltip("계산 결과를 매끄럽게 만드는 시간. " +
                 "PlatformTilt.tiltResponsiveness와 역할이 겹치므로 둘 중 하나만 조절할 것. " +
                 "기본값에서 건드리지 않는 쪽을 권장함.")]
        [SerializeField] private float smoothTime = 0.05f;

        private readonly Dictionary<IWeightSource, float> pendingExits = new();
        private readonly List<IWeightSource> occupants = new();
        private readonly List<IWeightSource> scratch = new();

        private Vector3 smoothedOffset;
        private Vector3 smoothVelocity;

        /// <summary>축 기준 수평 오프셋(월드 XZ). 길이가 0이면 균형 상태.</summary>
        public Vector3 WorldOffset => smoothedOffset;

        /// <summary>무게중심의 월드 위치. 디버그 표시용.</summary>
        public Vector3 WorldCenterOfMass => PivotPosition + smoothedOffset;

        /// <summary>현재 판 위 총 무게. 0이면 아무도 없는 상태.</summary>
        public float TotalWeight { get; private set; }

        public bool HasOccupants => TotalWeight > Mathf.Epsilon;

        /// <summary>현재 판 위에 있는 것으로 간주되는 대상들.</summary>
        public IReadOnlyList<IWeightSource> Occupants => occupants;

        private Vector3 PivotPosition => pivot != null ? pivot.position : transform.position;

        private void Awake()
        {
            if (pivot == null)
                Debug.LogWarning($"{name}: pivot이 비어 있습니다. 구체의 Transform을 지정하세요.", this);
        }

        // ---- 점유 감지 ----

        private void OnTriggerEnter(Collider other)
        {
            if (!IsInMask(other.gameObject.layer)) return;

            var source = ResolveSource(other);
            if (source == null) return;

            pendingExits.Remove(source);
            if (!occupants.Contains(source)) occupants.Add(source);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsInMask(other.gameObject.layer)) return;

            var source = ResolveSource(other);
            if (source == null) return;

            if (exitGracePeriod <= 0f) Remove(source);
            else pendingExits[source] = Time.time + exitGracePeriod;
        }

        private void Update()
        {
            if (pendingExits.Count > 0)
            {
                scratch.Clear();
                foreach (var kvp in pendingExits)
                    if (Time.time >= kvp.Value) scratch.Add(kvp.Key);

                foreach (var source in scratch) Remove(source);
            }

            // 파괴되었거나 비활성화된 대상 정리
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                var source = occupants[i];
                if (source == null || (source is MonoBehaviour mb && mb == null))
                    occupants.RemoveAt(i);
            }
        }

        private void Remove(IWeightSource source)
        {
            occupants.Remove(source);
            pendingExits.Remove(source);
        }

        private static IWeightSource ResolveSource(Collider other)
        {
            // 자식 콜라이더로 들어오는 경우를 고려해 Rigidbody 쪽을 먼저 확인
            if (other.attachedRigidbody != null)
            {
                var fromBody = other.attachedRigidbody.GetComponentInChildren<IWeightSource>();
                if (fromBody != null) return fromBody;
            }
            return other.GetComponentInParent<IWeightSource>();
        }

        private bool IsInMask(int layer) => (detectionMask.value & (1 << layer)) != 0;

        // ---- 무게중심 ----

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
                    smoothedOffset, target, ref smoothVelocity, smoothTime,
                    Mathf.Infinity, Time.fixedDeltaTime);
            }
        }

        private Vector3 CalculateRawOffset()
        {
            Vector3 weightedSum = Vector3.zero;
            float total = 0f;

            for (int i = 0; i < occupants.Count; i++)
            {
                var source = occupants[i];
                if (source == null || !source.Contributes) continue;

                float w = source.Weight;
                if (w <= 0f) continue;

                weightedSum += source.WorldPosition * w;
                total += w;
            }

            TotalWeight = total;
            if (total <= Mathf.Epsilon) return Vector3.zero;

            Vector3 offset = (weightedSum / total) - PivotPosition;
            offset.y = 0f;   // 수평 성분만 기울기에 기여함.
            return offset;
        }

        /// <summary>외부 시스템이 무게중심을 밀어낼 때 사용.</summary>
        public void SetExternalBias(Vector3 bias)
        {
            bias.y = 0f;
            externalBias = bias;
        }

        private void OnDisable()
        {
            occupants.Clear();
            pendingExits.Clear();
        }
    }
}