using UnityEngine;

/// <summary>
/// 무게중심 오프셋을 판의 기울기로 바꾸고, 동시에 구체 위치를 따라감.
/// 판은 Kinematic Rigidbody여야 함.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CenterOfMassSolver))]
public class PlatformTilt : MonoBehaviour
{

    [System.Serializable]
    public struct DeckAttachment
    {
        [Tooltip("모델 데크 평면을 따라갈 오브젝트.")]
        public Transform target;

        [Tooltip("데크 평면 기준 상대 높이. 데크 콜라이더는 0 부근, 감지 트리거는 양수.")]
        public float offsetY;
    }

    [Header("References")]
    [SerializeField] private Transform sphere;
    [SerializeField] private WheelConfig config;

    [Header("Placement")]
    [Tooltip("구체의 실제 반지름에서 높이를 자동 계산함. 비활성화시 manualHeight를 사용.")]
    [SerializeField] private bool deriveHeightFromSphere = true;

    [Tooltip("구체 반지름의 배수. 1 = 판이 구체 꼭대기. 0 = 판이 구체 중심.\n" +
                "모델의 시각적 정렬이 아니라 데크 평면의 위치를 정하는 값.\n" +
                "모델이 떠 보이면 이 값이 아니라 modelPositionOffset으로 맞출 것.")]
    [Range(-1.5f, 2f)][SerializeField] private float radiusMultiplier = 1f;

    [Tooltip("자동 계산값에 추가로 더하는 보정값.")]
    [SerializeField] private float extraOffset = 0f;

    [Tooltip("deriveHeightFromSphere가 꺼졌을 때 쓰는 고정 높이.")]
    [SerializeField] private float manualHeight = 0f;

    [Header("Model")]
    [Tooltip("기울기 계산과 무관하게 회전 보정을 받는 오브젝트.\n" +
                "해당 오브젝트에 콜라이더를 두지 말 것.")]
    [SerializeField] private Transform modelRoot;

    [Tooltip("모델 오프셋 변경 시 콜라이더와 트리거를 자동으로 따라가게 함.\n" +
        "비활성화시 수동 배치.")]
    [SerializeField] private bool syncCollidersToModel = true;

    [Tooltip("모델 데크 평면을 따라갈 오브젝트와 각각의 상대 높이.\n" +
                "데크 콜라이더와 감지 트리거를 서로 다른 offsetY로 등록.")]
    [SerializeField] private DeckAttachment[] deckAttachments;

    /// <summary>실제로 적용되는 모델 로컬 오프셋.</summary>
    public Vector3 ModelLocalOffset => scaleModelOffsetWithSphere
        ? modelPositionOffset * SphereRadius
        : modelPositionOffset;


    [Tooltip("모델의 축이 유니티와 다를 때 쓰는 보정값. Blender 등에서 Z-up으로 만든 모델은 보통 (-90, 0, 0).")]
    [SerializeField] private Vector3 modelRotationOffset = new Vector3(-90f, 0f, 0f);

    [Tooltip("모델 피벗이 데크 평면에 있지 않을 때 쓰는 위치 보정.\n" +
                "다리가 아래로 뻗은 모델은 피벗이 다리 끝에 있는 경우가 많음.\n" +
                "그럴 때 Y에 음수를 넣어 데크 평면을 판의 원점에 맞춤.\n" +
                "시각 보정이며 물리와 무관함.")]
    [SerializeField] private Vector3 modelPositionOffset = Vector3.zero;

    [Tooltip("모델 위치 보정을 구체 반지름에 비례시킴.\n" +
                "구체를 감싸도록 설계된 모델이면 활성화. 구체 크기가 바뀌어도 소켓이 유지됨.\n" +
                "이때 modelPositionOffset은 절대 거리가 아니라 반지름 배수로 해석됨.")]
    [SerializeField] private bool scaleModelOffsetWithSphere = false;

    [Header("Shape")]
    [Tooltip("판의 콜라이더 크기에서 오프셋 반경을 자동으로 측정.\n" +
                "비활성화시 WheelConfig.maxOffsetRadius를 사용.")]
    [SerializeField] private bool autoCalibrateOffsetRadius = true;

    [Tooltip("측정한 반경에 곱하는 값. 1이면 판 가장자리에서 최대 기울기가 나옴.\n" +
                "0.8 정도로 낮추면 가장자리에 닿기 전에 최대에 도달해 조작이 예민짐.")]
    [Range(0.3f, 1.5f)][SerializeField] private float offsetRadiusScale = 1f;


    private Rigidbody body;
    private CenterOfMassSolver solver;
    private Quaternion currentTilt = Quaternion.identity;
    private float cachedSphereRadius = -1f;
    private float cachedPlatformRadius = -1f;
    private Quaternion previousRotation = Quaternion.identity;
    private Vector3 angularVelocity;

