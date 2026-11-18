using Fusion;
using UnityEngine;

[DefaultExecutionOrder(1100)]
[RequireComponent(typeof(NetworkObject))]
public sealed class MemoryPath : NetworkBehaviour
{
    [SerializeField] private SphereDriver _transport;
    [SerializeField] private Transform departureLine;
    [SerializeField] private Transform hiddenVisuals;
    [SerializeField] private Camera playCamera;
    [SerializeField] private Transform previewView;

    [Networked] public NetworkBool PathHidden { get; private set; }

    private Rigidbody transportBody;
    private Renderer[] pathRenderers;
    private bool renderersHidden;

    public override void Spawned()
    {
        pathRenderers = hiddenVisuals.GetComponentsInChildren<Renderer>(true);
        if (HasStateAuthority)
        {
            transportBody = _transport.GetComponent<Rigidbody>();
            _transport.Respawned += RevealPath;
            PathHidden = false;
        }

        ApplyVisibility(PathHidden);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_transport != null)
            _transport.Respawned -= RevealPath;

        ApplyVisibility(false);
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && !PathHidden &&
            Vector3.Dot(transportBody.position - departureLine.position, departureLine.forward) >= 0f)
        {
            PathHidden = true;
        }
    }

    public override void Render() => ApplyVisibility(PathHidden);

    private void LateUpdate()
    {
        // GameCamera continues updating its normal follow pose while the overview is on screen.
        if (Object != null && Object.IsValid && !PathHidden)
            playCamera.transform.SetPositionAndRotation(previewView.position, previewView.rotation);
    }

    private void RevealPath() => PathHidden = false;

    private void ApplyVisibility(bool hidden)
    {
        if (renderersHidden == hidden)
            return;

        foreach (var pathRenderer in pathRenderers)
            pathRenderer.forceRenderingOff = hidden;

        renderersHidden = hidden;
    }
}
