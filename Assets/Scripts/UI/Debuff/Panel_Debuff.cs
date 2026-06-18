using System.Collections.Generic;
using UnityEngine;

public class Panel_Debuff : MonoBehaviour
{
    // 현재 이 적에게 떠 있는 아이콘들 (중복 방지용)
    private Dictionary<DebuffStackType, DebuffIcon> activeIcons = new Dictionary<DebuffStackType, DebuffIcon>();
    private Dictionary<DebuffBoolType, DebuffIcon> activeBoolIcons = new Dictionary<DebuffBoolType, DebuffIcon>(); // [추가]

    // 현재 각 디버프의 스택 수치 저장
    private Dictionary<DebuffStackType, int> currentStacks = new Dictionary<DebuffStackType, int>();

    public void AddDebuff(DebuffStackType type, Sprite sprite, int _stack)
    {
        // 1. 일단 현재 스택 값 저장/갱신
        currentStacks[type] = _stack;

        // 2. 이미 아이콘이 떠 있는지 확인
        if (activeIcons.TryGetValue(type, out DebuffIcon existingIcon))
        {
            existingIcon.UpdateStack(_stack); // 저장된 값 바로 사용
        }
        else
        {
            DebuffIcon newIcon = DebuffPool.Instance.Pop();
            newIcon.transform.SetParent(this.transform, false);

            newIcon.Initialize(sprite, _stack, type); // 1 대신 전달받은 _stack 사용

            activeIcons.Add(type, newIcon);
        }
    }

    // [추가] Bool 타입 디버프 추가
    public void AddDebuff(DebuffBoolType type, Sprite sprite, int _stack)
    {
        if (activeBoolIcons.TryGetValue(type, out DebuffIcon existingIcon))
        {
            existingIcon.UpdateStack(_stack); // 전달받은 스택 값 사용 (0이면 알아서 안보임)
            existingIcon.SetTriggeredState(true);
        }
        else
        {
            DebuffIcon newIcon = DebuffPool.Instance.Pop();
            newIcon.transform.SetParent(this.transform, false);
            newIcon.Initialize(sprite, _stack, DebuffStackType.Vulnerability); // 임시 타입 (UI 표시용)
            newIcon.SetTriggeredState(true);
            activeBoolIcons.Add(type, newIcon);
        }
    }

    // 디버프가 해제될 때 호출
    public void RemoveDebuff(DebuffStackType type)
    {
        if (activeIcons.TryGetValue(type, out DebuffIcon icon))
        {
            DebuffPool.Instance.Push(icon); // 풀로 반납
            activeIcons.Remove(type);
            currentStacks.Remove(type);
        }
    }

    // [추가] Bool 타입 디버프 제거
    public void RemoveDebuff(DebuffBoolType type)
    {
        if (activeBoolIcons.TryGetValue(type, out DebuffIcon icon))
        {
            DebuffPool.Instance.Push(icon);
            activeBoolIcons.Remove(type);
        }
    }

    public void ClearAllDebuffs()
    {
        List<DebuffStackType> keys = new List<DebuffStackType>(activeIcons.Keys);
        foreach (var type in keys) RemoveDebuff(type);

        List<DebuffBoolType> boolKeys = new List<DebuffBoolType>(activeBoolIcons.Keys); // [추가]
        foreach (var type in boolKeys) RemoveDebuff(type);

        activeIcons.Clear();
        activeBoolIcons.Clear();
        currentStacks.Clear();
    }
}
