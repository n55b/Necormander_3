using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGather", menuName = "Necromancer/Skills/Player/B_Gather_StatusEffect")]
public class PlayerGatherSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float gatherRadius = 4f;
    public float gatherDuration = 0.2f;
    public float baseDamage = 10f;
    
    public override void ExecuteSkill(Transform user, Transform target = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        player.StartCoroutine(GatherRoutine(player));
    }

    private IEnumerator GatherRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 dir = (mousePos - (Vector2)player.transform.position).normalized;
        Vector2 center = (Vector2)player.transform.position + dir * 3f;

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onGatherSuccess = (health) => {
            if (!hasInvokedKeyword) {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Player Skill B]</color> 모으기 적중! (호출: StatusEffect)");
                GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.StatusEffect);
            }
        };

        if (hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.identity);
            box.transform.localScale = new Vector3(gatherRadius * 2f, gatherRadius * 2f, 1f);
            DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Gather!");
            box.Init(info, LayerMask.GetMask("Enemy"), 0.5f, 0f, true, onGatherSuccess);
        }

        // 시각 및 데미지는 HitBox가 주지만, 물리적으로 끌어당기는 것은 직접 수행
        Collider2D[] cols = Physics2D.OverlapCircleAll(center, gatherRadius, LayerMask.GetMask("Enemy"));
        List<Transform> targetsToMove = new List<Transform>();
        List<Vector2> startPositions = new List<Vector2>();

        foreach (var col in cols)
        {
            var health = col.GetComponentInChildren<CharacterHealth>();
            if (health == null) health = col.GetComponentInParent<CharacterHealth>();

            if (health != null && !health.IsDead)
            {
                targetsToMove.Add(col.transform);
                startPositions.Add(col.transform.position);
            }
        }

        float elapsed = 0f;
        while (elapsed < gatherDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / gatherDuration;
            
            for (int i = 0; i < targetsToMove.Count; i++)
            {
                if (targetsToMove[i] != null)
                {
                    Vector2 targetPos = new Vector2(center.x, startPositions[i].y);
                    targetsToMove[i].position = Vector2.Lerp(startPositions[i], targetPos, t);
                }
            }
            yield return null;
        }
    }
}
