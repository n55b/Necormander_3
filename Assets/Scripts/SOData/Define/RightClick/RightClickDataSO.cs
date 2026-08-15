using UnityEngine;

/// <summary>
/// 우클릭 한 종류를 담는 에셋. 패링 / 카운터 / 가드 각각 하나씩 존재한다.
///
/// [26/08/15] 서브 소환수 삭제로 생긴 그릇이다. 예전엔 이 수치들이 SubMinionDataSO 안에
/// 인라인으로 들어 있었는데, 서브가 사라지면서 살 집이 없어졌다. 굳이 SO 로 뺀 이유는 두 가지다:
///   · 기획자가 종류별로 따로 인스펙터에서 튜닝할 수 있어야 한다.
///   · NPC 교체 UI(VillageDebugLoadout)가 '에셋 목록을 순회하는' 기존 패턴을 그대로 쓸 수 있다.
///     enum 으로 두면 UI 가 하드코딩된 switch 가 된다.
///
/// 해금 여부는 여기 없다 — <see cref="RightClickUnlockState"/> 가 따로 들고 있다.
/// 에셋은 '무엇이 존재하는가', 해금 상태는 '플레이어가 무엇을 쓸 수 있는가'라 수명이 다르다
/// (에셋은 빌드에 굽혀 있고 해금은 플레이어별로 저장된다).
/// </summary>
[CreateAssetMenu(fileName = "NewRightClick", menuName = "Necromancer/Data/Right Click")]
public class RightClickDataSO : ScriptableObject
{
    [Header("표시")]
    [Tooltip("NPC 목록·슬롯 툴팁에 뜨는 이름. 비우면 에셋 파일 이름을 쓴다.")]
    public string displayName;

    [Tooltip("비우면 config 수치에서 한 줄을 자동으로 만들어 쓴다(Describe). " +
             "직접 적으면 그게 항상 우선한다.")]
    [TextArea(2, 4)] public string description;

    [Tooltip("비워두면 config.sectorColor 색으로 임시 아이콘을 코드에서 만들어 쓴다. " +
             "스프라이트를 넣으면 폴백을 안 타고 그 스프라이트를 쓴다.")]
    public Sprite icon;

    [Header("수치")]
    public RightClickConfig config = new RightClickConfig();

    public RightClickType Type => config != null ? config.type : RightClickType.None;

    /// <summary>실제로 발동할 내용이 있는가. None 짜리 에셋은 장착 목록에 띄우지 않는다.</summary>
    public bool IsValid => config != null && config.IsValid;

    public string ResolveTitle() => string.IsNullOrEmpty(displayName) ? name : displayName;

    public string ResolveDescription()
        => string.IsNullOrEmpty(description) ? (config != null ? config.Describe() : null) : description;

    /// <summary>스프라이트가 있으면 그것, 없으면 색만 다른 임시 아이콘.</summary>
    public Sprite ResolveIcon()
        => icon != null
            ? icon
            : RightClickIconFactory.Get(config != null ? config.sectorColor : Color.gray);
}
