using UnityEngine;
using UnityEngine.UI;

internal sealed class LocalPlayerPin
{
    private readonly RectTransform _root;
    private readonly Image _image;
    private Player _markedPlayer;

    public LocalPlayerPin(RectTransform root)
    {
        _root = root;
        var sprite = GameUiSprites.Shared.LocalPlayerPin;
        if (sprite == null)
            return;

        var pin = GameUi.Child("Local Player Pin", root, root.pivot, new Vector2(.5f, 0f),
            Vector2.zero, new Vector2(58f, 38f));
        _image = GameUi.Picture(pin, sprite);
        _image.enabled = false;
    }

    public void Update(Camera camera, Player localPlayer)
    {
        if (_image == null)
            return;

        var anchor = localPlayer != null ? localPlayer.NameTagAnchor : null;
        if (anchor == null)
            localPlayer = null;
        if (_markedPlayer != localPlayer)
        {
            if (_markedPlayer != null) _markedPlayer.SetNameTagSuppressed(false);
            _markedPlayer = localPlayer;
            if (_markedPlayer != null) _markedPlayer.SetNameTagSuppressed(true);
        }

        if (anchor != null && camera != null)
        {
            var screen = camera.WorldToScreenPoint(anchor.position);
            if (screen.z > 0f && camera.pixelRect.Contains(screen) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out var point))
            {
                _image.rectTransform.anchoredPosition = point;
                _image.enabled = true;
                return;
            }
        }
        _image.enabled = false;
    }
}
