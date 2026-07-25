using UnityEngine;

/// <summary>
/// 상태이상의 '표시'를 식별하는 키. 게임플레이 enum(StatusType)과 일부러 분리했다.
///
/// StatusType 에는 없는 표시 상태가 있기 때문이다 — 빙결깨짐(FreezeBreak)이 그렇다.
/// 빙결이 깨지는 건 상태이상이 아니라 '빙결이 사라지는 사건'이라 StatusType 에 넣을 수 없다.
/// 반대로 StatusType 에 새 상태이상이 늘면 여기에도 한 줄 추가하고 팔레트 항목만 채우면 된다.
/// </summary>
public enum StatusVisual
{
    Stun,
    Freeze,
    FreezeBreak,
    Bleed,
    Poison,
    BloodPop,
    Hitstun,
}

/// <summary>
/// 상태이상 표시 색을 한곳에 모은 팔레트. 텍스트 팝업 색과 스프라이트 틴트 색을 같이 들고 있다.
///
/// [26/07/25] 구 DamageTextColorConfigSO 를 개명·흡수했다. 파일 GUID 를 보존한 채 이름만
/// 바꿨으므로 기존 DamageTextColorConfig.asset 의 값과 프리팹의 참조가 끊기지 않는다.
/// 데미지 숫자 색도 계속 여기 있다 — '전투 텍스트 색은 이 에셋 하나'라는 규칙을 지키려는 것이다.
/// 이름이 내용보다 좁은 건 인정하고 섹션(Header)으로 나눠둔다.
///
/// [왜 문자열이 아니라 enum 으로 가르는가]
/// 예전엔 팝업 문자열에 "기절"이 들어있는지 Contains 로 보고 색을 정했다. 라벨을 한 글자만
/// 바꿔도 색이 조용히 기본값으로 돌아가는 구조였다. 지금은 StatusVisual 로 식별한다.
/// </summary>
[CreateAssetMenu(fileName = "StatusEffectPalette", menuName = "Necromancer/UI/StatusEffectPalette")]
public class StatusEffectPalette : ScriptableObject
{
    /// <summary>
    /// 상태이상 하나의 표시 묶음. 인스펙터에서 색을 바로 집을 수 있고,
    /// 상태이상이 늘어나면 리스트에 항목만 추가하면 된다.
    /// </summary>
    [System.Serializable]
    public class Entry
    {
        [Tooltip("이 항목이 어떤 표시 상태를 담당하는지")]
        public StatusVisual visual;

        [Tooltip("상태이상 팝업에 띄울 문구. 비우면 텍스트를 띄우지 않는다")]
        public string label;

        [Tooltip("팝업 텍스트 색")]
        public Color textColor = Color.white;

        [Tooltip("이 상태에 걸린 유닛 스프라이트를 물들일 색. 틴트를 안 쓰는 상태는 알파 0 으로 둔다")]
        public Color tintColor = new Color(1f, 1f, 1f, 0f);

        [Tooltip("켜면 popScale 배율로 크게 띄운다")]
        public bool emphasize;
    }

