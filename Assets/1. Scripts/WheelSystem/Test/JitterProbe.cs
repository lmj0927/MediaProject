using System.Globalization;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;

/// <summary>
/// 테스트용. 로컬 플레이어의 떨림을 기록함. 측정이 끝나면 지워도 됨.
///
/// 렌더(매 프레임, 모든 LateUpdate 뒤): 판 렌더 포즈 기준 플레이어 위치, 화면상 위치, 카메라 위치.
///   판에 가만히 서 있는데 판 기준 위치나 화면상 위치가 프레임마다 흔들리면 시각 떨림임.
/// 이 오브젝트에 붙이면 바로 기록을 시작하고, 비활성화되면 Logs/BalanceTest에 저장함.
/// </summary>
[DefaultExecutionOrder(10000)]
public class JitterProbe : MonoBehaviour
{
    [Tooltip("파일 이름.")]
    public string fileName = "jitter";

    private Player player;
    private PlatformTilt platform;
    private Rigidbody platformBody;
    private Camera cam;
    private NetworkRunner runner;

    private readonly StringBuilder render = new();

    private void OnEnable()
    {
        render.Clear();
        render.AppendLine("t,dt,tick,alpha,localX,localY,localZ,screenX,screenY,deckScreenX,deckScreenY,camX,camY,camZ");
    }

    private bool Resolve()
    {
        if (runner == null) runner = FindFirstObjectByType<NetworkRunner>();
        if (platform == null)
        {
            platform = FindFirstObjectByType<PlatformTilt>();
            if (platform != null) platformBody = platform.GetComponent<Rigidbody>();
        }
        if (cam == null) cam = Camera.main;
        if (player == null)
        {
            foreach (var p in FindObjectsByType<Player>(FindObjectsSortMode.None))
                if (p.Object != null && p.Object.IsValid && p.Object.HasInputAuthority) player = p;
        }
        return runner != null && platform != null && platformBody != null && player != null && cam != null;
    }

    private void LateUpdate()
    {
        if (!Resolve()) return;

        platform.GetRenderPose(out Vector3 deckPos, out Quaternion deckRot);
        Vector3 p = player.transform.position;
        Vector3 local = Quaternion.Inverse(deckRot) * (p - deckPos);
        Vector3 sp = cam.WorldToViewportPoint(p);
        Vector3 dp = cam.WorldToViewportPoint(deckPos);
        Vector3 cp = cam.transform.position;

        var c = CultureInfo.InvariantCulture;
        render.Append(Time.timeAsDouble.ToString("F4", c)).Append(',')
              .Append(Time.deltaTime.ToString("F4", c)).Append(',')
              .Append(runner.Tick.Raw).Append(',')
              .Append(runner.LocalAlpha.ToString("F3", c)).Append(',')
              .Append(local.x.ToString("F4", c)).Append(',').Append(local.y.ToString("F4", c)).Append(',').Append(local.z.ToString("F4", c)).Append(',')
              .Append(sp.x.ToString("F5", c)).Append(',').Append(sp.y.ToString("F5", c)).Append(',')
              .Append(dp.x.ToString("F5", c)).Append(',').Append(dp.y.ToString("F5", c)).Append(',')
              .Append(cp.x.ToString("F3", c)).Append(',').Append(cp.y.ToString("F3", c)).Append(',').Append(cp.z.ToString("F3", c)).AppendLine();
    }

    private void OnDisable() => Save();

    /// <summary>기록을 저장하고 경로를 돌려줌.</summary>
    public string Save()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "BalanceTest"));
        Directory.CreateDirectory(dir);
        string renderPath = Path.Combine(dir, fileName + "_render.csv");
        File.WriteAllText(renderPath, render.ToString());
        return renderPath;
    }
}
