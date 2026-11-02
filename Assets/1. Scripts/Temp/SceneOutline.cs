using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 맵 톤 비교용 외곽선. 렌더러 에셋을 건드리지 않고 이 컴포넌트가 있는 씬에서만 패스를 끼워넣음
[ExecuteAlways]
public class SceneOutline : MonoBehaviour
{
    [SerializeField] Shader shader;
    [Tooltip("선 진하기. 0이면 선 없음")]
    [Range(0f, 1f)] public float strength = 0.8f;
    [Tooltip("선 두께(픽셀)")]
    [Range(0.5f, 3f)] public float thickness = 1.2f;
    [Tooltip("깊이 차이 비율 기준. 낮을수록 선이 많아짐")]
    [Range(0.01f, 0.5f)] public float depthThreshold = 0.06f;
    [Tooltip("법선 차이 기준. 낮을수록 꺾인 면마다 선이 생김")]
    [Range(0.05f, 1f)] public float normalThreshold = 0.35f;
    [Tooltip("선 위치의 원래 색에 곱하는 값. 색상은 그대로 두고 어둡게만 함")]
    [Range(0f, 1f)] public float lineDarkness = 0.35f;
    [Tooltip("이 거리부터 선이 옅어짐")]
    public float fadeStart = 150f;
    [Tooltip("이 거리에서 선이 사라짐")]
    public float fadeEnd = 600f;

    Material material;
    OutlinePass pass;

    void OnEnable()
    {
        pass ??= new OutlinePass();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        CoreUtils.Destroy(material);
        material = null;
    }

    void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        if (shader == null) return;
        if (material == null) material = CoreUtils.CreateEngineMaterial(shader);

        material.SetFloat("_Strength", strength);
        material.SetFloat("_Thickness", thickness);
        material.SetFloat("_DepthThreshold", depthThreshold);
        material.SetFloat("_NormalThreshold", normalThreshold);
        material.SetFloat("_LineDarkness", lineDarkness);
        material.SetFloat("_FadeStart", fadeStart);
        material.SetFloat("_FadeEnd", fadeEnd);

        var renderer = cam.GetUniversalAdditionalCameraData().scriptableRenderer;
        if (renderer == null) return;
        pass.material = material;
        renderer.EnqueuePass(pass);
    }

    class OutlinePass : ScriptableRenderPass
    {
        public Material material;

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public OutlinePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            requiresIntermediateTexture = true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;

            TextureHandle source = resources.activeColorTexture;
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "_SceneOutlineColor";
            desc.clearBuffer = false;
            TextureHandle target = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Scene Outline", out var data))
            {
                data.source = source;
                data.material = material;
                builder.UseTexture(source);
                if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                if (resources.cameraNormalsTexture.IsValid()) builder.UseTexture(resources.cameraNormalsTexture);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(target, 0);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0));
            }

            resources.cameraColor = target;
        }
    }
}
