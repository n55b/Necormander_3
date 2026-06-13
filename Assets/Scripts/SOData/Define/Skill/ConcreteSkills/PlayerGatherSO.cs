using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerGather", menuName = "Necromancer/Skills/Player/B_Gather_StatusEffect")]
public class PlayerGatherSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab;
    public float gatherRadius = 4f;
    public float gatherDuration = 0.15f; // 좀 더 짧고 강하게
    public float baseDamage = 10f;
    public float maxRange = 6f;
    
    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;

        player.StartCoroutine(GatherRoutine(player));
    }

    private IEnumerator GatherRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 offset = mousePos - (Vector2)player.transform.position;
        Vector2 dir = offset.normalized;
        if (dir == Vector2.zero) dir = Vector2.right;
        
        // 최대 범위 제한 적용 (평타처럼)
        Vector2 center = (Vector2)player.transform.position + Vector2.ClampMagnitude(offset, maxRange);

        bool hasInvokedKeyword = false;
        System.Action<CharacterHealth> onGatherSuccess = (health) => {
            if (!hasInvokedKeyword) {
                hasInvokedKeyword = true;
                Debug.Log("<color=cyan>[Player Skill B]</color> 모으기 적중! (호출: StatusEffect)");
            }
            GameManager.Instance.PLAYERCONTROLLER.GetComponent<PlayerSkillController>()?.OnKeywordApplied(SkillKeyword.StatusEffect, health.transform);
        };

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (hitBoxPrefab != null)
        {
            BaseHitBox box = Instantiate(hitBoxPrefab, center, Quaternion.Euler(0, 0, angle));
            box.transform.localScale = new Vector3(gatherRadius * 2f, gatherRadius * 2f, 1f);
            DamageInfo info = new DamageInfo(baseDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Gather!");
            box.Init(info, LayerMask.GetMask("Enemy"), 0.5f, 0f, true, onGatherSuccess);
        }

        // 시각 및 데미지는 HitBox가 주지만, 물리적으로 끌어당기는 것은 직접 수행
        Collider2D[] cols;
        
        // 프리팹에 붙어있는 콜라이더가 원형인지 사각형인지 시스템이 스스로 판별하여 적용!
        bool isCircle = false;
        if (hitBoxPrefab != null)
        {
            var prefabCol = hitBoxPrefab.GetComponent<Collider2D>();
            if (prefabCol is CircleCollider2D) isCircle = true;
        }

        if (isCircle)
        {
            cols = Physics2D.OverlapCircleAll(center, gatherRadius, LayerMask.GetMask("Enemy"));
        }
        else
        {
            cols = Physics2D.OverlapBoxAll(center, new Vector2(gatherRadius * 2f, gatherRadius * 2f), angle, LayerMask.GetMask("Enemy"));
        }
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
        Vector2 lineOrigin = player.transform.position;
        Vector2 lineDir = dir;

        while (elapsed < gatherDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / gatherDuration);
            
            for (int i = 0; i < targetsToMove.Count; i++)
            {
                if (targetsToMove[i] != null)
                {
                    Vector2 p = startPositions[i];
                    Vector2 v = p - lineOrigin;
                    float dot = Vector2.Dot(v, lineDir);
                    Vector2 closestPointOnLine = lineOrigin + lineDir * dot;

                    // 점이 아닌 플레이어 시선 방향의 중심축(Line)으로 쫙 모이도록 수정
                    targetsToMove[i].position = Vector2.Lerp(p, closestPointOnLine, t);
                }
            }
            yield return null;
        }
    }
}
