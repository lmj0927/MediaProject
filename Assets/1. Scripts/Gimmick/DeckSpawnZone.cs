using Fusion;
using UnityEngine;

/// <summary>
/// 탈것이 이 구역에 들어오면 판 위에 프리팹(폭탄, 몬스터 등)을 떨어뜨림. Host에서만 생성함.
/// 구역은 같은 오브젝트의 BoxCollider(트리거) 크기로 정함. 탈것이 구역을 벗어나면 다시 작동할 수 있음.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class DeckSpawnZone : MonoBehaviour
{
    [Tooltip("판 위에 생성할 네트워크 프리팹.")]
    [SerializeField] private NetworkObject prefab;

    [Tooltip("한 번에 생성할 개수.")]
    [Min(1)][SerializeField] private int count = 1;

    [Tooltip("여러 개일 때 생성 간격(초).")]
    [Min(0f)][SerializeField] private float interval = 0.6f;

    [Tooltip("판 위로 이 높이(m)에서 떨어뜨림.")]
    [SerializeField] private float spawnHeight = 3f;

    [Tooltip("판 반경에 곱한 원 안의 무작위 자리에 떨어뜨림.")]
    [Range(0f, 0.9f)][SerializeField] private float scatter = 0.5f;

    [Tooltip("구역을 벗어난 뒤 다시 작동하기까지 걸리는 시간(초).")]
    [Min(0f)][SerializeField] private float rearmSeconds = 3f;

    private BoxCollider area;
    private PlatformTilt platform;
    private Rigidbody platformBody;
    private NetworkRunner runner;
    private bool armed = true;
    private int remaining;
    private float nextSpawnTime;
    private float leftTime = -1f;

    private void Awake()
    {
        area = GetComponent<BoxCollider>();
        area.isTrigger = true;
    }

    private void FixedUpdate()
    {
        if (prefab == null) return;
        if (runner == null || !runner.IsRunning) runner = FindFirstObjectByType<NetworkRunner>();
        if (runner == null || !runner.IsRunning || !runner.IsServer) return;
        if (platform == null)
        {
            platform = FindFirstObjectByType<PlatformTilt>();
            if (platform == null) return;
            platformBody = platform.GetComponent<Rigidbody>();
        }

        Vector3 center = platformBody != null ? platformBody.position : platform.transform.position;
        Vector3 local = transform.InverseTransformPoint(center) - area.center;
        bool inside = Mathf.Abs(local.x) <= area.size.x * 0.5f && Mathf.Abs(local.y) <= area.size.y * 0.5f && Mathf.Abs(local.z) <= area.size.z * 0.5f;

        if (inside)
        {
            leftTime = -1f;
            if (armed)
            {
                armed = false;
                remaining = count;
                nextSpawnTime = Time.time;
            }
        }
        else if (!armed)
        {
            if (leftTime < 0f) leftTime = Time.time;
            else if (Time.time - leftTime >= rearmSeconds) armed = true;
        }

        if (remaining > 0 && Time.time >= nextSpawnTime)
        {
            remaining--;
            nextSpawnTime = Time.time + interval;
            Vector2 offset = Random.insideUnitCircle * (platform.PlatformRadius * scatter);
            var spawned = runner.Spawn(prefab, center + new Vector3(offset.x, spawnHeight, offset.y), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (spawned == null) return;

            // 휠의 이동 속도를 물려줘서 떨어지는 동안 판이 먼저 가 버리지 않게 함
            var rider = spawned.GetComponent<WheelRider>();
            if (rider != null) rider.BindToPlatformAtSpawn(platform);
            else if (spawned.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
            {
                Vector3 v = platform.FrameVelocity;
                v.y = 0f;
                rb.linearVelocity = v;
            }
        }
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
    }
}
