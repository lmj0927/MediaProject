using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using CartoonFX;
using UnityEngine;

/// <summary>
/// VFXType으로 VFX 프리팹을 찾아 재생하는 로컬 매니저. 인스턴스는 타입별로 풀링해 재사용함.
/// 네트워크 동기화 없음: 모든 피어에 보여야 하는 이펙트는 각 피어가 직접 호출할 것.
/// 재생이 끝나면 비활성화되어 풀로 돌아옴 (CFXR_Effect는 Clear Behavior=Disable, 일반 ParticleSystem은 Stop Action=Disable로 강제).
/// 루프 이펙트는 스스로 끝나지 않으므로 Stop으로 돌려줄 것.
/// </summary>
public class VFXManager : Singleton<VFXManager>
{
    [SerializedDictionary("Type", "VFX Prefab")]
    [SerializeField] private SerializedDictionary<VFXType, GameObject> vfxDictionary = new();

    private readonly Dictionary<VFXType, List<GameObject>> pools = new();

    public bool HasVFX(VFXType type) =>
        vfxDictionary.TryGetValue(type, out var prefab) && prefab != null;

    public GameObject Play(VFXType type, Vector3 position) =>
        Play(type, position, Quaternion.identity);

    /// <summary>
    /// 타입에 등록된 VFX를 월드 위치에 재생함.
    /// parent를 주면 그 대상을 따라다님. 대상이 파괴되면 이펙트도 함께 사라지므로 따라다녀야 할 때만 사용.
    /// </summary>
    public GameObject Play(VFXType type, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (!HasVFX(type))
        {
            Debug.LogWarning($"{name}: {type}에 등록된 VFX 프리팹이 없습니다.", this);
            return null;
        }

        Transform container = parent != null ? parent : transform;
        var pool = GetPool(type);
        var instance = FindInactive(pool);

        if (instance == null)
        {
            instance = Instantiate(vfxDictionary[type], position, rotation, container);
            PrepareForPooling(instance);
            pool.Add(instance);
            return instance;
        }

        instance.transform.SetParent(container, worldPositionStays: true);
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.SetActive(true);

        if (instance.TryGetComponent<ParticleSystem>(out var particle) && !particle.isPlaying)
            particle.Play(true);

        return instance;
    }

    /// <summary>재생 중인 VFX를 즉시 멈추고 풀로 돌려줌. 루프 이펙트 정리용.</summary>
    public void Stop(GameObject instance)
    {
        if (instance == null)
            return;

        instance.SetActive(false);
        instance.transform.SetParent(transform, worldPositionStays: false);
    }

    private List<GameObject> GetPool(VFXType type)
    {
        if (!pools.TryGetValue(type, out var pool))
        {
            pool = new List<GameObject>();
            pools[type] = pool;
        }
        return pool;
    }

    private static GameObject FindInactive(List<GameObject> pool)
    {
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            var item = pool[i];
            if (item == null)
            {
                // 따라다니던 parent와 함께 파괴된 인스턴스
                pool.RemoveAt(i);
                continue;
            }

            if (!item.activeSelf)
                return item;
        }
        return null;
    }

    /// <summary>재생이 끝나면 파괴 대신 비활성화되도록 바꿔 풀로 돌아오게 함.</summary>
    private static void PrepareForPooling(GameObject instance)
    {
        if (instance.TryGetComponent<CFXR_Effect>(out var effect))
        {
            effect.clearBehavior = CFXR_Effect.ClearBehavior.Disable;
            return;
        }

        if (instance.TryGetComponent<ParticleSystem>(out var particle))
        {
            var main = particle.main;
            if (!main.loop)
                main.stopAction = ParticleSystemStopAction.Disable;
        }
    }
}
