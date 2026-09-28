using UnityEngine;

/// <summary>
/// Host 렌더 보간: 직전·현재 틱의 루트 포즈를 보관하고 Runner.LocalAlpha로 시각 자식만 옮긴다.
/// 물리 루트(Rigidbody/콜라이더)는 틱 포즈에 그대로 둔다 — Render에서 루트를 움직이면 다음 Simulate가 그 값을 텔레포트로 읽는다.
/// 시각 자식에는 콜라이더를 두지 않는다.
/// </summary>
public sealed class TickVisualInterpolator
{
    private readonly Transform _root;
    private readonly Transform _visual;
    private readonly Vector3 _restLocalPosition;
    private readonly Quaternion _restLocalRotation;

    private Vector3 _previousPosition;
    private Vector3 _currentPosition;
    private Quaternion _previousRotation = Quaternion.identity;
    private Quaternion _currentRotation = Quaternion.identity;
    private bool _hasPose;
    private bool _visualDirty;

    public TickVisualInterpolator(Transform root, Transform visual)
    {
        _root = root;
        _visual = visual != root ? visual : null;
        if (_visual != null)
        {
            _restLocalPosition = _visual.localPosition;
            _restLocalRotation = _visual.localRotation;
        }
    }

    public bool IsValid => _visual != null && _root != null;

    public bool HasPose => _hasPose;

    /// <summary>틱 종료 포즈 기록. snapDistance 이상 점프하면 보간하지 않고 끊는다.</summary>
    public void Capture(Vector3 position, Quaternion rotation, float snapDistance)
    {
        if (!_hasPose || (position - _currentPosition).sqrMagnitude > snapDistance * snapDistance)
        {
            ResetTo(position, rotation);
            return;
        }

        _previousPosition = _currentPosition;
        _previousRotation = _currentRotation;
        _currentPosition = position;
        _currentRotation = rotation;
    }

    public void ResetTo(Vector3 position, Quaternion rotation)
    {
        _previousPosition = position;
        _currentPosition = position;
        _previousRotation = rotation;
        _currentRotation = rotation;
        _hasPose = true;
    }

    public void Clear() => _hasPose = false;

    public void Evaluate(float alpha, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.Lerp(_previousPosition, _currentPosition, alpha);
        rotation = Quaternion.Slerp(_previousRotation, _currentRotation, alpha);
    }

    /// <summary>보간된 루트 포즈에 시각 자식의 원래 로컬 오프셋을 얹어 배치.</summary>
    public void Apply(float alpha)
    {
        if (!IsValid)
            return;

        if (!_hasPose)
        {
            RestoreLocal();
            return;
        }

        Evaluate(alpha, out var position, out var rotation);
        Vector3 offset = Vector3.Scale(_root.lossyScale, _restLocalPosition);
        _visual.SetPositionAndRotation(position + rotation * offset, rotation * _restLocalRotation);
        _visualDirty = true;
    }

    /// <summary>시각 자식을 루트 기준 원래 로컬 포즈로 되돌림 (Client / held 등).</summary>
    public void RestoreLocal()
    {
        if (!_visualDirty || _visual == null)
            return;

        _visual.localPosition = _restLocalPosition;
        _visual.localRotation = _restLocalRotation;
        _visualDirty = false;
    }
}
