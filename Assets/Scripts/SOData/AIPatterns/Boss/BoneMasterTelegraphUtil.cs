using UnityEngine;

/// <summary>
/// 본 마스터 패턴들이 공유하는 텔레그래프(피해범위 인디케이터) 스폰 헬퍼.
/// LineRenderer로 그리며, 머티리얼은 순수 흰색 계열 Sprites/Default 셰이더로 만든 단색 머티리얼을 우선 사용한다.
/// </summary>
public static class BoneMasterTelegraphUtil
{
    private static Material _cachedMaterial;

    private static Material GetSafeMaterial()
    {
        if (_cachedMaterial != null) return _cachedMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _cachedMaterial = new Material(shader);
        }
        return _cachedMaterial;
    }

    public static void ApplySafeMaterialAndSorting(LineRenderer lr, BaseEntity entity, int sortingOrder)
    {
        var mat = GetSafeMaterial();
        if (mat != null) lr.material = mat;

        if (entity != null && entity.SpriteRenderer != null)
        {
            lr.sortingLayerID = entity.SpriteRenderer.sortingLayerID;
        }
        lr.sortingOrder = sortingOrder;
    }

    private static void ApplyCommon(LineRenderer lr, BaseEntity entity, Color color, int sortingOrder, float width)
    {
        ApplySafeMaterialAndSorting(lr, entity, sortingOrder);
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 4;
        lr.useWorldSpace = false;
    }

    /// <summary>중심(pos)에 반지름(radius)의 두꺼운 원형 경고를 그린다. bandRatio가 클수록 두꺼워(=채워)진다.</summary>
    public static GameObject SpawnCircle(BaseEntity entity, Vector2 pos, float radius, Color color, float bandRatio = 0.9f, int sortingOrder = 5000)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_Circle");
        go.transform.position = pos;

        var lr = go.AddComponent<LineRenderer>();
        const int segments = 48;
        lr.loop = true;
        lr.positionCount = segments;

        float width = Mathf.Max(0.05f, radius * bandRatio);
        float drawRadius = radius - width * 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * drawRadius, Mathf.Sin(angle) * drawRadius, 0f));
        }

        ApplyCommon(lr, entity, color, sortingOrder, width);
        return go;
    }

    /// <summary>중심(pos)에 반지름(radius)의 얇은 경계 링을 그린다.</summary>
    public static GameObject SpawnRing(BaseEntity entity, Vector2 pos, float radius, Color color, float bandRatio = 0.15f, int sortingOrder = 5000)
    {
        return SpawnCircle(entity, pos, radius, color, bandRatio, sortingOrder);
    }

    // ── 차오르는 원형 전조 ───────────────────────────────────────────
    //
    // [추가 — "외곽선이 뜨자마자 터진다"는 억까 피드백]
    // SpawnRing/SpawnCircle 은 굵기만 다른 정지 원이라 "언제 터지는지"를 담고 있지 않다.
    // 레인/타원 전조는 프리팹에 차오름 게이지가 딸려 있어 남은 시간이 읽히는데, 페이즈2의
    // 광역기 3종(회전베기·몸통박치기·마무리베기)만 이 정지 링을 써서, 예고 시간을 늘려도
    // 플레이어 입장에선 여전히 "빨간 원이 떴다 → 맞았다"로만 보였다.
    //
    // 안전지대(safeRadius)에서 바깥(radius)으로 띠가 차오르게 만들면 두 가지가 동시에 읽힌다:
    //   · 얼마나 남았는지 (띠가 외곽선에 닿는 순간 = 발동)
    //   · 어디가 안전한지 (띠가 시작되는 안쪽이 곧 excludeRadius)
    // 즉 판정(DealCircle 의 radius/excludeRadius)과 그림이 같은 두 값으로 그려져 어긋날 수 없다.

    /// <summary>
    /// 안전지대(safeRadius)에서 바깥(radius)으로 <b>차오르는</b> 도넛형 전조를 만든다.
    /// 매 프레임 <see cref="UpdateRingCountdown"/>로 진행도를 갱신해야 실제로 차오른다.
    /// safeRadius 를 0으로 주면 중심에서 퍼지는 원판이 된다.
    /// </summary>
    public static GameObject SpawnRingCountdown(BaseEntity entity, Vector2 pos, float radius, float safeRadius,
                                                Color color, int sortingOrder = 5000)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_RingCountdown");
        go.transform.position = pos;

        float safe = Mathf.Max(0f, safeRadius);

        // 최종 위험 범위 외곽선(정지) — "여기까지 온다"
        MakeUnitCircleChild(go, "Outline", entity, color, sortingOrder, radius, Mathf.Max(0.06f, radius * 0.04f));

        // 안전지대 경계(정지, 옅게) — "여기 안쪽은 안 맞는다"
        if (safe > 0.01f)
        {
            Color safeColor = new Color(color.r, color.g, color.b, color.a * 0.45f);
            MakeUnitCircleChild(go, "SafeEdge", entity, safeColor, sortingOrder, safe, Mathf.Max(0.05f, safe * 0.06f));
        }

        // 차오르는 띠 — 외곽선보다 한 단계 아래에 깔아서 테두리가 위에 선명하게 남게 한다.
        Color fillColor = new Color(color.r, color.g, color.b, color.a * 0.55f);
        MakeUnitCircleChild(go, "Fill", entity, fillColor, sortingOrder - 1, Mathf.Max(0.01f, safe), 0.02f);

        UpdateRingCountdown(go, radius, safe, 0f);
        return go;
    }

    /// <summary>
    /// <see cref="SpawnRingCountdown"/>이 만든 전조의 차오름 진행도를 갱신한다(progress01: 0=시작, 1=발동).
    /// 반지름을 인자로 다시 받는 것은 이 유틸이 상태를 안 들고 있기 때문이다 — 호출측(패턴 코루틴)이
    /// 이미 두 값을 갖고 있으므로, 그림과 판정이 같은 변수에서 나오는 편이 어긋날 여지가 없다.
    /// </summary>
    public static void UpdateRingCountdown(GameObject telegraph, float radius, float safeRadius, float progress01)
    {
        if (telegraph == null) return;

        Transform fill = telegraph.transform.Find("Fill");
        if (fill == null) return;

        var lr = fill.GetComponent<LineRenderer>();
        if (lr == null) return;

        float safe = Mathf.Max(0f, safeRadius);
        float cur = Mathf.Lerp(safe, radius, Mathf.Clamp01(progress01));

        // 띠의 '가운데 반지름'은 정점 위치로, '두께'는 LineRenderer 폭으로 준다.
        // transform 스케일은 쓰지 않는다 — LineRenderer 의 폭이 스케일을 타는지 여부는 Unity
        // 버전/설정에 따라 헷갈리는 지점이라, 스케일에 의존하면 전조 크기가 판정과 어긋날 수 있다.
        // 정점을 매 프레임 다시 쓰는 비용은 48개 × 전조 1개라 무시할 수 있다.
        float band = Mathf.Max(0.02f, cur - safe);
        float drawRadius = Mathf.Max(0.01f, (cur + safe) * 0.5f);

        WriteCirclePoints(lr, drawRadius);
        lr.startWidth = band;
        lr.endWidth = band;
    }

    // 원 정점 버퍼(재사용). 전조는 한 번에 몇 개 안 뜨므로 정적 하나로 충분하다.
    private const int RingSegments = 48;
    private static readonly Vector3[] _ringPoints = new Vector3[RingSegments];

    private static void WriteCirclePoints(LineRenderer lr, float radius)
    {
        for (int i = 0; i < RingSegments; i++)
        {
            float a = i * Mathf.PI * 2f / RingSegments;
            _ringPoints[i] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
        }
        lr.positionCount = RingSegments;
        lr.SetPositions(_ringPoints);
    }

    /// <summary>지정한 반지름의 원을 그리는 LineRenderer 자식을 만든다(스케일은 항상 1로 둔다).</summary>
    private static Transform MakeUnitCircleChild(GameObject parent, string name, BaseEntity entity,
                                                 Color color, int sortingOrder, float radius, float width)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localScale = Vector3.one;

        var lr = child.AddComponent<LineRenderer>();
        lr.loop = true;
        WriteCirclePoints(lr, Mathf.Max(0.01f, radius));

        ApplyCommon(lr, entity, color, sortingOrder, width);
        return child.transform;
    }

    /// <summary>
    /// 전조 프리팹을 "시각 전용"으로 가동한다.
    /// targetLayer 를 0 으로 넘겨 BaseHitBox 의 자동 타격을 끈다(OnTriggerStay2D 의 레이어 검사가 항상 걸러낸다).
    /// 피해는 오직 BossCombat.DealXXX 가 준다 — 보스의 단일 데미지 경로는 그대로다.
    /// Init 이 ApplyTeamColor 로 RGB 를 공용 팔레트 색으로 덮으므로, 보스 고유 경고색(3연타 마지막 노랑 등)은
    /// 그 뒤에 다시 칠한다. 알파는 프리팹 값을 유지해야 차오름 연출이 안 가려진다.
    /// </summary>
    private static void InitAsVisualOnly(BaseHitBox box, BaseEntity entity, float windup, float life, Color color)
    {
        box.Init(new DamageInfo(0f, DamageType.Physical, entity != null ? entity.gameObject : null), 0, life, windup);

        foreach (var sr in box.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null) continue;
            sr.color = new Color(color.r, color.g, color.b, sr.color.a);
        }
    }

    /// <summary>
    /// origin 에서 dir 방향으로 length 만큼 뻗는 직사각 레인 경고. 폭은 width. (창 찌르기/내려찍기/돌진 예고용)
    ///
    /// 절차적 LineRenderer 대신 공용 전조 프리팹(Telegraph Line Hitbox)을 쓴다:
    /// (1) LineRenderer 는 numCapVertices 때문에 양 끝에 width/2 씩 둥근 캡을 덧그려서, 그림이
    ///     BossCombat.DealLane 의 판정 박스(정확히 length×width)보다 앞뒤로 길었다 —
    ///     "그림 끝에 서 있는데 안 맞는" 현상의 원인.
    /// (2) 이 프리팹은 콜라이더가 하나도 없어서 Init 을 불러도 데미지가 나갈 물리적 경로 자체가 없다.
    /// (3) 자식 앵커가 로컬 x∈[0,1] 이라 DealLane 의 origin 기준 박스와 좌표계가 그대로 일치한다.
    /// (4) windup 동안 차오르는 게이지 연출이 프리팹에 딸려 있어 런타임 코드가 0줄이다.
    /// </summary>
    public static GameObject SpawnLane(BaseEntity entity, Vector2 origin, Vector2 dir, float length, float width,
                                       Color color, BaseHitBox prefab, float windup, float life = 0.1f)
    {
        if (prefab == null)
        {
            Debug.LogError("[BoneMaster] laneTelegraphPrefab 이 비어 있습니다 — AI Pattern 에셋에 " +
                           "'Telegraph Line Hitbox Prefab' 을 넣어주세요. 전조 없이 진행합니다.");
            return null;
        }

        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        BaseHitBox box = Object.Instantiate(prefab, origin,
                                            Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg));
        // 이름 접두사로 찾아서 정리하므로(BoneMasterController.CleanupDanglingTelegraphs) 반드시 맞춘다.
        box.name = "BoneMaster_Telegraph_Lane";
        box.transform.localScale = new Vector3(length, width, 1f);
        InitAsVisualOnly(box, entity, windup, life, color);
        return box.gameObject;
    }

    /// <summary>
    /// 중심(pos)에 가로 radiusX / 세로 radiusY 인 타원 경고. (도약 & 내려찍기 착지 지점용)
    ///
    /// 프리팹은 CircleCollider2D 를 갖고 있으므로 InitAsVisualOnly 의 targetLayer 0 이 필수다 —
    /// 안 그러면 DealEllipse 와 이중 판정이 난다. (비균등 스케일에서 CircleCollider2D 는 큰 축 기준
    /// 원으로 뭉개지므로, 콜라이더로 타원 판정을 내는 선택지는 애초에 없다.)
    /// 예전 LineRenderer 방식은 띠 두께를 min(rx, ry) 공통으로 잡아서 rx > ry 일 때 가로 외곽을
    /// 짧게 그렸다(판정 2.2 → 그림 1.93). x/y 스케일이 독립인 프리팹은 그 오차가 없다.
    /// </summary>
    public static GameObject SpawnEllipse(BaseEntity entity, Vector2 pos, float radiusX, float radiusY,
                                          Color color, BaseHitBox prefab, float windup, float life = 0.1f)
    {
        if (prefab == null)
        {
            Debug.LogError("[BoneMaster] circleTelegraphPrefab 이 비어 있습니다 — AI Pattern 에셋에 " +
                           "'Center Skill Hitbox Circle Prefab' 을 넣어주세요. 전조 없이 진행합니다.");
            return null;
        }

        BaseHitBox box = Object.Instantiate(prefab, pos, Quaternion.identity);
        box.name = "BoneMaster_Telegraph_Ellipse";
        box.transform.localScale = new Vector3(radiusX * 2f, radiusY * 2f, 1f);
        InitAsVisualOnly(box, entity, windup, life, color);
        return box.gameObject;
    }

    /// <summary>
    /// 중심(pos)에서 facingDir 방향을 기준으로 halfAngleDegrees(반각)만큼 벌어진 부채꼴(콘) 외곽선을 그린다.
    /// halfAngleDegrees=90이면 반달(180도) 모양. 중심점 → 부채꼴 호 → 중심점으로 돌아오는 외곽선(두 직선 변 + 호)이라
    /// "반달로 휩쓰는 범위"가 한눈에 보인다.
    /// </summary>
