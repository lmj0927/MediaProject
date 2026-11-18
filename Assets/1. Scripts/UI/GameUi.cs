using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class GameUi
{
    public static readonly Color32 Ink = new Color32(19, 38, 65, 255);
    public static readonly Color32 Yellow = new Color32(255, 209, 91, 255);
    public static readonly Color32 Coral = new Color32(191, 69, 53, 255);

    public static RectTransform Child(string name, Transform parent, Vector2 anchor, Vector2 pivot,
        Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var child = (RectTransform)go.transform;
        child.SetParent(parent, false);
        Place(child, anchor, pivot, position, size);
        return child;
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static void Fill(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min;
        rect.offsetMax = max;
    }

    public static Image Picture(RectTransform rect, Sprite sprite, bool raycast = false, bool preserveAspect = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = preserveAspect;
        image.raycastTarget = raycast;
        return image;
    }

    public static TextMeshProUGUI Label(RectTransform rect, string value, int size, TextAlignmentOptions alignment)
    {
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = GameUiSprites.Shared.Typography;
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.color = Ink;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        text.text = value;
        return text;
    }

    public static Button Button(RectTransform rect, Sprite sprite)
    {
        var graphic = Picture(rect, sprite, true);
        var button = rect.gameObject.AddComponent<Button>();
        StyleSelectable(button, graphic);
        SFXManager.BindButton(button);
        return button;
    }

    public static void StyleSelectable(Selectable selectable, Graphic graphic)
    {
        selectable.targetGraphic = graphic;
        selectable.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.highlightedColor = new Color(0.96f, 0.98f, 1f, 1f);
        colors.selectedColor = new Color(0.97f, 0.985f, 1f, 1f);
        colors.pressedColor = new Color(0.82f, 0.88f, 0.96f, 1f);
        colors.disabledColor = new Color(0.64f, 0.71f, 0.82f, 0.73f);
        colors.fadeDuration = 0.09f;
        selectable.colors = colors;
    }
}
