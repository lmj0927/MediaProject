using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>Tracks safe positions and recovers the Transport and its crew on the Host tick.</summary>
internal sealed class TransportRecovery
{
    private const float PlayerFallDistance = 2.5f;
    private const float WaterContactTolerance = .05f;
    private static readonly Vector3 CheckpointHalfSize = new Vector3(24f, 4f, 8f);
    private static readonly Vector3[] SpawnOffsets = {
        Vector3.zero, new Vector3(-1.4f, 0f, -.8f), new Vector3(1.4f, 0f, -.8f), new Vector3(0f, 0f, 1.6f)
    };

    private readonly PlatformTilt _deck;
    private readonly SphereDriver _wheel;
    private readonly Rigidbody _wheelBody;
    private readonly SphereCollider _wheelCollider;
    private readonly Transform _waterSurface;
    private readonly Transform _spawnPoint;
    private readonly Transform[] _checkpoints;
    private readonly Dictionary<PlayerRef, NetworkObject> _avatars;
    private Vector3 _recoveryPosition;
    private int _latestCheckpoint = -1;

    public TransportRecovery(PlatformTilt deck, Transform waterSurface, Transform spawnPoint,
        Transform[] checkpoints, Dictionary<PlayerRef, NetworkObject> avatars)
    {
        _deck = deck;
        _wheel = deck.Sphere.GetComponent<SphereDriver>();
        _wheelBody = _wheel.GetComponent<Rigidbody>();
        _wheelCollider = _wheel.GetComponent<SphereCollider>();
        _waterSurface = waterSurface;
        _spawnPoint = spawnPoint;
        _checkpoints = checkpoints;
        _avatars = avatars;
        _recoveryPosition = _wheel.transform.position;
    }

    public Vector3 GetSpawnPosition(NetworkObject returning = null)
    {
        foreach (var offset in SpawnOffsets)
        {
            Vector3 candidate = _spawnPoint.TransformPoint(offset);
            bool occupied = false;
            foreach (var avatar in _avatars.Values)
            {
                if (avatar == null || avatar == returning) continue;
                Vector3 separation = avatar.transform.position - candidate;
                if (Mathf.Abs(separation.y) > 2f) continue;
                separation.y = 0f;
                if (separation.sqrMagnitude >= 1.21f) continue;
                occupied = true;
                break;
            }
            if (!occupied) return candidate;
        }
        return _spawnPoint.position;
    }

    public Vector3 GetGroupSpawnPosition(int index, int count)
    {
        if (count <= 1) return _spawnPoint.position;

        float angle = index * (2f * Mathf.PI / count);
        return _spawnPoint.TransformPoint(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.25f);
    }

    public void Step()
    {
        if (_wheel == null || _wheel.Object == null || !_wheel.Object.IsValid || !_wheel.Object.HasStateAuthority ||
            _deck == null || _deck.Object == null || !_deck.Object.IsValid || !_deck.Object.HasStateAuthority)
            return;

        Vector3 center = _wheelBody.position + _wheelBody.rotation * Vector3.Scale(_wheelCollider.center, _wheelCollider.transform.lossyScale);
        Vector3 contact = center - Vector3.up * _deck.SphereRadius;
        bool resetTransport = contact.y <= _waterSurface.position.y + WaterContactTolerance;
        if (resetTransport)
        {
            _wheel.RespawnAt(_recoveryPosition);
            _deck.ResetForRespawn(_recoveryPosition);
        }
        else if (_wheel.IsGrounded && _checkpoints != null)
        {
            for (int i = _checkpoints.Length - 1; i > _latestCheckpoint; i--)
            {
                Transform checkpoint = _checkpoints[i];
                if (checkpoint == null || !new Bounds(checkpoint.position, CheckpointHalfSize * 2f).Contains(contact)) continue;
                _latestCheckpoint = i;
                _recoveryPosition = checkpoint.position + Vector3.up * (_deck.SphereRadius + .15f);
                break;
            }
        }

        float fallHeight = _spawnPoint.position.y - PlayerFallDistance;
        int slot = 0;
        foreach (var avatar in _avatars.Values)
        {
            if (avatar == null || !avatar.IsValid || (!resetTransport && avatar.transform.position.y >= fallHeight)) continue;
            if (avatar.TryGetComponent<Player>(out var player))
                player.RespawnAt(resetTransport ? GetGroupSpawnPosition(slot++, _avatars.Count) : GetSpawnPosition(avatar));
        }
    }
}
