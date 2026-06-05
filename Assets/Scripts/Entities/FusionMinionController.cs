using System.Collections.Generic;
using UnityEngine;

public class FusionMinionController : MonoBehaviour
{
    private List<CharacterStatus> _materials = new List<CharacterStatus>();
    private float _timer = 0f;
    private bool _isFused = false;
    private CharacterStat _stat;

    public void Setup(List<IThrowable> units, float duration, float hpRatio, float scaleMultiplier, Color color, string popupName)
    {
        _stat = GetComponentInChildren<CharacterStat>();
        if (_stat == null) return;

        _timer = duration;
        _materials.Clear();

        float totalBaseMaxHP = 0f;
        float totalBaseAtk = 0f;
        float totalMaxHP = 0f;
        float totalCurHP = 0f;

        Debug.Log($"<color=orange>[Fusion]</color> Setup started! Base Golem MAXHP before: {_stat.MAXHP}");

        // 재료 미니언들 숨기기 및 스탯 합산
        foreach (var unit in units)
        {
            if (unit is MonoBehaviour mb)
            {
                var stat = mb.GetComponentInChildren<CharacterStat>();
                if (stat != null)
                {
                    totalBaseMaxHP += stat.BaseMaxHP;
                    totalBaseAtk += stat.BaseAtk;
                    totalMaxHP += stat.MAXHP;
                    totalCurHP += (stat.Health != null && !stat.Health.IsDead) ? stat.Health.CurHP : stat.MAXHP;
                    _materials.Add(stat.Status);
                    Debug.Log($"<color=orange>[Fusion]</color> Fused minion: {mb.gameObject.name}, BaseMaxHP: {stat.BaseMaxHP}, BaseATK: {stat.BaseAtk}, MAXHP: {stat.MAXHP}");
                }
                mb.gameObject.SetActive(false); // 비활성화
            }
        }

        // 융합체 스탯 오버라이드
        _stat.IsFusion = true;
        _stat.OverrideBaseStats(totalBaseMaxHP, totalBaseAtk);
        
        Debug.Log($"<color=orange>[Fusion]</color> Setup finished! Expected BaseMaxHP: {totalBaseMaxHP}, Golem new MAXHP: {_stat.MAXHP}");

        // 체력 맞추기 (비율 유지)
        if (_stat.Health != null && totalMaxHP > 0)
        {
            float targetCurHP = _stat.MAXHP * (totalCurHP / totalMaxHP);
            if (targetCurHP < _stat.MAXHP)
            {
                float damageToApply = _stat.MAXHP - targetCurHP;
                _stat.Health.GetDamage(new DamageInfo(damageToApply, DamageType.Fixed, null));
            }
        }

        // 시각적 피드백(색상) 적용
        if (_stat.Visual != null)
        {
            _stat.Visual.SetBaseColor(color);
        }
        else
        {
            var renderers = GetComponentsInChildren<SpriteRenderer>();
            foreach (var r in renderers) r.color = color;
        }

        // 크기 적용
        transform.localScale = Vector3.one * scaleMultiplier;

        // 집기 방지를 위한 태그/레이어 변경 (아군 인식은 되도록 레이어 유지, 대신 IsFused 플래그로 집기 방지)
        _isFused = true;

        if (_stat.Health != null)
        {
            _stat.Health.OnDeath -= HandleDeath;
            _stat.Health.OnDeath += HandleDeath;
        }
    }

    private void HandleDeath()
    {
        Debug.Log($"<color=orange>[Fusion]</color> Golem Died! Triggering Defuse.");
        Defuse();
    }

    private void Update()
    {
        if (!_isFused) return;

        _timer -= Time.deltaTime;

        if (_timer <= 0f)
        {
            Debug.Log($"<color=orange>[Fusion]</color> Timer ended! Triggering Defuse.");
            Defuse();
        }
    }

    private void Defuse()
    {
        if (!_isFused) return;
        _isFused = false;

        // 현재 체력 비율
        float curHPRatio = (_stat != null && _stat.Health != null && _stat.MAXHP > 0) ? _stat.Health.CurHP / _stat.MAXHP : 0f;
        
        // 공식: 0.5 + (현재 체력 비율 * 0.5)
        float returnHPRatio = 0.5f + (curHPRatio * 0.5f);

        Debug.Log($"<color=orange>[Fusion]</color> Defuse called! Materials count: {_materials.Count}, Golem HP Ratio: {curHPRatio}");

        foreach (var material in _materials)
        {
            if (material != null && material.gameObject != null)
            {
                var rootObj = material.transform.root.gameObject;
                
                // NavMeshAgent가 활성화된 상태에서 transform.position을 변경하면 무시될 수 있으므로, 활성화 전에 위치를 옮깁니다.
                rootObj.transform.position = transform.position + (Vector3)Random.insideUnitCircle * 0.5f;
                
                rootObj.SetActive(true);
                Debug.Log($"<color=orange>[Fusion]</color> Reactivated minion: {rootObj.name} at {rootObj.transform.position}");
                
                var stat = material.GetComponent<CharacterStat>();
                if (stat != null && stat.Health != null)
                {
                    // 반환 체력 설정
                    float targetHP = Mathf.Max(1f, stat.MAXHP * returnHPRatio);
                    Debug.Log($"<color=orange>[Fusion]</color> Minion HP update: {stat.Health.CurHP} -> {targetHP} (Ratio: {returnHPRatio})");
                    
                    if (stat.Health.CurHP > targetHP)
                    {
                        stat.Health.GetDamage(new DamageInfo(stat.Health.CurHP - targetHP, DamageType.Fixed, null));
                    }
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[Fusion]</color> Material is null or destroyed!");
            }
        }

        _materials.Clear();
        
        // 확실한 파괴 (이미 죽어서 Destroy가 예약된 상태가 아닐 때만 직접 파괴)
        if (_stat != null && _stat.Health != null && !_stat.Health.IsDead)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (_stat != null && _stat.Health != null)
        {
            _stat.Health.OnDeath -= HandleDeath;
        }
        
        if (_isFused)
        {
            Debug.Log($"<color=orange>[Fusion]</color> Destroyed unexpectedly! Defusing as fallback.");
            Defuse();
        }
    }

    // 외부(ThrowController 등)에서 집기 시도 시 막기 위해
    public bool IsFused => _isFused;
}
