using UnityEngine;

namespace Necromancer.Skills
{
    [CreateAssetMenu(fileName = "NewPlayerSkill", menuName = "Necromancer/Skills/PlayerSkill")]
    public class PlayerSkillSO : SkillSO
    {
        public override void ExecuteSkill(Transform user, Transform target = null)
        {
            Debug.Log($"<color=cyan>[PlayerSkill]</color> {skillName} 발동! (사용자: {user.name})");
            // 차후 실제 플레이어 스킬 로직 연결 (파티클 재생, 데미지 등)
        }
    }
}
