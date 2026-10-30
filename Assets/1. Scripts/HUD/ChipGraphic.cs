using UnityEngine;
using UnityEngine.UI;

/// <summary>팀원 상태 한 줄. 남색 둥근 바탕 + 캐릭터 색 원. 판 밖으로 떨어진 동안은 회색으로 어두워짐.</summary>
public class ChipGraphic : HudGraphic
{
    [HideInInspector] public Color32 characterColor = HudColors.White;
    [HideInInspector] public bool dim;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        float h = rect.height;
        float unit = h / 32f;
        HudMesh.RoundRect(vh, rect, h * 0.5f, dim ? HudColors.Dim : HudColors.Navy);
        Vector2 dot = new Vector2(rect.x + h * 0.5f, rect.center.y);
        HudMesh.OutlinedCircle(vh, dot, 10f * unit, 2f * unit, dim ? new Color32(110, 110, 120, 255) : characterColor, HudColors.White, 20);
    }
}
