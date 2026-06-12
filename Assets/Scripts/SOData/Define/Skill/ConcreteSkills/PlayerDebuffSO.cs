using UnityEngine;

[CreateAssetMenu(fileName = "PlayerDebuff", menuName = "Necromancer/Skills/Player/C_Debuff_Corrosion")]
public class PlayerDebuffSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float damageRadius = 2.5f;
    public float baseDamage = 15f;
    public float corrosionTime = 3f;

    public override void ExecuteSkill(Transform user, Transform target = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mousePos - (Vector2)player.transform.position).normalized;
        Vector2 attackCenter = (Vector2)player.transform.position + dir * 2f;

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onDebuffHit = (health) => {
            // 개별 적에게 부식 디버프 적용
            var stat = health.GetComponent<CharacterStat>();
            if (stat != null && stat.Status != null)
            {
                stat.Status.SetDebuffBool(DebuffBoolType.Corroded, corrosionTime);
            }

            if (!hasInvokedKeyword)
            {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Player Skill C]</color> 전방 디버프 적중! (호출: Corrosion)");
                GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.Corrosion);
            }
        };

        if (hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.identity);
            box.transform.localScale = new Vector3(damageRadius * 2f, damageRadius * 2f, 1f);
            
            DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, user.gameObject, false, 1f, false, "Player Debuff", false, true, 2f);
            box.Init(info, LayerMask.GetMask("Enemy"), 0.5f, 0f, true, onDebuffHit);
        }
    }
}