    /// <summary>
    /// 시각 모델에만 축과 위치 보정을 적용함.
    /// 기울기 계산은 currentTilt를 기준으로 하므로 이 보정에 영향받지 않음.
    /// </summary>
    private void ApplyModelRotationOffset()
    {
        if (modelRoot == null) return;
        if (modelRoot == transform)
        {
            Debug.LogError($"{name}: modelRoot에 자기 자신을 넣으면 콜라이더까지 회전합니다. " +
                            "모델 전용 자식 오브젝트를 지정하세요.", this);
            return;
        }

        modelRoot.localRotation = Quaternion.Euler(modelRotationOffset);
        modelRoot.localPosition = ModelLocalOffset;

        SyncColliders();
    }

    /// <summary>
    /// 등록된 오브젝트를 모델 데크 평면에 맞춤.
    /// 각자 offsetY를 가지므로 콜라이더와 트리거를 다른 높이에 둘 수 있음.
    /// X/Z는 건드리지 않음. 수평 배치는 각 오브젝트 설정을 존중.
    /// </summary>
    private void SyncColliders()
    {
        if (!syncCollidersToModel || deckAttachments == null) return;

        float deckY = ModelLocalOffset.y;

        for (int i = 0; i < deckAttachments.Length; i++)
        {
            var a = deckAttachments[i];
            if (a.target == null || a.target == transform || a.target == modelRoot) continue;

            Vector3 p = a.target.localPosition;
            p.y = deckY + a.offsetY;
            a.target.localPosition = p;
        }
    }

    /// <summary>기울기 계산에 실제로 쓰이는 오프셋 반경.</summary>
    public float EffectiveOffsetRadius => autoCalibrateOffsetRadius
        ? PlatformRadius * offsetRadiusScale
        : config.maxOffsetRadius;

    /// <summary>판 콜라이더의 수평 반경. 외접 반경을 측정함.</summary>
    public float PlatformRadius
    {
        get
        {
            if (cachedPlatformRadius >= 0f) return cachedPlatformRadius;
            cachedPlatformRadius = MeasurePlatformRadius();
            return cachedPlatformRadius;
        }
    }


    /// <summary>판의 각속도(rad/s). Rider가 표면 속도를 계산할 때 사용함.</summary>
    public Vector3 AngularVelocity => angularVelocity;

    /// <summary>휠 전체의 이동 속도. 구체의 Rigidbody에서 직접 읽음.</summary>
    public Vector3 FrameVelocity
    {
        get
        {
            if (sphere != null && sphere.TryGetComponent<Rigidbody>(out var rb))
                return rb.linearVelocity;
            return Vector3.zero;
        }
    }

    /// <summary>판 위 임의 지점의 실제 속도. 이동 + 회전.</summary>
    public Vector3 PointVelocity(Vector3 worldPoint)
    {
        Vector3 r = worldPoint - body.position;
        return FrameVelocity + Vector3.Cross(angularVelocity, r);
    }

