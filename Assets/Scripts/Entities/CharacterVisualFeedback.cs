using UnityEngine;
using System.Collections;

/// <summary>
/// 유닛의 피격 반짝임, VFX(보호막 등) 시각적 피드백을 담당하는 컴포넌트입니다.
/// </summary>
public class CharacterVisualFeedback : MonoBehaviour
{
    private CharacterHealth _health;
    private CharacterStatus _status;
    private SpriteRenderer _sr;
    private Color _originalColor;
    private Coroutine _flashCoroutine;
    private Color _originalBaseColor;
    private bool _hasSavedOriginalBaseColor = false;
    private Coroutine _hitFlashCoroutine;

    [Header("빙결 VFX")]
    [Tooltip("빙결(Freeze) 상태인 동안 유닛에 붙여둘 이펙트 프리팹 (Assets/Prefabs/Effect/Effect_Freezing.prefab). " +
             "비워두면 Resources/Effects/Effect_Freezing 에서 자동 로드를 시도합니다.")]
    [SerializeField] private GameObject freezingVFXPrefab;
    [Tooltip("빙결 이펙트를 붙일 위치 오프셋 (유닛 로컬 기준)")]
    [SerializeField] private Vector3 freezingVFXOffset = Vector3.zero;

    private GameObject _freezingVFXInstance;
    private bool _freezingPrefabResolved;

    private GameObject _shieldVFXInstance;
    private GameObject _ccVFXInstance;

    public Color OriginalColor => _originalColor;

    public void Init(CharacterHealth health, CharacterStatus status)
    {
        _health = health;
        _status = status;
        
        // [개선] 이름을 사용하지 않고 계층 구조를 횡단하여 SpriteRenderer 탐색
        // 1. 현재 컴포넌트에서 시작
        _sr = GetComponent<SpriteRenderer>();

        // 2. 찾지 못했다면 최상위 부모(Root)를 찾은 후 그 아래의 모든 자식 탐색
        // (이 방식은 CharacterStatStuff와 Visual이 형제 관계여도 Root를 통해 찾을 수 있게 해줍니다)
        if (_sr == null)
        {
            var parentEntity = GetComponentInParent<BaseEntity>();
            if (parentEntity != null)
            {
                _sr = parentEntity.GetComponentInChildren<SpriteRenderer>();
            }
        }

        if (_sr == null)
        {
            _sr = GetComponentInChildren<SpriteRenderer>();
        }

        if (_sr == null)
        {
            Transform root = transform.root;

            // Prefer the dedicated "Body" child by name first - multiple SpriteRenderers exist
            // under root (hands, silhouettes, icon), so a blind "first match" search is unreliable.
            Transform bodyChild = root.Find("Body");
            if (bodyChild != null) _sr = bodyChild.GetComponent<SpriteRenderer>();

            // Fallback: old behavior if no "Body" child exists (e.g. other character prefabs)
            if (_sr == null)
                _sr = root.GetComponentInChildren<SpriteRenderer>();
        }

        if (_sr != null) 
        {
            _originalColor = _sr.color;
            Debug.Log($"<color=cyan>[VisualFeedback]</color> {gameObject.name}: SpriteRenderer found on <b>{_sr.gameObject.name}</b> (Root: {transform.root.name})");
        }
        else
        {
            Debug.LogWarning($"<color=red>[VisualFeedback]</color> {gameObject.name}: SpriteRenderer NOT found in any hierarchy!");
        }

        // 이벤트 구독
        _health.OnDamageTaken += PlayHitFlash;
        _health.OnHeal += PlayHealFlash;
        _health.OnDeath += PlayDeathVisual;
    }

    private void OnDestroy()
    {
        if (_health != null)
        {
            _health.OnDamageTaken -= PlayHitFlash;
            _health.OnHeal -= PlayHealFlash;
            _health.OnDeath -= PlayDeathVisual;
        }
    }

    private void PlayDeathVisual()
    {
        if (_sr == null) return;
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        
        // 사망 시 약간 어둡고 투명하게 (혹은 회색조)
        _sr.color = new Color(0.3f, 0.3f, 0.3f, 0.7f);
    }

