using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 버튼 이미지에 입체감을 줌. 원래 색은 그대로 두고 위는 밝게, 아래는 어둡게 칠하며,
/// 아래쪽에 짙은 테두리를 깔아 도톰해 보이게 함. 마우스를 올리면 커지고 누르면 작아짐.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class ButtonStyle : BaseMeshEffect, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Gradient")]
    [Tooltip("위쪽을 흰색 쪽으로 밝히는 정도(0~1).")]
    [Range(0f, 1f)][SerializeField] private float topLighten = 0.22f;

    [Tooltip("아래쪽을 어둡게 하는 정도(0~1).")]
    [Range(0f, 1f)][SerializeField] private float bottomDarken = 0.18f;

    [Header("Lip")]
    [Tooltip("아래쪽 짙은 테두리 두께(픽셀).")]
    [Min(0f)][SerializeField] private float lipDepth = 6f;

    [Tooltip("테두리 색을 원래 색보다 어둡게 하는 배율.")]
    [Range(0f, 1f)][SerializeField] private float lipShade = 0.55f;

    [Header("Motion")]
    [SerializeField] private float hoverScale = 1.04f;
    [SerializeField] private float pressScale = 0.96f;
    [Tooltip("크기가 바뀌는 빠르기(1/초).")]
    [Min(0.1f)][SerializeField] private float speed = 18f;

    private static readonly List<UIVertex> stream = new();
    private static readonly List<UIVertex> output = new();
    private Selectable selectable;
    private Vector3 baseScale = Vector3.one;
    private bool hover, press;

    protected override void Awake()
    {
        base.Awake();
        selectable = GetComponent<Selectable>();
        baseScale = transform.localScale;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        hover = press = false;
        if (Application.isPlaying) transform.localScale = baseScale;
    }

    public void OnPointerEnter(PointerEventData eventData) => hover = true;
    public void OnPointerExit(PointerEventData eventData) { hover = false; press = false; }
    public void OnPointerDown(PointerEventData eventData) => press = true;
    public void OnPointerUp(PointerEventData eventData) => press = false;

    private void Update()
    {
        if (!Application.isPlaying) return;
        bool usable = selectable == null || selectable.IsInteractable();
        float target = !usable ? 1f : press ? pressScale : hover ? hoverScale : 1f;
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        transform.localScale = Vector3.Lerp(transform.localScale, baseScale * target, k);
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        stream.Clear();
        vh.GetUIVertexStream(stream);
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in stream) { minY = Mathf.Min(minY, v.position.y); maxY = Mathf.Max(maxY, v.position.y); }
        float height = Mathf.Max(1e-3f, maxY - minY);

        output.Clear();
        // 테두리: 같은 모양을 아래로 내려 어둡게 깔아 둠
        if (lipDepth > 0f)
            foreach (var v in stream)
            {
                var lip = v;
                lip.position.y -= lipDepth;
                Color c = lip.color;
                lip.color = new Color(c.r * lipShade, c.g * lipShade, c.b * lipShade, c.a);
                output.Add(lip);
            }
        foreach (var v in stream)
        {
            var face = v;
            float t = (v.position.y - minY) / height;
            Color c = face.color;
            c = Color.Lerp(c, Color.white, topLighten * t);
            float dark = 1f - bottomDarken * (1f - t);
            face.color = new Color(c.r * dark, c.g * dark, c.b * dark, c.a);
            output.Add(face);
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(output);
    }
}
