using UnityEngine;

/// <summary>
/// 장판 표면의 타일링과 흐름을 크기에 맞춰 자동으로 맞춤.
/// 장판 본체의 시각 자식(Quad)에 붙임. 물리와 무관함.
///
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class PadSurface : MonoBehaviour
{
    public enum TilingMode { World, FitWidth }

    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");   // URP
    private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");   // Built-in

    [Tooltip("World: 크기와 무관하게 tileSize마다 반복. 사방으로 이음매 없는 텍스처용(진흙).\n" +
             "FitWidth: 가로는 항상 한 장, 세로는 비율에 맞춰 반복. 양옆에 테두리가 있는 텍스처용(속도장판).")]
    [SerializeField] private TilingMode tilingMode = TilingMode.World;

    [Tooltip("World 모드 전용. 텍스처 한 장이 차지하는 월드 크기(m).")]
    [Min(0.01f)][SerializeField] private float tileSize = 3f;

    [Tooltip("반복 수를 정수로 맞춤. 끝에 잘린 무늬가 남지 않는 대신 무늬가 약간 늘어남.")]
    [SerializeField] private bool wholeRepeats = false;

    [Tooltip("텍스처가 +V 방향(장판의 forward)으로 흐르는 속도(텍스처 반복 단위/초). 0이면 정지.\n" +
             "플레이 중에만 흐름.")]
    [SerializeField] private float scrollSpeed = 0f;

    private Renderer surface;
    private MaterialPropertyBlock block;
    private float scrollOffset;

    private void OnEnable() => Apply();

    private void OnValidate() => Apply();

    private void Update()
    {
        if (Application.isPlaying && !Mathf.Approximately(scrollSpeed, 0f))
        {
            // 오프셋을 줄여야 텍스처가 +V, 즉 장판의 forward 방향으로 흐름
            scrollOffset = Mathf.Repeat(scrollOffset - scrollSpeed * Time.deltaTime, 1f);
            Apply();
        }
        else if (!Application.isPlaying && transform.hasChanged)
        {
            // 에디터에서 본체 스케일을 바꾸면 즉시 반영
            transform.hasChanged = false;
            Apply();
        }
    }

    private void Apply()
    {
        if (surface == null) surface = GetComponent<Renderer>();
        if (surface == null) return;

        block ??= new MaterialPropertyBlock();

        Vector2 tiling = CalculateTiling();
        var st = new Vector4(tiling.x, tiling.y, 0f, scrollOffset);

        surface.GetPropertyBlock(block);
        block.SetVector(BaseMapST, st);
        block.SetVector(MainTexST, st);
        surface.SetPropertyBlock(block);
    }

    private Vector2 CalculateTiling()
    {
        Vector3 s = transform.lossyScale;
        float width = Mathf.Abs(s.x);
        float length = Mathf.Abs(s.y);

        Vector2 tiling;

        if (tilingMode == TilingMode.FitWidth)
        {
            tiling = new Vector2(1f, width > 1e-4f ? length / width : 1f);
        }
        else
        {
            tiling = new Vector2(width / tileSize, length / tileSize);
        }

        if (wholeRepeats)
        {
            tiling.y = Mathf.Max(1f, Mathf.Round(tiling.y));
            if (tilingMode == TilingMode.World)
                tiling.x = Mathf.Max(1f, Mathf.Round(tiling.x));
        }

        return tiling;
    }
}