    private void Update()
    {
        UpdateStatusVFX();
        UpdateBaseTint();
    }



    /// <summary>
    /// 틴트를 볼 상태이상의 우선순위. 여러 개가 동시에 걸려 있어도 '하나만' 칠한다 —
    /// 중독(초록)과 출혈(빨강)을 겹쳐 곱하면 탁한 갈색이 되어 둘 다 안 읽히기 때문이다.
    /// 행동 불가(빙결/기절)가 가장 급한 정보라 앞에 둔다.
    /// </summary>
    private static readonly StatusType[] TintPriority =
    {
        StatusType.Freeze, StatusType.Stun, StatusType.Poison, StatusType.Bleed, StatusType.BloodPop,
    };

    /// <summary>
    /// 스프라이트의 '기본색'을 매 프레임 한 번에 결정한다. 슈퍼아머와 상태이상 틴트가 각자
    /// _sr.color 를 건드리면 서로 덮어써서 깜빡이므로, 레이어를 순서대로 겹쳐 한 번만 쓴다.
    ///
    /// 겹치는 순서: 원래색 → 슈퍼아머 → 상태이상 틴트.
    /// 슈퍼아머는 빙결을 애초에 막으므로(StatusRules.BlockedBySuperArmor) 둘이 동시에 켜지는 건
    /// '이미 빙결된 유닛이 슈퍼아머를 얻는' 경로뿐이다. 그때는 나중에 얹는 빙결이 이긴다 —
    /// 행동 불가라는 정보가 더 급하기 때문이다.
    ///
    /// 피격 플래시 중(_flashCoroutine != null)에는 SetBaseColor 가 _sr.color 를 안 건드리고
    /// 값만 갱신해두므로, 플래시가 끝나면 여기서 정한 색으로 복귀한다.
    /// 상태이상이 풀리면 result 가 다시 _originalBaseColor 가 되어 원래 색으로 돌아온다.
    /// </summary>
    /// <summary>
    /// 스프라이트의 '기본색'을 매 프레임 한 번에 결정한다. 여러 곳이 각자 _sr.color 를 건드리면
    /// 서로 덮어써서 깜빡이므로, 색을 쓰는 창구를 여기 하나로 둔다.
    ///
    /// [26/07/25] 슈퍼아머는 여기서 빠졌다. 스프라이트를 파랗게 물들이는 대신 아웃라인 셰이더를
    /// 겹쳐 그리는 방식(UpdateSuperArmorOverlay)으로 옮겼다. 지금 남은 건 상태이상 틴트뿐이다.
    ///
    /// 피격 플래시 중에는 SetBaseColor 가 _sr.color 를 안 건드리고 값만 갱신해두므로,
    /// 플래시가 끝나면 여기서 정한 색으로 복귀한다.
    /// </summary>
    private void UpdateBaseTint()
    {
        if (_sr == null) return;

        // 사망 연출(PlayDeathVisual)이 칠해둔 색을 매 프레임 덮어쓰지 않도록 여기서 손을 뗀다.
        if (_health != null && _health.IsDead) return;

        if (!_hasSavedOriginalBaseColor)
        {
            _originalBaseColor = _originalColor;
            _hasSavedOriginalBaseColor = true;
        }

        // 대부분의 유닛은 대부분의 프레임에 상태이상이 없다. 그 경우 팔레트 조회와 우선순위
        // 순회를 통째로 건너뛴다 — Dictionary.Count 비교 한 번으로 끝난다.
        Color result = (_status != null && _status.HasAnyStatus)
            ? ApplyStatusTint(_originalBaseColor)
            : _originalBaseColor;

        // 바뀐 게 없으면 건드리지 않는다 — 매 프레임 SetBaseColor 를 때리면 사망색 같은
        // '외부에서 칠한 색'을 눈치 없이 지운다.
        if (result != _originalColor) SetBaseColor(result);
    }

