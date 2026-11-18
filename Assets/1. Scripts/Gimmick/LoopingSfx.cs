using UnityEngine;

/// <summary>반복 재생하는 환경음(선풍기 등)의 크기를 게임의 효과음 설정에 맞춤.</summary>
[RequireComponent(typeof(AudioSource))]
public class LoopingSfx : MonoBehaviour
{
    [Tooltip("효과음 설정이 최대일 때의 크기.")]
    [Range(0f, 1f)][SerializeField] private float volume = 0.5f;

    private AudioSource source;
    private float nextRefresh;

    private void Awake() => source = GetComponent<AudioSource>();

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.5f;
        source.volume = volume * Mathf.Clamp01(PlayerPrefs.GetFloat("RollWithUs.Sfx", 1f));
    }
}
