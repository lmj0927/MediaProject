using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD 도형을 코드로 그리는 도구. 텍스처 없이 메시로 그려서 해상도와 상관없이 선명함.
/// 각도는 도 단위, 0이 오른쪽(+x)이고 반시계 방향이 +. 좌표는 RectTransform 로컬(픽셀) 기준.
/// </summary>
public static class HudMesh
{
    public static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, color, Vector4.zero);
        vh.AddVert(b, color, Vector4.zero);
        vh.AddVert(c, color, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
    }

    public static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color32 ca, Color32 cb, Color32 cc, Color32 cd)
    {
        int i = vh.currentVertCount;
        vh.AddVert(a, ca, Vector4.zero);
        vh.AddVert(b, cb, Vector4.zero);
        vh.AddVert(c, cc, Vector4.zero);
        vh.AddVert(d, cd, Vector4.zero);
        vh.AddTriangle(i, i + 1, i + 2);
        vh.AddTriangle(i, i + 2, i + 3);
    }

    /// <summary>볼록 다각형 채우기. 꼭짓점마다 색을 정할 수 있음(선형 그라데이션은 정확히 표현됨).</summary>
    public static void Polygon(VertexHelper vh, Vector2[] points, Func<Vector2, Color32> color)
    {
        Vector2 center = Vector2.zero;
        foreach (var p in points) center += p;
        center /= points.Length;

        int c = vh.currentVertCount;
        vh.AddVert(center, color(center), Vector4.zero);
        foreach (var p in points) vh.AddVert(p, color(p), Vector4.zero);
        for (int i = 0; i < points.Length; i++)
            vh.AddTriangle(c, c + 1 + i, c + 1 + (i + 1) % points.Length);
    }

    public static void Polygon(VertexHelper vh, Vector2[] points, Color32 color) => Polygon(vh, points, _ => color);

    public static Vector2[] Regular(Vector2 center, float radius, int sides, float startDegrees)
    {
        var points = new Vector2[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (startDegrees + 360f * i / sides) * Mathf.Deg2Rad;
            points[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return points;
    }

    public static Vector2[] Offset(Vector2[] points, Vector2 offset)
    {
        var moved = new Vector2[points.Length];
        for (int i = 0; i < points.Length; i++) moved[i] = points[i] + offset;
        return moved;
    }

    public static void Circle(VertexHelper vh, Vector2 center, float radius, Color32 color, int segments = 28)
        => Polygon(vh, Regular(center, radius, segments, 0f), color);

    /// <summary>테두리가 있는 원. 바깥 색 원 위에 안쪽 색 원을 겹쳐 그림.</summary>
    public static void OutlinedCircle(VertexHelper vh, Vector2 center, float radius, float outline, Color32 fill, Color32 line, int segments = 28)
    {
        Circle(vh, center, radius + outline, line, segments);
        Circle(vh, center, radius, fill, segments);
    }

    public static void Arc(VertexHelper vh, Vector2 center, float inner, float outer, float fromDegrees, float toDegrees, Color32 color, int segments)
    {
        for (int k = 0; k < segments; k++)
        {
            float a0 = Mathf.Lerp(fromDegrees, toDegrees, k / (float)segments) * Mathf.Deg2Rad;
            float a1 = Mathf.Lerp(fromDegrees, toDegrees, (k + 1) / (float)segments) * Mathf.Deg2Rad;
            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            Quad(vh, center + d0 * inner, center + d0 * outer, center + d1 * outer, center + d1 * inner, color, color, color, color);
        }
    }

    public static void Ring(VertexHelper vh, Vector2 center, float inner, float outer, Color32 color, int segments = 40)
        => Arc(vh, center, inner, outer, 0f, 360f, color, segments);

    /// <summary>
    /// 반지름·각도에 따라 색이 바뀌는 부채꼴. 부드러운 그라데이션용으로 잘게 나눔.
    /// color(radiusT 0~1, angleT -1~1)로 꼭짓점 색을 정함.
    /// </summary>
    public static void ShadedWedge(VertexHelper vh, Vector2 center, float inner, float outer, float centerDegrees, float halfDegrees,
        int angleSegments, int radiusSegments, Func<float, float, Color32> color)
    {
        for (int a = 0; a < angleSegments; a++)
        {
            float t0 = -1f + 2f * a / angleSegments;
            float t1 = -1f + 2f * (a + 1) / angleSegments;
            float g0 = (centerDegrees + t0 * halfDegrees) * Mathf.Deg2Rad;
            float g1 = (centerDegrees + t1 * halfDegrees) * Mathf.Deg2Rad;
            var d0 = new Vector2(Mathf.Cos(g0), Mathf.Sin(g0));
            var d1 = new Vector2(Mathf.Cos(g1), Mathf.Sin(g1));
            for (int r = 0; r < radiusSegments; r++)
            {
                float r0 = r / (float)radiusSegments;
                float r1 = (r + 1) / (float)radiusSegments;
                float rad0 = Mathf.Lerp(inner, outer, r0);
                float rad1 = Mathf.Lerp(inner, outer, r1);
                Quad(vh, center + d0 * rad0, center + d0 * rad1, center + d1 * rad1, center + d1 * rad0,
                    color(r0, t0), color(r1, t0), color(r1, t1), color(r0, t1));
            }
        }
    }

    public static void Line(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        Vector2 dir = to - from;
        if (dir.sqrMagnitude < 1e-6f) return;
        Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
        Quad(vh, from - n, from + n, to + n, to - n, color, color, color, color);
    }

    /// <summary>둥근 모서리 사각형.</summary>
    public static void RoundRect(VertexHelper vh, Rect rect, float radius, Color32 color, int cornerSegments = 6)
    {
        radius = Mathf.Min(radius, rect.width * 0.5f, rect.height * 0.5f);
        float x0 = rect.xMin, x1 = rect.xMax, y0 = rect.yMin, y1 = rect.yMax;
        Quad(vh, new Vector2(x0 + radius, y0), new Vector2(x0 + radius, y1), new Vector2(x1 - radius, y1), new Vector2(x1 - radius, y0), color, color, color, color);
        Quad(vh, new Vector2(x0, y0 + radius), new Vector2(x0, y1 - radius), new Vector2(x0 + radius, y1 - radius), new Vector2(x0 + radius, y0 + radius), color, color, color, color);
        Quad(vh, new Vector2(x1 - radius, y0 + radius), new Vector2(x1 - radius, y1 - radius), new Vector2(x1, y1 - radius), new Vector2(x1, y0 + radius), color, color, color, color);
        Arc(vh, new Vector2(x1 - radius, y1 - radius), 0f, radius, 0f, 90f, color, cornerSegments);
        Arc(vh, new Vector2(x0 + radius, y1 - radius), 0f, radius, 90f, 180f, color, cornerSegments);
        Arc(vh, new Vector2(x0 + radius, y0 + radius), 0f, radius, 180f, 270f, color, cornerSegments);
        Arc(vh, new Vector2(x1 - radius, y0 + radius), 0f, radius, 270f, 360f, color, cornerSegments);
    }

    public static void Diamond(VertexHelper vh, Vector2 center, float halfWidth, float halfHeight, Color32 color)
        => Polygon(vh, new[]
        {
            center + new Vector2(0f, halfHeight), center + new Vector2(halfWidth, 0f),
            center - new Vector2(0f, halfHeight), center - new Vector2(halfWidth, 0f)
        }, color);

    public static Color32 WithAlpha(Color32 color, float alpha)
    {
        color.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
        return color;
    }

    public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, Mathf.Clamp01(t));
}