    [Header("상태이상 팔레트")]
    [Tooltip("상태이상별 팝업 색·틴트 색·라벨. 상태이상이 추가되면 여기에 항목을 늘린다")]
    public Entry[] entries =
    {
        // 빙결: 차분한 하늘빛 파랑. 얼어붙어 '멈춘' 느낌이라 채도를 과하게 올리지 않는다.
        new Entry { visual = StatusVisual.Freeze,      label = "빙결!",     textColor = new Color(0.35f, 0.70f, 1.00f), tintColor = new Color(0.55f, 0.78f, 1.00f, 1f) },
        // 빙결깨짐: 같은 파랑 계열이되 더 밝고 창백하게. 깨지는 순간의 섬광이라 흰빛에 가깝다.
        // 빙결(0.35,0.70,1.00)과 명도 차가 커서 나란히 떠도 구분되고, 색상 계열은 그대로 읽힌다.
        new Entry { visual = StatusVisual.FreezeBreak, label = "빙결 파괴!", textColor = new Color(0.75f, 0.94f, 1.00f), emphasize = true },
        new Entry { visual = StatusVisual.Stun,        label = "기절!",     textColor = new Color(1.00f, 0.85f, 0.30f), emphasize = true },
        new Entry { visual = StatusVisual.Bleed,       label = "출혈",      textColor = new Color(0.90f, 0.20f, 0.25f) },
        new Entry { visual = StatusVisual.Poison,      label = "중독",      textColor = new Color(0.55f, 0.85f, 0.30f), tintColor = new Color(0.70f, 0.95f, 0.60f, 1f) },
        new Entry { visual = StatusVisual.BloodPop,    label = "비폭!",     textColor = new Color(1.00f, 0.75f, 0.20f), emphasize = true },
        // 경직은 평타마다 묻으므로 라벨을 비워둔다 — 채우면 화면이 텍스트로 덮인다.
        new Entry { visual = StatusVisual.Hitstun,     label = "",         textColor = new Color(0.75f, 0.75f, 0.75f) },
    };

    [Tooltip("팔레트에 없는 표시 상태가 들어왔을 때 쓸 색")]
    [UnityEngine.Serialization.FormerlySerializedAs("statusTextColor")]
    public Color fallbackStatusColor = Color.gray;

    [Tooltip("emphasize 가 켜진 항목의 팝업 크기 배율. 1보다 크게 하면 더 눈에 띈다")]
    [UnityEngine.Serialization.FormerlySerializedAs("statusPopScale")]
    public float popScale = 1.4f;

    [Header("데미지 숫자 색상")]
    [Tooltip("물리 공격 데미지 (평타 등 대부분의 기본 공격). ATK 기반")]
    public Color physicalColor = Color.white;
    [Tooltip("마법 공격 데미지. MAGIC 기반. 적은 마법사 계열이 이 색으로 뜬다")]
    public Color magicColor = new Color(0.6f, 0.4f, 1f);
    [Tooltip("어느 상태이상에도 속하지 않는 고정 피해. 방어력을 무시합니다(쉴드는 못 뚫습니다)")]
    public Color fixedColor = Color.cyan;
    [Tooltip("빙결이 깨질 때 터지는 고정 피해 '숫자'의 색상. 텍스트 색은 위 FreezeBreak 항목이 따로 관리한다")]
    public Color freezeColor = new Color(0.4f, 0.85f, 1f);
    [Tooltip("중독 틱(초당) 고정 피해의 색상")]
    public Color poisonColor = Color.green;
    [Tooltip("비폭(BloodPop) 10스택이 터질 때의 폭발 데미지 색상")]
    public Color bloodPopColor = Color.yellow;
    [Tooltip("출혈(Bleed) 상태에서 피격 시 추가로 들어가는 고정 피해의 색상")]
    public Color bleedColor = Color.red;

    [Header("특수 팝업 색상 (팝업 문자열로 강제 지정되는 경우)")]
    [Tooltip("쉴드가 데미지를 대신 막아냈을 때(팝업 문자열이 'Shield'인 경우) 표시되는 색상")]
    public Color shieldColor = Color.grey;
    [Tooltip("공격이 빗나갔을 때(회피, 팝업 문자열이 'MISS'인 경우) 표시되는 색상")]
    public Color missColor = Color.gray;

    [Header("기타")]
    [Tooltip("아군(Army 레이어)이 피해를 입었을 때 데미지 타입과 무관하게 적용되는 색상")]
    public Color allyHitColor = Color.red;
    [Tooltip("회복(힐)을 받았을 때 '+숫자' 형태로 표시되는 색상")]
    public Color healColor = Color.green;

    // ── 공용 인스턴스 ──────────────────────────────────────────────

    private static StatusEffectPalette _shared;

