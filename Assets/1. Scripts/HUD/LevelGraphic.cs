using UnityEngine;

/// <summary>카메라 기준으로 판의 기울기를 표시한다.</summary>
public class LevelGraphic : MonoBehaviour
{
    [HideInInspector] public Vector2 lean;
    [HideInInspector] public bool showLowSide = true;

    private RectTransform dot;

    private void Awake()
    {
        var sprites = GameUiSprites.Shared;
        var center = new Vector2(.5f, .5f);
        GameUi.Picture((RectTransform)transform, sprites.TiltDial);
        dot = GameUi.Child("Tilt Dot", transform, center, center, Vector2.zero, new Vector2(28, 28));
        GameUi.Picture(dot, sprites.TiltDot);
    }

    public void Redraw() =>
        dot.anchoredPosition = Vector2.ClampMagnitude(lean, 1f) * (showLowSide ? 40f : -40f);
}