/// <summary>코드로 도형을 그리는 UI 그래픽의 공통 부분. Build에서 메시를 채움.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public abstract class HudGraphic : MaskableGraphic
{
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Build(vh, rectTransform.rect);
    }

    protected abstract void Build(VertexHelper vh, Rect rect);

    /// <summary>값이 바뀌었으니 다시 그림.</summary>
    public void Redraw() => SetVerticesDirty();
}

/// <summary>HUD 공통 색. 판(테두리 주황·노랑, 초록 바닥)과 같은 색을 써서 한눈에 같은 물건으로 보이게 함.</summary>
public static class HudColors
{
    public static readonly Color32 Navy = new Color32(28, 32, 58, 255);
    public static readonly Color32 Orange = new Color32(240, 130, 30, 255);
    public static readonly Color32 Yellow = new Color32(255, 196, 40, 255);
    public static readonly Color32 Deck = new Color32(70, 140, 60, 255);
    public static readonly Color32 DeckLine = new Color32(40, 95, 40, 255);
    public static readonly Color32 Cream = new Color32(250, 245, 230, 255);
    public static readonly Color32 White = new Color32(255, 255, 255, 255);
    public static readonly Color32 Bolt = new Color32(70, 70, 80, 255);
    public static readonly Color32 Lean = new Color32(255, 140, 40, 255);
    public static readonly Color32 LeanTick = new Color32(255, 170, 90, 200);
    public static readonly Color32 Track = new Color32(70, 75, 110, 255);
    public static readonly Color32 Pending = new Color32(120, 125, 160, 255);
    public static readonly Color32 Danger = new Color32(220, 50, 40, 255);
    public static readonly Color32 Monster = new Color32(140, 60, 170, 255);
    public static readonly Color32 Bomb = new Color32(45, 45, 52, 255);
    public static readonly Color32 Dim = new Color32(70, 70, 80, 220);

    /// <summary>캐릭터 이름(프리팹 이름)으로 색을 고름. 모르는 이름은 흰색.</summary>
    public static Color32 ForCharacter(string objectName)
    {
        if (objectName.Contains("Blue")) return new Color32(50, 90, 220, 255);
        if (objectName.Contains("Green")) return new Color32(90, 170, 60, 255);
        if (objectName.Contains("Pink")) return new Color32(250, 150, 200, 255);
        if (objectName.Contains("Red")) return new Color32(225, 60, 50, 255);
        if (objectName.Contains("Yellow")) return new Color32(250, 210, 60, 255);
        return White;
    }

    /// <summary>캐릭터 이름(프리팹 이름)에서 색 이름만 뽑음.</summary>
    public static string NameForCharacter(string objectName)
    {
        foreach (var n in new[] { "Blue", "Green", "Pink", "Red", "Yellow" })
            if (objectName.Contains(n)) return n;
        return objectName;
    }
}
