using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방패병 관련 유니크 효과 (방 진입 시 보호막/버프 부여 및 주기적인 오오라 효과)를 관리합니다.
/// InventoryManager에 부착되어 전역으로 관리됩니다.
/// </summary>
public class ShieldbearerUniqueManager : MonoBehaviour
{
    private float _updateInterval = 5.0f;
    private float _timer = 0f;

    [Header("VFX Prefabs (자동 생성 지원)")]
    public GameObject AuraVFXPrefab;
    private GameObject _auraVFXInstance;

    private void OnEnable()
    {
        RoomInstance.OnPlayerEnteredRoom += HandleRoomEnter;
    }

    private void OnDisable()
    {
        RoomInstance.OnPlayerEnteredRoom -= HandleRoomEnter;
    }

    private void Update()
    {
        var inven = InventoryManager.Instance;
        if (inven == null) return;

        bool hasPatience = false; // inven.HasUniqueEffect(GemUniqueType.AuraOfPatience);
        bool hasOverwhelm = false; // inven.HasUniqueEffect(GemUniqueType.AuraOfOverwhelming);

        if (!hasPatience && !hasOverwhelm)
        {
            if (_auraVFXInstance != null) _auraVFXInstance.SetActive(false);
            return;
        }

        // 오오라는 아군 방패병 기준으로 발동
        CharacterStatus shieldbearer = null;
        foreach (var ally in CharacterStatus.ActiveAllies)
        {
            if (ally != null && ally.TryGetComponent(out CharacterStat stat) && stat.jobType == CommandData.SkeletonShieldbearer)
            {
                shieldbearer = ally;
                break;
            }
        }

        if (shieldbearer == null)
        {
            if (_auraVFXInstance != null) _auraVFXInstance.SetActive(false);
            return;
        }

        // VFX 관리 (없으면 자동 생성)
        if (_auraVFXInstance == null)
        {
            if (AuraVFXPrefab != null)
            {
                _auraVFXInstance = Instantiate(AuraVFXPrefab);
            }
            else
            {
                _auraVFXInstance = new GameObject("Shieldbearer_AuraVFX_Auto");
                var sr = _auraVFXInstance.AddComponent<SpriteRenderer>();
                // 임시로 반투명한 노란색 원 (원형 스프라이트가 없으므로 색상만 넣을 순 없지만 임시 보존)
                sr.color = new Color(1f, 0.8f, 0.2f, 0.3f); 
            }
        }

        _auraVFXInstance.SetActive(true);
        _auraVFXInstance.transform.position = shieldbearer.transform.position;
        // 반경 임의 설정 (기본 투척 반경 2.5f 등을 고려해 지름 5f)
        _auraVFXInstance.transform.localScale = Vector3.one * 5.0f;

        _timer += Time.deltaTime;
        if (_timer >= _updateInterval)
        {
            _timer = 0f;
            TriggerAuras(shieldbearer, hasPatience, hasOverwhelm);
        }
    }

    private void TriggerAuras(CharacterStatus shieldbearer, bool hasPatience, bool hasOverwhelm)
    {
        float radius = 2.5f; // 반경
        var stat = shieldbearer.GetComponent<CharacterStat>();
        float shieldMaxHP = stat != null ? stat.MAXHP : 100f;

        // 인내의 오오라 (주변 소환수에게 방패병 체력 18% 보호막 부여)
        if (hasPatience)
        {
            float shieldAmount = shieldMaxHP * 0.18f;
            foreach (var ally in CharacterStatus.ActiveAllies)
            {
                if (ally == null) continue;
                if (Vector2.Distance(shieldbearer.transform.position, ally.transform.position) <= radius)
                {
                    // "보호막은 방패병 체력의 18%를 초과할 수 없습니다" -> 기존 보호막이 18% 이상이면 부여하지 않음 (단순 구현)
                    if (ally.TotalShield < shieldAmount)
                    {
                        // 기존 실드를 리셋하고 새로 주거나, 차이만큼만 줍니다.
                        float amountToAdd = shieldAmount - ally.TotalShield;
                        if (amountToAdd > 0)
                        {
                            ally.AddShield(amountToAdd, 5.0f); // 5초 지속
                        }
                    }
                }
            }
        }

        // 압도 (주변 적에게 방패병 최대 체력 4% 피해 및 피해량 120% 회복)
        if (hasOverwhelm)
        {
            float damageAmount = shieldMaxHP * 0.04f;
            float totalDamageDealt = 0f;

            foreach (var enemy in CharacterStatus.ActiveEnemies)
            {
                if (enemy == null) continue;
                if (Vector2.Distance(shieldbearer.transform.position, enemy.transform.position) <= radius)
                {
                    if (enemy.TryGetComponent(out CharacterHealth health))
                    {
                        DamageInfo info = new DamageInfo(damageAmount, DamageType.Fixed, shieldbearer.gameObject);
                        health.GetDamage(info);
                        totalDamageDealt += damageAmount;
                    }
                }
            }

            if (totalDamageDealt > 0 && shieldbearer.TryGetComponent(out CharacterHealth sbHealth))
            {
                sbHealth.Heal(totalDamageDealt * 1.2f);
            }
        }
    }

    // 버프 전역 상태 (CharacterStat 등에서 참조)
    public static bool IsWillCourageActive { get; private set; }
    public static bool IsWillWindActive { get; private set; }
    public static bool IsWillClashActive { get; private set; }

    private void HandleRoomEnter(RoomInstance room)
    {
        var inven = InventoryManager.Instance;
        if (inven == null) return;

        bool hasSturdy = false; // inven.HasUniqueEffect(GemUniqueType.SturdyShield);
        bool hasCourage = false; // inven.HasUniqueEffect(GemUniqueType.ShieldsWillCourage);
        bool hasWind = false; // inven.HasUniqueEffect(GemUniqueType.ShieldsWillWind);
        bool hasClash = false; // inven.HasUniqueEffect(GemUniqueType.ShieldsWillClash);

        if (!hasSturdy && !hasCourage && !hasWind && !hasClash) return;

        // 아군 방패병 찾기
        CharacterStatus shieldbearer = null;
        foreach (var ally in CharacterStatus.ActiveAllies)
        {
            if (ally != null && ally.TryGetComponent(out CharacterStat stat) && stat.jobType == CommandData.SkeletonShieldbearer)
            {
                shieldbearer = ally;
                break;
            }
        }

        if (shieldbearer != null)
        {
            var stat = shieldbearer.GetComponent<CharacterStat>();
            float maxHP = stat != null ? stat.MAXHP : 100f;

            // 든든한 방패: 체력 50%만큼 보호막 부여
            if (hasSturdy)
            {
                // 보호막 지속시간은 무한이 없으므로 매우 큰 값 적용
                shieldbearer.AddShield(maxHP * 0.5f, 9999f); 
            }
        }

        // 방패의 의지 버프 타이머 실행
        if (hasCourage || hasWind || hasClash)
        {
            StopCoroutine("RoomBuffCoroutine");
            StartCoroutine(RoomBuffCoroutine(hasCourage, hasWind, hasClash));
        }
    }

    private IEnumerator RoomBuffCoroutine(bool courage, bool wind, bool clash)
    {
        IsWillCourageActive = courage;
        IsWillWindActive = wind;
        IsWillClashActive = clash;

        yield return new WaitForSeconds(10f);

        IsWillCourageActive = false;
        IsWillWindActive = false;
        IsWillClashActive = false;
    }
}
