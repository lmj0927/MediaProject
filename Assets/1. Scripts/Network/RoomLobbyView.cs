using UnityEngine;

public sealed class RoomLobbyView : MonoBehaviour
{
    [SerializeField] private RoomLobby _lobby;
    [SerializeField] private UnityEngine.UI.Text _code;
    [SerializeField] private UnityEngine.UI.Text _copyText;
    [SerializeField] private UnityEngine.UI.Text _roomStatus;
    [SerializeField] private UnityEngine.UI.Text _hint;
    [SerializeField] private UnityEngine.UI.Text _readyText;
    [SerializeField] private UnityEngine.UI.Button _ready;
    [SerializeField] private UnityEngine.UI.Button _start;
    [SerializeField] private UnityEngine.UI.Button _copy;
    [SerializeField] private UnityEngine.UI.Button[] _choices;
    [SerializeField] private UnityEngine.UI.Text[] _names;
    [SerializeField] private UnityEngine.UI.Text[] _states;
    [SerializeField] private GameObject[] _highlights;

    private readonly RoomSeat[] _byCharacter = new RoomSeat[RoomLobby.CharacterCount];
    private float _copiedUntil;

    private void Update()
    {
        bool connected = _lobby.IsAvailable && RoomSession.Active != null;
        _copy.interactable = connected;
        _start.gameObject.SetActive(connected && _lobby.Object.HasStateAuthority);
        if (!connected)
        {
            _ready.interactable = false;
            foreach (var choice in _choices)
                choice.interactable = false;
            return;
        }

        var runner = _lobby.Runner;
        var local = _lobby.GetSeat(runner.LocalPlayer);
        bool starting = RoomSession.Active.StartingGame;
        _code.text = runner.SessionInfo.Name;
        _copyText.text = Time.unscaledTime < _copiedUntil ? "복사됨" : "복사";

        System.Array.Clear(_byCharacter, 0, _byCharacter.Length);
        int joined = 0;
        int ready = 0;
        foreach (var seat in _lobby.Seats)
        {
            if (!seat.Player.IsRealPlayer)
                continue;

            joined++;
            if (seat.Ready)
                ready++;
            if (seat.Character != RoomLobby.Unselected)
                _byCharacter[seat.Character] = seat;
        }

        for (int i = 0; i < _choices.Length; i++)
        {
            var seat = _byCharacter[i];
            bool taken = seat.Player.IsRealPlayer;
            bool mine = taken && seat.Player == runner.LocalPlayer;
            _choices[i].interactable = !starting && local.Player.IsRealPlayer && !local.Ready && !taken;
            _highlights[i].SetActive(mine);
            _names[i].text = taken ? seat.Nickname.ToString() : "";
            _states[i].text = taken ? (seat.Ready ? "준비 완료" : mine ? "내 선택" : "선택 중") : "";
        }

        _roomStatus.text = $"{joined} / {runner.SessionInfo.MaxPlayers}명   ·   준비 {ready}명";
        _ready.interactable = !starting && local.Character != RoomLobby.Unselected;
        _readyText.text = local.Ready ? "준비 취소" : "준비";
        _start.interactable = _lobby.CanStart();
        _hint.text = starting ? "게임을 불러오는 중..." : local.Character == RoomLobby.Unselected
            ? "캐릭터를 선택해 주세요."
            : ready == joined ? "전원 준비 완료" : "준비가 되면 아래 버튼을 눌러 주세요.";
    }

    public void SelectCharacter(int character)
    {
        if (_lobby.IsAvailable)
            _lobby.RPC_Select(character);
    }

    public void ToggleReady()
    {
        if (_lobby.IsAvailable)
            _lobby.RPC_SetReady(!_lobby.GetSeat(_lobby.Runner.LocalPlayer).Ready);
    }

    public void CopyCode()
    {
        if (!_lobby.IsAvailable)
            return;

        GUIUtility.systemCopyBuffer = _lobby.Runner.SessionInfo.Name;
        _copiedUntil = Time.unscaledTime + 1.5f;
    }

    public void StartGame() => _lobby.StartIfReady();
    public void Leave() => RoomSession.Active?.Leave();
}
