using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// 안면강타: 1초간 힘을 모은 뒤 전방 부채꼴 범위의 적을 강타합니다.
// 기본: 기본 공격력의 100% 피해. 부채꼴 가운데에 있는 적은 +50% 추가 피해(총 150%).
// 구현 방식: 같은 모양의 부채꼴 히트박스를 두 개(넓은 폭 100% + 좁은 중앙 폭 +50%) 겹쳐 스폰해서
// 중앙에 있는 적만 두 번 맞아 150%가 되도록 처리합니다.
[CreateAssetMenu(fileName = "PlayerFaceCrusher", menuName = "Necromancer/Skills/Player/Physical/FaceCrusher")]
public class PlayerFaceCrusherSO : PlayerSkillSO
{
    public BaseHitBox hitBoxPrefab; // 부채꼴(콘) 형태의 히트박스 프리팹
    public float chargeTime = 1f;
    public float distance = 3f;
    public float width = 3f;          // 부채꼴 전체 폭
    public float centerWidth = 1f;    // 가운데 보너스 판정 폭 (width보다 작아야 함)
    public float damageMultiplier = 1.0f;       // 기본 100%
    public float centerBonusMultiplier = 0.5f;  // 가운데 추가 +50%

    public override void ExecuteSkill(Transform user, Transform target = null, List<Transform> validTargets = null)
    {
        PlayerController player = user.GetComponent<PlayerController>();
        if (player == null) return;
        player.StartSkillCasting(FaceCrusherRoutine(player));
    }

    private IEnumerator FaceCrusherRoutine(PlayerController player)
    {
        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2 startPos = player.transform.position;
        Vector2 dir = (mousePos - startPos).normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        // 1초간 선딜레이 (힘을 모으는 구간)
        yield return new WaitForSeconds(chargeTime);
        if (player == null) yield break;

        PlaySkillSound();
        ShakeCamera();

        // 시전 시점의 플레이어 위치 기준으로 다시 계산 (위치가 바뀌었을 수 있으므로)
        startPos = player.transform.position;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (hitBoxPrefab == null) yield break;

        Vector2 spawnPos = startPos + dir * (distance * 0.5f);

        // 1. 전체 부채꼴 (기본 100%)
        BaseHitBox wideBox = Instantiate(hitBoxPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
        wideBox.transform.localScale = new Vector3(distance, width, 1f);
        float baseDamage = player.Stat.ATK * damageMultiplier;
        DamageInfo baseInfo = new DamageInfo(baseDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Face Crusher!");
        wideBox.Init(baseInfo, LayerMask.GetMask("Enemy"), 0.1f, 0f, true, null);

        // 2. 중앙 보너스 판정 (가운데에 있는 적만 +50% 추가)
        BaseHitBox centerBox = Instantiate(hitBoxPrefab, spawnPos, Quaternion.Euler(0, 0, angle));
        centerBox.transform.localScale = new Vector3(distance, centerWidth, 1f);
        float bonusDamage = player.Stat.ATK * centerBonusMultiplier;
        DamageInfo bonusInfo = new DamageInfo(bonusDamage, DamageType.Physical, player.gameObject, false, 1f, false, "Face Crusher (Center)!");
        centerBox.Init(bonusInfo, LayerMask.GetMask("Enemy"), 0.1f, 0f, true, null);
    }
}
