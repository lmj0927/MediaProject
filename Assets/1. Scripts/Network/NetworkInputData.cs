using Fusion;
using UnityEngine;

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
