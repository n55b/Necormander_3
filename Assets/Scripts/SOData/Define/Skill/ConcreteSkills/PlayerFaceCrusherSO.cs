using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 안면강타: 손 모션 타이밍(hitTimingRatio)에 맞춰 전방 부채꼴 범위의 적을 강타합니다.
// 기본: 기본 공격력의 100% 피해. 부채꼴 가운데에 있는 적은 +50% 추가 피해(총 150%).
// 구현 방식: 같은 모양의 부채꼴 히트박스를 두 개(넓은 폭 100% + 좁은 중앙 폭 +50%) 겹쳐 스폰해서
// 중앙에 있는 적만 두 번 맞아 150%가 되도록 처리합니다.
[CreateAssetMenu(fileName = "PlayerFaceCrusher", menuName = "Necromancer/Skills/Player/Physical/FaceCrusher")]
public class PlayerFaceCrusherSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab; // 부채꼴(콘) 형태의 히트박스 프리팹
    public float distance = 4.2f;      // [상향] 3f -> 4.2f
    public float width = 4.0f;         // [상향] 3f -> 4.0f 부채꼴/사각형 전체 폭
    public float centerWidth = 1.8f;   // [상향] 1f -> 1.8f 가운데 보너스 판정 폭 (width보다 작아야 함)
    public float damageMultiplier = 1.0f;       // 기본 100%
    public float centerBonusMultiplier = 0.5f;  // 가운데 추가 +50%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.PlayHandSkillAnim(handSkillAnimName);
        player.StartSkillCasting(FaceCrusherRoutine(player));
    }

    private IEnumerator FaceCrusherRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        // 손 모션 클립 길이 * hitTimingRatio 시점까지 기다렸다가 타격 (chargeTime은 고정값이라 클립 길이와 엇갈려 애니매이션 끝난 뒤 히트박스가 나가는 버그가 있었음)
        float hitDelay = player.GetHandSkillClipLength(handSkillAnimName) * hitTimingRatio;
        if (hitDelay > 0f) yield return new WaitForSeconds(hitDelay);
        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        // 시전 시점의 플레이어 위치 기준으로 다시 계산 (위치가 바뀌었을 수 있으므로)
        startPos = player.transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (hitBoxPrefab == null) yield break;

        Vector2 spawnPos = startPos;

        // 1. 전체 부채꼴 (기본 100%)
        BaseHitBox wideBox = Instantiate(hitBoxPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
        wideBox.transform.localScale = new Vector3(distance, width, 1f);
        float baseDamage = GetBaseDamage(player.Stat) * damageMultiplier;
        DamageInfo baseInfo = new DamageInfo(baseDamage, ResolveDamageType(), player.gameObject, 1f, "Face Crusher!");
        wideBox.Init(baseInfo, Layers.EnemyMask, 0.1f, 0f, true, null);

        // 2. 중앙 보너스 판정 (가운데에 있는 적만 +50% 추가)
        BaseHitBox centerBox = Instantiate(hitBoxPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
        centerBox.transform.localScale = new Vector3(distance, centerWidth, 1f);
        float bonusDamage = GetBaseDamage(player.Stat) * centerBonusMultiplier;
        DamageInfo bonusInfo = new DamageInfo(bonusDamage, ResolveDamageType(), player.gameObject, 1f, "Face Crusher (Center)!");
        centerBox.Init(bonusInfo, Layers.EnemyMask, 0.1f, 0f, true, null);
    }
}
