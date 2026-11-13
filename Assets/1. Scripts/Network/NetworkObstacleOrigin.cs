using UnityEngine;

/// <summary>Preserves the scene pose before the asset's local animation starts.</summary>
[DisallowMultipleComponent]
public sealed class NetworkObstacleOrigin : MonoBehaviour
{
    public Pose InitialPose { get; private set; }

    private void Awake()
    {
        InitialPose = new Pose(transform.position, transform.rotation);
    }
}
