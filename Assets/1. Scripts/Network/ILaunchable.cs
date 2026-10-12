using Fusion;
using UnityEngine;

/// <summary>
/// 폭발 등 외부 충격으로 날아갈 수 있는 대상.
/// 들고 있거나 들려 있던 관계는 풀고 날아간다. Host(State Authority)에서만 호출.
/// </summary>
public interface ILaunchable
{
    NetworkObject Object { get; }

    /// <summary>월드 속도로 날려 보냄.</summary>
    void Launch(Vector3 worldVelocity);
}
