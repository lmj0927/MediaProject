// Owned by MinJun Lee
using UnityEngine;

/// <summary>
/// Rotates transform to face the camera.
/// </summary>
public class LookAtCamera : MonoBehaviour
{
    private enum Mode
    {
        LookAt,
        LookAtInverted,
        CameraForward,
        CameraForwardInverted,
    }

    [SerializeField] Mode mode = Mode.LookAtInverted; // facing mode

    private void LateUpdate()
    {
        switch (mode)
        {
            case Mode.LookAt:
                // face camera directly
                transform.LookAt(Camera.main.transform);
                break;
            case Mode.LookAtInverted:
                // face camera but keep readable (billboard flip)
                Vector3 dirFromCamara = transform.position - Camera.main.transform.position;
                transform.LookAt(transform.position + dirFromCamara);
                break;
            case Mode.CameraForward:
                // align to camera forward
                transform.forward = Camera.main.transform.forward;
                break;
            case Mode.CameraForwardInverted:
                // align opposite to camera forward
                transform.forward = -Camera.main.transform.forward;
                break;
        }
    }
}