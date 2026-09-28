using Fusion;
using UnityEngine;

/// <summary>
/// Host 전용: Unity 자동 물리를 끄고 매 틱 모든 FixedUpdateNetwork 뒤에 Physics.Simulate를 한 번 호출한다.
/// FUN(캐리/토크/판 포즈)과 PhysX 스텝이 같은 틱에 1:1로 묶인다.
/// Client는 자동 물리를 그대로 둔다 (공유 휠·Prop 예측 없음).
/// </summary>
[DefaultExecutionOrder(1000)]
public class HostPhysicsStepper : SimulationBehaviour
{
    private bool _active;
    private SimulationMode _originalMode;
    private int _lastSimulatedTick = -1;

    public override void FixedUpdateNetwork()
    {
        if (Runner == null || !Runner.IsServer || !Runner.IsForward)
            return;

        if (!_active)
        {
            _originalMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _active = true;
        }

        int tick = Runner.Tick.Raw;
        if (tick == _lastSimulatedTick)
            return;

        _lastSimulatedTick = tick;
        Physics.Simulate(Runner.DeltaTime);
    }

    private void OnDisable() => Restore();

    private void OnDestroy() => Restore();

    private void Restore()
    {
        if (!_active)
            return;

        Physics.simulationMode = _originalMode;
        _active = false;
    }
}
