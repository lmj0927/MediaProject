using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WheelSystem
{
    /// <summary>
    /// 휠 감도 테스트용 플레이어.
    /// WASD 이동, Space 점프, 공중에서 Space 한 번 더 누르면 내리찍기.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TestWeightPlayer : MonoBehaviour, IWeightSource
    {
        [Header("Weight")]
        [SerializeField] private float baseWeight = 1f;
        [Tooltip("내리찍는 동안의 무게 배율.")]
        [SerializeField] private float slamWeightMultiplier = 4f;
        [Tooltip("착지 후에도 무게 배증이 유지되는 시간.")]
        [SerializeField] private float slamHoldTime = 0.2f;
        [Tooltip("배증된 무게가 평상시로 돌아오는 데 걸리는 시간.")]
        [SerializeField] private float slamRecoverTime = 0.35f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float acceleration = 40f;
        [SerializeField] private float airAcceleration = 8f;

        [Header("Jump")]
        [SerializeField] private float jumpSpeed = 6f;
        [SerializeField] private float slamSpeed = 18f;
        [Tooltip("점프 직후 이 시간 안에는 내리찍기를 쓸 수 없음.")]
        [SerializeField] private float slamLockout = 0.12f;
        [Tooltip("내리찍기가 이 시간 안에 착지하지 못하면 강제로 해제함(무한 잠김 방지).")]
        [SerializeField] private float slamTimeout = 3f;

        [Tooltip("점프 후 다시 점프할 수 있게 되기까지의 시간.")]
        [SerializeField] private float jumpCooldown = 0.2f;

        [Header("Ground check")]
        [Tooltip("접촉면의 법선 Y가 이 값보다 크면 바닥으로 간주. 0.5면 약 60도까지 허용.")]
        [Range(0f, 1f)][SerializeField] private float groundNormalThreshold = 0.4f;
        [Tooltip("바닥에서 떨어진 뒤에도 잠시 착지 상태로 취급하는 시간.")]
        [SerializeField] private float coyoteTime = 0.12f;

        [Header("Ground alignment")]
        [Tooltip("접촉면의 기울기에 맞춰 몸을 세울지 여부.")]
        [SerializeField] private bool alignToGroundNormal = true;
        [Tooltip("정렬이 따라붙는 속도. 높을수록 즉각적.")]
        [SerializeField] private float alignSpeed = 14f;

        [Tooltip("착지 상태에서 이 속도 이하로 떠오르는 것은 무시함. " +
                 "판이 회전하며 밀어 올리는 힘을 걸러냄. 0이면 흡착 없음.")]
        [SerializeField] private float groundStickThreshold = 4f;

        [Tooltip("착지 상태에서 바닥으로 눌러 주는 가속도. 접촉이 끊기는 것을 막음.\n" +
                 "주의: 이 값은 수직항력을 키워 마찰을 함께 늘리므로 크게 잡으면 완만한 기울기에서 아무것도 미끄러지지 않음.")]
        [SerializeField] private float groundStickForce = 4f;


        [Header("Camera")]
        [Tooltip("이동 방향의 기준. 비우면 메인 카메라를 사용함.")]
        [SerializeField] private Transform cameraTransform;

        [Header("Debug")]
        [SerializeField] private bool showDebugHud = true;

        private Rigidbody body;
        private bool jumpQueued;
        private bool slamQueued;
        private bool isSlamming;
        private float slamElapsed;
        private float airborneTime;
        private float weightMultiplier = 1f;
        private float slamTimer;
        private float lastGroundedTime = -99f;
        private float jumpCooldownTimer;

        // 충돌 접점에서 모은 바닥 정보
        private bool contactGrounded;
        private Transform contactGround;
        private Vector3 contactNormal = Vector3.up;
        private Vector3 groundNormal = Vector3.up;

        // 움직이는 발판 위에서 같이 실려 가기 위한 추정값
        private Transform groundTransform;
        private Vector3 lastGroundPosition;
        private Quaternion lastGroundRotation = Quaternion.identity;
        private Vector3 groundVelocity;         // 판 원점의 선속도
        private Vector3 groundAngularVelocity;  // 판의 각속도 (rad/s)
        private Vector3 groundPointVelocity;    // 플레이어 발밑 지점의 실제 속도

        private Vector2 debugInput;

        public bool IsGrounded { get; private set; }
        public bool HasCoyote => Time.time - lastGroundedTime <= coyoteTime;

        // ---- IWeightSource ----
        public Vector3 WorldPosition => transform.position;
        public float Weight => baseWeight * weightMultiplier;
        public bool Contributes => isActiveAndEnabled;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = false;
            body.freezeRotation = true;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            if (!JumpPressedThisFrame()) return;

            bool canJump = (IsGrounded || HasCoyote) && jumpCooldownTimer <= 0f;

            if (canJump) jumpQueued = true;
            else if (!isSlamming && airborneTime > slamLockout) slamQueued = true;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            bool wasGrounded = IsGrounded;

            if (jumpCooldownTimer > 0f) jumpCooldownTimer -= dt;

            // 쿨다운 중에는 접촉이 남아 있어도 착지로 치지 않음.
            IsGrounded = contactGrounded && jumpCooldownTimer <= 0f;
            Transform ground = contactGround;
            if (contactGrounded) groundNormal = contactNormal;
            contactGrounded = false;
            contactGround = null;

            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
                airborneTime = 0f;
                TrackGroundVelocity(ground, dt);
            }
            else
            {
                airborneTime += dt;
                groundTransform = null;
                groundVelocity = Vector3.Lerp(groundVelocity, Vector3.zero, dt * 5f);
                groundAngularVelocity = Vector3.zero;
                groundPointVelocity = Vector3.Lerp(groundPointVelocity, Vector3.zero, dt * 5f);
            }

            UpdateSlamState(wasGrounded, dt);

            HandleJump();
            HandleSlam();
            HandleMovement(dt);
            ApplyGroundStick();
            HandleGroundAlignment(dt);
            UpdateWeight(dt);
        }

        private void UpdateSlamState(bool wasGrounded, float dt)
        {
            if (!isSlamming) return;

            slamElapsed += dt;

            // 착지했거나, 너무 오래 끌면 해제함.
            if (IsGrounded || slamElapsed > slamTimeout)
            {
                isSlamming = false;
                slamElapsed = 0f;
                slamTimer = slamHoldTime;
            }
        }

        /// <summary>
        /// 발 밑 발판의 이동 속도를 추정함.
        /// </summary>
        private void TrackGroundVelocity(Transform ground, float dt)
        {
            if (ground == null)
            {
                groundVelocity = Vector3.zero;
                groundAngularVelocity = Vector3.zero;
                groundPointVelocity = Vector3.zero;
                return;
            }

            if (ground != groundTransform)
            {
                groundTransform = ground;
                lastGroundPosition = ground.position;
                lastGroundRotation = ground.rotation;
                groundVelocity = Vector3.zero;
                groundAngularVelocity = Vector3.zero;
                groundPointVelocity = Vector3.zero;
                return;
            }

            if (dt <= 0f) return;

            // 선속도
            groundVelocity = (ground.position - lastGroundPosition) / dt;
            lastGroundPosition = ground.position;

            // 각속도: 회전 변화량을 축-각으로 풀어서 rad/s로 환산
            Quaternion deltaRot = ground.rotation * Quaternion.Inverse(lastGroundRotation);
            lastGroundRotation = ground.rotation;

            deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;   // 최단 경로로 보정

            groundAngularVelocity = float.IsNaN(axis.x) || Mathf.Abs(angleDeg) < 0.0001f
                ? Vector3.zero
                : axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);

            // 판이 회전하면 중심에서 멀수록 표면이 빠르게 움직임.
            Vector3 lever = transform.position - ground.position;
            groundPointVelocity = groundVelocity + Vector3.Cross(groundAngularVelocity, lever);
        }

        private void HandleJump()
        {
            if (!jumpQueued) return;
            jumpQueued = false;

            Vector3 v = body.linearVelocity;
            v.y = jumpSpeed + Mathf.Max(0f, groundPointVelocity.y);
            body.linearVelocity = v;

            IsGrounded = false;
            lastGroundedTime = -99f;
            jumpCooldownTimer = jumpCooldown;
        }

        private void HandleSlam()
        {
            if (!slamQueued) return;
            slamQueued = false;

            isSlamming = true;
            slamElapsed = 0f;

            Vector3 v = body.linearVelocity;
            v.y = -slamSpeed;
            body.linearVelocity = v;
        }

        private void HandleMovement(float dt)
        {
            Vector2 input = ReadMoveInput();
            debugInput = input;

            // 내리찍는 중에는 수평 조작을 잠금.
            if (isSlamming) return;

            Vector3 wish = ToWorldDirection(input) * (moveSpeed * Mathf.Clamp01(input.magnitude));

            // 움직이고 회전하는 판 위에서 제자리를 유지.
            Vector3 carry = IsGrounded ? groundPointVelocity : groundVelocity;
            Vector3 target = new Vector3(carry.x + wish.x, 0f, carry.z + wish.z);
            Vector3 current = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            Vector3 diff = target - current;

            float accel = IsGrounded ? acceleration : airAcceleration;
            body.AddForce(Vector3.ClampMagnitude(diff / dt, accel), ForceMode.Acceleration);

        }

        /// <summary>
        /// 판이 회전하며 밀어 올리는 속도를 걸러 내고, 바닥으로 살짝 눌러 접촉을 유지함.
        /// 점프와 내리찍기에는 적용하지 않음.
        /// </summary>
        private void ApplyGroundStick()
        {
            if (!IsGrounded || isSlamming || jumpQueued) return;
            if (jumpCooldownTimer > 0f) return;

            if (groundStickThreshold > 0f)
            {
                // 판이 회전하며 밀어 올린 성분만 제거.
                float rise = body.linearVelocity.y - groundPointVelocity.y;
                if (rise > 0f && rise < groundStickThreshold)
                {
                    Vector3 v = body.linearVelocity;
                    v.y = groundPointVelocity.y;
                    body.linearVelocity = v;
                }
            }

            if (groundStickForce > 0f)
            {
                body.AddForce(Vector3.down * groundStickForce, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// 몸을 접촉면 법선에 맞춰 세움.
        /// </summary>
        private void HandleGroundAlignment(float dt)
        {
            if (!alignToGroundNormal) return;

            // 공중에서는 서서히 수직으로 되돌림.
            Vector3 targetUp = IsGrounded ? groundNormal : Vector3.up;

            Quaternion target = Quaternion.FromToRotation(transform.up, targetUp) * transform.rotation;
            float t = 1f - Mathf.Exp(-alignSpeed * dt);

            body.MoveRotation(Quaternion.Slerp(transform.rotation, target, t));
        }

        private void UpdateWeight(float dt)
        {
            float target;

            if (isSlamming)
            {
                target = slamWeightMultiplier;
                slamTimer = slamHoldTime;
            }
            else if (slamTimer > 0f)
            {
                slamTimer -= dt;
                target = slamWeightMultiplier;
            }
            else
            {
                target = 1f;
            }

            if (target > weightMultiplier)
            {
                weightMultiplier = target;   // 즉각 가중.
            }
            else if (slamRecoverTime > 0f)
            {
                float rate = (slamWeightMultiplier - 1f) / slamRecoverTime;
                weightMultiplier = Mathf.MoveTowards(weightMultiplier, target, rate * dt);
            }
            else
            {
                weightMultiplier = target;
            }
        }

        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Vector3 forward = Vector3.forward;
            Vector3 right = Vector3.right;

            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            }

            return (forward * input.y + right * input.x).normalized;
        }

        // ---- 바닥 감지 (충돌 접점) ----

        private void OnCollisionStay(Collision collision) => EvaluateContacts(collision);
        private void OnCollisionEnter(Collision collision) => EvaluateContacts(collision);

        private void EvaluateContacts(Collision collision)
        {
            int count = collision.contactCount;
            float best = groundNormalThreshold;

            for (int i = 0; i < count; i++)
            {
                Vector3 n = collision.GetContact(i).normal;
                if (n.y <= best) continue;

                // 가장 평평한 접촉면을 바닥으로 간주.
                best = n.y;
                contactGrounded = true;
                contactGround = collision.transform;
                contactNormal = n;
            }
        }

        // ---- Input ----

        private static Vector2 ReadMoveInput()
        {
            Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            }
#else
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1f;
#endif
            return Vector2.ClampMagnitude(v, 1f);
        }

        private static bool JumpPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugHud || !Application.isPlaying) return;

            var rect = new Rect(Screen.width - 250, 10, 240, 152);
            GUI.Box(rect, "");
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));
            GUILayout.Label($"input      : {debugInput}");
            GUILayout.Label($"grounded   : {IsGrounded}");
            GUILayout.Label($"slamming   : {isSlamming}");
            GUILayout.Label($"weight x   : {weightMultiplier:F2}");
            GUILayout.Label($"velocity   : {body.linearVelocity.magnitude:F2}");
            GUILayout.Label($"groundVel  : {groundVelocity.magnitude:F2}");
            GUILayout.Label($"pointVel.y : {groundPointVelocity.y:F2}");
            GUILayout.Label($"groundSpin : {groundAngularVelocity.magnitude:F2}");
            GUILayout.EndArea();
        }
#endif
    }
}