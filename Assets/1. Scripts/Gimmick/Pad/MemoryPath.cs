using Fusion;
using UnityEngine;

[DefaultExecutionOrder(1100)]
[RequireComponent(typeof(NetworkObject))]
public sealed class MemoryPath : NetworkBehaviour
{
    [SerializeField] private SphereDriver _transport;
    [SerializeField] private BoxCollider previewTrigger;
    [SerializeField] private Transform departureLine;
    [SerializeField] private Transform hiddenVisuals;
    [SerializeField] private Camera playCamera;
    [SerializeField] private Transform previewView;

    [Networked] private NetworkBool HasDeparted { get; set; }
    [Networked] private NetworkBool PreviewActive { get; set; }

    public bool PathHidden => !PreviewActive;

    private Rigidbody transportBody;
    private Renderer[] pathRenderers;
    private bool renderersHidden;

    private void Awake()
    {
        pathRenderers = hiddenVisuals.GetComponentsInChildren<Renderer>(true);
        ApplyVisibility(true);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            transportBody = _transport.GetComponent<Rigidbody>();
            _transport.Respawned += ResetAttempt;
            ResetAttempt();
        }

        ApplyVisibility(PathHidden);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_transport != null)
            _transport.Respawned -= ResetAttempt;

        ApplyVisibility(true);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || HasDeparted)
            return;

        Vector3 position = transportBody.position;
        if (Vector3.Dot(position - departureLine.position, departureLine.forward) >= 0f)
            HasDeparted = true;

        PreviewActive = !HasDeparted && previewTrigger != null && previewTrigger.bounds.Contains(position);
    }

    public override void Render() => ApplyVisibility(PathHidden);

    private void LateUpdate()
    {
        // GameCamera continues updating its normal follow pose while the overview is on screen.
        if (Object != null && Object.IsValid && PreviewActive)
            playCamera.transform.SetPositionAndRotation(previewView.position, previewView.rotation);
    }

    private void ResetAttempt()
    {
        HasDeparted = false;
        PreviewActive = false;
    }

    private void ApplyVisibility(bool hidden)
    {
        if (renderersHidden == hidden)
            return;

        foreach (var pathRenderer in pathRenderers)
            pathRenderer.forceRenderingOff = hidden;

        renderersHidden = hidden;
    }
}
