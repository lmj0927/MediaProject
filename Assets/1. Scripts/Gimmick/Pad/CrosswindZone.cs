using Fusion;
using UnityEngine;

/// <summary>주기적으로 방향이 바뀌는 옆바람. 공유 휠만 Host 틱에서 밀고, 팬과 바람은 같은 시뮬레이션 시각을 표시함.</summary>
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BoxCollider))]
public class CrosswindZone : WheelPadBase
{
    [Header("Gust")]
    [Tooltip("최대 옆바람 가속도(m/s²).")]
    [Min(0f)][SerializeField] private float acceleration = 4.5f;

    [Tooltip("좌우 바람 한 주기(초). 절반마다 방향이 바뀌며 전환 직전에는 약해짐.")]
    [Min(1f)][SerializeField] private float period = 28f;

    [Tooltip("주기 시작 위치(초).")]
    [SerializeField] private float phaseOffset;

    [Header("Visuals")]
    [SerializeField] private Transform positiveRotor;
    [SerializeField] private Transform negativeRotor;
    [SerializeField] private Transform[] additionalRotors = System.Array.Empty<Transform>();
    [SerializeField] private ParticleSystem positiveWind;
    [SerializeField] private ParticleSystem negativeWind;

    private Quaternion positiveRest;
    private Quaternion negativeRest;
    private Quaternion[] additionalRest;

    public override void Spawned()
    {
        if (positiveRotor != null) positiveRest = positiveRotor.localRotation;
        if (negativeRotor != null) negativeRest = negativeRotor.localRotation;
        additionalRest = new Quaternion[additionalRotors.Length];
        for (int i = 0; i < additionalRotors.Length; i++)
            if (additionalRotors[i] != null) additionalRest[i] = additionalRotors[i].localRotation;
    }

    /// <summary>양수는 로컬 +X, 음수는 -X. 같은 시각에는 모든 피어가 같은 바람을 표시함.</summary>
    public float GustAt(float time)
    {
        float phase = Mathf.Repeat(time + phaseOffset, Mathf.Max(1f, period)) / Mathf.Max(1f, period);
        return Mathf.Clamp(Mathf.Sin(phase * Mathf.PI * 2f) * 2f, -1f, 1f);
    }

    protected override void Affect(Rigidbody rb, float dt)
    {
        // 부모의 기본 필터에 더해 캐릭터나 소품에는 힘을 가하지 않음.
        if (!rb.TryGetComponent<SphereDriver>(out _)) return;
        Vector3 direction = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        float gust = acceleration * GustAt((float)Runner.SimulationTime);
        rb.AddForce(direction * gust, ForceMode.Acceleration);
        float radius = SphereRadiusOf(rb);
        if (radius > 0.01f)
            rb.AddTorque(Vector3.Cross(Vector3.up, direction) * (gust / radius), ForceMode.Acceleration);
    }

    public override void Render()
    {
        float time = (float)Runner.SimulationTime + Runner.DeltaTime * Runner.LocalAlpha;
        float gust = GustAt(time);
        Quaternion spin = Quaternion.AngleAxis(time * 720f, Vector3.forward);
        ShowWind(positiveRotor, positiveRest, positiveWind, Mathf.Max(0f, gust), spin);
        ShowWind(negativeRotor, negativeRest, negativeWind, Mathf.Max(0f, -gust), spin);
        for (int i = 0; i < additionalRotors.Length; i++)
            if (additionalRotors[i] != null) additionalRotors[i].localRotation = additionalRest[i] * spin;
    }

    private static void ShowWind(Transform rotor, Quaternion rest, ParticleSystem wind, float strength, Quaternion spin)
    {
        if (rotor != null) rotor.localRotation = rest * spin;
        if (wind == null) return;
        var emission = wind.emission;
        emission.rateOverTime = strength * 38f;
        // 이전 방향의 잔상이 반대 돌풍과 겹치지 않게 전환 때 비움.
        if (strength <= 0f && wind.particleCount > 0) wind.Clear();
    }
}
