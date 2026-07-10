using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 엘리트 몬스터(차저)가 방 진입 시 4방향에 소환하는 기둥입니다.
///
/// 붕괴 방식은 두 경로로 나뉩니다:
/// - 돌진에 직격당해 붕괴(CollapseInstantly): 기획서대로, 그 순간에는 아무에게도 피해를 주지 않고
///   조용히 붕괴합니다. 오직 2초의 유예시간이 끝난 뒤 안전지대 폭발만 피해를 줍니다. (기둥 뒤에 바짝
///   붙어서 돌진을 피했는데 즉시 피해를 입는 것처럼 느껴지는 문제를 방지)
/// - 다른 패턴으로 내구도가 소진되어 붕괴(DamagePattern): 이 경우에는 안전지대 밖에 즉시 충격 피해를
///   한 번 주고, 이후 2초 뒤 유예시간이 끝나면 그 자리에 남아있는 대상에게 폭발 피해를 한 번 더 줍니다.
///
/// 붕괴 유예시간 동안에는 바닥에 빨간 경고 원을 표시해 곧 폭발할 것임을 알려줍니다.
/// 머리 위에는 작은 4칸짜리 체력 표시가 붙어있습니다.
/// </summary>
public class EliteMonsterPillar : MonoBehaviour
{
    [Header("내구도 설정")]
    [SerializeField] private int maxHP = 4;
    [SerializeField] private int curHP;

    [Header("안전지대 설정")]
    [Tooltip("이 반경 안에 있으면 기둥 뒤에 숨은 것으로 간주합니다. 넉넉하게 잡아야 '기둥 뒤에 숨기'가 안정적으로 인식됩니다.")]
    [SerializeField] private float shelterRadius = 1.8f;
    [Tooltip("기둥이 무너진 뒤 안전지대가 유지되는 시간(초). 이후 안전지대가 폭발하며 완전히 사라집니다.")]
    [SerializeField] private float collapseGraceTime = 2f;
    [SerializeField] private float explosionRadius = 2f;
    [SerializeField] private float explosionDamage = 15f;
    [Tooltip("내구도 소진으로 붕괴할 때(DamagePattern)만 적용되는 즉시 충격 피해. 돌진 직격(CollapseInstantly)에는 적용되지 않습니다.")]
    [SerializeField] private float impactDamage = 10f;

