using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Necromancer.Skills
{
    [CreateAssetMenu(fileName = "LeapStrikeSkill", menuName = "Necromancer/Skills/Player/LeapStrike")]
    public class LeapStrikePlayerSkillSO : PlayerSkillSO
    {
        [Header("도약 설정")]
        public float jumpDuration = 0.5f;
        public float jumpHeight = 3f;
        public float baseDamage = 30f;
        public float baseStunTime = 1f;
        public float hitRadius = 2.5f;
        
        [Header("시너지 스케일링 설정")]
        public float damagePerMinion = 15f;
        public float stunTimePerMinion = 0.2f;

        private float _lastUsedTime = -9999f;

        public override void ExecuteSkill(Transform user, Transform target = null)
        {
            if (Time.time < _lastUsedTime + cooldownTime) return;

            PlayerController player = user.GetComponent<PlayerController>();
            if (player == null) return;

            _lastUsedTime = Time.time;
            player.SetInputBlocked(true);

            Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

            // 시너지 미니언 탐색 (창병) - 나중에는 미니언 데이터 종속적으로 수정 가능
            List<AllyController> synergyMinions = new List<AllyController>();
            var allyManager = player.GetComponent<AllyManager>();
            if (allyManager != null)
            {
                synergyMinions = allyManager.GetAliveAllies(CommandData.SkeletonSpearman);
                foreach (var minion in synergyMinions)
                {
                    minion.EnterSkillState();
                }
            }

            Debug.Log($"<color=cyan>[Skill]</color> 도약 타격 발동! 창병 수: {synergyMinions.Count}");
            player.StartCoroutine(JumpRoutine(player, targetPos, synergyMinions));
        }

        private IEnumerator JumpRoutine(PlayerController player, Vector2 targetPos, List<AllyController> synergyMinions)
        {
            float elapsed = 0f;
            Vector2 pStartPos = player.transform.position;

            List<Vector2> mStartPos = new List<Vector2>();
            List<Vector2> mEndPos = new List<Vector2>();
            
            float radius = 1.5f;
            float angleStep = synergyMinions.Count > 0 ? 360f / synergyMinions.Count : 0f;

            for (int i = 0; i < synergyMinions.Count; i++)
            {
                mStartPos.Add(synergyMinions[i].transform.position);
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                mEndPos.Add(targetPos + offset);
            }

            while (elapsed < jumpDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / jumpDuration;

                float height = 4f * jumpHeight * t * (1f - t);

                Vector2 pCur = Vector2.Lerp(pStartPos, targetPos, t);
                player.transform.position = new Vector3(pCur.x, pCur.y + height, 0f);

                for (int i = 0; i < synergyMinions.Count; i++)
                {
                    var m = synergyMinions[i];
                    if (m != null && !m.Stats.Health.IsDead)
                    {
                        Vector2 mCur = Vector2.Lerp(mStartPos[i], mEndPos[i], t);
                        m.transform.position = new Vector3(mCur.x, mCur.y + height, 0f);
                    }
                }

                yield return null;
            }

            // 착지 시점
            player.transform.position = targetPos;
            for (int i = 0; i < synergyMinions.Count; i++)
            {
                var m = synergyMinions[i];
                if (m != null && !m.Stats.Health.IsDead)
                {
                    m.transform.position = mEndPos[i];
                    m.ExitSkillState();
                }
            }

            ApplyLandingImpact(player, synergyMinions);
            player.SetInputBlocked(false);
        }

        private void ApplyLandingImpact(PlayerController player, List<AllyController> synergyMinions)
        {
            if (GameManager.Instance != null && GameManager.Instance.cameraManager != null)
            {
                GameManager.Instance.cameraManager.HitShakeCamera();
            }

            float finalDamage = baseDamage + (synergyMinions.Count * damagePerMinion);
            float finalStun = baseStunTime + (synergyMinions.Count * stunTimePerMinion);

            HashSet<int> hitEnemies = new HashSet<int>();

            CheckAndDamage(hitEnemies, player.transform.position, player.gameObject, finalDamage, finalStun);

            foreach (var m in synergyMinions)
            {
                if (m != null && !m.Stats.Health.IsDead)
                {
                    CheckAndDamage(hitEnemies, m.transform.position, player.gameObject, finalDamage, finalStun);
                }
            }
        }

        private void CheckAndDamage(HashSet<int> hitEnemies, Vector2 checkPos, GameObject attacker, float damage, float stunTime)
        {
            Collider2D[] cols = Physics2D.OverlapCircleAll(checkPos, hitRadius, LayerMask.GetMask("Enemy"));
            foreach (var col in cols)
            {
                var health = col.GetComponentInChildren<CharacterHealth>();
                if (health == null) health = col.GetComponentInParent<CharacterHealth>();

                if (health != null && !health.IsDead && !hitEnemies.Contains(health.gameObject.GetInstanceID()))
                {
                    hitEnemies.Add(health.gameObject.GetInstanceID());
                    
                    DamageInfo info = new DamageInfo(damage, DamageType.Physical, attacker, false, 1f, false, "Leap Strike!");
                    
                    var stat = health.GetComponent<CharacterStat>();
                    if (stat != null && stat.Status != null)
                    {
                        stat.Status.SetDebuffBool(DebuffBoolType.Stunned, stunTime);
                    }

                    health.GetDamage(info);
                }
            }
        }
    }
}
