using System;
using System.Collections.Generic;
using System.IO;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// 내려찍기 타이밍 확인용 테스트 도구.
/// 로컬 플레이어 입력을 대신 넣어 점프 → 내려찍기를 하고, 매 프레임 높이·상태·애니 상태를 CSV로 남김.
/// 실제 입력 경로(OnInput → Player)를 그대로 타므로 게임 화면 포커스와 상관없이 같은 결과가 나옴.
/// </summary>
public class StampProbe : MonoBehaviour, INetworkRunnerCallbacks
{
    private static readonly string[] StateNames =
        { "Idle", "Running", "Jump_Start", "Jump_Idle", "Jump_Land", "Stamp", "Holding", "HoldRun", "Throw" };

    private const System.Reflection.BindingFlags Hidden =
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    private NetworkRunner runner;
    private Player player;
    private Animator animator;
    private NetworkCharacterController cc;
    private float startTime;
    private float stampDelay;
    private float duration;
    private Vector3 walk;
    private string fileName;
    private bool driving;
    private readonly List<string> rows = new();

    public bool Running { get; private set; }
    public string LastFile { get; private set; }

    /// <summary>
    /// 점프 후 stampDelay초에 내려찍기 입력. walk 방향은 처음부터 끝까지 누르고 있음(착지 후 이동이 막히는지 확인용).
    /// </summary>
    public void Begin(NetworkRunner targetRunner, Player target, float delay, Vector3 walkDirection, float recordSeconds, string name)
    {
        Begin(targetRunner, target, delay, walkDirection, recordSeconds, name, true);
    }

    /// <summary>driveInput가 false면 입력은 넣지 않고 기록만 함 (더미 확인용).</summary>
    public void Begin(NetworkRunner targetRunner, Player target, float delay, Vector3 walkDirection, float recordSeconds, string name, bool driveInput)
    {
        runner = targetRunner;
        player = target;
        animator = target.GetComponentInChildren<Animator>();
        cc = target.GetComponent<NetworkCharacterController>();
        stampDelay = delay;
        walk = walkDirection;
        duration = recordSeconds;
        fileName = name;
        rows.Clear();
        rows.Add("t,y,vy,speed,phase,stamping,recovering,grounded,state,norm,next");
        startTime = Time.time;
        phase = 0;
        Running = true;
        driving = driveInput;
        if (driving) runner.AddCallbacks(this);
    }

    private void Update()
    {
        if (!Running || player == null) return;

        float t = Time.time - startTime;
        var info = animator.GetCurrentAnimatorStateInfo(0);
        string next = animator.IsInTransition(0) ? StateName(animator.GetNextAnimatorStateInfo(0)) : "";
        Vector3 v = cc.Velocity;
        rows.Add(string.Join(",",
            t.ToString("F3"), player.transform.position.y.ToString("F3"), v.y.ToString("F2"),
            new Vector2(v.x, v.z).magnitude.ToString("F2"), phase.ToString(),
            Flag("IsStamping"), Flag("IsStampRecovering"), Flag("GroundedNet"),
            StateName(info), info.normalizedTime.ToString("F2"), next));

        if (t < duration) return;

        Running = false;
        if (driving) runner.RemoveCallbacks(this);
        Directory.CreateDirectory("Temp/StampProbe");
        LastFile = $"Temp/StampProbe/{fileName}.csv";
        File.WriteAllLines(LastFile, rows);
    }

    private int Flag(string property)
    {
        var value = typeof(Player).GetProperty(property, Hidden)?.GetValue(player);
        return value is NetworkBool b && b ? 1 : 0;
    }

    private static string StateName(AnimatorStateInfo info)
    {
        foreach (var n in StateNames)
            if (info.IsName(n)) return n;
        return "?";
    }

    // 입력 단계: 0 점프 누름(뜰 때까지) → 1 뜬 뒤 stampDelay 기다림 → 2 내려찍기 누름(시작될 때까지) → 3 손 뗌
    private int phase;
    private float airborneTime;

    public void OnInput(NetworkRunner networkRunner, NetworkInput input)
    {
        if (!Running) return;

        float now = (float)networkRunner.SimulationTime;
        bool grounded = Flag("GroundedNet") == 1;
        var data = new NetworkInputData();

        switch (phase)
        {
            case 0:
                if (!grounded) { phase = 1; airborneTime = now; }
                else data.buttons.Set(PlayerInputButton.Jump, (networkRunner.Tick % 2) == 0);
                break;
            case 1:
                if (now - airborneTime >= stampDelay) phase = 2;
                break;
            case 2:
                if (Flag("IsStamping") == 1) phase = 3;
                else data.buttons.Set(PlayerInputButton.Stamp, (networkRunner.Tick % 2) == 0);
                break;
        }

        // 걷기 입력은 내려찍기를 누른 뒤부터 넣음. 착지 자세 동안 이동이 막히는지 확인용
        if (phase >= 2) data.direction = walk;
        input.Set(data);
    }

    public void OnInputMissing(NetworkRunner networkRunner, PlayerRef playerRef, NetworkInput input) { }
    public void OnObjectExitAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef playerRef) { }
    public void OnObjectEnterAOI(NetworkRunner networkRunner, NetworkObject obj, PlayerRef playerRef) { }
    public void OnPlayerJoined(NetworkRunner networkRunner, PlayerRef playerRef) { }
    public void OnPlayerLeft(NetworkRunner networkRunner, PlayerRef playerRef) { }
    public void OnShutdown(NetworkRunner networkRunner, ShutdownReason shutdownReason) { }
    public void OnDisconnectedFromServer(NetworkRunner networkRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner networkRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner networkRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnSessionListUpdated(NetworkRunner networkRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner networkRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner networkRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner networkRunner, PlayerRef playerRef, ReliableKey key, ReadOnlySpan<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner networkRunner, PlayerRef playerRef, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner networkRunner) { }
    public void OnSceneLoadStart(NetworkRunner networkRunner) { }
    public void OnConnectedToServer(NetworkRunner networkRunner) { }
}
