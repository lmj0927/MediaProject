using System.IO;
using System.Text;
using Fusion;
using UnityEngine;

/// <summary>
/// 엘리베이터 떨림 측정용 테스트 도구.
/// 틱마다 시뮬 위치(물리 본체)를, 프레임마다 화면 위치(보이는 메시)를 높이 기준으로 기록함.
/// 코드에서 Begin으로 대상을 지정하고 Save로 CSV를 남김.
/// </summary>
[DefaultExecutionOrder(2000)]
public class ElevatorProbe : SimulationBehaviour, IAfterTick
{
    private MovingPad pad;
    private Player rider;
    private Rigidbody padBody, wheelBody, deckBody;
    private Transform padVisual, wheelVisual, deckVisual;
    private WheelCarrier carrier;
    private readonly StringBuilder log = new();
    private bool active;

    public bool Active => active;

    /// <summary>기록을 시작함. 이전 기록은 지움.</summary>
    public void Begin(MovingPad target, Player who)
    {
        pad = target;
        rider = who;
        padBody = pad.GetComponentInChildren<Rigidbody>();
        padVisual = padBody.transform.Find("Visual");

        var platform = FindFirstObjectByType<PlatformTilt>();
        deckBody = platform.GetComponent<Rigidbody>();
        deckVisual = platform.GetComponentInChildren<Renderer>().transform;
        wheelBody = platform.Sphere.GetComponent<Rigidbody>();
        wheelVisual = platform.Sphere.GetComponentInChildren<Renderer>().transform;
        carrier = rider.GetComponent<WheelCarrier>();

        log.Clear();
        log.AppendLine("kind,time,tick,phase,pad,wheel,deck,rider,standing,onPlatform,riding,ccGrounded,ccVy");
        active = true;
    }

    public void End() => active = false;

    void IAfterTick.AfterTick()
    {
        if (!active) return;
        Row("T", padBody.position.y, wheelBody.position.y, deckBody.position.y, rider.transform.position.y);
    }

    private void LateUpdate()
    {
        if (!active) return;
        Row("F", padVisual.position.y, wheelVisual.position.y, deckVisual.position.y, rider.transform.position.y);
    }

    private void Row(string kind, float padY, float wheelY, float deckY, float riderY)
    {
        log.Append(kind).Append(',')
           .Append(Time.time.ToString("F4")).Append(',')
           .Append(Runner != null ? Runner.Tick.Raw : 0).Append(',')
           .Append(pad.CurrentPhase).Append(',')
           .Append(padY.ToString("F4")).Append(',')
           .Append(wheelY.ToString("F4")).Append(',')
           .Append(deckY.ToString("F4")).Append(',')
           .Append(riderY.ToString("F4")).Append(',')
           .Append(carrier != null && carrier.IsStandingOnDeck ? 1 : 0).Append(',')
           .Append(carrier != null && carrier.CurrentPlatform != null ? 1 : 0).Append(',')
           .Append(RidingFlag()).Append(',')
           .Append(Controller != null && Controller.Grounded ? 1 : 0).Append(',')
           .Append(Controller != null ? Controller.Velocity.y.ToString("F3") : "").AppendLine();
    }

    // 판 기준 시각 보정 여부. Player의 비공개 복제 상태라 리플렉션으로 읽음.
    private static readonly System.Reflection.PropertyInfo ridingProperty =
        typeof(Player).GetProperty("RidingPlatform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static readonly System.Reflection.FieldInfo controllerField =
        typeof(Player).GetField("_cc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private NetworkCharacterController Controller =>
        rider != null && controllerField != null ? controllerField.GetValue(rider) as NetworkCharacterController : null;

    private int RidingFlag()
    {
        if (ridingProperty == null || rider == null) return -1;
        return (NetworkBool)ridingProperty.GetValue(rider) ? 1 : 0;
    }

    /// <summary>Logs/BalanceTest 아래에 CSV로 저장하고 경로를 돌려줌.</summary>
    public string Save(string fileName)
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BalanceTest"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName + ".csv");
        File.WriteAllText(path, log.ToString());
        return path;
    }
}
