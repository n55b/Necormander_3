using UnityEngine;

public enum SkillKeyword
{
    None = 0,
    Strike = 1,
    Corrosion = 2,
    StatusEffect = 3
}

public abstract class SkillSO : ScriptableObject
{
    public string skillName;
    [TextArea] public string description;
    public float cooldownTime = 5f;

    public abstract void ExecuteSkill(Transform user, Transform target = null, System.Collections.Generic.List<Transform> validTargets = null);
}

public abstract class PlayerSkillSO : SkillSO
{
    // 추가적인 플레이어 전용 데이터 (스테미나 소모량 등)
}

public abstract class MinionSkillSO : SkillSO
{
    [Header("Reaction")]
    public SkillKeyword reactKeyword;

    // 추가적인 미니언 전용 데이터
}
