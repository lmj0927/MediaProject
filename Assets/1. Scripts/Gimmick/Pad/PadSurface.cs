using UnityEngine;

/// <summary>
/// 장판 표면의 타일링과 흐름을 크기에 맞춰 자동으로 맞춤.
/// 장판 부모에 붙이고, 크기는 부모의 X/Z 스케일로 조절함.
///
/// 머티리얼을 복제하지 않고 렌더러별 속성으로 적용하므로 같은 머티리얼을 쓰는 장판끼리 영향이 없음.
/// 에디터에서도 즉시 반영됨.
/// </summary>
[ExecuteAlways]
public class PadSurface : MonoBehaviour
{
    private static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");   // URP
    private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");   // Built-in

    [Tooltip("표면 렌더러. 비우면 자식에서 찾음.")]
    [SerializeField] private Renderer surface;

    [Tooltip("텍스처 한 장이 차지하는 월드 크기(m). 크기와 무관하게 무늬 밀도를 유지함.")]
    [Min(0.01f)][SerializeField] private float tileSize = 3f;

    [Tooltip("텍스처가 +V 방향(장판의 forward)으로 흐르는 속도(텍스처 반복 단위/초). 0이면 정지.\n" +
             "플레이 중에만 흐름.")]
    [SerializeField] private float scrollSpeed = 0f;

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
            // 에디터에서 스케일을 바꾸면 즉시 반영
            transform.hasChanged = false;
            Apply();
        }
    }

    private void Apply()
    {
        if (surface == null) surface = GetComponentInChildren<Renderer>();
        if (surface == null) return;

        block ??= new MaterialPropertyBlock();

        // Quad는 1×1이고 X축으로 눕혀 있어, 부모의 X/Z 스케일이 곧 표면의 가로세로 크기
        Vector3 s = transform.lossyScale;
        var st = new Vector4(Mathf.Abs(s.x) / tileSize, Mathf.Abs(s.z) / tileSize, 0f, scrollOffset);

        surface.GetPropertyBlock(block);
        block.SetVector(BaseMapST, st);
        block.SetVector(MainTexST, st);
        surface.SetPropertyBlock(block);
    }
}
