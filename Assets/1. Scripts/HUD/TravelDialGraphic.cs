using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 진행 방향 원판(새 좌하단 HUD). 남색 돔(왼쪽 위가 밝은 볼록한 원) + 흰 테두리 위에 흰 화살표로 굴러가는 방향을 보여줌.
/// 화살표는 원 중심을 지나 양쪽에 걸치고 길이는 고정. 멈춰 있으면 화살표를 숨김.
/// 값은 GameHud가 매 프레임 넣어 줌. 위쪽 = 화면 위쪽(카메라가 보는 방향).
/// </summary>
public class TravelDialGraphic : HudGraphic
{
    /// <summary>굴러가는 방향 × 속도 비율(최고 속도 = 1).</summary>
    [HideInInspector] public Vector2 travel;

    /// <summary>켜면 화살표가 원 중심을 지나 양쪽에 걸침. 끄면 중심에서 바깥쪽으로만 뻗음.</summary>
    [HideInInspector] public bool throughCenter = true;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        Vector2 c = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f * 0.92f;
        float unit = radius / 62f;

        // 바탕(볼록한 돔), 테두리
        HudMesh.ShadedCircle(vh, c, radius, p => HudMesh.Dome(p, HudColors.Navy, HudColors.DomeLight, 0.92f));
        HudMesh.Ring(vh, c, radius - 3f * unit, radius, HudColors.White, 48);

        float speed = Mathf.Clamp01(travel.magnitude);
        if (speed < 0.03f)
        {
            HudMesh.OutlinedCircle(vh, c, 6f * unit, 2f * unit, HudColors.Cream, HudColors.Navy, 20);
            return;
        }

        Vector2 dir = travel / travel.magnitude;
        float length = throughCenter ? radius * 1.4f : radius * 0.7f;
        Vector2 tail = throughCenter ? c - dir * length * 0.5f : c;
        Vector2 tip = tail + dir * length;
        DrawArrow(vh, tail, tip, dir, unit);
    }

    private static void DrawArrow(VertexHelper vh, Vector2 tail, Vector2 tip, Vector2 dir, float unit)
    {
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 neck = tip - dir * 20f * unit;
        const float shaft = 12f, head = 22f, edge = 1.5f;

        // 남색 외곽을 먼저 조금 크게 그리고 그 위에 흰색
        HudMesh.Line(vh, tail - dir * edge * unit, neck, (shaft + edge * 2f) * unit, HudColors.Navy);
        HudMesh.Triangle(vh, tip + dir * edge * 2f * unit, neck + side * (head + edge * 2f) * unit - dir * edge * unit,
            neck - side * (head + edge * 2f) * unit - dir * edge * unit, HudColors.Navy);
        HudMesh.Line(vh, tail, neck + dir * unit, shaft * unit, HudColors.White);
        HudMesh.Triangle(vh, tip, neck + side * head * unit, neck - side * head * unit, HudColors.White);
    }
}
