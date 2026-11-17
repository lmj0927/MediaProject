using UnityEngine;
using UnityEngine.UI;

/// <summary>팀원 상태 한 줄. 남색 둥근 바탕 + 캐릭터 색 원. 판 밖으로 떨어진 동안은 회색으로 어두워짐.</summary>
public class ChipGraphic : MonoBehaviour
{
    [HideInInspector] public Color32 characterColor = HudColors.White;
    [HideInInspector] public bool dim;

    private Image panel;
    private Image dot;

    private void Awake()
    {
        panel = GameUi.Picture((RectTransform)transform, GameUiSprites.Shared.HudPill);
        var centerLeft = new Vector2(0f, .5f);
        var center = new Vector2(.5f, .5f);
        var dotRect = GameUi.Child("Character color", transform, centerLeft, center, new Vector2(24, 0), new Vector2(36, 36));
        GameUi.Picture(dotRect, GameUiSprites.Shared.TeamRing);
        dot = GameUi.Picture(GameUi.Child("Dot", dotRect, center, center, Vector2.zero, new Vector2(29, 29)),
            GameUiSprites.Shared.TeamDot);
    }

    public void Redraw()
    {
        panel.sprite = dim ? GameUiSprites.Shared.HudPillDim : GameUiSprites.Shared.HudPill;
        dot.color = dim ? new Color32(110, 110, 120, 255) : characterColor;
    }
}
