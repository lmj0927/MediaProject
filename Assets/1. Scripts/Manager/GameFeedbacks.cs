using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

/// <summary>
/// 손맛 연출을 재생하는 로컬 매니저. 휠의 충돌·착지, 캐릭터 Stamp 착지, 폭탄 폭발에 맞춰 Feel 피드백(MMF_Player)을 재생함.
/// 네트워크 동기화 없음: 각 피어가 자기 화면의 휠·캐릭터 움직임으로 판단함.
/// 피드백 내용(흔들림 세기, 번쩍임 등)은 각 MMF_Player에서 조절함.
/// </summary>
[DefaultExecutionOrder(1000)]
public class GameFeedbacks : MonoBehaviour
{
    public static GameFeedbacks Instance { get; private set; }

    [Header("Feedbacks")]
    [Tooltip("휠이 장애물·벽에 부딪히거나 세게 밀렸을 때.")]
    [SerializeField] private MMF_Player impactFeedback;

    [Tooltip("휠이 공중에서 떨어져 바닥에 닿았을 때.")]
    [SerializeField] private MMF_Player landingFeedback;

    [Tooltip("캐릭터가 Stamp로 판에 내려찍었을 때.")]
    [SerializeField] private MMF_Player stampFeedback;

    [Tooltip("폭탄이 터졌을 때.")]
    [SerializeField] private MMF_Player explosionFeedback;

    [Header("Wheel Impact")]
    [Tooltip("짧은 시간 동안 수평 속도가 이만큼(m/s) 넘게 바뀌면 충돌로 봄.")]
    [SerializeField] private float impactSpeed = 6f;

    [Tooltip("수평 속도 변화가 이 값(m/s)이면 최대 세기.")]
    [SerializeField] private float impactMaxSpeed = 20f;

    [Header("Wheel Landing")]
    [Tooltip("이 속도(m/s)보다 빠르게 떨어지다 멈추면 착지로 봄.")]
    [SerializeField] private float landingSpeed = 7f;

    [Tooltip("떨어지던 속도가 이 값(m/s)이면 최대 세기.")]
    [SerializeField] private float landingMaxSpeed = 25f;

    [Header("Detection")]
    [Tooltip("속도 변화를 비교하는 시간 간격(초).")]
    [Min(0.02f)][SerializeField] private float window = 0.12f;

    [Tooltip("같은 종류의 연출이 다시 나오기까지 최소 시간(초).")]
    [Min(0f)][SerializeField] private float cooldown = 0.3f;

    [Tooltip("기준을 막 넘었을 때의 세기(0~1). 최대 세기 사이는 속도에 비례함.")]
    [Range(0f, 1f)][SerializeField] private float minIntensity = 0.25f;

    [Header("Range")]
    [Tooltip("내 캐릭터가 아닌 Stamp의 세기 배율.")]
    [Range(0f, 1f)][SerializeField] private float otherStampScale = 0.5f;

    [Tooltip("폭탄 폭발을 느끼는 거리(m). 휠에서 멀수록 약해짐.")]
    [Min(1f)][SerializeField] private float explosionRange = 40f;

    private struct Sample { public float time; public Vector3 position; }

    private readonly List<Sample> samples = new();
    private readonly Dictionary<Player, bool> stampStates = new();
    private readonly List<Player> players = new();
    private System.Reflection.PropertyInfo stampRecovering;
    private PlatformTilt platform;
    private Transform wheelVisual;
    private float nextSearch, nextImpact, nextLanding;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        stampRecovering = typeof(Player).GetProperty("IsStampRecovering", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>폭탄 폭발 연출. 휠에서 가까울수록 셈.</summary>
    public static void PlayExplosion(Vector3 position)
    {
        if (Instance == null || Instance.explosionFeedback == null) return;
        float intensity = 1f;
        if (Instance.wheelVisual != null)
            intensity = 1f - Mathf.Clamp01(Vector3.Distance(position, Instance.wheelVisual.position) / Instance.explosionRange);
        if (intensity > 0.05f) Instance.explosionFeedback.PlayFeedbacks(position, intensity);
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextSearch) Search();
        if (wheelVisual != null) UpdateWheel();
        UpdateStamps();
    }

