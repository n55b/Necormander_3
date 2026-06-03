using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FusionMinionController : MonoBehaviour
{
    private List<CharacterStatus> _materials = new List<CharacterStatus>();
    private float _timer = 0f;
    private float _duration = 10f; // 기본 10초
    private bool _isFused = false;
    private CharacterStat _stat;

    public void Setup(List<IThrowable> units, float duration, float hpRatio, float scaleMultiplier, Color color, string popupName)
    {
        _stat = GetComponent<CharacterStat>();
        if (_stat == null) return;

        _duration = duration;
        _timer = duration;
        _materials.Clear();

        float totalMaxHP = 0f;
        float totalCurHP = 0f;
        float totalAtk = 0f;

        // 재료 미니언들 숨기기 및 스탯 합산
        foreach (var unit in units)
        {
            if (unit is MonoBehaviour mb)
            {
                var stat = mb.GetComponent<CharacterStat>();
                if (stat != null)
                {
                    totalMaxHP += stat.MAXHP;
                    totalCurHP += stat.CURHP;
                    totalAtk += stat.ATK;
                    _materials.Add(stat.Status);
                }
                mb.gameObject.SetActive(false); // 비활성화
            }
        }

        // 융합체 스탯 오버라이드
        _stat.SetBaseMoveSpeed(_stat.MOVESPEED); // 이속 유지
        _stat.ApplySplitStats(); // 기존 스탯 초기화 우회
        
        // 데이터 주입 (Base Entity 또는 SO에서 초기화된 후 덮어쓰기)
        _stat.InitializeStats(null); // 더미 호출로 덮어씌울 준비
        StartCoroutine(OverrideStatsNextFrame(totalMaxHP, totalCurHP, totalAtk, scaleMultiplier, color, popupName));

        _isFused = true;
    }

    private IEnumerator OverrideStatsNextFrame(float totalMaxHP, float totalCurHP, float totalAtk, float scaleMultiplier, Color color, string popupName)
    {
        yield return null; // 1프레임 대기 후 덮어쓰기

        // 반사 리플렉션을 사용할 수 없으므로 직접 조작 불가하다면 Health 컴포넌트에 직접 설정
        if (_stat.Health != null)
        {
            _stat.Health.GetDamage(new DamageInfo(_stat.Health.CurHP - totalCurHP, DamageType.Fixed, null));
        }

        transform.localScale = Vector3.one * scaleMultiplier;
        
        // 색상 변경
        var renderers = GetComponentsInChildren<SpriteRenderer>();
        foreach (var r in renderers)
        {
            r.color = color;
        }

        // 집기 방지를 위한 태그/레이어 변경 (Army -> Default 등, 단 아군 인식은 되도록)
        // 일단 IThrowable의 픽업을 방지하기 위해 isFused 플래그로 예외 처리 권장
        // 임시로 레이어 변경
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    private void Update()
    {
        if (!_isFused) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f || (_stat != null && _stat.Health != null && _stat.Health.IsDead))
        {
            Defuse();
        }
    }

    private void Defuse()
    {
        _isFused = false;

        // 현재 체력 비율
        float curHPRatio = (_stat.Health != null && _stat.MAXHP > 0) ? _stat.Health.CurHP / _stat.MAXHP : 0f;
        // 공식: 0.5 + (현재 체력 비율 * 0.5)
        float returnHPRatio = 0.5f + (curHPRatio * 0.5f);

        foreach (var material in _materials)
        {
            if (material != null && material.gameObject != null)
            {
                material.gameObject.SetActive(true);
                material.transform.position = transform.position + (Vector3)Random.insideUnitCircle * 0.5f;
                
                var stat = material.GetComponent<CharacterStat>();
                if (stat != null && stat.Health != null)
                {
                    // 반환 체력 설정
                    float newHP = stat.MAXHP * returnHPRatio;
                    if (stat.Health.CurHP > newHP)
                    {
                        stat.Health.GetDamage(new DamageInfo(stat.Health.CurHP - newHP, DamageType.Fixed, null));
                    }
                }
            }
        }

        _materials.Clear();
        
        // 자신 파괴
        if (_stat != null && _stat.Health != null && !_stat.Health.IsDead)
        {
            _stat.Health.GetDamage(new DamageInfo(9999f, DamageType.Fixed, null));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // 외부(ThrowController 등)에서 집기 시도 시 막기 위해
    public bool IsFused => _isFused;
}