    /// <summary>
    /// 걸려 있는 상태이상의 틴트를 곱연산으로 얹는다. Lerp 가 아니라 곱인 이유는,
    /// Lerp 는 스프라이트의 명암을 틴트색 쪽으로 뭉개서 도트가 납작해 보이기 때문이다.
    /// 곱은 원래 음영을 유지한 채 색만 물든다.
    ///
    /// 틴트 색의 알파를 '세기'로 쓴다 — 알파 0 이면 그 상태는 틴트를 안 쓴다는 뜻이라 그냥 지나간다.
    /// 색값은 전부 StatusEffectPalette 에서 오므로 여기엔 상태이상 색이 하드코딩되지 않는다.
    /// </summary>
    private Color ApplyStatusTint(Color baseColor)
    {
        if (_status == null) return baseColor;

        var palette = StatusEffectPalette.Shared;
        if (palette == null) return baseColor;

        for (int i = 0; i < TintPriority.Length; i++)
        {
            StatusType type = TintPriority[i];
            if (!_status.HasStatus(type)) continue;
            if (!palette.TryGetTint(StatusEffectPalette.FromStatus(type), out Color tint)) continue;

            Color multiplied = new Color(baseColor.r * tint.r, baseColor.g * tint.g, baseColor.b * tint.b, baseColor.a);
            return Color.Lerp(baseColor, multiplied, tint.a);
        }

        return baseColor;
    }

    // ── 슈퍼아머 오버레이 ────────────────────────────────────────────
    //
    // 본체 머티리얼을 갈아끼우지 않고, 같은 스프라이트를 아웃라인 머티리얼로 한 번 더 겹쳐 그린다.
    // 본체를 안 건드리므로 피격 플래시(_HitFlash)·상태이상 틴트·2D 라이팅이 전부 그대로 산다.
    // 아웃라인 셰이더(PickUpOutline)에는 _HitFlash 가 없어서, 본체를 통째로 교체하면
    // 슈퍼아머가 켜진 동안 피격 플래시가 '에러 없이 조용히' 죽는다. 그래서 겹쳐 그리는 쪽을 택했다.

    private SpriteRenderer _superArmorOverlay;
    private MaterialPropertyBlock _superArmorMpb;
    private Color _superArmorBaseColor = Color.white;
    private int _superArmorColorId;
    private float _lastOverlayBrightness = -1f;

    /// <summary>
    /// 슈퍼아머 상태에 맞춰 오버레이를 만들고/지우고/따라붙인다.
    /// LateUpdate 에서 부르는 이유는 본체 sortingOrder 를 YSortableObject 가 LateUpdate 에서
    /// 갱신하기 때문이다 — 빙결 VFX 도 같은 이유로 같은 자리에서 동기화한다.
    /// </summary>
    private void UpdateSuperArmorOverlay()
    {
        bool active = _sr != null
                   && _status != null && _status.HasSuperArmor
                   && !(_health != null && _health.IsDead);

        if (!active)
        {
            if (_superArmorOverlay != null)
            {
                Destroy(_superArmorOverlay.gameObject);
                _superArmorOverlay = null;
            }
            return;
        }

        var palette = StatusEffectPalette.Shared;
        // 머티리얼 칸이 비어 있으면 효과를 끈 것으로 본다.
        if (palette == null || palette.superArmorOverlayMaterial == null) return;

        if (_superArmorOverlay == null) SpawnSuperArmorOverlay(palette);
        if (_superArmorOverlay == null) return;

        SyncSuperArmorOverlay(palette);
    }

    private void SpawnSuperArmorOverlay(StatusEffectPalette palette)
    {
        var go = new GameObject("SuperArmorOverlay");
        go.transform.SetParent(_sr.transform, false);
        go.layer = _sr.gameObject.layer;

        _superArmorOverlay = go.AddComponent<SpriteRenderer>();
        _superArmorOverlay.sharedMaterial = palette.superArmorOverlayMaterial;
        _lastOverlayBrightness = -1f; // 새로 만들었으니 다음 Sync 에서 반드시 한 번은 쓰게 한다

        // 색은 머티리얼에 저장된 값을 그대로 읽어 쓴다. 팔레트에 색을 또 적어두면 둘이 어긋난다.
        _superArmorColorId = Shader.PropertyToID(palette.superArmorColorProperty);
        _superArmorBaseColor = palette.superArmorOverlayMaterial.HasProperty(_superArmorColorId)
            ? palette.superArmorOverlayMaterial.GetColor(_superArmorColorId)
            : Color.white;
    }

