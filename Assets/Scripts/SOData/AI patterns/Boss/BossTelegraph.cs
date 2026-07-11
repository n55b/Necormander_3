using UnityEngine;

/// <summary>
/// 보스 패턴들이 공유하는 "바닥 전조(텔레그래프)" 유틸.
/// 기존에 EliteChargerAIPatternSO 와 EliteMonsterPillar 에 복붙돼 있던 절차적 원/사각/링 스프라이트
/// 생성과 배치를 한 곳으로 모은 것. 스프라이트는 정적 캐시(런타임 1회 생성)라 매번 만들지 않는다.
/// (SkillCombatUtil 처럼 상태 없는 정적 유틸 — 이 코드베이스의 공용 유틸 관례를 따른다.)
/// </summary>
public static class BossTelegraph
{
    // ── 스폰 헬퍼 ─────────────────────────────────────────────
    public static GameObject SpawnCircle(Vector2 pos, float diameter, Color color, int sortingOrder = 8)
        => Spawn("Boss_Telegraph_Circle", GetCircleSprite(), pos, diameter, color, sortingOrder);

    public static GameObject SpawnRing(Vector2 pos, float diameter, Color color, int sortingOrder = 7)
        => Spawn("Boss_Telegraph_Ring", GetRingSprite(), pos, diameter, color, sortingOrder);

    /// <summary>방향형 직사각 전조 생성(피벗 중앙, +X로 뻗음). 이후 <see cref="UpdateRect"/>로 갱신.</summary>
    public static GameObject SpawnRect(Color color, int sortingOrder = 6)
    {
        GameObject obj = new GameObject("Boss_Telegraph_Rect");
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetSquareSprite();
        sr.color = color;
        sr.sortingOrder = sortingOrder;
        return obj;
    }

    /// <summary>origin에서 dir 방향으로 length만큼 뻗는 직사각 전조를 배치/갱신한다.</summary>
    public static void UpdateRect(GameObject rect, Vector2 origin, Vector2 dir, float length, float width)
    {
        if (rect == null) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        rect.transform.position = origin + dir * (length * 0.5f);
        rect.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        rect.transform.localScale = new Vector3(length, width, 1f);
    }

    private static GameObject Spawn(string name, Sprite sprite, Vector2 pos, float diameter, Color color, int order)
    {
        GameObject obj = new GameObject(name);
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * diameter;
        return obj;
    }

    // ── 절차적 스프라이트 (정적 캐시) ───────────────────────────
    private static Sprite _circle, _square, _ring;

    public static Sprite GetCircleSprite()
    {
        if (_circle == null) _circle = MakeSprite(64, (p, c, r) => Vector2.Distance(p, c) <= r);
        return _circle;
    }

    public static Sprite GetSquareSprite()
    {
        if (_square == null) _square = MakeSprite(8, (p, c, r) => true);
        return _square;
    }

    public static Sprite GetRingSprite()
    {
        if (_ring == null) _ring = MakeSprite(128, (p, c, r) =>
        {
            float d = Vector2.Distance(p, c);
            return d <= r && d >= r * 0.85f;
        });
        return _ring;
    }

    private static Sprite MakeSprite(int size, System.Func<Vector2, Vector2, float, bool> fill)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                tex.SetPixel(x, y, fill(new Vector2(x + 0.5f, y + 0.5f), center, r) ? Color.white : new Color(1, 1, 1, 0));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
