using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 속도 막대(새 좌하단 HUD). 남색 둥근 바탕 위 막대가 속도 비율만큼 차오르고, 빠를수록 노랑 → 주황 → 빨강으로 바뀜.
/// 값은 GameHud가 넣어 줌.
/// </summary>
public class SpeedBarGraphic : HudGraphic
{
    /// <summary>속도 비율(0~1, 최고 속도 = 1).</summary>
    [HideInInspector] public float ratio;

    protected override void Build(VertexHelper vh, Rect rect)
    {
        float h = rect.height;
        HudMesh.RoundRect(vh, rect, h * 0.5f, HudMesh.WithAlpha(HudColors.Navy, 0.9f));
        float pad = h * 0.22f;
        var inner = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, h - pad * 2f);
        float fill = inner.width * Mathf.Clamp01(ratio);
        if (fill > inner.height * 0.5f)
        {
            HudMesh.RoundRect(vh, new Rect(inner.x, inner.y, fill, inner.height), inner.height * 0.5f, ColorFor(ratio));
            float line = inner.height * 0.18f;
            HudMesh.RoundRect(vh, new Rect(inner.x + inner.height * 0.3f, inner.yMax - line * 2.2f, Mathf.Max(0f, fill - inner.height * 0.6f), line), line * 0.5f, new Color32(255, 255, 255, 90));
        }
    }

    /// <summary>속도 비율에 따른 막대 색. 노랑 → 주황(60%~) → 빨강(최고 속도 근처).</summary>
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
