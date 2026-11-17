using UnityEngine;
using UnityEngine.UI;

/// <summary>카메라 기준 진행 방향을 화살표로 표시한다.</summary>
public class TravelDialGraphic : MonoBehaviour
{
    [HideInInspector] public Vector2 travel;
    [HideInInspector] public bool throughCenter = true;

    private RectTransform arrow;
    private Image dot;

    private void Awake()
    {
        var sprites = GameUiSprites.Shared;
        var center = new Vector2(.5f, .5f);
        GameUi.Picture((RectTransform)transform, sprites.TravelDial);
        dot = GameUi.Picture(GameUi.Child("Center Dot", transform, center, center, Vector2.zero, new Vector2(24, 24)), sprites.TravelDot);
        arrow = GameUi.Child("Arrow", transform, center, new Vector2(0, .5f), Vector2.zero, new Vector2(134, 62));
        GameUi.Picture(arrow, sprites.TravelArrow);
    }

    public void Redraw()
    {
        bool moving = travel.sqrMagnitude >= .03f * .03f;
        arrow.gameObject.SetActive(moving);
        dot.enabled = !moving;
        if (!moving) return;

        Vector2 direction = travel.normalized;
        arrow.sizeDelta = new Vector2(throughCenter ? 134 : 68, 62);
        arrow.anchoredPosition = throughCenter ? -direction * 67f : Vector2.zero;
        arrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }
}