    private void SyncSuperArmorOverlay(StatusEffectPalette palette)
    {
        var o = _superArmorOverlay;

        // 애니메이션으로 매 프레임 스프라이트가 바뀌므로 계속 따라간다.
        // SpriteRenderer.sprite 대입은 공짜가 아니라 내부에서 메시/UV 를 다시 만든다.
        // 애니메이션 프레임이 그대로인 프레임에는 건너뛴다.
        if (!ReferenceEquals(o.sprite, _sr.sprite)) o.sprite = _sr.sprite;
        o.flipX = _sr.flipX;
        o.flipY = _sr.flipY;
        o.enabled = _sr.enabled;
        o.sortingLayerID = _sr.sortingLayerID;
        o.sortingOrder = _sr.sortingOrder + 1; // 본체 바로 앞

        // 게이지가 깎일수록 어두워져서 '닳고 있다'가 보인다.
        float maxSA = _status.MaxSuperArmorGauge;
        float ratio = maxSA > 0f ? Mathf.Clamp01(_status.SuperArmorGauge / maxSA) : 0f;
        float brightness = Mathf.Lerp(palette.superArmorMinBrightness, palette.superArmorMaxBrightness, ratio);

        // MaterialPropertyBlock 을 쓰는 이유: o.material 로 건드리면 유닛마다 머티리얼 인스턴스가
        // 하나씩 새로 생겨 배칭이 깨진다. MPB 는 인스턴스를 만들지 않는다.
        // 알파는 원본 값을 유지한다 — 밝기만 곱하려는 것이지 투명도를 건드리려는 게 아니다.
        // 게이지가 안 변한 프레임에는 MPB 왕복을 통째로 생략한다. 슈퍼아머는 맞을 때만
        // 깎이므로 대부분의 프레임에서 밝기가 그대로다.
        if (Mathf.Abs(brightness - _lastOverlayBrightness) <= 0.002f) return;
        _lastOverlayBrightness = brightness;

        if (_superArmorMpb == null) _superArmorMpb = new MaterialPropertyBlock();
        o.GetPropertyBlock(_superArmorMpb);
        _superArmorMpb.SetColor(_superArmorColorId, new Color(
            _superArmorBaseColor.r * brightness,
            _superArmorBaseColor.g * brightness,
            _superArmorBaseColor.b * brightness,
            _superArmorBaseColor.a));
        o.SetPropertyBlock(_superArmorMpb);
    }


    private void UpdateStatusVFX()
    {
        if (_status == null) return;

        // 보호막 VFX 관리
        if (_status.TotalShield < 0.01f && _shieldVFXInstance != null)
        {
            Destroy(_shieldVFXInstance);
            _shieldVFXInstance = null;
        }

        // 빙결 VFX 관리: 빙결이 걸려 있는 동안만 유닛에 붙어 있고, 풀리면(피격 파괴/자연 만료/사망) 사라진다.
        // ApplyStatus/RemoveStatus 양쪽에 훅을 거는 대신 여기서 상태를 폴링하는 이유는,
        // 빙결이 풀리는 경로가 여러 개(OnDirectDamageTaken, UpdateStatuses 만료, ClearStatus)라
        // 한 곳이라도 빠뜨리면 이펙트가 켜진 채 남기 때문이다.
        bool frozen = _status.HasStatus(StatusType.Freeze);

        if (frozen && _freezingVFXInstance == null)
        {
            SpawnFreezingVFX();
        }
        else if (!frozen && _freezingVFXInstance != null)
        {
            Destroy(_freezingVFXInstance);
            _freezingVFXInstance = null;
        }
    }

