using System.Collections.Generic;
using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 판 위에 무엇이 올라와 있는지만 관리한다. 무게중심 계산은 하지 않는다.
    /// 판에 붙이고, isTrigger가 켜진 콜라이더를 함께 둔다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlatformOccupancySensor : MonoBehaviour
    {
        [Tooltip("이 레이어에 속한 것만 감지한다.")]
        [SerializeField] private LayerMask detectionMask = ~0;

        [Tooltip("퇴장 후 목록에서 제거하기까지의 유예 시간. 점프나 물리 떨림으로 무게중심이 튀는 것을 막는다.")]
        [SerializeField] private float exitGracePeriod = 0.25f;

        private readonly Dictionary<IWeightSource, float> pendingExits = new();
        private readonly List<IWeightSource> occupants = new();
        private readonly List<IWeightSource> scratch = new();

        /// <summary>현재 판 위에 있는 것으로 간주되는 대상들.</summary>
        public IReadOnlyList<IWeightSource> Occupants => occupants;

        public int Count => occupants.Count;

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
            // 유예 시간이 지난 대상 정리
            if (pendingExits.Count > 0)
            {
                scratch.Clear();
                foreach (var kvp in pendingExits)
                {
                    if (Time.time >= kvp.Value) scratch.Add(kvp.Key);
                }
                foreach (var source in scratch) Remove(source);
            }

            // 파괴되었거나 비활성화된 대상 정리
            for (int i = occupants.Count - 1; i >= 0; i--)
            {
                var source = occupants[i];
                if (source == null || source is MonoBehaviour mb && mb == null)
                {
                    occupants.RemoveAt(i);
                }
            }
        }

        private void Remove(IWeightSource source)
        {
            occupants.Remove(source);
            pendingExits.Remove(source);
        }

        private static IWeightSource ResolveSource(Collider other)
        {
            // 자식 콜라이더로 들어오는 경우를 고려해 Rigidbody 쪽을 먼저 본다.
            if (other.attachedRigidbody != null)
            {
                var fromBody = other.attachedRigidbody.GetComponentInChildren<IWeightSource>();
                if (fromBody != null) return fromBody;
            }
            return other.GetComponentInParent<IWeightSource>();
        }

        private bool IsInMask(int layer) => (detectionMask.value & (1 << layer)) != 0;

        private void OnDisable()
        {
            occupants.Clear();
            pendingExits.Clear();
        }
    }
}
