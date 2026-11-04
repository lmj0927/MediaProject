using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 수평계(새 좌하단 HUD, 켜고 끌 수 있음). 남색 원 안의 주황 구슬이 판이 기운 쪽(낮은 쪽)으로 굴러감.
/// 가운데 작은 원 안에 있으면 거의 수평. 바깥 테두리에 닿으면 최대로 기운 상태.
/// 값은 GameHud가 넣어 줌. 위쪽 = 화면 위쪽(카메라가 보는 방향).
/// </summary>
public class LevelGraphic : HudGraphic
{
    /// <summary>기운 방향 × 세기(0~1).</summary>
    [HideInInspector] public Vector2 lean;

    /// <summary>켜면 구슬이 낮은 쪽, 끄면 물방울처럼 높은 쪽으로 감.</summary>
    [HideInInspector] public bool showLowSide = true;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        Vector2 c = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f * 0.92f;
        float unit = radius / 40f;

        HudMesh.ShadedCircle(vh, c, radius, p => HudMesh.Dome(p, HudColors.Navy, HudColors.DomeLight, 0.92f), 32, 6);
        HudMesh.Ring(vh, c, radius - 3f * unit, radius, HudColors.White, 40);
        HudMesh.Ring(vh, c, 13f * unit, 15f * unit, HudColors.Pending, 28);

        Vector2 offset = Vector2.ClampMagnitude(lean, 1f) * (radius - 13f * unit);
        if (!showLowSide) offset = -offset;
        Vector2 ball = c + offset;
        HudMesh.OutlinedCircle(vh, ball, 8f * unit, 2f * unit, HudColors.Orange, HudColors.Navy, 20);
        HudMesh.Circle(vh, ball + new Vector2(-2.6f, 2.6f) * unit, 2.4f * unit, new Color32(255, 220, 170, 220), 12);
    }
}