    /// <summary>
    /// 프리팹마다 배선하지 않고 코드에서 바로 집는 공용 팔레트. HitBoxColorConfigSO 와 같은 방식이다 —
    /// 에셋이 'Resources' 폴더 아래에 있고 이름이 "StatusEffectPalette" 여야 한다
    /// (현재 Assets/SOData/UI/Resources/StatusEffectPalette.asset).
    /// Unity 는 프로젝트 내 모든 Resources 폴더를 하나로 합쳐 보므로 중첩 폴더에 둬도 된다.
    ///
    /// 인스펙터로 꽂을 수 있는 쪽(FloatingTextSpawner)은 계속 SerializeField 를 쓴다. 여기는
    /// CharacterVisualFeedback 처럼 프리팹이 많아 일일이 꽂기 어려운 쪽을 위한 통로다.
    /// 못 찾으면 null 을 주고, 호출부는 조용히 틴트를 건너뛴다.
    /// </summary>
    public static StatusEffectPalette Shared
    {
        get
        {
            if (_shared == null) _shared = Resources.Load<StatusEffectPalette>("StatusEffectPalette");
            return _shared;
        }
    }

    // ── 조회 ────────────────────────────────────────────────────────

    /// <summary>게임플레이 상태이상을 표시 키로 옮긴다. 빙결깨짐은 StatusType 에 없으므로 여기서 안 나온다.</summary>
    public static StatusVisual FromStatus(StatusType type) => type switch
    {
        StatusType.Stun => StatusVisual.Stun,
        StatusType.Freeze => StatusVisual.Freeze,
        StatusType.Bleed => StatusVisual.Bleed,
        StatusType.Poison => StatusVisual.Poison,
        StatusType.BloodPop => StatusVisual.BloodPop,
        _ => StatusVisual.Hitstun,
    };

    /// <summary>못 찾으면 null. 인스펙터에서 항목을 지웠을 때 조용히 기본값으로 폴백하기 위함이다.</summary>
    public Entry Find(StatusVisual visual)
    {
        if (entries == null) return null;
        for (int i = 0; i < entries.Length; i++)
            if (entries[i] != null && entries[i].visual == visual) return entries[i];
        return null;
    }

    public Color GetTextColor(StatusVisual visual)
    {
        var e = Find(visual);
        return e != null ? e.textColor : fallbackStatusColor;
    }

    /// <summary>
    /// 스프라이트 틴트 색. 알파가 0 이면 '이 상태는 틴트를 쓰지 않는다'는 뜻이므로
    /// 호출부는 TryGetTint 로 물어보는 편이 안전하다.
    /// </summary>
    public Color GetTintColor(StatusVisual visual)
    {
        var e = Find(visual);
        return e != null ? e.tintColor : new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>틴트를 쓰는 상태면 true 와 색을 준다. 알파 0 은 '틴트 없음'으로 취급한다.</summary>
    public bool TryGetTint(StatusVisual visual, out Color tint)
    {
        tint = GetTintColor(visual);
        return tint.a > 0.001f;
    }

    /// <summary>띄울 문구. 비어 있으면 이 상태는 텍스트를 안 띄운다는 뜻이다.</summary>
    public string GetLabel(StatusVisual visual)
    {
        var e = Find(visual);
        return e != null ? e.label : string.Empty;
    }

    /// <summary>emphasize 가 켜진 항목이면 popScale, 아니면 1.</summary>
    public float GetPopScale(StatusVisual visual)
    {
        var e = Find(visual);
        return e != null && e.emphasize ? popScale : 1f;
    }

    public Color GetDamageColor(DamageType type)
    {
        switch (type)
        {
            case DamageType.Physical: return physicalColor;
            case DamageType.Magic: return magicColor;
            case DamageType.Fixed: return fixedColor;
            case DamageType.Freeze: return freezeColor;
            case DamageType.Poison: return poisonColor;
            case DamageType.BloodPop: return bloodPopColor;
            case DamageType.Bleed: return bleedColor;
            default: return physicalColor;
        }
    }
}
