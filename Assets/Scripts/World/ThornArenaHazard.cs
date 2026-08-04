using UnityEngine;

/// <summary>
/// "뼈로 된 투기장" 가시. 본 마스터 아레나 외곽에 배치한다.
/// 플레이어가 닿으면 피해를 입고, 25% 확률로 출혈을 부여한다.
///
/// 방이 정사각형이 아닌 경우가 많아(예: 29x19) 원이 아니라 타원(가로/세로 반지름이 다름)으로 그린다.
/// PolygonCollider2D를 "고리(도넛) 모양"(바깥 경로 + 안쪽 경로를 반대 방향으로 감아 구멍을 뚫음)으로
/// 만들어서, 실제로 시각적인 가시 띠 부분에 겹칠 때만 콜라이더가 감지된다.
///
/// GetDistanceToInnerEdge()는 박치기 돌격 패턴이 "벽까지 최대한 돌진"하도록 방향별 거리를 계산할 때 쓴다.
/// </summary>
[RequireComponent(typeof(PolygonCollider2D))]
public class ThornArenaHazard : MonoBehaviour
{
    [Tooltip("플레이어가 가시에 닿았을 때 입는 고정 피해량")]
    [SerializeField] private float damage = 5f;

    [Tooltip("출혈 부여 확률 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float bleedChance = 0.25f;

    [Tooltip("출혈 지속시간(초). 0이면 StatusRules 기본값 사용")]
    [SerializeField] private float bleedDuration = 0f;

    [Tooltip("같은 대상에게 연속으로 피해를 주는 최소 간격(초)")]
    [SerializeField] private float hitCooldown = 0.5f;

    public float OuterRadiusX { get; private set; }
    public float OuterRadiusY { get; private set; }
    public float InnerRadiusX { get; private set; }
    public float InnerRadiusY { get; private set; }

    private readonly System.Collections.Generic.Dictionary<Collider2D, float> _lastHitTime = new System.Collections.Generic.Dictionary<Collider2D, float>();

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
        gameObject.tag = "BoneSpikeWall";
    }

    private void OnTriggerEnter2D(Collider2D other) => TryDamage(other);
    private void OnTriggerStay2D(Collider2D other) => TryDamage(other);

    private void TryDamage(Collider2D other)
    {
        if (!other.CompareTag("Player") && other.gameObject.layer != Layers.Player) return;

        if (_lastHitTime.TryGetValue(other, out float last) && Time.time - last < hitCooldown) return;
        _lastHitTime[other] = Time.time;

        var hp = other.GetComponentInParent<CharacterHealth>();
        if (hp == null) hp = other.GetComponentInChildren<CharacterHealth>();
        if (hp == null || hp.IsDead) return;

        bool applyBleed = Random.value <= bleedChance;
        var info = new DamageInfo(
            damage,
            DamageType.Physical,
            gameObject,
            category: DamageCategory.Trap,
            applyStatus: applyBleed ? StatusType.Bleed : (StatusType?)null,
            statusDuration: bleedDuration
        );
        hp.GetDamage(info);
    }

    /// <summary>
    /// 타원 경계 장벽을 LineRenderer로 그리고, PolygonCollider2D를 진짜 "고리(도넛) 모양"으로 설정한다.
    /// size: 타원의 (가로 지름, 세로 지름). bandRatio: 바깥 타원 대비 링 두께 비율.
    /// </summary>
    public void SetupAsRing(Vector2 size, Color color, int sortingOrder, float bandRatio, BaseEntity ownerEntity)
    {
        OuterRadiusX = Mathf.Max(0.1f, size.x * 0.5f);
        OuterRadiusY = Mathf.Max(0.1f, size.y * 0.5f);
        float innerRatio = Mathf.Clamp01(1f - bandRatio);
        InnerRadiusX = OuterRadiusX * innerRatio;
        InnerRadiusY = OuterRadiusY * innerRatio;

        const int segments = 96;

        var poly = GetComponent<PolygonCollider2D>();
        if (poly == null) poly = gameObject.AddComponent<PolygonCollider2D>();

        Vector2[] outerPoints = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            outerPoints[i] = new Vector2(Mathf.Cos(angle) * OuterRadiusX, Mathf.Sin(angle) * OuterRadiusY);
        }
        Vector2[] innerPoints = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = (segments - 1 - i) * Mathf.PI * 2f / segments;
            innerPoints[i] = new Vector2(Mathf.Cos(angle) * InnerRadiusX, Mathf.Sin(angle) * InnerRadiusY);
        }

        poly.pathCount = 2;
        poly.SetPath(0, outerPoints);
        poly.SetPath(1, innerPoints);
        poly.isTrigger = true;

        float midRatio = (1f + innerRatio) * 0.5f;
        var lr = GetComponent<LineRenderer>();
        if (lr == null) lr = gameObject.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * OuterRadiusX * midRatio, Mathf.Sin(angle) * OuterRadiusY * midRatio, 0f));
        }
        float lineWidth = Mathf.Max(0.1f, Mathf.Min(OuterRadiusX, OuterRadiusY) * bandRatio);
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.startColor = color;
        lr.endColor = color;

        BoneMasterTelegraphUtil.ApplySafeMaterialAndSorting(lr, ownerEntity, sortingOrder);

        transform.localScale = Vector3.one;
    }

    /// <summary>
    /// origin에서 dir 방향으로 나아갈 때 이 링의 "안쪽 경계"(=가시 판정이 시작되는 지점)까지의 거리.
    /// 타원 방정식에 직선을 대입해 t를 구하는 정확한 계산(레이캐스트에 의존하지 않음).
    /// 실패 시(반지름이 0 등) -1을 반환한다.
    /// </summary>
    public float GetDistanceToInnerEdge(Vector2 origin, Vector2 dir)
    {
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        Vector2 rel = origin - (Vector2)transform.position;
        float rx = InnerRadiusX, ry = InnerRadiusY;
        if (rx < 0.01f || ry < 0.01f) return -1f;

        float A = (d.x * d.x) / (rx * rx) + (d.y * d.y) / (ry * ry);
        float B = 2f * (rel.x * d.x / (rx * rx) + rel.y * d.y / (ry * ry));
        float C = (rel.x * rel.x) / (rx * rx) + (rel.y * rel.y) / (ry * ry) - 1f;

        if (A < 0.0001f) return -1f;
        float disc = B * B - 4f * A * C;
        if (disc < 0f) return -1f;

        float sqrtDisc = Mathf.Sqrt(disc);
        float t1 = (-B + sqrtDisc) / (2f * A);
        float t2 = (-B - sqrtDisc) / (2f * A);
        float t = Mathf.Max(t1, t2); // origin이 타원 내부에 있다는 전제 하에, 더 큰(양수) 근이 전방 교차점이다.

        return t > 0f ? t : -1f;
    }
}
