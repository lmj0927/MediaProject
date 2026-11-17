using Fusion;
using UnityEngine;

/// <summary>
/// 피스톤 점프 패드를 주기적으로만 작동하게 함. 판이 튀어 오르는 동안에만 패드가 휠을 쏘고, 내려가 있는 동안은 그냥 밟고 지나갈 수 있음.
/// 판 움직임은 세션 시각으로 계산해서 모든 피어가 같은 박자를 봄. 패드를 켜고 끄는 것은 Host에서만 함.
/// period가 0이면 항상 작동함(판은 가만히 있음).
/// </summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(BoxCollider))]
public class PistonCycle : MonoBehaviour
{
    [Tooltip("한 주기(초). 0이면 항상 작동함.")]
    [Min(0f)][SerializeField] private float period = 3f;

    [Tooltip("한 주기 중 판이 올라와 작동하는 시간(초).")]
    [Min(0.05f)][SerializeField] private float activeTime = 0.9f;

    [Tooltip("주기 시작 위치(0~1). 나란히 놓인 패드끼리 다르게 주면 번갈아 튀어 오름.")]
    [Range(0f, 1f)][SerializeField] private float phaseOffset;

    [Tooltip("튀어 오르기 전에 판이 살짝 내려가며 예고하는 시간(초).")]
    [Min(0f)][SerializeField] private float warnTime = 0.5f;

    [Header("Visual")]
    [Tooltip("위아래로 움직일 판 모델. 비우면 움직임 없이 작동만 함.")]
    [SerializeField] private Transform plate;

    [Tooltip("판이 튀어 오르는 높이(m).")]
    [SerializeField] private float plateRise = 1.6f;

    [Tooltip("예고할 때 판이 내려가는 깊이(m).")]
    [SerializeField] private float plateDip = 0.35f;

    private BoxCollider area;
    private NetworkObject networkObject;
    private Vector3 areaCenter;
    private Vector3 plateRest;
    private bool wasActive = true;

    private void Awake()
    {
        area = GetComponent<BoxCollider>();
        networkObject = GetComponent<NetworkObject>();
        areaCenter = area.center;
        if (plate != null) plateRest = plate.localPosition;
    }

    private void Update()
    {
        if (period <= 0f || networkObject == null || !networkObject.IsValid) return;
        var runner = networkObject.Runner;
        if (runner == null) return;

        float t = Mathf.Repeat((float)runner.SimulationTime / period + phaseOffset, 1f) * period;
        bool active = t < activeTime;

        // 작동하지 않는 동안에는 판정 상자를 바닥 아래로 내려 패드가 휠을 찾지 못하게 함
        if (networkObject.HasStateAuthority && active != wasActive)
        {
            area.center = active ? areaCenter : areaCenter + Vector3.down * 1000f;
            wasActive = active;
        }

        if (plate == null) return;
        float height;
        if (active)
        {
            // 빠르게 튀어 올랐다가 천천히 내려옴
            float k = t / activeTime;
            height = plateRise * (k < 0.15f ? k / 0.15f : 1f - (k - 0.15f) / 0.85f);
        }
        else
        {
            float untilNext = period - t;
            height = untilNext < warnTime ? -plateDip * (1f - untilNext / Mathf.Max(warnTime, 0.01f)) : 0f;
        }
        plate.localPosition = plateRest + Vector3.up * (height / Mathf.Max(0.01f, plate.parent != null ? plate.parent.lossyScale.y : 1f));
    }
}
