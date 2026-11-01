using Fusion;
using UnityEngine;

/// <summary>Only the private test runner uses this clock. Fixed simulation dt is unchanged.</summary>
[DefaultExecutionOrder(-1000)]
public class TraversalClock : MonoBehaviour, INetworkRunnerUpdater
{
    public float Speed = 1f;
    private NetworkRunner runner;
    public void Initialize(NetworkRunner value) => runner = value;
    public void Shutdown(NetworkRunner value) => runner = null;
    private void Update()
    {
        if (runner != null) runner.UpdateInternal(Mathf.Min(Time.unscaledDeltaTime, 0.1f) * Mathf.Clamp(Speed, 1f, 4f));
    }
    private void LateUpdate() { if (runner != null) runner.RenderInternal(); }
}
