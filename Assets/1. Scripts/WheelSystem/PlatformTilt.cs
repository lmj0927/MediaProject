using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 무게중심 오프셋을 판의 기울기로 바꾸고, 동시에 구체 위치를 따라감.
    /// 판은 Kinematic Rigidbody여야 함.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CenterOfMassSolver))]
    public class PlatformTilt : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform sphere;
        [SerializeField] private WheelConfig config;

        [Header("Placement")]
        [Tooltip("구체의 실제 반지름에서 높이를 자동 계산함. 끄면 manualHeight를 그대로 사용함.")]
        [SerializeField] private bool deriveHeightFromSphere = true;

        [Tooltip("구체 반지름의 배수. 1 = 구체 위. 0 = 구체 중심.")]
        [Range(-1.5f, 2f)][SerializeField] private float radiusMultiplier = 1f;

        [Tooltip("자동 계산값에 추가로 더하는 보정값.")]
        [SerializeField] private float extraOffset = 0f;

        [Tooltip("deriveHeightFromSphere가 꺼졌을 때 쓰는 고정 높이.")]
        [SerializeField] private float manualHeight = 0f;

        private Rigidbody body;
        private CenterOfMassSolver solver;
        private Quaternion currentTilt = Quaternion.identity;
        private float cachedSphereRadius = -1f;

        /// <summary>구체 중심에서 판까지의 최종 높이.</summary>
        public float HeightOffset => deriveHeightFromSphere
            ? SphereRadius * radiusMultiplier + extraOffset
            : manualHeight;

        /// <summary>스케일이 반영된 구체의 실제 반지름.</summary>
        public float SphereRadius
        {
            get
            {
                if (cachedSphereRadius >= 0f) return cachedSphereRadius;
                cachedSphereRadius = MeasureSphereRadius();
                return cachedSphereRadius;
            }
        }

        private float MeasureSphereRadius()
        {
            if (sphere == null) return 0.5f;

            if (sphere.TryGetComponent<SphereCollider>(out var col))
            {
                Vector3 s = col.transform.lossyScale;
                float maxScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                return col.radius * maxScale;
            }

            // SphereCollider가 없으면 렌더러 경계로 추정
            if (sphere.TryGetComponent<Renderer>(out var rend))
            {
                return rend.bounds.extents.y;
            }

            return sphere.lossyScale.y * 0.5f;
        }

        /// <summary>런타임에 구체 크기가 바뀌면 호출해 캐시를 비움.</summary>
        public void InvalidateSphereRadius() => cachedSphereRadius = -1f;

        /// <summary>판의 현재 법선.</summary>
        public Vector3 SurfaceNormal => currentTilt * Vector3.up;

        /// <summary>수평면 기준 기울기 각도(도).</summary>
        public float TiltAngle => Vector3.Angle(Vector3.up, SurfaceNormal);

        /// <summary>0~1로 정규화한 기울기 세기.</summary>
        public float NormalizedTilt =>
            config.maxTiltAngle > 0f ? Mathf.Clamp01(TiltAngle / config.maxTiltAngle) : 0f;

        /// <summary>구체가 굴러가야 할 수평 방향. 기울기가 없으면 zero.</summary>
        public Vector3 DownhillDirection
        {
            get
            {
                Vector3 n = SurfaceNormal;
                Vector3 downhill = new Vector3(n.x, 0f, n.z);
                return downhill.sqrMagnitude > 1e-6f ? downhill.normalized : Vector3.zero;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            solver = GetComponent<CenterOfMassSolver>();

            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WheelConfig>();
                Debug.LogWarning($"{name}: WheelConfig가 없어 기본값으로 실행합니다.", this);
            }

            if (sphere == null)
            {
                Debug.LogError($"{name}: sphere 참조가 비어 있습니다. 위치 동기화를 중단합니다.", this);
            }
            else
            {
                IgnoreSphereCollisions();
            }

            currentTilt = transform.rotation;
        }

        /// <summary>
        /// 판의 솔리드 콜라이더가 구체를 밀어내지 않도록 충돌을 비활성화.
        /// </summary>
        private void IgnoreSphereCollisions()
        {
            var platformColliders = GetComponentsInChildren<Collider>();
            var sphereColliders = sphere.GetComponentsInChildren<Collider>();

            foreach (var a in platformColliders)
            {
                if (a.isTrigger) continue;
                foreach (var b in sphereColliders)
                {
                    if (b.isTrigger) continue;
                    Physics.IgnoreCollision(a, b, true);
                }
            }
        }

        private void FixedUpdate()
        {
            Quaternion target = CalculateTargetTilt();

            float t = config.tiltResponsiveness <= 0f
                ? 1f
                : 1f - Mathf.Exp(-config.tiltResponsiveness * Time.fixedDeltaTime);

            currentTilt = Quaternion.Slerp(currentTilt, target, t);
            body.MoveRotation(currentTilt);

            // sphere가 없으면 위치를 건드리지 않음.
            if (sphere == null) return;

            body.MovePosition(sphere.position + Vector3.up * HeightOffset);
        }

        private Quaternion CalculateTargetTilt()
        {
            Vector3 offset = solver.WorldOffset;
            if (offset.sqrMagnitude < 1e-6f) return Quaternion.identity;

            float radius = Mathf.Max(0.01f, config.maxOffsetRadius);
            float magnitude = Mathf.Clamp01(offset.magnitude / radius);

            // 응답 곡선: 중앙 근처는 둔감하게, 가장자리로 갈수록 급격하게
            magnitude = config.tiltResponseCurve.Evaluate(magnitude);

            Vector3 direction = offset.normalized;
            float angle = magnitude * config.maxTiltAngle;

            // 무게가 쏠린 쪽이 내려가도록 축을 잡음.
            // Unity의 AngleAxis는 왼손 법칙이므로 Cross(up, direction) 순서
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude < 1e-6f) return Quaternion.identity;

            return Quaternion.AngleAxis(angle, axis.normalized);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !Application.isPlaying)
            {
                rb.isKinematic = true;
            }

            cachedSphereRadius = -1f;

            // 에디터에서 값 수정시 즉각 반영.
            if (!Application.isPlaying && sphere != null)
            {
                transform.position = sphere.position + Vector3.up * HeightOffset;
            }
        }
#endif
    }
}