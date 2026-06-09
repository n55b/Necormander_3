using UnityEngine;

public interface IActiveSkill
{
    string SkillName { get; }
    float Cooldown { get; }
    bool IsActive { get; }
    bool IsOnCooldown { get; }

    void Initialize(PlayerController player);
    
    // 입력 관련 (Q, E 등 스킬키)
    void OnInputStart();   // 키를 누른 순간
    void OnInputHold();    // 키를 누르고 있는 중 (매 프레임 호출)
    void OnInputRelease(); // 키를 뗀 순간

    void OnActivate();
    void OnDeactivate();
    void UpdateSkill();
    bool HandleLeftClick(); // 좌클릭 이벤트 가로채기 (true 반환 시 기본 던지기 무시)
}
