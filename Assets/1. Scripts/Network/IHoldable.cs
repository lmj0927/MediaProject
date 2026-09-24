using Fusion;
using UnityEngine;

/// <summary>
/// 잡기·들기·놓기·던지기가 가능한 대상.
/// 소유권/판정은 Host(State Authority)가 결정한다.
/// </summary>
public interface IHoldable
{
    NetworkObject Object { get; }

    /// <summary>이미 누군가에게 잡혀 있으면 false.</summary>
    bool CanBeHeld { get; }

    /// <summary>잡히기 시작 (Host에서 호출).</summary>
    void OnPickedUp(NetworkObject holder);

    /// <summary>제자리 놓기.</summary>
    void OnReleased();

    /// <summary>월드 속도가 적용된 던지기.</summary>
    void OnThrown(Vector3 worldVelocity);
}
