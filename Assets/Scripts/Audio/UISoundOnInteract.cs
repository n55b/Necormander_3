using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// UI 버튼 · 아이템 칸에 마우스를 올리거나 클릭할 때 소리를 낸다.
/// 소리는 'UI 소리 설정'의 공용 소리를 쓰고, 이 버튼만 다르게 하려면 아래 '이 버튼 전용 소리'에 넣는다.
/// 버튼이 눌릴 수 없는 상태(비활성)면 소리를 내지 않는다.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("사운드/UI 버튼 소리")]
public class UISoundOnInteract : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    public enum Kind
    {
        [InspectorName("일반 버튼")] Button,
        [InspectorName("아이템 칸")] Slot,
        [InspectorName("탭 버튼")] Tab,
    }

    [KoLabel("종류"), Tooltip("UI 소리 설정에서 어느 공용 소리를 쓸지 고릅니다.")]
    public Kind kind = Kind.Button;
    [KoLabel("마우스 올림 소리")] public bool playHover = true;
    [KoLabel("클릭 소리"), Tooltip("상점 구매 버튼처럼 결과에 따라 따로 소리가 나는 버튼은 꺼 두세요.")] public bool playClick = true;
    [KoLabel("이 버튼 전용 · 마우스 올림"), Tooltip("비워 두면 공용 소리를 씁니다.")] public SoundClipGroup hoverOverride = new SoundClipGroup();
    [KoLabel("이 버튼 전용 · 클릭"), Tooltip("비워 두면 공용 소리를 씁니다.")] public SoundClipGroup clickOverride = new SoundClipGroup();

    private Selectable _selectable;

    private void Awake() => _selectable = GetComponent<Selectable>();

    private bool Interactable => _selectable == null || _selectable.IsInteractable();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHover || !Interactable) return;
        UISoundSettings.PlayCue(kind == Kind.Slot ? UISoundSettings.Cue.SlotHover : UISoundSettings.Cue.ButtonHover, hoverOverride);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!playClick || eventData.button != PointerEventData.InputButton.Left || !Interactable) return;
        var cue = kind == Kind.Slot ? UISoundSettings.Cue.SlotClick
                : kind == Kind.Tab ? UISoundSettings.Cue.TabClick
                : UISoundSettings.Cue.ButtonClick;
        UISoundSettings.PlayCue(cue, clickOverride);
    }
}
