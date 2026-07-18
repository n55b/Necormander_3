using System.Collections.Generic;
using UnityEngine;

public class Panel_Debuff : MonoBehaviour
{
    // 현재 이 유닛에게 떠 있는 아이콘들 (중복 방지용)
    private Dictionary<StatusType, DebuffIcon> activeIcons = new Dictionary<StatusType, DebuffIcon>();

    /// <summary>
    /// stack 이 1 이하면 숫자를 안 띄운다(DebuffIcon.UpdateStack 참조).
    /// 스택 개념이 없는 상태이상은 0 을 넘기면 아이콘만 뜬다.
    /// </summary>
    public void AddDebuff(StatusType type, Sprite sprite, int stack)
    {
        if (activeIcons.TryGetValue(type, out DebuffIcon existingIcon))
        {
            existingIcon.UpdateStack(stack);
        }
        else
        {
            DebuffIcon newIcon = DebuffPool.Instance.Pop();
            newIcon.transform.SetParent(this.transform, false);
            newIcon.Initialize(sprite, stack);
            activeIcons.Add(type, newIcon);
        }
    }

    // 디버프가 해제될 때 호출
    public void RemoveDebuff(StatusType type)
    {
        if (activeIcons.TryGetValue(type, out DebuffIcon icon))
        {
            DebuffPool.Instance.Push(icon); // 풀로 반납
            activeIcons.Remove(type);
        }
    }

    public void ClearAllDebuffs()
    {
        List<StatusType> keys = new List<StatusType>(activeIcons.Keys);
        foreach (var type in keys) RemoveDebuff(type);
        activeIcons.Clear();
    }
}
