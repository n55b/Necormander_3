using UnityEngine;

public class PriestController : AllyController
{
    [Header("사제 전용 설정")]
    [SerializeField] private float healAmount = 10f;
    [SerializeField] private float healRange = 5f;
    [SerializeField] private LayerMask allyLayer;

    public override void ExecuteAttack(Transform target)
    {
        // 사제는 적을 공격하는 대신 주변에 가장 체력이 낮은 아군을 찾아 힐을 합니다.
        // 만약 힐할 대상이 없으면 기본 공격을 수행합니다. (선택 사항)

        Collider2D[] allies = Physics2D.OverlapCircleAll(transform.position, healRange, allyLayer);
        
        AllyController lowestHPAlly = null;
        float minHP = float.MaxValue;

        foreach (var col in allies)
        {
            if (col.TryGetComponent<CharacterStat>(out var stat))
            {
                if (stat.CURHP < stat.MAXHP && stat.CURHP < minHP)
                {
                    minHP = stat.CURHP;
                    lowestHPAlly = col.GetComponent<AllyController>();
                }
            }
        }

        if (lowestHPAlly != null)
        {
            // 힐 실행 (CharacterStat에 Heal 메서드가 있다고 가정하거나 직접 curHP 수정)
            // lowestHPAlly.FSM.stats.Heal(healAmount); 
            Debug.Log($"<color=green>[Priest]</color> {lowestHPAlly.name}을(를) {healAmount}만큼 치유했습니다!");
        }
        else
        {
            // 힐할 대상이 없으면 적을 기본 공격
            base.ExecuteAttack(target);
        }
    }
}
