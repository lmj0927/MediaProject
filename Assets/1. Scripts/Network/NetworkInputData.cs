using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fusion으로 전송하는 플레이어 입력 버튼 비트 인덱스.
/// </summary>
public enum PlayerInputButton
{
    Jump = 0,
    Hold = 1,
    Throw = 2,
    Stamp = 3,
}

/// <summary>
/// 클라이언트에서 수집해 Host/시뮬로 넘기는 틱 입력 데이터.
/// </summary>
public struct NetworkInputData : INetworkInput
{
    /// <summary>수평 이동 의도 (보통 XZ).</summary>
    public Vector3 direction;

    /// <summary>점프/잡기/던지기/Stamp 등 버튼 상태.</summary>
    public NetworkButtons buttons;
}

internal sealed class PlayerInputReader
{
    private readonly InputActionMap _actions;
    private readonly InputAction _move;
    private readonly InputAction _jump;
    private readonly InputAction _hold;
    private readonly InputAction _throw;
    private readonly InputAction _stamp;

    public PlayerInputReader(InputActionMap actions)
    {
        _actions = actions;
        _move = actions.FindAction("Move", throwIfNotFound: true);
        _jump = actions.FindAction("Jump", throwIfNotFound: true);
        _hold = actions.FindAction("Hold", throwIfNotFound: true);
        _throw = actions.FindAction("Throw", throwIfNotFound: true);
        _stamp = actions.FindAction("Stamp", throwIfNotFound: true);
    }

    public void Enable() => _actions.Enable();
    public void Disable() => _actions.Disable();

    public NetworkInputData Read()
    {
        var move = _move.ReadValue<Vector2>();
        var data = new NetworkInputData { direction = new Vector3(move.x, 0f, move.y) };
        data.buttons.Set(PlayerInputButton.Jump, _jump.IsPressed());
        data.buttons.Set(PlayerInputButton.Hold, _hold.IsPressed());
        data.buttons.Set(PlayerInputButton.Throw, _throw.IsPressed());
        data.buttons.Set(PlayerInputButton.Stamp, _stamp.IsPressed());
        return data;
    }
}
