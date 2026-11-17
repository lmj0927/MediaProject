using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>코스의 현재 위치와 통과한 체크포인트를 표시한다.</summary>
public class ProgressBarGraphic : MonoBehaviour
{
    [HideInInspector] public float progress;
    [HideInInspector] public float reached;
    [HideInInspector] public float[] checkpoints = System.Array.Empty<float>();

    private const float Padding = 25f;
    private static readonly Vector2 Left = new Vector2(0f, .5f);
    private static readonly Vector2 Center = new Vector2(.5f, .5f);
    private readonly List<Image> markers = new();
    private Image fill;
    private RectTransform cursor;

    private void Awake()
    {
        var sprites = GameUiSprites.Shared;
        GameUi.Picture((RectTransform)transform, sprites.HudPill);
        var track = GameUi.Child("Track", transform, Center, Center, Vector2.zero, new Vector2(-2 * Padding, 16));
        track.anchorMin = Left;
        track.anchorMax = new Vector2(1f, .5f);
        GameUi.Picture(track, sprites.ProgressTrack);
        fill = GameUi.Picture(GameUi.Child("Fill", transform, Left, Left, new Vector2(Padding, 0), new Vector2(0, 16)), sprites.HudFill);
        fill.color = GameUi.Yellow;
        cursor = GameUi.Child("Progress Dot", transform, Left, Center, new Vector2(Padding, 0), new Vector2(24, 24));
        GameUi.Picture(cursor, sprites.ProgressDot);
    }

    public void Redraw()
    {
        var sprites = GameUiSprites.Shared;
        float trackWidth = ((RectTransform)transform).rect.width - 2 * Padding;
        float traveled = trackWidth * Mathf.Clamp01(progress);
        fill.enabled = traveled > 1f;
        fill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, traveled);
        cursor.anchoredPosition = new Vector2(Padding + traveled, 0);

        while (markers.Count < checkpoints.Length + 1)
        {
            var marker = GameUi.Child("Checkpoint", transform, Left, Center, Vector2.zero, new Vector2(27, 34));
            marker.SetSiblingIndex(cursor.GetSiblingIndex());
            markers.Add(GameUi.Picture(marker, sprites.Checkpoint));
        }
        float furthest = Mathf.Max(progress, reached) + 1e-4f;
        for (int i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            marker.enabled = i <= checkpoints.Length;
            if (!marker.enabled) continue;
            float fraction = i == 0 ? 0f : checkpoints[i - 1];
            marker.rectTransform.anchoredPosition = new Vector2(Padding + fraction * trackWidth, 0);
            marker.sprite = fraction <= furthest ? sprites.CheckpointPassed : sprites.Checkpoint;
        }
    }
}
