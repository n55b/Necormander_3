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

    /// <summary>origin에서 dir 방향으로 length만큼 뻗는 두꺼운 직선(레인) 경고를 그린다. 폭은 width. (창 찌르기 등 직선형 공격용)</summary>
    public static GameObject SpawnLane(BaseEntity entity, Vector2 origin, Vector2 dir, float length, float width, Color color, int sortingOrder = 5000)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_Lane");
        go.transform.position = origin;

        var lr = go.AddComponent<LineRenderer>();
        lr.loop = false;
        lr.positionCount = 2;
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, (Vector3)(d * length));

        ApplyCommon(lr, entity, color, sortingOrder, width);
        return go;
    }

    /// <summary>SpawnLane으로 만든 오브젝트의 시작점/방향/길이를 갱신한다(보스가 움직이며 조준할 때).</summary>
    public static void UpdateLane(GameObject lane, Vector2 origin, Vector2 dir, float length)
    {
        if (lane == null) return;
        var lr = lane.GetComponent<LineRenderer>();
        if (lr == null) return;
        lane.transform.position = origin;
        Vector2 d = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        lr.SetPosition(0, Vector3.zero);
        lr.SetPosition(1, (Vector3)(d * length));
    }

    /// <summary>중심(pos)에 타원(가로 radiusX, 세로 radiusY)의 두꺼운 경고를 그린다. (도약 & 내려찍기 착지 지점용)</summary>
    public static GameObject SpawnEllipse(BaseEntity entity, Vector2 pos, float radiusX, float radiusY, Color color, float bandRatio = 0.9f, int sortingOrder = 5000)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_Ellipse");
        go.transform.position = pos;

        var lr = go.AddComponent<LineRenderer>();
        const int segments = 48;
        lr.loop = true;
        lr.positionCount = segments;

        float minRadius = Mathf.Min(radiusX, radiusY);
        float width = Mathf.Max(0.05f, minRadius * bandRatio);
        float drawRatio = 1f - (width * 0.5f) / Mathf.Max(0.001f, minRadius);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radiusX * drawRatio, Mathf.Sin(angle) * radiusY * drawRatio, 0f));
        }

        ApplyCommon(lr, entity, color, sortingOrder, width);
        return go;
    }

    /// <summary>
    /// 중심(pos)에서 facingDir 방향을 기준으로 halfAngleDegrees(반각)만큼 벌어진 부채꼴(콘) 외곽선을 그린다.
    /// halfAngleDegrees=90이면 반달(180도) 모양. 중심점 → 부채꼴 호 → 중심점으로 돌아오는 외곽선(두 직선 변 + 호)이라
    /// "반달로 휩쓰는 범위"가 한눈에 보인다.
    /// </summary>
    public static GameObject SpawnCone(BaseEntity entity, Vector2 pos, Vector2 facingDir, float radius, float halfAngleDegrees, Color color, int sortingOrder = 5000, float lineWidth = 0.18f)
    {
        GameObject go = new GameObject("BoneMaster_Telegraph_Cone");
        go.transform.position = pos;

        var lr = go.AddComponent<LineRenderer>();
        const int arcSegments = 20;
        lr.loop = true; // 마지막 점에서 첫 점(중심)으로 자동으로 닫힌다.
        lr.positionCount = arcSegments + 1; // 중심 1개 + 호 위의 점들

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
