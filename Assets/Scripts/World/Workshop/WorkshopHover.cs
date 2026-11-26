using System;
using UnityEngine;
using UnityEngine.EventSystems;

// WorkshopUI.Hover가 붙인다. 마우스 올림과 키보드 선택 모두 설명 칸을 바꾼다.
internal sealed class WorkshopHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    internal Action enter;
    public void OnPointerEnter(PointerEventData _)=>enter?.Invoke();
    public void OnSelect(BaseEventData _)=>enter?.Invoke();
}
