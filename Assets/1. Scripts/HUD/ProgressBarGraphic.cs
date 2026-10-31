using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 코스 진행도 막대. 남색 바탕 위 노란 진행선, 체크포인트는 마름모(한 번이라도 지난 것은 노랑, 남은 것은 회청색).
/// 현재 위치는 흰 원. 값은 GameHud가 넣어 줌.
/// </summary>
public class ProgressBarGraphic : HudGraphic
{
    /// <summary>진행도(0~1). 뒤로 가면 줄어들지만 마지막으로 지난 체크포인트가 하한임(GameHud에서 정함).</summary>
    [HideInInspector] public float progress;

    /// <summary>지금까지 가장 멀리 간 곳(0~1). 이보다 앞의 체크포인트는 지난 것으로 칠함.</summary>
    [HideInInspector] public float reached;

    /// <summary>체크포인트 위치(0~1). 마지막은 결승점.</summary>
    [HideInInspector] public float[] checkpoints = new float[0];

    protected override void Build(VertexHelper vh, Rect rect)
    {
        float h = rect.height;
        float unit = h / 36f;
        float pad = h * 0.55f;

        HudMesh.RoundRect(vh, new Rect(rect.x, rect.y - 6f * unit, rect.width, h), h * 0.5f, new Color32(0, 0, 0, 70));
        HudMesh.RoundRect(vh, rect, h * 0.5f, HudColors.Navy);

        float x0 = rect.x + pad, x1 = rect.xMax - pad, y = rect.center.y;
        float track = h * 0.34f;
        HudMesh.RoundRect(vh, new Rect(x0, y - track * 0.5f, x1 - x0, track), track * 0.5f, HudColors.Track);
        float fill = Mathf.Lerp(x0, x1, Mathf.Clamp01(progress));
        if (fill - x0 > track) HudMesh.RoundRect(vh, new Rect(x0, y - track * 0.5f, fill - x0, track), track * 0.5f, HudColors.Yellow);

        // 출발점 + 체크포인트들
        DrawCheck(vh, x0, y, unit, true);
        foreach (float f in checkpoints)
            DrawCheck(vh, Mathf.Lerp(x0, x1, f), y, unit, f <= Mathf.Max(progress, reached) + 1e-4f);

        HudMesh.OutlinedCircle(vh, new Vector2(fill, y), 7f * unit, 2.5f * unit, HudColors.White, HudColors.Navy, 20);
    }

    private static void DrawCheck(VertexHelper vh, float x, float y, float unit, bool passed)
    {
        HudMesh.Diamond(vh, new Vector2(x, y), 11f * unit, 14f * unit, HudColors.Navy);
        HudMesh.Diamond(vh, new Vector2(x, y), 8f * unit, 11f * unit, passed ? HudColors.Yellow : HudColors.Pending);
    }
}
