using Fusion;

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

    [UnityEngine.SerializeField] private NetworkObject[] _characterPrefabs;

    [Networked, Capacity(RoomSession.MaxPlayers)]
    public NetworkArray<RoomSeat> Seats => default;

    public bool IsAvailable => Object != null && Object.IsValid;
    public NetworkObject GetCharacterPrefab(int character) => _characterPrefabs[character];

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
            if (Seats[i].Player.IsRealPlayer && !Runner.IsPlayerValid(Seats[i].Player))
                Seats.Set(i, default);

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
