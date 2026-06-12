namespace Necromancer.Skills
{
    public enum SkillTriggerType
    {
        None = 0,
        Debuff = 1,     // 독성, 빙결 등 디버프 발생 시
        Parry = 2,      // 패링 성공 시
        HardCC = 3      // 스턴 등 강력한 상태이상 적중 시
    }
}
