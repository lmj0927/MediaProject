using UnityEngine;
using UnityEngine.UI;

/// <summary>속도를 막대 길이와 색으로 표시한다.</summary>
public class SpeedBarGraphic : MonoBehaviour
{
    [HideInInspector] public float ratio;

    private Image fill;

    private void Awake()
    {
        var sprites = GameUiSprites.Shared;
        GameUi.Picture((RectTransform)transform, sprites.HudTrack);
        var left = new Vector2(0f, .5f);
        var bar = GameUi.Child("Fill", transform, left, left, new Vector2(4, 0), new Vector2(0, 14));
        fill = GameUi.Picture(bar, sprites.HudFill);
    }

    public void Redraw()
    {
        float width = (((RectTransform)transform).rect.width - 8f) * Mathf.Clamp01(ratio);
        fill.enabled = width > 1f;
        fill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        fill.color = ColorFor(ratio);
    }

    public static Color32 ColorFor(float t)
    {
        t = Mathf.Clamp01(t);
        var yellow = new Color32(255, 214, 60, 255);
        var gold = new Color32(255, 196, 40, 255);
        var orange = new Color32(250, 140, 30, 255);
        var red = new Color32(235, 70, 40, 255);
        if (t < 0.6f) return HudMesh.Lerp(yellow, gold, t / 0.6f);
        if (t < 0.85f) return HudMesh.Lerp(gold, orange, (t - 0.6f) / 0.25f);
        return HudMesh.Lerp(orange, red, (t - 0.85f) / 0.15f);
    }
}
