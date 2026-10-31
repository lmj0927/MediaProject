using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 밖 위험 표시. 화면 가장자리에서 대상 쪽을 가리키는 빨간 삼각형 + 대상 아이콘(폭탄/몬스터).
/// angle은 화면 기준(0 = 오른쪽, 반시계 방향이 +).
/// </summary>
public class EdgeArrowGraphic : HudGraphic
{
    [HideInInspector] public float angle;
    [HideInInspector] public BalanceDiscGraphic.MarkerKind kind = BalanceDiscGraphic.MarkerKind.Bomb;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        Vector2 c = rect.center;
        float unit = Mathf.Min(rect.width, rect.height) / 80f;
        float a = angle * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        Vector2 side = new Vector2(-dir.y, dir.x);

        // 대상 쪽 삼각형
        Vector2 baseCenter = c + dir * 20f * unit;
        Vector2 tip = c + dir * 38f * unit;
        HudMesh.Triangle(vh, tip + dir * 3f * unit, baseCenter + side * 17f * unit - dir * 2f * unit, baseCenter - side * 17f * unit - dir * 2f * unit, HudColors.Navy);
        HudMesh.Triangle(vh, tip, baseCenter + side * 13f * unit, baseCenter - side * 13f * unit, HudColors.Danger);

        // 아이콘
        if (kind == BalanceDiscGraphic.MarkerKind.Monster)
        {
            HudMesh.RoundRect(vh, new Rect(c.x - 15f * unit, c.y - 15f * unit, 30f * unit, 30f * unit), 7f * unit, HudColors.Navy);
            HudMesh.RoundRect(vh, new Rect(c.x - 11f * unit, c.y - 11f * unit, 22f * unit, 22f * unit), 5f * unit, HudColors.Monster);
        }
        else
        {
            HudMesh.OutlinedCircle(vh, c, 12f * unit, 3.5f * unit, HudColors.Bomb, HudColors.Navy, 24);
            HudMesh.Circle(vh, c + new Vector2(6f, 7f) * unit, 4f * unit, HudColors.Danger, 12);
        }
        HudMesh.Line(vh, c + new Vector2(0f, 6f) * unit, c + new Vector2(0f, -1.5f) * unit, 3.5f * unit, HudColors.Yellow);
        HudMesh.Circle(vh, c + new Vector2(0f, -5.5f) * unit, 2f * unit, HudColors.Yellow, 10);
    }
}
