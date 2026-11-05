using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class RoomSession : MonoBehaviour, INetworkRunnerCallbacks
{
    public const int MaxPlayers = 4;
    public const int CodeLength = 6;
    public const int NicknameLength = 16;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
#if UNITY_EDITOR
    internal const string LobbySceneGuid = "04cd708f433b67147961eacafa6fbcfd";
    internal const string GameSceneGuid = "f7765f5ff92d14b4f9fbc900754091ee";
#endif

    private NetworkRunner _runner;
    private int _titleSceneIndex;
    private int _gameSceneIndex;
    private readonly Dictionary<PlayerRef, (int Character, NetworkObject Prefab)> _selections = new();
    private bool _connected;
    private bool _attachGameplay;
    private bool _returnToTitle;
    private static string _returnMessage;

    public static RoomSession Active { get; private set; }
    public bool StartingGame { get; private set; }
    public string Nickname { get; private set; }

    private void Awake()
    {
        Active = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    private void Update()
    {
        if (_returnToTitle)
        {
            _returnToTitle = false;
            SceneManager.LoadScene(_titleSceneIndex);
            Destroy(gameObject);
            return;
        }

        if (_attachGameplay)
        {
            _attachGameplay = false;
            FindFirstObjectByType<BasicSpawner>()?.AttachRoomRunner(_runner, this);
        }
    }

    public static bool IsValidCode(string code)
    {
        if (code.Length != CodeLength)
            return false;

        foreach (char character in code)
            if (CodeAlphabet.IndexOf(character) < 0)
                return false;

        return true;
    }

    public static string ReadNickname(byte[] token)
    {
        if (token == null || token.Length == 0 || token.Length > NicknameLength * 3)
            return null;

        string name = Encoding.UTF8.GetString(token).Trim();
        return name.Length > 0 && name.Length <= NicknameLength &&
            !Array.Exists(name.ToCharArray(), char.IsControl) ? name : null;
    }

    public static string TakeReturnMessage()
    {
        string message = _returnMessage;
        _returnMessage = null;
        return message;
    }

    public async Task<string> Connect(bool host, string code, int capacity, string nickname,
        int titleSceneIndex, int lobbySceneIndex, int gameSceneIndex)
    {
        Nickname = nickname;
        if (titleSceneIndex < 0 || lobbySceneIndex < 0 || gameSceneIndex < 0)
        {
            Destroy(gameObject);
            return "빌드 씬 목록에 타이틀, 로비와 게임 맵을 등록해 주세요.";
        }
        _titleSceneIndex = titleSceneIndex;
        _gameSceneIndex = gameSceneIndex;

        var settings = PhotonAppSettings.Global.AppSettings.GetCopy();
        if (string.IsNullOrEmpty(settings.FixedRegion))
            settings.FixedRegion = "kr";

        string error = "방 코드를 생성하지 못했습니다. 다시 시도해 주세요.";
        for (int attempt = 0; attempt < (host ? 3 : 1); attempt++)
        {
            var owner = new GameObject("Room Runner");
            DontDestroyOnLoad(owner);
            _runner = owner.AddComponent<NetworkRunner>();
            _runner.AddCallbacks(this);
            var sceneInfo = new NetworkSceneInfo();
            if (host)
                sceneInfo.AddSceneRef(SceneRef.FromIndex(lobbySceneIndex), LoadSceneMode.Single);

            try
            {
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = host ? GameMode.Host : GameMode.Client,
                    SessionName = host ? CreateCode() : code,
                    PlayerCount = host ? capacity : null,
                    IsOpen = true,
                    IsVisible = false,
                    EnableClientSessionCreation = false,
                    Scene = host ? sceneInfo : null,
                    SceneManager = owner.AddComponent<NetworkSceneManagerDefault>(),
                    ConnectionToken = Encoding.UTF8.GetBytes(nickname),
                    CustomPhotonAppSettings = settings
                });

                if (result.Ok && _runner != null && _runner.IsRunning)
                {
                    _connected = true;
                    return null;
                }

                error = result.ShutdownReason switch
                {
                    ShutdownReason.GameNotFound => "해당 코드의 방을 찾을 수 없습니다.",
                    ShutdownReason.GameIsFull => "방이 가득 찼습니다.",
                    ShutdownReason.GameClosed => "이미 게임이 시작된 방입니다.",
                    ShutdownReason.GameIdAlreadyExists => error,
                    _ => $"방에 연결하지 못했습니다. ({result.ShutdownReason})"
                };
                await ReleaseRunner(owner);
                if (result.ShutdownReason != ShutdownReason.GameIdAlreadyExists)
                    break;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                await ReleaseRunner(owner);
                error = "방에 연결하는 중 문제가 발생했습니다.";
                break;
            }
        }

        // A connection can fail after Fusion has already replaced the title scene.
        if (SceneManager.GetActiveScene().buildIndex != _titleSceneIndex)
        {
            _returnMessage = error;
            _returnToTitle = true;
        }
        else
        {
            Destroy(gameObject);
        }

        return error;
    }

    private async Task ReleaseRunner(GameObject owner)
    {
        if (_runner != null && !_runner.IsShutdown)
            await _runner.Shutdown(destroyGameObject: false);
        _runner = null;
        if (owner != null)
            Destroy(owner);
    }

    private static string CreateCode()
    {
        var code = new char[CodeLength];
        for (int i = 0; i < code.Length; i++)
            code[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(code);
    }

    public void BeginGame(RoomLobby lobby)
    {
        if (_runner != lobby.Runner || !_runner.IsServer || StartingGame)
            return;

        StartingGame = true;
        foreach (var player in _runner.ActivePlayers)
        {
            int character = lobby.GetSeat(player).Character;
            _selections.Add(player, (character, lobby.GetCharacterPrefab(character)));
        }
        _runner.SessionInfo.IsOpen = false;
        _runner.LoadScene(SceneRef.FromIndex(_gameSceneIndex), LoadSceneMode.Single);
    }

    public bool TryGetSelection(PlayerRef player, out (int Character, NetworkObject Prefab) selection) =>
        _selections.TryGetValue(player, out selection);

    public void Leave()
    {
        if (_runner != null && !_runner.IsShutdown)
            _ = _runner.Shutdown();
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // Attach on the next Update, outside Fusion's callback iteration.
        _attachGameplay = SceneManager.GetActiveScene().buildIndex == _gameSceneIndex;
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        if (StartingGame || ReadNickname(token) == null)
            request.Refuse();
        else
            request.Accept();
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason reason)
    {
        if (!_connected || _runner != runner)
            return;

        _returnMessage = reason == ShutdownReason.Ok ? null : $"방 연결이 종료되었습니다. ({reason})";
        _returnToTitle = true;
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress address, NetConnectFailedReason reason) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken token) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // A connection accepted just before the room closed may finish joining after the roster is locked.
        if (StartingGame && runner.IsServer && !_selections.ContainsKey(player))
            runner.Disconnect(player);
    }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessions) { }
}
