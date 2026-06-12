using UnityEngine;

namespace Necromancer.Skills
{
    [CreateAssetMenu(fileName = "NewMinionSkill", menuName = "Necromancer/Skills/MinionSkill")]
    public class MinionSkillSO : SkillSO
    {
        [Header("연계 트리거 설정")]
        public SkillTriggerType triggerType;
        [Tooltip("트리거 발생 후 불렛 타임(또는 발동 가능 상태)이 유지되는 시간 (초)")]
        public float triggerDuration = 2.0f;

        public override void ExecuteSkill(Transform user, Transform target = null)
        {
            Debug.Log($"<color=magenta>[MinionSkill]</color> {skillName} 발동! 트리거: {triggerType} (사용자: {user.name})");
            // 차후 실제 미니언 연계 스킬 로직 연결
        }
    }
}
