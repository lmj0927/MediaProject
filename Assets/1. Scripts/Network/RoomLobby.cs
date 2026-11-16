using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public struct RoomSeat : INetworkStruct
{
    public PlayerRef Player;
    public NetworkString<_32> Nickname;
    public int Character;
    public NetworkBool Ready;
}

public sealed class RoomLobby : NetworkBehaviour
{
    public const int CharacterCount = 5;
    public const int Unselected = -1;

    [SerializeField] private NetworkObject[] _characterPrefabs;
    [SerializeField] private InputActionAsset _inputActions;
    [SerializeField] private Transform[] _practiceSpawns;
    [SerializeField] private BoxCollider _practiceFloor;
    [SerializeField] private BoxCollider[] _practiceWalls;
    [SerializeField] private HoldableProp _practiceBall;

    private readonly Player[] _practiceAvatars = new Player[RoomSession.MaxPlayers];
    private readonly int[] _practiceCharacters = new int[RoomSession.MaxPlayers];
    private Rigidbody _ballBody;
    private Pose _ballHome;
    private Bounds _practiceBounds;

    [Networked, Capacity(RoomSession.MaxPlayers)]
    public NetworkArray<RoomSeat> Seats => default;

    public bool IsAvailable => Object != null && Object.IsValid;
    public NetworkObject GetCharacterPrefab(int character) => _characterPrefabs[character];

    public override void Spawned()
    {
        if (RoomSession.Active == null)
            return;

        RoomSession.Active.StartPracticeInput(this, _inputActions);
        _ballBody = _practiceBall.GetComponent<Rigidbody>();
        if (!Object.HasStateAuthority)
        {
            _ballBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _ballBody.isKinematic = true;
            _ballBody.useGravity = false;
            return;
        }

        var floor = _practiceFloor.bounds;
        _practiceBounds.SetMinMax(
            new Vector3(floor.min.x - 0.5f, floor.max.y - 2f, floor.min.z - 0.5f),
            new Vector3(floor.max.x + 0.5f, floor.max.y + 10f, floor.max.z + 0.5f));
        _ballHome = new Pose(_ballBody.position, _ballBody.rotation);
        Runner.AddGlobal(gameObject.AddComponent<HostPhysicsStepper>());
    }

    public override void Despawned(NetworkRunner runner, bool hasState) =>
        RoomSession.Active?.StopPracticeInput(this);


    public RoomSeat GetSeat(PlayerRef player)
    {
        int index = FindSeat(player);
        return index < 0 ? new RoomSeat { Character = Unselected } : Seats[index];
    }

    public bool CanStart()
    {
        if (!IsAvailable || !Object.HasStateAuthority || RoomSession.Active.StartingGame)
            return false;

        int count = 0;
        foreach (var player in Runner.ActivePlayers)
        {
            if (!GetSeat(player).Ready)
                return false;
            count++;
        }

        return count > 0;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || RoomSession.Active == null || RoomSession.Active.StartingGame)
            return;

        for (int i = 0; i < Seats.Length; i++)
        {
            var seat = Seats[i];
            if (!seat.Player.IsRealPlayer)
                continue;

            if (!Runner.IsPlayerValid(seat.Player))
            {
                DespawnPracticeAvatar(i);
                Seats.Set(i, default);
            }
            else if (seat.Character != Unselected)
            {
                UpdatePracticeAvatar(i, seat);
            }
        }

        if (!_practiceBall.IsHeld && !IsInPracticeArea(_ballBody.position))
        {
            _ballBody.position = _ballHome.position;
            _ballBody.rotation = _ballHome.rotation;
            _ballBody.linearVelocity = Vector3.zero;
            _ballBody.angularVelocity = Vector3.zero;
        }

        foreach (var player in Runner.ActivePlayers)
        {
            if (FindSeat(player) >= 0)
                continue;

            string nickname = player == Runner.LocalPlayer
                ? RoomSession.Active.Nickname
                : RoomSession.ReadNickname(Runner.GetPlayerConnectionToken(player));

            for (int i = 0; i < Seats.Length; i++)
            {
                if (Seats[i].Player.IsRealPlayer)
                    continue;

                Seats.Set(i, new RoomSeat
                {
                    Player = player,
                    Character = Unselected,
                    Nickname = nickname ?? "Player"
                });
                break;
            }
        }
    }

    private void UpdatePracticeAvatar(int index, RoomSeat seat)
    {
        var avatar = _practiceAvatars[index];
        var spawn = _practiceSpawns[index];
        if (avatar != null && _practiceCharacters[index] == seat.Character)
        {
            if (!IsInPracticeArea(avatar.transform.position))
                avatar.RespawnAt(spawn.position);
            return;
        }

        Vector3 position = avatar != null && IsInPracticeArea(avatar.transform.position)
            ? avatar.transform.position
            : spawn.position;
        Quaternion rotation = avatar != null ? avatar.transform.rotation : spawn.rotation;
        DespawnPracticeAvatar(index);
        var networkAvatar = Runner.Spawn(GetCharacterPrefab(seat.Character), position, rotation, seat.Player);
        _practiceAvatars[index] = networkAvatar.GetComponent<Player>();
        _practiceCharacters[index] = seat.Character;
    }

    private bool IsInPracticeArea(Vector3 position)
    {
        if (!_practiceBounds.Contains(position))
            return false;

        // The slanted corners leave space outside the walls but inside the floor's bounds.
        var center = _practiceBounds.center;
        center.y = position.y = _practiceBounds.min.y + 2.5f;
        var offset = position - center;
        var ray = new Ray(center, offset);
        foreach (var wall in _practiceWalls)
            if (wall.Raycast(ray, out _, offset.magnitude))
                return false;
        return true;
    }

    private void DespawnPracticeAvatar(int index)
    {
        var avatar = _practiceAvatars[index];
        if (avatar == null)
            return;

        avatar.ReleaseHoldRelationships();
        Runner.Despawn(avatar.Object);
        _practiceAvatars[index] = null;
    }

    internal void DespawnPracticeAvatars()
    {
        for (int i = 0; i < _practiceAvatars.Length; i++)
            DespawnPracticeAvatar(i);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_Select(int character, RpcInfo info = default)
    {
        if (RoomSession.Active.StartingGame || character < 0 || character >= CharacterCount)
            return;

        int index = FindSeat(info.Source);
        if (index < 0 || Seats[index].Ready)
            return;

        for (int i = 0; i < Seats.Length; i++)
            if (i != index && Seats[i].Player.IsRealPlayer && Seats[i].Character == character &&
                Runner.IsPlayerValid(Seats[i].Player))
                return;

        var seat = Seats[index];
        seat.Character = character;
        Seats.Set(index, seat);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
    public void RPC_SetReady(bool ready, RpcInfo info = default)
    {
        if (RoomSession.Active.StartingGame)
            return;

        int index = FindSeat(info.Source);
        if (index < 0 || Seats[index].Character == Unselected)
            return;

        var seat = Seats[index];
        seat.Ready = ready;
        Seats.Set(index, seat);
    }

    public void StartIfReady()
    {
        if (CanStart())
            RoomSession.Active.BeginGame(this);
    }

    private int FindSeat(PlayerRef player)
    {
        if (player.IsRealPlayer)
            for (int i = 0; i < Seats.Length; i++)
                if (Seats[i].Player == player)
                    return i;

        return -1;
    }
}
