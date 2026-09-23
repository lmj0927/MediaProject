using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 무게중심, 기울기, 이동 방향을 씬 뷰에 시각화
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

    [Header("HUD")]
    [Tooltip("HUD 글자 크기. 해상도가 높으면 키울 것.")]
    [Range(10, 32)][SerializeField] private int hudFontSize = 14;

    [Tooltip("HUD를 붙일 화면 모서리.")]
    [SerializeField] private HudAnchor hudAnchor = HudAnchor.TopRight;

    [Tooltip("모서리로부터의 여백(픽셀).")]
    [SerializeField] private Vector2 hudPosition = new Vector2(10f, 10f);

    public enum HudAnchor { TopLeft, TopRight }

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
        if (drawOffsetRadius)
        {
            // 자동 측정이 켜져 있으면 실제로 쓰이는 반경을 시각화
            float radius = platform != null ? platform.EffectiveOffsetRadius
                            : config != null ? config.maxOffsetRadius : 0f;
            if (radius > 0f)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
                DrawCircle(pivot, radius, 48);
            }
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
    private GUIStyle hudStyle;
    private readonly List<string> hudLines = new();

    private void OnGUI()
    {
        if (!Application.isPlaying || solver == null || platform == null) return;

        BuildHudStyle();
        BuildHudLines();

        float width = 0f;
        float lineHeight = hudStyle.lineHeight + 2f;
        var content = new GUIContent();

        for (int i = 0; i < hudLines.Count; i++)
        {
            content.text = hudLines[i];
            width = Mathf.Max(width, hudStyle.CalcSize(content).x);
        }

        float pad = 10f;
        float boxWidth = width + pad * 2f;
        float boxHeight = lineHeight * hudLines.Count + pad * 2f;

        float x = hudAnchor == HudAnchor.TopRight
            ? Screen.width - boxWidth - hudPosition.x
            : hudPosition.x;

        var box = new Rect(x, hudPosition.y, boxWidth, boxHeight);

        GUI.Box(box, GUIContent.none);

        var line = new Rect(box.x + pad, box.y + pad, width, lineHeight);
        for (int i = 0; i < hudLines.Count; i++)
        {
            GUI.Label(line, hudLines[i], hudStyle);
            line.y += lineHeight;
        }
    }

    private void BuildHudStyle()
    {
        if (hudStyle != null && hudStyle.fontSize == hudFontSize) return;

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = hudFontSize,
            richText = false,
            wordWrap = false,
            alignment = TextAnchor.MiddleLeft
        };
        hudStyle.normal.textColor = Color.white;
    }

    private void BuildHudLines()
    {
        hudLines.Clear();
        hudLines.Add($"Occupants   {solver.Occupants.Count}  ({solver.TotalWeight:F1})");
        hudLines.Add($"Offset      {solver.WorldOffset.magnitude:F2} / {platform.EffectiveOffsetRadius:F2}");
        hudLines.Add($"Tilt        {platform.TiltAngle:F1} deg");

        if (sphere != null)
            hudLines.Add($"Speed       {sphere.HorizontalSpeed:F1}   Grounded {sphere.IsGrounded}");
    }
#endif
}
