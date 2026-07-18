using UnityEngine;

[CreateAssetMenu(fileName = "PlayerDebuff", menuName = "Necromancer/Skills/Player/C_Debuff_Corrosion")]
public class PlayerDebuffSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float damageRadius = 2.5f;
    public float baseDamage = 15f;
    public float maxRange = 4.5f;

        public override void ExecuteSkill(Transform user, Transform target = null, System.Collections.Generic.List<Transform> validTargets = null)
    {
        PlaySkillSound();
        ShakeCamera();

        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 offset = mousePos - (Vector2)player.transform.position;
        Vector2 attackCenter = (Vector2)player.transform.position + Vector2.ClampMagnitude(offset, maxRange);


        if (hitBoxPrefab != null)
        {
            Vector2 dir = offset.normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            
            BaseHitBox box = Instantiate(hitBoxPrefab, attackCenter, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(damageRadius * 2f, damageRadius * 2f, 1f);
            
            DamageInfo info = new DamageInfo(baseDamage, ResolveDamageType(), user.gameObject, 1f, "Player Debuff", false, true, 2f);
            box.Init(info, Layers.EnemyMask, 0.5f, 0f, true);
        }
    }
}
