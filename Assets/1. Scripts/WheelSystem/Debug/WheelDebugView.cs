using UnityEngine;

namespace WheelSystem
{
    /// <summary>
    /// 무게중심, 기울기, 이동 방향을 씬 뷰에 시각화.
    /// 판에 연결함.
    /// </summary>
    public class WheelDebugView : MonoBehaviour
    {
        [SerializeField] private CenterOfMassSolver solver;
        [SerializeField] private PlatformTilt platform;
        [SerializeField] private SphereDriver sphere;
        [SerializeField] private WheelConfig config;

        [Header("Display")]
        [SerializeField] private bool drawOffsetRadius = true;
        [SerializeField] private bool drawOccupants = true;
        [SerializeField] private float markerSize = 0.25f;

        private void Reset()
        {
            solver = GetComponent<CenterOfMassSolver>();
            platform = GetComponent<PlatformTilt>();
        }

        private void OnDrawGizmos()
        {
            if (solver == null) return;

            Vector3 pivot = transform.position;

            // 허용 반경
            if (drawOffsetRadius && config != null)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                DrawCircle(pivot, config.maxOffsetRadius, 48);
            }

            if (!Application.isPlaying) return;

            // 무게중심과 오프셋
            Vector3 com = solver.WorldCenterOfMass;
            Gizmos.color = solver.HasOccupants ? Color.yellow : Color.gray;
            Gizmos.DrawLine(pivot, com);
            Gizmos.DrawSphere(com, markerSize);

            // 개별 점유자
            if (drawOccupants)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
                foreach (var occupant in solver.Occupants)
                {
                    if (occupant == null || !occupant.Contributes) continue;
                    Gizmos.DrawWireCube(occupant.WorldPosition, Vector3.one * markerSize);
                }
            }

            if (platform == null) return;

            // 판 법선
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(pivot, platform.SurfaceNormal * 2f);

            // 이동 방향(길이가 곧 기울기 세기)
            Vector3 downhill = platform.DownhillDirection;
            if (downhill != Vector3.zero)
            {
                Gizmos.color = Color.green;
                Vector3 end = pivot + downhill * (2f + platform.NormalizedTilt * 3f);
                Gizmos.DrawLine(pivot, end);
                Gizmos.DrawSphere(end, markerSize * 0.6f);
            }
        }

        private static void DrawCircle(Vector3 center, float radius, int segments)
        {
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!Application.isPlaying || solver == null || platform == null) return;

            GUI.Box(new Rect(10, 10, 220, 92), "");
            GUILayout.BeginArea(new Rect(18, 16, 210, 84));
            GUILayout.Label($"Occupants weight : {solver.TotalWeight:F1}");
            GUILayout.Label($"Offset           : {solver.WorldOffset.magnitude:F2}");
            GUILayout.Label($"Tilt             : {platform.TiltAngle:F1}°");
            if (sphere != null)
                GUILayout.Label($"Speed / Grounded : {sphere.HorizontalSpeed:F1} / {sphere.IsGrounded}");
            GUILayout.EndArea();
        }
#endif
    }
}