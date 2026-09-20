using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WheelSystem
{
    /// <summary>
    /// 휠 감도 테스트용 플레이어. 팀원의 본 컨트롤러가 나오면 버리는 임시 스크립트다.
    /// WASD 이동, Space 점프, 공중에서 Space 한 번 더 누르면 내리찍기.
    /// 바닥 감지는 충돌 접점으로 한다. 레이캐스트는 자기 콜라이더를 때리거나
    /// 기울어진 판 위에서 헛돌기 쉬워서 쓰지 않는다.
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
        [Tooltip("점프 직후 이 시간 안에는 내리찍기를 쓸 수 없다.")]
        [SerializeField] private float slamLockout = 0.12f;
        [Tooltip("내리찍기가 이 시간 안에 착지하지 못하면 강제로 해제한다. 무한 잠김 방지용.")]
        [SerializeField] private float slamTimeout = 3f;

        [Header("Ground check")]
        [Tooltip("접촉면의 법선 Y가 이 값보다 크면 바닥으로 친다. 0.5면 약 60도까지 허용.")]
        [Range(0f, 1f)][SerializeField] private float groundNormalThreshold = 0.4f;
        [Tooltip("바닥에서 떨어진 뒤에도 잠시 착지 상태로 취급하는 시간.")]
        [SerializeField] private float coyoteTime = 0.12f;

        [Header("Camera")]
        [Tooltip("이동 방향의 기준. 비우면 메인 카메라를 쓴다.")]
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

        // 충돌 접점에서 모은 바닥 정보
        private bool contactGrounded;
        private Transform contactGround;

        // 움직이는 발판 위에서 같이 실려 가기 위한 추정값
        private Transform groundTransform;
        private Vector3 lastGroundPosition;
        private Vector3 groundVelocity;

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

            if (IsGrounded || HasCoyote) jumpQueued = true;
            else if (!isSlamming && airborneTime > slamLockout) slamQueued = true;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            bool wasGrounded = IsGrounded;

            // 충돌 콜백은 FixedUpdate 이후에 오므로, 직전 스텝의 결과를 여기서 소비한다
            IsGrounded = contactGrounded;
            Transform ground = contactGround;
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
            }

            UpdateSlamState(wasGrounded, dt);

            HandleJump();
            HandleSlam();
            HandleMovement(dt);
            UpdateWeight(dt);
        }

        private void UpdateSlamState(bool wasGrounded, float dt)
        {
            if (!isSlamming) return;

            slamElapsed += dt;

            // 착지했거나, 너무 오래 끌면 해제한다.
            // 타임아웃이 없으면 바닥 감지가 어긋났을 때 조작이 영구히 잠긴다.
            if (IsGrounded || slamElapsed > slamTimeout)
            {
                isSlamming = false;
                slamElapsed = 0f;
                slamTimer = slamHoldTime;
            }
        }

        /// <summary>
        /// 발 밑 발판의 이동 속도를 추정한다.
        /// 판은 Kinematic이라 Rigidbody.linearVelocity를 믿을 수 없어서 직접 잰다.
        /// </summary>
        private void TrackGroundVelocity(Transform ground, float dt)
        {
            if (ground == null)
            {
                groundVelocity = Vector3.zero;
                return;
            }

            if (ground != groundTransform)
            {
                groundTransform = ground;
                lastGroundPosition = ground.position;
                groundVelocity = Vector3.zero;
                return;
            }

            Vector3 delta = ground.position - lastGroundPosition;
            lastGroundPosition = ground.position;
            groundVelocity = dt > 0f ? delta / dt : Vector3.zero;
        }

        private void HandleJump()
        {
            if (!jumpQueued) return;
            jumpQueued = false;

            Vector3 v = body.linearVelocity;
            v.y = jumpSpeed + Mathf.Max(0f, groundVelocity.y);
            body.linearVelocity = v;

            IsGrounded = false;
            lastGroundedTime = -99f;
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

            // 내리찍는 중에는 수평 조작만 잠근다
            if (isSlamming) return;

            Vector3 wish = ToWorldDirection(input) * (moveSpeed * Mathf.Clamp01(input.magnitude));

            // 발판 속도를 기준으로 삼아야 움직이는 판 위에서 제자리를 유지할 수 있다
            Vector3 target = new Vector3(groundVelocity.x + wish.x, 0f, groundVelocity.z + wish.z);
            Vector3 current = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            Vector3 diff = target - current;

            float accel = IsGrounded ? acceleration : airAcceleration;
            body.AddForce(Vector3.ClampMagnitude(diff / dt, accel), ForceMode.Acceleration);
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
                weightMultiplier = target;   // 즉시 무거워진다
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
            for (int i = 0; i < count; i++)
            {
                if (collision.GetContact(i).normal.y > groundNormalThreshold)
                {
                    contactGrounded = true;
                    contactGround = collision.transform;
                    return;
                }
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

            var rect = new Rect(Screen.width - 250, 10, 240, 116);
            GUI.Box(rect, "");
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12));
            GUILayout.Label($"input      : {debugInput}");
            GUILayout.Label($"grounded   : {IsGrounded}");
            GUILayout.Label($"slamming   : {isSlamming}");
            GUILayout.Label($"weight x   : {weightMultiplier:F2}");
            GUILayout.Label($"velocity   : {body.linearVelocity.magnitude:F2}");
            GUILayout.Label($"groundVel  : {groundVelocity.magnitude:F2}");
            GUILayout.EndArea();
        }
#endif
    }
}