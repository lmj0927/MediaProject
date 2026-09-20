using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 판 위에서 무게중심 계산에 포함되는 대상.
    /// </summary>
    public interface IWeightSource
    {
        /// <summary>무게중심 계산에 쓰는 월드 위치.</summary>
        Vector3 WorldPosition { get; }

        /// <summary>가중치. 0 이하면 계산에서 제외함.</summary>
        float Weight { get; }

        /// <summary>일시적으로 무게를 무시할지 여부(점프 중 등).</summary>
        bool Contributes { get; }
    }

    /// <summary>
    /// IWeightSource의 범용 구현체.
    /// 플레이어 외 무게 요소에 사용함.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeightSource : MonoBehaviour, IWeightSource
    {
        [Tooltip("무게중심 계산에 쓰는 가중치. Rigidbody 질량과 별개로 게임플레이용으로 조절 가능.")]
        [SerializeField] private float weight = 1f;

        [Tooltip("켜면 Rigidbody.mass를 weight 대신 사용함.")]
        [SerializeField] private bool useRigidbodyMass = false;

        [Tooltip("무게중심 기준점. 비우면 오브젝트의 Transform을 사용함.")]
        [SerializeField] private Transform measurePoint;

        [Tooltip("끄면 판 위에 있어도 무게중심에 영향을 주지 않음.")]
        [SerializeField] private bool contributes = true;

        private Rigidbody cachedBody;

        public Vector3 WorldPosition => measurePoint != null ? measurePoint.position : transform.position;

        public float Weight
        {
            get
            {
                if (!useRigidbodyMass) return weight;
                if (cachedBody == null) cachedBody = GetComponentInParent<Rigidbody>();
                return cachedBody != null ? cachedBody.mass : weight;
            }
        }

        public bool Contributes
        {
            get => contributes && isActiveAndEnabled;
            set => contributes = value;
        }

        /// <summary>런타임에 무게를 바꾸고 싶을 때 사용함.</summary>
        public void SetWeight(float value)
        {
            weight = Mathf.Max(0f, value);
            useRigidbodyMass = false;
        }

        private void Reset()
        {
            measurePoint = transform;
        }
    }
}