    /// <summary>
    /// 빙결 이펙트를 생성해 유닛에 붙인다.
    ///
    /// [왜 정렬을 코드에서 강제하는가]
    /// 적 스프라이트는 YSortableObject 가 매 프레임 sortingOrder = -(Y * 100) 로 덮어쓴다.
    /// 즉 Y=3 인 적의 order 는 약 -300 인데, 프리팹의 파티클은 고정 order 4 + 존재하지 않는
    /// 소팅 레이어를 들고 있어서, 레이어가 다르면 엉뚱한 깊이에 그려지고 레이어가 같아도
    /// 숫자만으로는 적과의 앞뒤가 매 프레임 뒤집힌다. 그래서 스폰 시점에 대상 스프라이트와
    /// '같은 레이어'로 맞추고 order 만 +1 해서 항상 본체 바로 앞에 오도록 고정한다.
    /// (본체가 움직여 order 가 바뀌어도 아래 LateUpdate 에서 계속 따라간다.)
    /// </summary>
    private void SpawnFreezingVFX()
    {
        GameObject prefab = ResolveFreezingPrefab();
        if (prefab == null) return;

        Transform anchor = _sr != null ? _sr.transform : transform;
        _freezingVFXInstance = Instantiate(prefab, anchor.position + freezingVFXOffset, prefab.transform.rotation, anchor);

        SyncFreezingVFXSorting();

        // 프리팹이 Play On Awake 가 꺼진 채 저장돼 있어도 확실히 재생시킨다.
        foreach (var ps in _freezingVFXInstance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(true);
            ps.Play(true);
        }
    }

    /// <summary>
    /// 빙결 이펙트의 소팅을 본체 스프라이트에 맞춘다. 본체는 Y 정렬로 order 가 매 프레임 바뀌므로
    /// 한 번만 맞추면 곧 어긋난다 — LateUpdate 에서 계속 동기화한다.
    /// </summary>
    private void SyncFreezingVFXSorting()
    {
        if (_freezingVFXInstance == null || _sr == null) return;

        foreach (var r in _freezingVFXInstance.GetComponentsInChildren<Renderer>(true))
        {
            r.sortingLayerID = _sr.sortingLayerID;
            r.sortingOrder = _sr.sortingOrder + 1; // 본체 바로 앞
        }
    }

    private void LateUpdate()
    {
        // 본체의 sortingOrder 는 YSortableObject 가 LateUpdate 에서 갱신하므로 여기서 뒤따라 맞춘다.
        if (_freezingVFXInstance != null) SyncFreezingVFXSorting();

        UpdateSuperArmorOverlay();
    }

    /// <summary>
    /// 빙결 이펙트 프리팹을 얻는다. 인스펙터 할당이 우선이고, 비어 있으면 Resources 경로를 한 번만 시도한 뒤
    /// 결과를 캐시한다(실패해도 매 프레임 재시도하지 않도록 _freezingPrefabResolved 로 잠근다).
    /// </summary>
    private GameObject ResolveFreezingPrefab()
    {
        if (freezingVFXPrefab != null) return freezingVFXPrefab;
        if (_freezingPrefabResolved) return null;

        _freezingPrefabResolved = true;
        freezingVFXPrefab = Resources.Load<GameObject>("Effects/Effect_Freezing");
        if (freezingVFXPrefab == null)
        {
            Debug.LogWarning($"<color=cyan>[VisualFeedback]</color> {gameObject.name}: 빙결 이펙트 프리팹이 비어 있습니다. " +
                             "프리팹의 CharacterVisualFeedback > Freezing VFX Prefab 에 Effect_Freezing 을 넣어주세요.");
        }
        return freezingVFXPrefab;
    }

    public void SetShieldVFX(GameObject vfx)
    {
        if (_shieldVFXInstance != null) Destroy(_shieldVFXInstance);
        _shieldVFXInstance = vfx;
    }

    public void SetCCVFX(GameObject vfx)
    {
        if (_ccVFXInstance != null) Destroy(_ccVFXInstance);
        _ccVFXInstance = vfx;
    }

    private void PlayHitFlash(float damage)
    {
        if (_status != null && _status.TotalShield > 0.01f) StartFlash(Color.cyan); // 보호막 피격
        else 
        {
            if (_hitFlashCoroutine != null)
            {
                StopCoroutine(_hitFlashCoroutine);
                if (_sr != null) _sr.material.SetFloat("_HitFlash", 0f);
            }
            _hitFlashCoroutine = StartCoroutine(FlashRoutine()); // 일반 피격

            // [수정] 레이어 체크 대신 root 태그를 사용하여 플레이어 판정 (안정성 강화)
            bool isPlayer = gameObject.CompareTag("Player") || transform.root.CompareTag("Player");
            
            if(isPlayer)
            {
                // [수정] 인스턴스 널 체크 추가하여 NRE 방지
if (HitStopManager.Instance != null) HitStopManager.Instance.DoHitStop(0.05f); 
                if (GameManager.Instance != null) GameManager.Instance.ChangeVignetteColor(0.05f, Color.red);
            }
        }
    }

