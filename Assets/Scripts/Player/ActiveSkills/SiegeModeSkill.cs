using UnityEngine;
using System.Collections.Generic;

public class SiegeModeSkill : IActiveSkill
{
    public string SkillName => "시즈 모드";
    public float Cooldown => 8f;
    
    public bool IsActive { get; private set; }
    public bool IsOnCooldown => Time.time < _lastUsedTime + Cooldown;

    private PlayerController _player;
    private float _lastUsedTime;

    public void Initialize(PlayerController player)
    {
        _player = player;
        _lastUsedTime = -9999f;
        IsActive = false;
    }

    public void OnActivate()
    {
        IsActive = true;
        _player.SetInputBlocked(true); // 이동 불가

        if (CameraTargetController.Instance != null)
        {
            CameraTargetController.Instance.SetAiming(true); // 시야 넓히기
        }
        Debug.Log("<color=cyan>[SiegeMode]</color> 시즈 모드 활성화!");
    }

    public void OnDeactivate()
    {
        IsActive = false;
        _lastUsedTime = Time.time;
        _player.SetInputBlocked(false); // 이동 가능

        if (CameraTargetController.Instance != null)
        {
            CameraTargetController.Instance.SetAiming(false);
        }
        Debug.Log("<color=cyan>[SiegeMode]</color> 시즈 모드 종료.");
    }

    public void UpdateSkill()
    {
        if (IsActive)
        {
            // 탄약(소환수)이 0이 되면 자동 종료하는 로직 추가 필요
            if (GetAliveMinionCount() <= 0)
            {
                OnDeactivate();
            }
        }
    }

    public bool HandleLeftClick()
    {
        if (!IsActive) return false;

        // 좌클릭 시 포격 실시
        FireArtillery();
        return true;
    }

    private int GetAliveMinionCount()
    {
        // TODO: AllyManager에서 살아있는 소환수 개수 가져오기
        var allies = Object.FindObjectsByType<AllyController>(FindObjectsSortMode.None);
        int count = 0;
        foreach (var ally in allies)
        {
            if (ally.Stats != null && !ally.Stats.Health.IsDead && ally.MinionType != CommandData.None)
                count++;
        }
        return count;
    }

    private void FireArtillery()
    {
        var allies = Object.FindObjectsByType<AllyController>(FindObjectsSortMode.None);
        AllyController targetAmmo = null;
        foreach (var ally in allies)
        {
            if (ally.Stats != null && !ally.Stats.Health.IsDead && ally.MinionType != CommandData.None)
            {
                targetAmmo = ally;
                break;
            }
        }

        if (targetAmmo == null)
        {
            OnDeactivate();
            return;
        }

        // 탄약 사망 처리 (고정 피해)
        targetAmmo.Stats.Health.GetDamage(new DamageInfo(99999f, DamageType.Fixed, _player.gameObject));

        // 포격체 생성 (투포환)
        Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        // TODO: 포격 프리팹 생성 및 폭발 로직 연결 (33% / 10% / 5% 로직 구현)
        GameManager.Instance.cameraManager.HitShakeCamera();
        Debug.Log($"<color=red>[SiegeMode]</color> {targetPos} 위치로 포격 실시!");
        
        // 여기에 타격 처리 (OverlapCircle)
        float explosionRadius = 3f;
        Collider2D[] colliders = Physics2D.OverlapCircleAll(targetPos, explosionRadius, LayerMask.GetMask("Enemy"));
        foreach(var col in colliders)
        {
            if (col.TryGetComponent<EnemyController>(out var enemy))
            {
                if (enemy.Stats != null && !enemy.Stats.Health.IsDead)
                {
                    float maxHp = enemy.Stats.Health.MaxHP;
                    float damagePercent = 0.33f;
                    if (enemy.MinionData != null && enemy.MinionData.isElite) damagePercent = 0.10f;
                    // 보스 구분 플래그가 없으므로 엘리트와 동일하게 취급

                    float damage = maxHp * damagePercent;
                    enemy.Stats.Health.GetDamage(new DamageInfo(damage, DamageType.Fixed, _player.gameObject));
                }
            }
        }
    }
}
