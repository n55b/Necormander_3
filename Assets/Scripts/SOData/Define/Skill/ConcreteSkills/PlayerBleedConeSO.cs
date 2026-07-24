using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerBleedCone", menuName = "Necromancer/Skills/Player/Physical/BleedCone")]
public class PlayerBleedConeSO : PlayerSkillSO
{
    [Header("Skill Settings")]
    public BaseHitBox hitBoxPrefab; // 부채꼴 형태의 PolygonCollider2D가 달린 프리팹 할당
    public float damageMultiplier = 1.3f; // 기본 공격력의 130%
    public float distance = 3f;
    public float width = 3f;

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) {
            Debug.LogError("ExecuteSkill: player is null!");
            return;
        }
        if (hitBoxPrefab == null) {
            Debug.LogError("ExecuteSkill: hitBoxPrefab is null! (The prefab is not properly linked in the Inspector)");
            return;
        }
        Debug.Log("ExecuteSkill: All checks passed. Spawning hitbox...");

        PlaySkillSound();
        ShakeCamera();

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        
        // 자식 오브젝트(삼각형)가 중심(0,0)에 있다면, 부모를 거리의 절반만큼 앞으로 밀어야 꼭짓점(또는 밑변)이 플레이어에 닿습니다.
        Vector2 spawnPos = startPos;
        BaseHitBox box = Instantiate(hitBoxPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
        
        // 부채꼴(삼각형)의 크기를 distance와 width로 조절합니다.
        box.transform.localScale = new Vector3(distance, width, 1f);
        
        float finalDamage = ResolveDamage(player.Stat, damageMultiplier);
        DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "Bleed Cone");
        

        box.Init(info, Layers.EnemyMask, 0.1f, 0f, true);
    }
}
