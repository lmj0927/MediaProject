using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace WheelSystem
{
    /// <summary>
    /// 휠을 따라다니는 테스트용 카메라. Q/E로 좌우 회전.
    /// 카메라가 회전하면 플레이어 이동 방향도 같이 돌아간다.
    /// </summary>
    public class TestFollowCamera : MonoBehaviour
    {
        [Tooltip("따라갈 대상. 보통 구체.")]
        [SerializeField] private Transform target;

        [SerializeField] private float distance = 14f;
        [SerializeField] private float height = 9f;
        [SerializeField] private float followSmoothTime = 0.25f;
        [SerializeField] private float rotateSpeed = 90f;

        [Tooltip("대상보다 이 높이를 바라본다.")]
        [SerializeField] private float lookHeight = 1.5f;

        private float yaw;
        private Vector3 velocity;

        private void Start()
        {
            if (target != null)
            {
                transform.position = DesiredPosition();
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;

            yaw += ReadRotateInput() * rotateSpeed * Time.deltaTime;

            transform.position = Vector3.SmoothDamp(
                transform.position, DesiredPosition(), ref velocity, followSmoothTime);

            transform.LookAt(target.position + Vector3.up * lookHeight);
        }

        private Vector3 DesiredPosition()
        {
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, -distance);
            return target.position + offset;
        }

        private static float ReadRotateInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return 0f;
            float v = 0f;
            if (kb.eKey.isPressed) v += 1f;
            if (kb.qKey.isPressed) v -= 1f;
            return v;
#else
            float v = 0f;
            if (Input.GetKey(KeyCode.E)) v += 1f;
            if (Input.GetKey(KeyCode.Q)) v -= 1f;
            return v;
#endif
        }
    }
}
