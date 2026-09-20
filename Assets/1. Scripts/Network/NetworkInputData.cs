using Fusion;
using UnityEngine;

public enum PlayerInputButton
{
    Jump = 0,
    Hold = 1,
    Throw = 2,
}

public struct NetworkInputData : INetworkInput
{
    public Vector3 direction;
    public NetworkButtons buttons;
}
