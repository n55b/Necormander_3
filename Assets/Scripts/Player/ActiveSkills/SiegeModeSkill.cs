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

    private GameObject _trajectoryObj;
    private LineRenderer _lineRenderer;

    public void Initialize(PlayerController player)
    {
        _player = player;
        _lastUsedTime = -9999f;
        IsActive = false;
    }

    public void OnInputStart()
    {
        if (IsActive) OnDeactivate();
        else if (!IsOnCooldown) OnActivate();
        else Debug.Log($"<color=orange>[Skill]</color> {SkillName} 쿨타임 중입니다.");
    }

    public void OnInputHold() { }
    public void OnInputRelease() { }

    public void OnActivate()
    {
        IsActive = true;
        _player.SetInputBlocked(true); // 이동 불가

        if (CameraTargetController.Instance != null)
        {
            CameraTargetController.Instance.SetAiming(true); // 시야 넓히기
        }
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.SetZoom(true); // 줌아웃
        }

        _trajectoryObj = new GameObject("SiegeTrajectory");
        _lineRenderer = _trajectoryObj.AddComponent<LineRenderer>();
        _lineRenderer.startWidth = 0.05f;
        _lineRenderer.endWidth = 0.05f;
        _lineRenderer.positionCount = 50;
        _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _lineRenderer.startColor = Color.red;
        _lineRenderer.endColor = Color.red;
        _lineRenderer.sortingOrder = 50;

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
        if (CameraManager.Instance != null)
        {
            CameraManager.Instance.SetZoom(false); // 줌복구
        }

        if (_trajectoryObj != null)
        {
            Object.Destroy(_trajectoryObj);
        }

        Debug.Log("<color=cyan>[SiegeMode]</color> 시즈 모드 종료.");
    }

    public void UpdateSkill()
    {
        if (IsActive)
        {
            // 탄약(소환수)이 0이 되면 자동 종료
            if (GetAliveMinionCount() <= 0)
            {
                OnDeactivate();
                return;
            }

            if (_lineRenderer != null)
            {
                Vector2 startPos = _player.transform.position;
                Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                
                int numPoints = 50;
                float distance = Vector2.Distance(startPos, targetPos);
                float maxHeight = Mathf.Min(distance * 0.5f, 3f); // 포물선 최대 높이

                Vector3[] points = new Vector3[numPoints];
                for (int i = 0; i < numPoints; i++)
                {
                    float t = i / (float)(numPoints - 1);
                    Vector2 currentPos = Vector2.Lerp(startPos, targetPos, t);
                    float height = 4f * maxHeight * t * (1f - t);
                    points[i] = new Vector3(currentPos.x, currentPos.y + height, 0f);
                }
                _lineRenderer.SetPositions(points);
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
        float minDistance = float.MaxValue;
        Vector2 playerPos = _player.transform.position;

        foreach (var ally in allies)
        {
            if (ally.Stats != null && !ally.Stats.Health.IsDead && ally.MinionType != CommandData.None)
            {
                float dist = Vector2.Distance(playerPos, ally.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    targetAmmo = ally;
                }
            }
        }

        if (targetAmmo == null)
        {
            OnDeactivate();
            return;
        }

        Vector2 targetPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        
        GameManager.Instance.cameraManager.HitShakeCamera();
        
        // 미니언 시각 요소 복사 및 날아가기 연출 (플레이어 위치에서 시작)
        SpriteRenderer sr = targetAmmo.GetComponentInChildren<SpriteRenderer>();
        GameObject projObj = new GameObject("ArtilleryProjectile");
        projObj.transform.position = _player.transform.position; // 플레이어에서 발사
        if (sr != null)
        {
            SpriteRenderer newSr = projObj.AddComponent<SpriteRenderer>();
            newSr.sprite = sr.sprite;
            newSr.color = sr.color;
            newSr.sortingOrder = 100;
        }

        var artillery = projObj.AddComponent<SiegeArtilleryProjectile>();
        artillery.Initialize(targetPos, _player.gameObject);

        // 탄약 즉시 사망 처리 (로직상 소비됨)
        targetAmmo.Stats.Health.GetDamage(new DamageInfo(99999f, DamageType.Fixed, _player.gameObject));
    }
}

public class SiegeArtilleryProjectile : MonoBehaviour
{
    private Vector2 _startPos;
    private Vector2 _targetPos;
    private GameObject _playerObj;
    private float _time;
    private float _duration = 0.5f;

    public void Initialize(Vector2 targetPos, GameObject playerObj)
    {
        _startPos = transform.position;
        _targetPos = targetPos;
        _playerObj = playerObj;
        _time = 0f;
    }

    private void Update()
    {
        _time += Time.deltaTime;
        float t = Mathf.Clamp01(_time / _duration);
        
        // 포물선 궤적 (y축 높이 궤적 그리기와 동일하게 연산)
        float distance = Vector2.Distance(_startPos, _targetPos);
        float maxHeight = Mathf.Min(distance * 0.5f, 3f);
        float height = 4f * maxHeight * t * (1f - t);

        Vector2 currentPos = Vector2.Lerp(_startPos, _targetPos, t);
        currentPos.y += height;

        transform.position = currentPos;

        if (t >= 1f)
        {
            Explode();
        }
    }

    private void Explode()
    {
        float explosionRadius = 3f;
        Collider2D[] colliders = Physics2D.OverlapCircleAll(_targetPos, explosionRadius, LayerMask.GetMask("Enemy"));
        foreach (var col in colliders)
        {
            if (col.TryGetComponent<EnemyController>(out var enemy))
            {
                if (enemy.Stats != null && !enemy.Stats.Health.IsDead)
                {
                    float maxHp = enemy.Stats.Health.MaxHP;
                    float damagePercent = 0.33f;
                    if (enemy.MinionData != null && enemy.MinionData.isElite) damagePercent = 0.10f;

                    float damage = maxHp * damagePercent;
                    enemy.Stats.Health.GetDamage(new DamageInfo(damage, DamageType.Fixed, _playerObj));
                }
            }
        }
        
        Destroy(gameObject);
    }
}
