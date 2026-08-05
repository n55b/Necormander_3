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


    private GameObject _voidBarrierObj;

    /// <summary>
    /// 뼈 투기장 바깥 전체를 검은색으로 칠하고, 물리적으로도 못 지나가게 막는다.
    /// SetupAsRing() 이후(OuterRadiusX/Y가 계산된 뒤) 호출해야 하며, 링이 리사이즈될 때마다
    /// (페이즈2 축소 등) 다시 호출해서 갱신한다. 링과 마찬가지로 이 오브젝트의 로컬 좌표계 기준.
    /// </summary>
    public void SetupVoidBarrier(float outerMargin, Color color, int sortingOrder)
    {
        float bigX = OuterRadiusX + Mathf.Max(1f, outerMargin);
        float bigY = OuterRadiusY + Mathf.Max(1f, outerMargin);
        const int segments = 96;

        if (_voidBarrierObj == null)
        {
            _voidBarrierObj = new GameObject("ThornArenaVoidBarrier");
            _voidBarrierObj.transform.SetParent(transform, false);
            _voidBarrierObj.transform.localPosition = Vector3.zero;
            _voidBarrierObj.transform.localRotation = Quaternion.identity;
            _voidBarrierObj.transform.localScale = Vector3.one;
        }

        // ── 물리적 차단: 링 바깥 경계(OuterRadius)부터 훨씬 큰 바깥 경계까지를 고리 모양의
        // "막힌"(트리거 아님) 콜라이더로 채운다. 안쪽(OuterRadius 이내)은 뚫려 있어 자유롭게 다닌다.
        var blockCol = _voidBarrierObj.GetComponent<PolygonCollider2D>();
        if (blockCol == null) blockCol = _voidBarrierObj.AddComponent<PolygonCollider2D>();
        blockCol.isTrigger = false;

        Vector2[] bigPoints = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            bigPoints[i] = new Vector2(Mathf.Cos(angle) * bigX, Mathf.Sin(angle) * bigY);
        }
        Vector2[] holePoints = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = (segments - 1 - i) * Mathf.PI * 2f / segments;
            holePoints[i] = new Vector2(Mathf.Cos(angle) * OuterRadiusX, Mathf.Sin(angle) * OuterRadiusY);
        }
        blockCol.pathCount = 2;
        blockCol.SetPath(0, bigPoints);
        blockCol.SetPath(1, holePoints);

        // ── 시각: 같은 모양(OuterRadius ~ bigBound)을 검은색 메쉬로 채워서 "바깥은 갈 수 없는 곳"임을 보여준다.
        var mf = _voidBarrierObj.GetComponent<MeshFilter>();
        if (mf == null) mf = _voidBarrierObj.AddComponent<MeshFilter>();
        var mr = _voidBarrierObj.GetComponent<MeshRenderer>();
        if (mr == null) mr = _voidBarrierObj.AddComponent<MeshRenderer>();

        Mesh mesh = mf.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "ThornArenaVoidMesh" };
            mf.sharedMesh = mesh;
        }
        mesh.Clear();

        Vector3[] verts = new Vector3[segments * 2];
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
            verts[i * 2] = new Vector3(cos * OuterRadiusX, sin * OuterRadiusY, 0f);
            verts[i * 2 + 1] = new Vector3(cos * bigX, sin * bigY, 0f);
        }
        int[] tris = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;
            int i0 = i * 2, i1 = i * 2 + 1, i2 = next * 2, i3 = next * 2 + 1;
            int t = i * 6;
            tris[t + 0] = i0; tris[t + 1] = i2; tris[t + 2] = i1;
            tris[t + 3] = i1; tris[t + 4] = i2; tris[t + 5] = i3;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        if (mr.sharedMaterial == null)
        {
            mr.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        }
        mr.sharedMaterial.color = color;

        var lr = GetComponent<LineRenderer>();
        mr.sortingLayerID = lr != null ? lr.sortingLayerID : 0;
        mr.sortingOrder = sortingOrder;
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


    /// <summary>
    /// [버그 수정 — 뼈 투기장을 뚫는 문제] WarpTo()는 물리 충돌을 거치지 않는 순간이동이라, 개별
    /// 패턴이 벽 체크를 깜빡하면(예: 견갑 찌르기의 재조준+대시) 경계를 그대로 뚫고 나갈 수 있다.
    /// 안쪽 경계(InnerRadius)가 아니라 "바깥 경계"(OuterRadius, 뼈 투기장의 맨 끝)를 기준으로 clamp
    /// 한다 — 안쪽 경계로 막으면 돌진 패턴이 의도적으로 가시 띠까지 파고들어 벽 접촉을 감지하는
    /// 로직과 충돌하기 때문이다. 이렇게 하면 가시 띠 안까지는 들어갈 수 있어도(무해함 — 가시는
    /// 플레이어만 판정), 검은 차단 영역 쪽으로는 절대 못 나간다.
    /// </summary>
    public Vector2 ClampInsideArena(Vector2 worldPos, float safetyMargin = 0.98f)
    {
        Vector2 rel = worldPos - (Vector2)transform.position;
        float rx = OuterRadiusX * safetyMargin;
        float ry = OuterRadiusY * safetyMargin;
        if (rx < 0.01f || ry < 0.01f) return worldPos;

        float norm = (rel.x * rel.x) / (rx * rx) + (rel.y * rel.y) / (ry * ry);
        if (norm <= 1f) return worldPos; // 이미 안쪽

        float scale = 1f / Mathf.Sqrt(norm);
        return (Vector2)transform.position + rel * scale;
    }

}
