using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 균형 원판. 판을 위에서 본 축소판(같은 팔각형, 같은 테두리 색).
/// 원판 위쪽 = 화면 위쪽(카메라가 보는 방향).
///   명암: 낮은 쪽이 어둡고 높은 쪽이 밝음.
///   주황 부채꼴: 기운 방향. 세기만큼 바깥으로 늘어나고, 옅은 눈금 3줄로 단계를 읽음.
///   흰 화살표(속이 빈 외곽선): 굴러가는 방향과 속도. 표식 위에 그림.
///   점: 판 위의 플레이어(캐릭터 색, 내 캐릭터는 흰 고리), 몬스터(보라 네모), 폭탄(검은 원).
/// 값은 GameHud가 매 프레임 넣어 줌. 좌표는 원판 반지름 기준 -1~1.
/// </summary>
public class BalanceDiscGraphic : HudGraphic
{
    public enum MarkerKind { Player, Monster, Bomb }

    public struct Marker
    {
        public Vector2 position;
        public MarkerKind kind;
        public Color32 color;
        public bool local;
    }

    /// <summary>기운 방향 × 세기(0~1).</summary>
    [HideInInspector] public Vector2 lean;

    /// <summary>굴러가는 방향 × 속도 비율(최고 속도 = 1).</summary>
    [HideInInspector] public Vector2 travel;

    public readonly List<Marker> markers = new();

    protected override void Build(VertexHelper vh, Rect rect)
    {
        Vector2 c = rect.center;
        float outer = Mathf.Min(rect.width, rect.height) * 0.5f * 0.94f;
        float unit = outer / 120f;          // 선 두께 등은 원판 크기에 비례
        float deck = outer * 0.82f;         // 초록 바닥 반지름
        const float rot = 22.5f;            // 팔각형 변이 위아래를 향함

        // 그림자: 크기를 조금씩 키운 반투명 판을 겹침
        for (int i = 3; i >= 1; i--)
            HudMesh.Polygon(vh, HudMesh.Regular(c + new Vector2(2f, -8f) * unit, outer + i * 3f * unit, 8, rot), new Color32(0, 0, 0, 40));

        // 테두리: 남색 → 주황 → 노랑 → 초록
        HudMesh.Polygon(vh, HudMesh.Regular(c, outer, 8, rot), HudColors.Navy);
        HudMesh.Polygon(vh, HudMesh.Regular(c, outer * 0.967f, 8, rot), HudColors.Orange);
        HudMesh.Polygon(vh, HudMesh.Regular(c, outer * 0.90f, 8, rot), HudColors.Yellow);

        // 바닥 명암: 기운 쪽 어둡게, 반대쪽 밝게
        float amount = Mathf.Clamp01(lean.magnitude);
        Vector2 leanDir = amount > 1e-4f ? lean / lean.magnitude : Vector2.zero;
        Color32 dark = new Color32(18, 50, 22, 255);
        Color32 light = new Color32(150, 205, 120, 255);
        HudMesh.Polygon(vh, HudMesh.Regular(c, deck, 8, rot), p =>
        {
            float s = Mathf.Clamp(Vector2.Dot(p - c, leanDir) / deck, -1f, 1f);
            return s > 0f
                ? HudMesh.Lerp(HudColors.Deck, dark, s * 0.6f * amount)
                : HudMesh.Lerp(HudColors.Deck, light, -s * 0.35f * amount);
        });

        // 바닥 무늬(동심원)와 볼트
        HudMesh.Ring(vh, c, deck * 0.33f - unit, deck * 0.33f + unit, HudColors.DeckLine);
        HudMesh.Ring(vh, c, deck * 0.66f - unit, deck * 0.66f + unit, HudColors.DeckLine);
        foreach (var p in HudMesh.Regular(c, outer * 0.935f, 8, rot))
            HudMesh.Circle(vh, p, 4f * unit, HudColors.Bolt, 12);

        // 기울기: 주황 부채꼴(바깥쪽일수록 진함) + 단계 눈금 3줄
        if (amount > 0.02f)
        {
            float angle = Mathf.Atan2(leanDir.y, leanDir.x) * Mathf.Rad2Deg;
            float reach = deck * (0.25f + 0.7f * amount);
            const float half = 24f;
            HudMesh.ShadedWedge(vh, c, deck * 0.06f, reach, angle, half, 10, 8, (rt, at) =>
            {
                float edge = Mathf.Clamp01((1f - Mathf.Abs(at)) * 4f);
                float alpha = Mathf.Pow(Mathf.Lerp(0.06f / (0.25f + 0.7f * amount), 1f, rt), 1.5f) * 0.82f * edge;
                return HudMesh.WithAlpha(HudColors.Lean, alpha);
            });
            for (int k = 1; k <= 3; k++)
            {
                float r = deck * (0.25f + 0.7f * k / 3f);
                HudMesh.Arc(vh, c, r - 1.3f * unit, r + 1.3f * unit, angle - half, angle + half, HudColors.LeanTick, 10);
            }
        }

        // 가운데 축
        HudMesh.OutlinedCircle(vh, c, 8f * unit, 2f * unit, new Color32(235, 225, 200, 255), HudColors.Navy, 20);

        // 표식: 폭탄 → 몬스터 → 다른 플레이어 → 내 캐릭터 순으로 그림
        DrawMarkers(vh, c, deck, unit, MarkerKind.Bomb, false);
        DrawMarkers(vh, c, deck, unit, MarkerKind.Monster, false);
        DrawMarkers(vh, c, deck, unit, MarkerKind.Player, false);
        DrawMarkers(vh, c, deck, unit, MarkerKind.Player, true);

        // 진행 방향: 표식 위에 속이 빈 화살표(남색 외곽 + 흰 선)
        DrawTravelArrow(vh, c, deck, unit);
    }

