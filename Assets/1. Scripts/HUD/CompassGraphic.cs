using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 다음 목표 나침반. 둥근 판 위 주황 화살표가 다음 체크포인트 방향을 가리킴.
/// angle은 화면 기준(0 = 화면 위쪽, 시계 방향이 +).
/// </summary>
public class CompassGraphic : HudGraphic
{
    [HideInInspector] public float angle;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        Vector2 c = rect.center;
        float r = Mathf.Min(rect.width, rect.height) * 0.5f * 0.92f;
        float unit = r / 30f;

        HudMesh.Circle(vh, c + new Vector2(0f, -4f) * unit, r + 2f * unit, new Color32(0, 0, 0, 70), 36);
        HudMesh.Circle(vh, c, r, HudColors.Navy, 36);
        HudMesh.Circle(vh, c, r - 4f * unit, HudColors.Cream, 36);

        float a = (90f - angle) * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 tip = c + dir * 20f * unit;
        Vector2 back = c - dir * 8f * unit;
        Vector2 left = c - dir * 12f * unit + side * 11f * unit;
        Vector2 right = c - dir * 12f * unit - side * 11f * unit;

        // 남색 테두리를 먼저 크게 그리고 그 위에 주황
        float o = 2.2f * unit;
        HudMesh.Polygon(vh, new[] { tip + dir * o, left + (side - dir) * o, back - dir * o * 0.5f, right + (-side - dir) * o }, HudColors.Navy);
        HudMesh.Triangle(vh, tip, left, back, HudColors.Orange);
        HudMesh.Triangle(vh, tip, back, right, new Color32(255, 160, 70, 255));
    }
}
