using UnityEngine;

public class EnemyDebuffTerminal : Base_DebuffUITerminal
{
    [SerializeField] DebuffDataSO debuffData; // 도감 하나 등록
    [SerializeField] Panel_Debuff debuffUI;

    // 표시만 숨긴다. 은신 중에도 상태이상 만료/스택 갱신은 계속 반영한다.
    public void SetVisible(bool visible)
    {
        if (debuffUI != null) debuffUI.gameObject.SetActive(visible);
    }

    public override void UpdateUI(StatusType type, float value)
    {
        // 도감에서 이미지 찾아서 UI에 넘겨줌
        Sprite _sprite = debuffData.GetSprite(type);
        debuffUI.AddDebuff(type, _sprite, (int)value);
    }

    public override void RemoveIcon(StatusType type)
    {
        debuffUI.RemoveDebuff(type);
    }

    public override void RemoveAll()
    {
        debuffUI.ClearAllDebuffs();
    }
}
