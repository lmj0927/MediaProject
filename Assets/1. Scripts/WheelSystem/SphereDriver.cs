using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 판의 기울기를 읽어 구체에 보정 토크를 넣는다.
    /// 구체는 non-kinematic Rigidbody여야 충돌, 경사, 넉백이 물리로 처리된다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SphereDriver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlatformTilt platform;
        [SerializeField] private WheelConfig config;

        [Header("Ground check")]
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float groundCheckPadding = 0.15f;

        private Rigidbody body;
        private SphereCollider sphereCollider;

        public bool IsGrounded { get; private set; }

        /// <summary>현재 수평 속도 크기. UI나 카메라 연출에 쓴다.</summary>
        public float HorizontalSpeed
        {
            get
            {
                Vector3 v = body.linearVelocity;
                v.y = 0f;
                return v.magnitude;
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            sphereCollider = GetComponent<SphereCollider>();

            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (config == null)
            {
                config = ScriptableObject.CreateInstance<WheelConfig>();
                Debug.LogWarning($"{name}: WheelConfig가 없어 기본값으로 실행합니다.", this);
            }
            body.maxAngularVelocity = config.maxAngularSpeed * 1.5f;
        }

        private void FixedUpdate()
        {
            IsGrounded = CheckGrounded();

            if (platform == null) return;

            Vector3 downhill = platform.DownhillDirection;
            float tilt = platform.NormalizedTilt;

            if (downhill == Vector3.zero || tilt <= 0.001f)
            {
                ApplyIdleDamping();
                return;
            }

            // 구르는 방향에 수직인 수평 축이 회전축
            Vector3 rollAxis = Vector3.Cross(Vector3.up, downhill).normalized;
            Vector3 targetAngular = rollAxis * (tilt * config.maxAngularSpeed);

            Vector3 error = targetAngular - body.angularVelocity;
            float gain = IsGrounded ? config.torqueGain : config.torqueGain * config.airControlFactor;

            body.AddTorque(error * gain, ForceMode.Acceleration);
        }

        private void ApplyIdleDamping()
        {
            if (!IsGrounded || config.idleDamping <= 0f) return;
            body.AddTorque(-body.angularVelocity * config.idleDamping, ForceMode.Acceleration);
        }

        private bool CheckGrounded()
        {
            float radius = sphereCollider != null
                ? sphereCollider.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y)
                : 0.5f;

            return Physics.CheckSphere(
                transform.position + Vector3.down * groundCheckPadding,
                radius * 0.95f,
                groundMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>폭발, 범프, 부스터 같은 외부 충격용.</summary>
        public void AddImpulse(Vector3 force)
        {
            body.AddForce(force, ForceMode.Impulse);
        }
    }
}
