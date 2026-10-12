using UnityEngine;

/// <summary>
/// Bomb의 도화선 진행도로 몸체를 붉히고, 점점 빨라지는 경고등 깜빡임(켜짐/꺼짐)을 냄.
/// 복제 상태 없음: 모든 피어가 Fuse 하나로 같은 색과 박자를 재구성한다.
/// URP Lit 기준(_BaseColor/_EmissionColor). 머티리얼의 Emission을 켜 두고 발광색은 검정으로 둘 것.
/// </summary>
public class BombBlinkVisual : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Bomb _bomb;

    [Tooltip("_visualRoot 아래 메시 Renderer들.")]
    [SerializeField] private Renderer[] _renderers;

    [Header("Heat")]
    [SerializeField] private Color _hotColor = Color.red;

    [Tooltip("진행도(0~1) → 몸체가 붉어지는 정도(0~1).")]
    [SerializeField] private AnimationCurve _heatCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Blink")]
    [Tooltip("점화 직후 깜빡임 횟수(회/초).")]
    [SerializeField] private float _startBlinkHz = 1f;

    [Tooltip("폭발 직전 깜빡임 횟수(회/초).")]
    [SerializeField] private float _endBlinkHz = 8f;

    [Tooltip("한 주기 중 켜져 있는 비율.")]
    [Range(0.1f, 0.9f)]
    [SerializeField] private float _litRatio = 0.5f;

    [Tooltip("폭발 직전 켜졌을 때의 발광 세기. 진행도에 비례해 0부터 올라감.")]
    [SerializeField] private float _emissionIntensity = 3f;

    [Tooltip("점화 직후에도 보장하는 최소 발광 비율(0~1).")]
    [Range(0f, 1f)]
    [SerializeField] private float _minEmissionRatio = 0.2f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock _block;
    private Color[] _baseColors;
    private bool _applied;

    private void Awake()
    {
        if (_bomb == null)
            _bomb = GetComponentInParent<Bomb>();

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>();

        _block = new MaterialPropertyBlock();
        _baseColors = new Color[_renderers.Length];
        for (int i = 0; i < _renderers.Length; i++)
        {
            var material = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
            _baseColors[i] = material != null && material.HasProperty(BaseColorId)
                ? material.GetColor(BaseColorId)
                : Color.white;
        }
    }

    private void OnDisable() => ResetVisual();

    private void LateUpdate()
    {
        if (_bomb == null || !_bomb.TryGetFuseElapsed(out float t))
        {
            ResetVisual();
            return;
        }

        float duration = _bomb.FuseSeconds;
        float progress = Mathf.Clamp01(t / duration);

        // 주파수를 시간에 대해 적분한 위상. sin(t * f(t))처럼 주파수 변화 때 박자가 튀지 않음.
        float phase = _startBlinkHz * t + (_endBlinkHz - _startBlinkHz) * t * t / (2f * duration);
        bool lit = phase - Mathf.Floor(phase) < _litRatio;

        float heat = Mathf.Clamp01(_heatCurve.Evaluate(progress));
        float glow = lit ? _emissionIntensity * Mathf.Lerp(_minEmissionRatio, 1f, progress) : 0f;
        Color emission = _hotColor * glow;

        for (int i = 0; i < _renderers.Length; i++)
        {
            var r = _renderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, Color.Lerp(_baseColors[i], _hotColor, heat));
            _block.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_block);
        }

        _applied = true;
    }

    private void ResetVisual()
    {
        if (!_applied || _renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].SetPropertyBlock(null);
        }

        _applied = false;
    }
}
