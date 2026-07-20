using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "PlayerBloodPopPunch", menuName = "Necromancer/Skills/Player/Physical/BloodPopPunch")]
public class PlayerBloodPopPunchSO : PlayerSkillSO
{
    [Header("Skill Settings")]
    public BaseHitBox hitBoxPrefab; // 가운데 소환되는 원형 장판 프리팹 할당
    public float hitRadius = 2f;
    public float damageMultiplier = 0.9f; // 기본 공격력의 90%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null || hitBoxPrefab == null) return;

        PlaySkillSound();
        ShakeCamera();

        // 플레이어 정중앙에서 생성
        Vector2 startPos = player.transform.position;
        BaseHitBox box = Instantiate(hitBoxPrefab, startPos, Quaternion.identity);
        
        // 원형이므로 X, Y 스케일을 동일하게 hitRadius 2배로 설정 (반지름이므로 직경은 2배)
        box.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);

        float finalDamage = GetBaseDamage(player.Stat) * damageMultiplier;
        DamageInfo info = new DamageInfo(finalDamage, ResolveDamageType(), player.gameObject, 1f, "BloodPop Punch");


        // 0.1초 딜레이 후 타격
        box.Init(info, Layers.EnemyMask, 0.1f, 0f, true);
    }
}
