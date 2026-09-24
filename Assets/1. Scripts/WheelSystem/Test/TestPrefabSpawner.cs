using System.Collections.Generic;
using Fusion;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 테스트용. 지정한 프리팹을 판 중앙 위에 생성함.
///
/// 네트워크 오브젝트(NetworkObject가 붙은 프리팹)는 러너를 통해 생성해야 함.
/// Instantiate로 만들면 네트워크에 등록되지 않아 동작하지 않음.
/// 그래서 프리팹 종류를 보고 알아서 방식을 고름.
///   - NetworkObject 있음: 호스트에서 runner.Spawn. 세션 시작 전이면 생성하지 않음.
///   - NetworkObject 없음: 일반 Instantiate. 세션과 무관하게 생성됨.
///
/// 조작
///   항목별 지정 키: 해당 프리팹 생성
///   정리 키(기본 Backspace): 이 스크립트로 만든 것을 모두 제거
///   인스펙터 우클릭 메뉴에서도 실행 가능
/// </summary>
public class TestPrefabSpawner : MonoBehaviour
{
    [System.Serializable]
    public struct Entry
    {
        [Tooltip("생성할 프리팹.")]
        public GameObject prefab;

#if ENABLE_INPUT_SYSTEM
        [Tooltip("생성 키.")]
        public Key key;
#else
        [Tooltip("생성 키.")]
        public KeyCode key;
#endif
    }

    [Header("Target")]
    [Tooltip("생성 기준이 되는 판. 비우면 씬에서 찾음.")]
    [SerializeField] private PlatformTilt platform;

    [Header("Prefabs")]
    [SerializeField] private Entry[] entries;

    [Header("Placement")]
    [Tooltip("데크 위로 이 높이에서 생성함. 떨어뜨려서 착지시키는 편이 겹침 문제가 적음.")]
    [SerializeField] private float spawnHeight = 2f;

    [Tooltip("중앙에서 수평으로 흩뿌리는 반경. 연속 생성 시 같은 자리에 겹치지 않게 함.")]
    [SerializeField] private float scatterRadius = 0.4f;

    [Header("Cleanup")]
#if ENABLE_INPUT_SYSTEM
    [SerializeField] private Key clearKey = Key.Backspace;
#else
    [SerializeField] private KeyCode clearKey = KeyCode.Backspace;
#endif

    private readonly List<GameObject> spawnedLocal = new();
    private readonly List<NetworkObject> spawnedNetwork = new();

    private void Update()
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (WasPressed(entries[i].key)) Spawn(i);
            }
        }

        if (WasPressed(clearKey)) ClearAll();
    }

    /// <summary>지정한 항목의 프리팹을 판 중앙 위에 생성함.</summary>
    public void Spawn(int index)
    {
        if (entries == null || index < 0 || index >= entries.Length) return;

        var prefab = entries[index].prefab;
        if (prefab == null)
        {
            Debug.LogWarning($"{name}: {index}번 항목에 프리팹이 없습니다.", this);
            return;
        }

        if (!TryGetSpawnPose(out Vector3 position, out Quaternion rotation)) return;

        var networkPrefab = prefab.GetComponent<NetworkObject>();
        if (networkPrefab != null)
        {
            var spawned = SpawnNetworked(networkPrefab, position, rotation);
            if (spawned != null) MatchWheelMotion(spawned.gameObject);
        }
        else
        {
            var spawned = Instantiate(prefab, position, rotation);
            spawnedLocal.Add(spawned);
            MatchWheelMotion(spawned);
        }
    }

    /// <summary>
    /// 생성 직후 휠의 이동 속도를 물려줌.
    /// 속도 0으로 생성하면 떨어지는 동안 휠이 먼저 가 버려, 빠를 때는 판 뒤로 떨어져 사라짐.
    /// </summary>
    private void MatchWheelMotion(GameObject spawned)
    {
        var rb = spawned.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic || platform == null) return;

        var rider = spawned.GetComponent<WheelRider>();
        if (rider != null)
        {
            // 트리거 감지는 다음 물리 스텝에야 오므로 직접 연결한 뒤 속도를 물려줌
            rider.AttachTo(platform);
            rider.InheritFrameVelocity(Vector3.zero);
        }
        else
        {
            Vector3 v = platform.FrameVelocity;
            v.y = 0f;
            rb.linearVelocity = v;
        }
    }

    private NetworkObject SpawnNetworked(NetworkObject prefab, Vector3 position, Quaternion rotation)
    {
        var runner = FindFirstObjectByType<NetworkRunner>();

        if (runner == null || !runner.IsRunning)
        {
            Debug.LogWarning($"{name}: 네트워크 프리팹은 세션 시작 후에만 생성할 수 있습니다. Host를 먼저 누르세요.", this);
            return null;
        }

        if (!runner.IsServer)
        {
            Debug.LogWarning($"{name}: 네트워크 오브젝트는 호스트만 생성할 수 있습니다.", this);
            return null;
        }

        var spawned = runner.Spawn(prefab, position, rotation);
        if (spawned != null) spawnedNetwork.Add(spawned);
        return spawned;
    }

    /// <summary>판 중앙 위의 생성 위치를 구함.</summary>
    private bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (platform == null) platform = FindFirstObjectByType<PlatformTilt>();
        if (platform == null)
        {
            Debug.LogWarning($"{name}: 씬에서 PlatformTilt를 찾지 못했습니다.", this);
            return false;
        }

        // 보간된 Transform이 아니라 실제 물리 위치를 기준으로 함
        var body = platform.GetComponent<Rigidbody>();
        Vector3 center = body != null ? body.position : platform.transform.position;

        Vector2 scatter = scatterRadius > 0f ? Random.insideUnitCircle * scatterRadius : Vector2.zero;
        position = center + new Vector3(scatter.x, spawnHeight, scatter.y);

        // 수평 방향만 무작위로 돌려 모두 같은 방향을 보지 않게 함
        rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        return true;
    }

    /// <summary>이 스크립트로 생성한 것을 모두 제거함.</summary>
    public void ClearAll()
    {
        foreach (var go in spawnedLocal)
        {
            if (go != null) Destroy(go);
        }
        spawnedLocal.Clear();

        var runner = FindFirstObjectByType<NetworkRunner>();
        foreach (var no in spawnedNetwork)
        {
            if (no == null) continue;
            if (runner != null && runner.IsRunning && runner.IsServer) runner.Despawn(no);
        }
        spawnedNetwork.Clear();
    }

    [ContextMenu("Spawn First Entry")]
    private void SpawnFirst() => Spawn(0);

    [ContextMenu("Clear All")]
    private void ClearAllFromMenu() => ClearAll();

#if ENABLE_INPUT_SYSTEM
    private static bool WasPressed(Key key)
    {
        var keyboard = Keyboard.current;
        return key != Key.None && keyboard != null && keyboard[key].wasPressedThisFrame;
    }
#else
    private static bool WasPressed(KeyCode key) => key != KeyCode.None && Input.GetKeyDown(key);
#endif

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var p = platform != null ? platform : FindFirstObjectByType<PlatformTilt>();
        if (p == null) return;

        Vector3 center = p.transform.position + Vector3.up * spawnHeight;
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0.1f, scatterRadius));
        Gizmos.DrawLine(p.transform.position, center);
    }
#endif
}