    private float MeasurePlatformRadius()
    {
        // 콜라이더 중 가장 큰 수평 범위를 판의 반경으로 간주
        float best = 0f;
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger) continue;
            Vector3 e = col.bounds.extents;
            best = Mathf.Max(best, Mathf.Max(e.x, e.z));
        }

        if (best <= 0.01f)
        {
            Debug.LogWarning($"{name}: 판 콜라이더를 찾지 못해 반경을 잴 수 없습니다. " +
                                "autoCalibrateOffsetRadius를 끄고 수동으로 지정하세요.", this);
            return Mathf.Max(0.01f, config != null ? config.maxOffsetRadius : 2.5f);
        }
        return best;
    }

    /// <summary>런타임에 판 크기가 바뀌면 호출해 캐시를 비움.</summary>
    public void InvalidatePlatformRadius() => cachedPlatformRadius = -1f;

    /// <summary>구체 중심에서 판까지의 최종 높이.</summary>
    public float HeightOffset => deriveHeightFromSphere
        ? SphereRadius * radiusMultiplier + extraOffset
        : manualHeight;

    /// <summary>스케일이 반영된 구체의 실제 반지름.</summary>
    public float SphereRadius
    {
        get
        {
            if (cachedSphereRadius >= 0f) return cachedSphereRadius;
            cachedSphereRadius = MeasureSphereRadius();
            return cachedSphereRadius;
        }
    }

    private float MeasureSphereRadius()
    {
        if (sphere == null) return 0.5f;

        if (sphere.TryGetComponent<SphereCollider>(out var col))
        {
            Vector3 s = col.transform.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            return col.radius * maxScale;
        }

        // SphereCollider가 없으면 렌더러 경계로 추정
        if (sphere.TryGetComponent<Renderer>(out var rend))
        {
            return rend.bounds.extents.y;
        }

        return sphere.lossyScale.y * 0.5f;
    }

    /// <summary>런타임에 구체 크기가 바뀌면 호출해 캐시를 비움.</summary>
    public void InvalidateSphereRadius() => cachedSphereRadius = -1f;

    /// <summary>판의 현재 법선. SphereDriver가 읽어감.</summary>
    public Vector3 SurfaceNormal => currentTilt * Vector3.up;

    /// <summary>수평면 기준 기울기 각도(도).</summary>
    public float TiltAngle => Vector3.Angle(Vector3.up, SurfaceNormal);

    /// <summary>0~1로 정규화한 기울기 세기.</summary>
    public float NormalizedTilt =>
        config.maxTiltAngle > 0f ? Mathf.Clamp01(TiltAngle / config.maxTiltAngle) : 0f;

    /// <summary>구체가 굴러가야 할 수평 방향. 기울기가 없으면 zero.</summary>
    public Vector3 DownhillDirection
    {
        get
        {
            Vector3 n = SurfaceNormal;
            Vector3 downhill = new Vector3(-n.x, 0f, -n.z);
            return downhill.sqrMagnitude > 1e-6f ? downhill.normalized : Vector3.zero;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        solver = GetComponent<CenterOfMassSolver>();

        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (config == null)
        {
            config = ScriptableObject.CreateInstance<WheelConfig>();
            Debug.LogWarning($"{name}: WheelConfig가 없어 기본값으로 실행합니다.", this);
        }

        if (sphere == null)
        {
            Debug.LogError($"{name}: sphere 참조가 비어 있습니다. 위치 추종을 중단합니다.", this);
        }
        else
        {
            IgnoreSphereCollisions();
        }

        ApplyModelRotationOffset();

        currentTilt = transform.rotation;
        previousRotation = currentTilt;
    }

    /// <summary>
    /// 판의 솔리드 콜라이더가 구체를 밀어내지 않도록 충돌을 끔.
    /// </summary>
    private void IgnoreSphereCollisions()
    {
        var platformColliders = GetComponentsInChildren<Collider>();
        var sphereColliders = sphere.GetComponentsInChildren<Collider>();

        foreach (var a in platformColliders)
        {
            if (a.isTrigger) continue;
            foreach (var b in sphereColliders)
            {
                if (b.isTrigger) continue;
                Physics.IgnoreCollision(a, b, true);
            }
        }
    }

    private void FixedUpdate()
    {
        Quaternion target = CalculateTargetTilt();

        float t = config.tiltResponsiveness <= 0f
            ? 1f
            : 1f - Mathf.Exp(-config.tiltResponsiveness * Time.fixedDeltaTime);

        Quaternion previous = currentTilt;
        currentTilt = Quaternion.Slerp(currentTilt, target, t);

        // 회전 속도에 상한을 둠.
        // 판 기울기 급변에 따른 물체 발사 방지.
        if (config.maxTiltAngularSpeed > 0f)
        {
            currentTilt = Quaternion.RotateTowards(
                previous, currentTilt, config.maxTiltAngularSpeed * Time.fixedDeltaTime);
        }

        // Rider가 표면 속도를 계산할 수 있도록 각속도를 기록함.
        Quaternion delta = currentTilt * Quaternion.Inverse(previousRotation);
        delta.ToAngleAxis(out float degrees, out Vector3 axis);
        if (degrees > 180f) degrees -= 360f;
        angularVelocity = float.IsNaN(axis.x) || Time.fixedDeltaTime <= 0f
            ? Vector3.zero
            : axis * (degrees * Mathf.Deg2Rad / Time.fixedDeltaTime);
        previousRotation = currentTilt;

        body.MoveRotation(currentTilt);

        if (sphere == null) return;

        body.MovePosition(sphere.position + currentTilt * Vector3.up * HeightOffset);
    }

    private Quaternion CalculateTargetTilt()
    {
        Vector3 offset = solver.WorldOffset;
        if (offset.sqrMagnitude < 1e-6f) return Quaternion.identity;

        float radius = Mathf.Max(0.01f, EffectiveOffsetRadius);
        float magnitude = Mathf.Clamp01(offset.magnitude / radius);

        // 응답 곡선: 중앙 근처는 둔감하게, 가장자리로 갈수록 급격하게
        magnitude = config.tiltResponseCurve.Evaluate(magnitude);

        Vector3 direction = offset.normalized;
        float angle = magnitude * config.maxTiltAngle;

        // 무게가 쏠린 쪽이 내려가도록 축을 잡음
        // Unity의 AngleAxis는 왼손 법칙이므로 Cross(up, direction) 순서여야 함.
        Vector3 axis = Vector3.Cross(Vector3.up, direction);
        if (axis.sqrMagnitude < 1e-6f) return Quaternion.identity;

        return Quaternion.AngleAxis(angle, axis.normalized);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null && !Application.isPlaying)
        {
            rb.isKinematic = true;
        }

        cachedSphereRadius = -1f;
        cachedPlatformRadius = -1f;
        ApplyModelRotationOffset();

        // 에디터에서 값을 만지면 즉시 배치가 반영되게 함.
        if (!Application.isPlaying && sphere != null)
        {
            transform.position = sphere.position + Vector3.up * HeightOffset;
            transform.rotation = Quaternion.identity;
        }
    }
#endif
}