    private IEnumerator FlashRoutine()
    {
        if (_sr == null) yield break; // [추가] 널 체크

        _sr.material.SetFloat("_HitFlash", 0.5f);
        yield return new WaitForSeconds(0.1f);       
        if (_sr != null) _sr.material.SetFloat("_HitFlash", 0f);

        StartFlash(Color.grey); 
    }

    private void PlayHealFlash() => StartFlash(Color.green);

    private void StartFlash(Color color)
    {
        if (_sr == null) return;
        if (_flashCoroutine != null) 
        {
            StopCoroutine(_flashCoroutine);
            _sr.color = _originalColor; // 강제 종료 시 색상 원상복구 보장
        }
        _flashCoroutine = StartCoroutine(FlashRoutine(color));
    }

    private IEnumerator FlashRoutine(Color color)
    {
        _sr.color = color;
        yield return new WaitForSeconds(0.1f);
        _sr.color = _originalColor;
        _flashCoroutine = null;
    }

    public void SetBaseColor(Color newColor)
    {
        _originalColor = newColor;
        if (_sr != null && _flashCoroutine == null)
        {
            _sr.color = newColor;
        }
    }

    public void ResetVisuals()
    {
        if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
        if (_sr != null) _sr.color = _originalColor;
    }

    private Coroutine _telegraphCoroutine;

    /// <summary>
    /// 공격 예고 플래시 (하데스식). 돌진/공격 윈드업 중 호출하면 스프라이트가 흰색으로 번쩍입니다.
    /// 피격 플래시(_HitFlash)와 같은 셰이더 프로퍼티를 재사용하므로 별도 셰이더가 필요 없습니다.
    /// </summary>
    /// <param name="pulses">번쩍임 횟수 (일반 1, 엘리트 2 권장)</param>
    /// <param name="onTime">1회 점등 시간</param>
    /// <param name="offTime">펄스 사이 소등 시간</param>
    /// <param name="intensity">플래시 강도 (0~1)</param>
    /// <summary>
    /// 공격 예고 플래시 (하데스식). 돌진/공격 윈드업 중 호출하면 스프라이트가 지정 색(기본 빨강)으로 번쩍입니다.
    /// </summary>
    /// <param name="pulses">번쩍임 횟수 (일반 1, 엘리트 2 권장)</param>
    public void PlayTelegraphFlash(int pulses = 1, float onTime = 0.09f, float offTime = 0.07f, float intensity = 1f)
    {
        PlayTelegraphFlash(new Color(1f, 0.2f, 0.2f), pulses, onTime, offTime);
    }

    /// <summary>색상 지정 버전. 피격 플래시와 동일하게 SpriteRenderer.color 펄스 방식이라 셰이더 무관하게 동작합니다.</summary>
    public void PlayTelegraphFlash(Color flashColor, int pulses, float onTime = 0.09f, float offTime = 0.07f)
    {
        if (_sr == null) return;
        if (_telegraphCoroutine != null)
        {
            StopCoroutine(_telegraphCoroutine);
            _sr.color = _originalColor; // 강제 종료 시 원상복구 보장 (StartFlash와 동일 규약)
        }
        _telegraphCoroutine = StartCoroutine(TelegraphFlashRoutine(flashColor, pulses, onTime, offTime));
    }

    private IEnumerator TelegraphFlashRoutine(Color flashColor, int pulses, float onTime, float offTime)
    {
        for (int i = 0; i < pulses; i++)
        {
            if (_sr == null) yield break;
            _sr.color = flashColor;
            yield return new WaitForSeconds(onTime);
            if (_sr == null) yield break;
            _sr.color = _originalColor;
            if (i < pulses - 1) yield return new WaitForSeconds(offTime);
        }
        _telegraphCoroutine = null;
    }

}