public static GameObject SpawnCone(BaseEntity entity, Vector2 pos, Vector2 facingDir, float radius, float halfAngleDegrees, Color color, int sortingOrder = 5000, float lineWidth = 0.18f, float fillAlpha = 0.35f)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_Cone");
        go.transform.position = pos;

        const int arcSegments = 20;
        Vector2 dir = facingDir.sqrMagnitude > 0.0001f ? facingDir.normalized : Vector2.right;
        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        Vector3[] points = new Vector3[arcSegments + 1];
        points[0] = Vector3.zero; // 중심(보스 위치)
        for (int i = 0; i < arcSegments; i++)
        {
            float t = arcSegments > 1 ? i / (float)(arcSegments - 1) : 0f;
            float angleDeg = baseAngle - halfAngleDegrees + t * (halfAngleDegrees * 2f);
            float rad = angleDeg * Mathf.Deg2Rad;
            points[i + 1] = new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0f);
        }

        // [추가 — 내부 채움] 예전엔 외곽선(LineRenderer)만 그려서 부채꼴 안이 텅 비어 어색해 보였다.
        // 같은 점들로 중심에서 부채꼴을 이루는 삼각형 팬(fan) 메쉬를 만들어 반투명하게 채운다.
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        Mesh fillMesh = new Mesh { name = "BoneMaster_Telegraph_Cone_Fill" };
        fillMesh.vertices = points;
        int triCount = arcSegments - 1;
        int[] tris = new int[triCount * 3];
        for (int i = 0; i < triCount; i++)
        {
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = i + 2;
        }
        fillMesh.triangles = tris;
        fillMesh.RecalculateBounds();
        mf.sharedMesh = fillMesh;

        Material coneMat = null;
        var baseMat = GetSafeMaterial();
        if (baseMat != null)
        {
            coneMat = new Material(baseMat); // 텔레그래프마다 알파가 달라질 수 있어 공유 캐시 재질을 복제해서 쓴다.
            Color fillColor = color;
            fillColor.a *= fillAlpha;
            coneMat.color = fillColor;
            mr.sharedMaterial = coneMat;
        }

        // [버그 수정 — Mesh/Material 누수] 코드로 만든 Mesh 와 복제 Material 은 GameObject 를 Destroy 해도
        // 같이 사라지지 않는다. 부채꼴 전조는 근접 기본 공격마다 하나씩 생기므로 전투가 길어질수록
        // 계속 쌓였다. 어느 경로로 파괴되든 OnDestroy 는 지나가므로 꼬리표를 붙여 함께 정리한다.
        go.AddComponent<TelegraphResourceCleanup>().Track(fillMesh, coneMat);
        mr.sortingLayerID = entity != null && entity.SpriteRenderer != null ? entity.SpriteRenderer.sortingLayerID : 0;
        mr.sortingOrder = sortingOrder - 1; // 외곽선보다 한 단계 아래에 채워서 테두리가 위에 선명하게 보이게 한다.

        // ── 외곽선(기존과 동일) ──
        var lr = go.AddComponent<LineRenderer>();
        lr.loop = true; // 마지막 점에서 첫 점(중심)으로 자동으로 닫힌다.
        lr.positionCount = arcSegments + 1;
        lr.SetPositions(points);

        ApplyCommon(lr, entity, color, sortingOrder, lineWidth);
        return go;
    }

    /// <summary>SpawnCircle/SpawnEllipse/SpawnCone 등으로 만든 텔레그래프의 위치를 갱신한다(대상을 따라갈 때).</summary>
    public static void UpdatePosition(GameObject telegraph, Vector2 pos)
    {
        if (telegraph != null) telegraph.transform.position = pos;
    }
}
