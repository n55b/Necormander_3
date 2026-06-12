using UnityEngine;

namespace Necromancer.Skills
{
    public abstract class SkillSO : ScriptableObject
    {
        [Header("기본 정보")]
        public string skillName;
        [TextArea] public string description;
        public Sprite icon;

        [Header("자원 및 쿨타임")]
        public float cooldownTime;
        public float manaCost;

        // 실제 스킬 로직은 구체적인 하위 클래스에서 오버라이드하여 구현
        // 지금은 Base 구조만 잡아둡니다.
        public abstract void ExecuteSkill(Transform user, Transform target = null);
    }
}
