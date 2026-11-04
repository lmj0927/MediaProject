using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카메라와 판(휠) 사이를 가리는 맵 물체를 점무늬로 비워 판이 보이게 함.
/// Island Textured Lit 재질은 _OcclusionFade 값만 넣고, 다른 셰이더는 가리는 동안 Island Textured Lit 복제 재질로 바꿨다가 되돌림.
/// 판, 캐릭터, 네트워크 오브젝트(기믹 등)와 투명 재질은 건드리지 않음.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraOcclusionFade : MonoBehaviour
{
    [Tooltip("가린 물체를 비우는 정도(0~1). 1이면 완전히 사라짐.")]
    [Range(0f, 1f)] [SerializeField] private float fadeAmount = 0.8f;

    [Tooltip("비워지고 돌아오는 데 걸리는 시간(초).")]
    [SerializeField] private float fadeTime = 0.2f;

    [Tooltip("더 이상 가리지 않아도 이 시간(초) 동안은 비운 채로 둠.")]
    [SerializeField] private float holdTime = 0.4f;

    [Tooltip("검사 지점에 쏘는 구의 반지름(m).")]
    [SerializeField] private float probeRadius = 0.4f;

    [Tooltip("검사 지점에서 이 거리(m) 안에 닿은 것은 무시함(판이 올라선 바닥 등).")]
    [SerializeField] private float nearIgnore = 1.5f;

    [Tooltip("판 둘레 검사 지점 수.")]
    [SerializeField] private int rimPoints = 8;

    [Tooltip("검사할 레이어.")]
    [SerializeField] private LayerMask layers = ~0;

    [Tooltip("다른 셰이더를 바꿔 끼울 셰이더. 비우면 Island Textured Lit을 찾음.")]
    [SerializeField] private Shader fadeShader;

    private static readonly int FadeId = Shader.PropertyToID("_OcclusionFade");

    private class Faded
    {
        public Renderer renderer;
        public float amount;
        public float lastHit;
        public Material[] originals;
    }

    private readonly Dictionary<Renderer, Faded> faded = new();
    private readonly Dictionary<Material, Material> swapCache = new();
    private readonly Dictionary<Collider, Renderer[]> colliderRenderers = new();
    private readonly List<Renderer> removeList = new();
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private MaterialPropertyBlock block;
    private PlatformTilt platform;
    private float nextSearch;

    private void Awake()
    {
        block = new MaterialPropertyBlock();
        if (fadeShader == null) fadeShader = Shader.Find("MediaProject/Island Textured Lit");
    }

    private void LateUpdate()
    {
        if (platform == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 0.5f;
            platform = FindFirstObjectByType<PlatformTilt>();
        }
        if (platform != null && platform.Sphere != null) Probe();
        Animate();
    }

    private void OnDisable()
    {
        foreach (var f in faded.Values) Restore(f);
        faded.Clear();
    }

    private void OnDestroy()
    {
        foreach (var m in swapCache.Values) if (m != null) Destroy(m);
        swapCache.Clear();
    }

    /// <summary>카메라에서 판 중심, 판 둘레, 휠 중심으로 구를 쏴 가리는 렌더러를 찾음.</summary>
    private void Probe()
    {
        Vector3 deck = platform.transform.position + Vector3.up * 0.5f;
        float radius = platform.PlatformRadius;
        ProbeTo(deck);
        ProbeTo(platform.Sphere.position);
        for (int i = 0; i < rimPoints; i++)
        {
            float a = i * Mathf.PI * 2f / rimPoints;
            ProbeTo(deck + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius);
        }
    }

    private void ProbeTo(Vector3 target)
    {
        Vector3 origin = transform.position;
        Vector3 delta = target - origin;
        float length = delta.magnitude - probeRadius;
        if (length <= 0f) return;
        int count = Physics.SphereCastNonAlloc(origin, probeRadius, delta / delta.magnitude, hits, length, layers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Vector3 point = hits[i].distance > 0f ? hits[i].point : origin;
            if ((point - target).sqrMagnitude < nearIgnore * nearIgnore) continue;
            foreach (var r in RenderersOf(hits[i].collider)) Mark(r);
        }
    }

    private Renderer[] RenderersOf(Collider collider)
    {
        if (colliderRenderers.TryGetValue(collider, out var list)) return list;
        list = Excluded(collider.transform) ? System.Array.Empty<Renderer>() : collider.GetComponentsInChildren<Renderer>();
        if (list.Length == 0 && !Excluded(collider.transform))
        {
            var r = collider.GetComponentInParent<Renderer>();
            if (r != null) list = new[] { r };
        }
        colliderRenderers[collider] = list;
        return list;
    }

    /// <summary>판, 캐릭터, 기믹 등 네트워크 오브젝트는 제외함.</summary>
    private bool Excluded(Transform t)
    {
        if (t.GetComponentInParent<Player>() != null) return true;
        if (t.GetComponentInParent<Fusion.NetworkObject>() != null) return true;
        return false;
    }

    private void Mark(Renderer r)
    {
        if (r == null || !r.enabled) return;
        if (!faded.TryGetValue(r, out var f))
        {
            if (!CanFade(r)) return;
            f = new Faded { renderer = r };
            faded[r] = f;
        }
        f.lastHit = Time.time;
    }

    private bool CanFade(Renderer r)
    {
        if (r is not MeshRenderer && r is not SkinnedMeshRenderer) return false;
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) return false;
            if (m.renderQueue >= 2450) return false;
        }
        return true;
    }

    private void Animate()
    {
        float step = fadeTime > 0f ? Time.deltaTime / fadeTime : 1f;
        removeList.Clear();
        foreach (var f in faded.Values)
        {
            if (f.renderer == null) { removeList.Add(f.renderer); continue; }
            bool hit = Time.time - f.lastHit <= holdTime;
            float before = f.amount;
            f.amount = Mathf.MoveTowards(f.amount, hit ? 1f : 0f, step);
            if (f.amount > 0f && before <= 0f) Swap(f);
            if (f.amount <= 0f && !hit) { Restore(f); removeList.Add(f.renderer); continue; }
            Apply(f.renderer, f.amount * fadeAmount);
        }
        foreach (var r in removeList) faded.Remove(r);
    }

    private void Apply(Renderer r, float value)
    {
        r.GetPropertyBlock(block);
        block.SetFloat(FadeId, value);
        r.SetPropertyBlock(block);
    }

    /// <summary>Island Textured Lit이 아닌 재질을 같은 색과 텍스처의 Island Textured Lit 복제 재질로 바꿈.</summary>
    private void Swap(Faded f)
    {
        if (fadeShader == null || f.originals != null) return;
        var originals = f.renderer.sharedMaterials;
        bool needs = false;
        foreach (var m in originals) if (m.shader != fadeShader) { needs = true; break; }
        if (!needs) return;
        var swapped = new Material[originals.Length];
        for (int i = 0; i < originals.Length; i++)
            swapped[i] = originals[i].shader == fadeShader ? originals[i] : SwapMaterial(originals[i]);
        f.originals = originals;
        f.renderer.sharedMaterials = swapped;
    }

    private void Restore(Faded f)
    {
        if (f.renderer == null) return;
        if (f.originals != null) f.renderer.sharedMaterials = f.originals;
        f.originals = null;
        f.renderer.GetPropertyBlock(block);
        block.SetFloat(FadeId, 0f);
        f.renderer.SetPropertyBlock(block);
    }

    private Material SwapMaterial(Material source)
    {
        if (swapCache.TryGetValue(source, out var m) && m != null) return m;
        m = new Material(fadeShader) { name = source.name + " (Fade)" };
        string tex = source.HasProperty("_Albedo_RGB") ? "_Albedo_RGB" : source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
        if (tex != null)
        {
            m.SetTexture("_BaseMap", source.GetTexture(tex));
            m.SetTextureScale("_BaseMap", source.GetTextureScale(tex));
            m.SetTextureOffset("_BaseMap", source.GetTextureOffset(tex));
        }
        string color = source.HasProperty("_BaseColor") ? "_BaseColor" : source.HasProperty("_Color") ? "_Color" : null;
        m.SetColor("_BaseColor", color != null ? source.GetColor(color) : Color.white);
        if (source.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", source.GetFloat("_Smoothness"));
        if (source.HasProperty("_Metallic")) m.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        m.SetFloat("_DetailAlbedoMapScale", 0f);
        m.SetFloat("_DetailNormalMapScale", 0f);
        m.SetFloat("_IslandTerrain", 0f);
        m.DisableKeyword("_ISLAND_TERRAIN");
        swapCache[source] = m;
        return m;
    }
}