    [Header("체력 표시(작은 4칸 바)")]
    [SerializeField] private bool showHealthBar = true;
    [SerializeField] private Vector3 healthBarOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private float healthPipSize = 0.14f;
    [SerializeField] private float healthPipSpacing = 0.18f;
    [SerializeField] private Color healthPipFilledColor = new Color(1f, 0.85f, 0.2f);
    [SerializeField] private Color healthPipEmptyColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);

    [Header("Refs")]
    [SerializeField] private SpriteRenderer visual;
    [SerializeField] private Collider2D physicsCollider;

    /// <summary>이 기둥을 소환한 엘리트 몬스터(공격자 정보로 사용).</summary>
    public GameObject Owner { get; set; }

    public bool IsAlive { get; private set; } = true;
    /// <summary>붕괴 유예시간 동안에도 true를 유지하다가, 폭발 이후 false로 전환됩니다.</summary>
    public bool ProvidesShelter { get; private set; } = true;

    public System.Action<EliteMonsterPillar> OnCollapsed;
    public System.Action<EliteMonsterPillar> OnFullyDestroyed;

    private GameObject _healthBarRoot;
    private readonly List<SpriteRenderer> _healthPips = new List<SpriteRenderer>();

    private void Awake()
    {
        curHP = maxHP;
        if (visual == null) visual = GetComponentInChildren<SpriteRenderer>();
        if (physicsCollider == null) physicsCollider = GetComponent<Collider2D>();

        RebuildHealthBar();
    }

    public void SetMaxHP(int hp)
    {
        maxHP = hp;
        curHP = hp;
        RebuildHealthBar();
    }

    /// <summary>
    /// worldPos가 기둥의 안전지대(shelterRadius) 안에 있는지 검사합니다.
    /// </summary>
    public bool IsSheltering(Vector2 worldPos)
    {
        if (!ProvidesShelter) return false;
        return Vector2.Distance(transform.position, worldPos) <= shelterRadius;
    }

    /// <summary>
    /// 패턴에 의한 내구도 피해입니다. (예: 바닥 충격파 1회당 기둥 내구도 피해)
    /// 이 경로로 붕괴하면 안전지대 밖에 즉시 충격 피해가 들어갑니다.
    /// </summary>
    public void DamagePattern(int amount)
    {
        if (!IsAlive) return;
        curHP -= amount;
        UpdateHealthBar();
        if (curHP <= 0)
        {
            Collapse(dealImmediateImpact: true);
        }
    }

    /// <summary>
    /// 체력과 무관하게 즉시 붕괴시킵니다. (돌진에 직격당했을 때)
    /// 기획서대로, 이 경로에서는 즉시 피해가 전혀 없습니다 — 오직 2초 뒤 안전지대 폭발만 피해를 줍니다.
    /// </summary>
    public void CollapseInstantly()
    {
        if (!IsAlive) return;
        curHP = 0;
        UpdateHealthBar();
        Collapse(dealImmediateImpact: false);
    }

    private void Collapse(bool dealImmediateImpact)
    {
        if (!IsAlive) return;
        IsAlive = false;
        OnCollapsed?.Invoke(this);

        if (dealImmediateImpact)
        {
            // 안전지대(shelterRadius) 안에 숨어있던 대상은 제외하고 즉시 충격 피해를 줍니다.
            ApplyAreaDamage(explosionRadius, impactDamage, shelterRadius);
        }

        if (_healthBarRoot != null) _healthBarRoot.SetActive(false);

        StartCoroutine(CollapseRoutine());
    }

    private IEnumerator CollapseRoutine()
    {
        // 붕괴 연출 (색을 어둡게 하여 붕괴 상태임을 표시)
        if (visual != null) visual.color = new Color(0.45f, 0.45f, 0.45f, 1f);

        // 유예시간 동안 곧 폭발할 것임을 알리는 빨간 경고 원을 표시합니다.
        GameObject warning = new GameObject("Pillar_Collapse_Warning");
        warning.transform.position = transform.position;
        SpriteRenderer warnSr = warning.AddComponent<SpriteRenderer>();
        warnSr.sprite = GetOrCreateCircleSprite();
        warnSr.color = new Color(1f, 0f, 0f, 0.35f);
        warnSr.sortingOrder = 8;
        warning.transform.localScale = Vector3.one * (explosionRadius * 2f);

        yield return new WaitForSeconds(collapseGraceTime);

        if (warning != null) Destroy(warning);

        // 안전지대 폭발: 폭발 시점까지 이 자리에 남아있던 대상에게는 예외 없이 피해를 줍니다.
        ApplyAreaDamage(explosionRadius, explosionDamage, 0f);

        ProvidesShelter = false;
        if (physicsCollider != null) physicsCollider.enabled = false;
        if (visual != null) visual.enabled = false;

        OnFullyDestroyed?.Invoke(this);
        Destroy(gameObject, 0.5f);
    }

    /// <summary>
    /// radius 안의 대상에게 damage를 줍니다. excludeRadius가 0보다 크면 그 반경 이내(안전지대)는 제외합니다.
    /// </summary>
    private void ApplyAreaDamage(float radius, float damage, float excludeRadius)
    {
        LayerMask targetLayer = LayerMask.GetMask("Player", "Army", "Ally");
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, radius, targetLayer);
        foreach (var hit in hits)
        {
            float d = Vector2.Distance(transform.position, hit.transform.position);
            if (excludeRadius > 0f && d <= excludeRadius) continue;

            CharacterStat stat = hit.GetComponentInParent<CharacterStat>();
            if (stat == null) stat = hit.GetComponentInChildren<CharacterStat>();
            if (stat != null && stat.Health != null && !stat.Health.IsDead)
            {
                DamageInfo info = new DamageInfo(damage, DamageType.Physical, Owner, false, 1f, false);
                stat.Health.GetDamage(info);
            }
        }
    }

    // ==============================================================
    // 작은 4칸 체력 표시
    // ==============================================================
    private void RebuildHealthBar()
    {
        if (_healthBarRoot != null)
        {
            Destroy(_healthBarRoot);
            _healthPips.Clear();
        }

        if (!showHealthBar || maxHP <= 0) return;

        _healthBarRoot = new GameObject("Pillar_HealthBar");
        _healthBarRoot.transform.SetParent(transform, false);
        _healthBarRoot.transform.localPosition = healthBarOffset;

        float totalWidth = (maxHP - 1) * healthPipSpacing;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < maxHP; i++)
        {
            GameObject pip = new GameObject("Pip" + i);
            pip.transform.SetParent(_healthBarRoot.transform, false);
            pip.transform.localPosition = new Vector3(startX + i * healthPipSpacing, 0f, 0f);

            SpriteRenderer sr = pip.AddComponent<SpriteRenderer>();
            sr.sprite = GetOrCreateSquareSprite();
            sr.sortingOrder = 20;
            pip.transform.localScale = Vector3.one * healthPipSize;

            _healthPips.Add(sr);
        }

        UpdateHealthBar();
    }

    private void UpdateHealthBar()
    {
        for (int i = 0; i < _healthPips.Count; i++)
        {
            if (_healthPips[i] == null) continue;
            _healthPips[i].color = i < curHP ? healthPipFilledColor : healthPipEmptyColor;
        }
    }

    private static Sprite _cachedCircleSprite;
    private static Sprite GetOrCreateCircleSprite()
    {
        if (_cachedCircleSprite != null) return _cachedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float r = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                tex.SetPixel(x, y, dist <= r ? Color.white : new Color(1, 1, 1, 0));
            }
        }
        tex.Apply();

        _cachedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedCircleSprite;
    }

    private static Sprite _cachedSquareSprite;
    private static Sprite GetOrCreateSquareSprite()
    {
        if (_cachedSquareSprite != null) return _cachedSquareSprite;

        int size = 8;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                tex.SetPixel(x, y, Color.white);
            }
        }
        tex.Apply();

        _cachedSquareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return _cachedSquareSprite;
    }
}
