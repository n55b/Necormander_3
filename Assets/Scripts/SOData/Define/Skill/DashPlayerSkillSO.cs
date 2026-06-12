using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Necromancer.Skills
{
    [CreateAssetMenu(fileName = "DashSkill", menuName = "Necromancer/Skills/Player/Dash")]
    public class DashPlayerSkillSO : PlayerSkillSO
    {
        [Header("돌진 설정")]
        public float dashDist = 8f;
        public float dashDuration = 0.2f;
        public float baseDamage = 20f;
        public float hitRadius = 1.5f;

        private float _lastUsedTime = -9999f;

        public override void ExecuteSkill(Transform user, Transform target = null)
        {
            if (Time.time < _lastUsedTime + cooldownTime) return;

            PlayerController player = user.GetComponent<PlayerController>();
            if (player == null) return;

            _lastUsedTime = Time.time;
            player.SetInputBlocked(true);

            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector2 dashDirection = (mousePos - (Vector2)player.transform.position).normalized;

            // 시너지 미니언 탐색 (방패병)
            List<AllyController> synergyMinions = new List<AllyController>();
            var allyManager = player.GetComponent<AllyManager>();
            if (allyManager != null)
            {
                synergyMinions = allyManager.GetAliveAllies(CommandData.SkeletonShieldbearer);
                foreach (var minion in synergyMinions)
                {
                    minion.EnterSkillState();
                }
            }

            Debug.Log($"<color=cyan>[Skill]</color> 방패 돌진 발동! 거리: {dashDist}");
            player.StartCoroutine(DashRoutine(player, dashDist, dashDirection, synergyMinions));
        }

        private IEnumerator DashRoutine(PlayerController player, float dist, Vector2 dir, List<AllyController> synergyMinions)
        {
            float elapsed = 0f;
            Vector2 pStartPos = player.transform.position;
            Vector2 pEndPos = pStartPos + dir * dist;

            List<Vector2> mStartPos = new List<Vector2>();
            List<Vector2> mEndPos = new List<Vector2>();
            foreach (var m in synergyMinions)
            {
                mStartPos.Add(m.transform.position);
                Vector2 mDir = (pEndPos - (Vector2)m.transform.position).normalized;
                mEndPos.Add((Vector2)m.transform.position + mDir * dist);
            }

            HashSet<int> hitEnemies = new HashSet<int>();

            while (elapsed < dashDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / dashDuration;

                player.transform.position = Vector2.Lerp(pStartPos, pEndPos, t);

                for (int i = 0; i < synergyMinions.Count; i++)
                {
                    var m = synergyMinions[i];
                    if (m != null && !m.Stats.Health.IsDead)
                    {
                        m.transform.position = Vector2.Lerp(mStartPos[i], mEndPos[i], t);
                    }
                }

                CheckHit(hitEnemies, player.transform.position, player.gameObject);

                foreach (var m in synergyMinions)
                {
                    if (m != null && !m.Stats.Health.IsDead)
                    {
                        CheckHit(hitEnemies, m.transform.position, m.gameObject);
                    }
                }

                yield return null;
            }

            player.transform.position = pEndPos;
            for (int i = 0; i < synergyMinions.Count; i++)
            {
                var m = synergyMinions[i];
                if (m != null && !m.Stats.Health.IsDead)
                {
                    m.transform.position = mEndPos[i];
                    m.ExitSkillState();
                }
            }

            player.SetInputBlocked(false);
        }

        private void CheckHit(HashSet<int> hitEnemies, Vector2 checkPos, GameObject attacker)
        {
            Collider2D[] cols = Physics2D.OverlapCircleAll(checkPos, hitRadius, LayerMask.GetMask("Enemy"));
            foreach (var col in cols)
            {
                var health = col.GetComponentInChildren<CharacterHealth>();
                if (health == null) health = col.GetComponentInParent<CharacterHealth>();

                if (health != null && !health.IsDead && !hitEnemies.Contains(health.gameObject.GetInstanceID()))
                {
                    hitEnemies.Add(health.gameObject.GetInstanceID());
                    
                    DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, attacker, false, 1f, false, "Charge!", false, true, 2f);
                    health.GetDamage(info);
                }
            }
        }
    }
}