    private void DrawTravelArrow(VertexHelper vh, Vector2 c, float deck, float unit)
    {
        float speed = Mathf.Clamp01(travel.magnitude);
        if (speed <= 0.03f) return;

        Vector2 dir = travel / travel.magnitude;
        Vector2 side = new Vector2(-dir.y, dir.x);
        // 길이는 속도에 비례, 최소 길이 있음
        float length = Mathf.Max(deck * (0.12f + 0.62f * speed), 34f * unit);
        Vector2 tip = c + dir * length;
        Vector2 neck = tip - dir * 16f * unit;
        Vector2 start = c + dir * 11f * unit;   // 가운데 축 바깥에서 시작함
        float shaft = 5f * unit, head = 14f * unit;

        Vector2[] outline =
        {
            start + side * shaft, neck + side * shaft, neck + side * head, tip,
            neck - side * head, neck - side * shaft, start - side * shaft
        };
        DrawOutline(vh, outline, 6.5f * unit, HudColors.Navy);
        DrawOutline(vh, outline, 3f * unit, HudColors.White);
    }

    /// <summary>닫힌 외곽선. 모서리는 원으로 메움.</summary>
    private static void DrawOutline(VertexHelper vh, Vector2[] points, float width, Color32 color)
    {
        for (int i = 0; i < points.Length; i++)
        {
            HudMesh.Line(vh, points[i], points[(i + 1) % points.Length], width, color);
            HudMesh.Circle(vh, points[i], width * 0.5f, color, 10);
        }
    }

    private void DrawMarkers(VertexHelper vh, Vector2 c, float deck, float unit, MarkerKind kind, bool local)
    {
        foreach (var m in markers)
        {
            if (m.kind != kind || m.local != local) continue;
            Vector2 p = c + Vector2.ClampMagnitude(m.position, 0.95f) * deck;
            switch (kind)
            {
                case MarkerKind.Player:
                    if (local) HudMesh.Ring(vh, p, 12f * unit, 15f * unit, HudColors.White, 24);
                    HudMesh.OutlinedCircle(vh, p, (local ? 9f : 8f) * unit, 3f * unit, m.color, HudColors.Navy, 20);
                    break;
                case MarkerKind.Monster:
                    HudMesh.RoundRect(vh, new Rect(p.x - 11f * unit, p.y - 11f * unit, 22f * unit, 22f * unit), 5f * unit, HudColors.Navy);
                    HudMesh.RoundRect(vh, new Rect(p.x - 8f * unit, p.y - 8f * unit, 16f * unit, 16f * unit), 3f * unit, HudColors.Monster);
                    HudMesh.Line(vh, p + new Vector2(0f, 4.5f) * unit, p + new Vector2(0f, -1f) * unit, 3f * unit, HudColors.White);
                    HudMesh.Circle(vh, p + new Vector2(0f, -4f) * unit, 1.6f * unit, HudColors.White, 8);
                    break;
                case MarkerKind.Bomb:
                    HudMesh.OutlinedCircle(vh, p, 8f * unit, 3f * unit, HudColors.Bomb, HudColors.Navy, 20);
                    HudMesh.Circle(vh, p + new Vector2(4f, 5f) * unit, 3f * unit, HudColors.Danger, 10);
                    break;
            }
        }
    }
}