    private void Search()
    {
        nextSearch = Time.unscaledTime + 0.5f;
        if (platform == null) platform = FindFirstObjectByType<PlatformTilt>();
        if (platform != null && wheelVisual == null && platform.Sphere != null)
        {
            var visual = platform.Sphere.Find("Visual");
            wheelVisual = visual != null ? visual : platform.Sphere;
        }
        players.Clear();
        players.AddRange(FindObjectsByType<Player>(FindObjectsSortMode.None));
    }

    /// <summary>휠 화면 위치로 속도를 구해, 짧은 시간 안의 큰 속도 변화를 충돌·착지로 봄.</summary>
    private void UpdateWheel()
    {
        float now = Time.time;
        samples.Add(new Sample { time = now, position = wheelVisual.position });
        while (samples.Count > 0 && samples[0].time < now - window * 3f) samples.RemoveAt(0);
        if (samples.Count < 4) return;

        Vector3 current = VelocityAt(now);
        Vector3 before = VelocityAt(now - window);
        // 리셋 등 순간이동은 무시함
        if (current.sqrMagnitude > 60f * 60f || before.sqrMagnitude > 60f * 60f) { samples.Clear(); return; }

        if (now >= nextLanding && before.y < -landingSpeed && current.y > -1.5f)
        {
            nextLanding = now + cooldown;
            nextImpact = Mathf.Max(nextImpact, now + cooldown);
            Play(landingFeedback, Mathf.Lerp(minIntensity, 1f, Mathf.InverseLerp(landingSpeed, landingMaxSpeed, -before.y)));
            return;
        }

        Vector3 change = current - before;
        change.y = 0f;
        float amount = change.magnitude;
        if (now >= nextImpact && amount > impactSpeed)
        {
            nextImpact = now + cooldown;
            Play(impactFeedback, Mathf.Lerp(minIntensity, 1f, Mathf.InverseLerp(impactSpeed, impactMaxSpeed, amount)));
        }
    }

    /// <summary>time 무렵 휠 속도. 그 앞뒤 샘플 두 개로 구함.</summary>
    private Vector3 VelocityAt(float time)
    {
        int i = samples.Count - 1;
        while (i > 1 && samples[i - 1].time > time) i--;
        var a = samples[Mathf.Max(0, i - 2)];
        var b = samples[i];
        float dt = b.time - a.time;
        return dt > 1e-4f ? (b.position - a.position) / dt : Vector3.zero;
    }

    /// <summary>캐릭터의 Stamp 착지 자세가 시작되는 순간을 찾아 재생함. 내 캐릭터는 세게, 다른 캐릭터는 약하게.</summary>
    private void UpdateStamps()
    {
        if (stampRecovering == null) return;
        foreach (var p in players)
        {
            if (p == null || p.Object == null || !p.Object.IsValid) continue;
            bool recovering = (bool)(Fusion.NetworkBool)stampRecovering.GetValue(p);
            stampStates.TryGetValue(p, out bool was);
            stampStates[p] = recovering;
            if (!recovering || was) continue;
            Play(stampFeedback, p.Object.HasInputAuthority ? 1f : otherStampScale, p.transform.position);
        }
    }

    private void Play(MMF_Player feedback, float intensity) =>
        Play(feedback, intensity, wheelVisual != null ? wheelVisual.position : transform.position);

    private static void Play(MMF_Player feedback, float intensity, Vector3 position)
    {
        if (feedback == null || intensity <= 0f) return;
        feedback.PlayFeedbacks(position, Mathf.Clamp01(intensity));
    }
}
