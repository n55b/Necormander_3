using UnityEngine;

public interface IActiveSkill
{
    string SkillName { get; }
    float Cooldown { get; }
    bool IsActive { get; }
    bool IsOnCooldown { get; }

    void Initialize(PlayerController player);
    void OnActivate();
    void OnDeactivate();
    void UpdateSkill();
    bool HandleLeftClick(); // 좌클릭 이벤트 가로채기 (true 반환 시 기본 던지기 무시)
}
