using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

/// <summary>
/// Fusion Host/Client 세션 시작, 플레이어 스폰, 로컬 입력 수집.
/// </summary>
public class BasicSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef _playerPrefab;
    [SerializeField] private InputActionAsset _inputActions;

    private NetworkRunner _runner;
    private InputAction _moveAction;
    private InputAction _jumpAction;
    private InputAction _holdAction;
    private InputAction _throwAction;
    private InputAction _stampAction;

    /// <summary>접속 중인 플레이어별 스폰된 아바타.</summary>
    private readonly Dictionary<PlayerRef, NetworkObject> _spawnedCharacters = new Dictionary<PlayerRef, NetworkObject>();

    private void OnEnable()
    {
        BindInputActions();
    }

    private void OnDisable()
    {
        if (_inputActions == null)
            return;

        var playerMap = _inputActions.FindActionMap("Player");
        playerMap?.Disable();
    }

    /// <summary>Player 액션 맵에서 Move/Jump/Hold/Throw/Stamp를 찾아 활성화한다.</summary>
    private void BindInputActions()
    {
        if (_inputActions == null)
        {
            Debug.LogError("BasicSpawner: InputActionAsset is not assigned.");
            return;
        }

        var playerMap = _inputActions.FindActionMap("Player");
        if (playerMap == null)
        {
            Debug.LogError("BasicSpawner: 'Player' action map not found.");
            return;
        }

        _moveAction = playerMap.FindAction("Move", throwIfNotFound: true);
        _jumpAction = playerMap.FindAction("Jump", throwIfNotFound: true);
        _holdAction = playerMap.FindAction("Hold", throwIfNotFound: true);
        _throwAction = playerMap.FindAction("Throw", throwIfNotFound: true);
        _stampAction = playerMap.FindAction("Stamp", throwIfNotFound: true);
        playerMap.Enable();
    }

    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    /// <summary>
    /// 매 틱 로컬 입력을 NetworkInputData로 채워 Runner에 전달한다.
    /// 시뮬/권한 처리는 Player.FixedUpdateNetwork에서 한다.
    /// </summary>
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetworkInputData();

        if (_moveAction != null)
        {
            var move = _moveAction.ReadValue<Vector2>();
            data.direction = new Vector3(move.x, 0f, move.y);
        }

        if (_jumpAction != null)
            data.buttons.Set(PlayerInputButton.Jump, _jumpAction.IsPressed());

        if (_holdAction != null)
            data.buttons.Set(PlayerInputButton.Hold, _holdAction.IsPressed());

        if (_throwAction != null)
            data.buttons.Set(PlayerInputButton.Throw, _throwAction.IsPressed());

        if (_stampAction != null)
            data.buttons.Set(PlayerInputButton.Stamp, _stampAction.IsPressed());

        input.Set(data);
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    /// <summary>Host만 플레이어 프리팹을 Spawn하고 Input Authority를 부여한다.</summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            Vector3 spawnPosition = new Vector3(0, 1, (player.RawEncoded % runner.Config.Simulation.PlayerCount) * 3);
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, spawnPosition, Quaternion.identity, player);
            _spawnedCharacters.Add(player, networkPlayerObject);
        }
    }

    /// <summary>퇴장한 플레이어 아바타를 Despawn한다.</summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedCharacters.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedCharacters.Remove(player);
        }
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }

    /// <summary>NetworkRunner를 붙이고 Host 또는 Client로 세션을 시작한다.</summary>
    async void StartGame(GameMode mode)
    {
        _runner = gameObject.AddComponent<NetworkRunner>();
        _runner.ProvideInput = true;

        var scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
        var sceneInfo = new NetworkSceneInfo();
        if (scene.IsValid)
            sceneInfo.AddSceneRef(scene, LoadSceneMode.Additive);

        await _runner.StartGame(new StartGameArgs()
        {
            GameMode = mode,
            SessionName = "TestRoom",
            Scene = scene,
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });
    }

    private void OnGUI()
    {
        if (_runner == null)
        {
            if (GUI.Button(new Rect(0, 0, 200, 40), "Host"))
                StartGame(GameMode.Host);

            if (GUI.Button(new Rect(0, 40, 200, 40), "Join"))
                StartGame(GameMode.Client);
        }
    }
}
