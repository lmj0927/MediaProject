using UnityEngine;
using UnityEngine.UI;

/// <summary>중앙을 비우고 화면 주변에서 바깥으로 흐르는 속도선. 텍스처나 오브젝트 생성 없이 그림.</summary>
public class SpeedStreaksGraphic : MaskableGraphic
{
    private float intensity;
    private float phase;

    public void SetSpeed(float value, float dt)
    {
        if (value <= 0.001f && intensity <= 0.001f) return;
        intensity = value;
        phase = Mathf.Repeat(phase + dt * Mathf.Lerp(0.6f, 1.9f, intensity), 1f);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (intensity <= 0.001f) return;
        Rect rect = rectTransform.rect;
        Vector2 center = new Vector2(rect.center.x, rect.yMin + rect.height * 0.56f);
        float scale = rect.height / 1080f;
        for (int i = 0; i < 44; i++)
        {
            float seed = Mathf.Repeat(i * 0.618034f, 1f);
            float angle = (i + seed * 0.6f) / 44f * Mathf.PI * 2f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            if (Mathf.Abs(direction.x) < 0.46f) continue; // 상단 코스 HUD와 아래 중앙을 비움
            float xEdge = (direction.x > 0f ? rect.xMax - center.x : center.x - rect.xMin) / Mathf.Abs(direction.x);
            float yEdge = (direction.y > 0f ? rect.yMax - center.y : center.y - rect.yMin) / Mathf.Max(0.001f, Mathf.Abs(direction.y));
            float edge = Mathf.Min(xEdge, yEdge);
            float travel = Mathf.Repeat(phase + seed, 1f);
            float distance = edge * Mathf.Lerp(0.72f, 1.2f, travel);
            float length = Mathf.Lerp(65f, 310f, intensity) * Mathf.Lerp(0.65f, 1.2f, seed) * scale;
            float start = Mathf.Max(edge * 0.64f, distance - length);
            Vector2 inner = center + direction * start;
            Vector2 outer = center + direction * distance;
            Vector2 width = new Vector2(-direction.y, direction.x) * Mathf.Lerp(2f, 5f, seed) * scale;
            float alpha = intensity * 0.65f * Mathf.Sin(travel * Mathf.PI);
            Color tint = Color.Lerp(new Color(0.64f, 0.91f, 1f), Color.white, seed);
            Diamond(vh, inner, outer, width * 3f, tint, alpha * 0.12f);
            Diamond(vh, inner, outer, width, tint, alpha);
        }
    }

    private static void Diamond(VertexHelper vh, Vector2 inner, Vector2 outer, Vector2 width, Color tint, float alpha)
    {
        int first = vh.currentVertCount;
        var clear = new Color(tint.r, tint.g, tint.b, 0f);
        var bright = new Color(tint.r, tint.g, tint.b, alpha);
        vh.AddVert(inner, clear, Vector2.zero);
        vh.AddVert(Vector2.Lerp(inner, outer, 0.55f) - width, bright, Vector2.zero);
        vh.AddVert(outer, clear, Vector2.zero);
        vh.AddVert(Vector2.Lerp(inner, outer, 0.55f) + width, bright, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}
