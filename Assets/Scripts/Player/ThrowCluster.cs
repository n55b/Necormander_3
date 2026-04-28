using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 2D 환경에서 여러 유닛을 하나로 묶어 던지기 위한 클러스터 오브젝트입니다.
/// 모든 유닛을 대신해 단일 Circle 물리 충돌과 궤적 이동을 처리합니다.
/// </summary>
public class ThrowCluster : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float baseRadius = 0.35f;
    [SerializeField] private float radiusPerUnit = 0.05f;
    [SerializeField] private Transform visualCircle; // [추가] 인스펙터에서 자식 Circle 스프라이트 할당

    private ArcMovement _arcMovement;
    private CircleCollider2D _collider;
    private Rigidbody2D _rb;
    private List<AllyController> _units = new List<AllyController>();
    private bool _isDirectThrow = false;
    
    private void Awake()
    {
        // 물리 및 이동 컴포넌트 자동 설정
        _arcMovement = gameObject.AddComponent<ArcMovement>();
        _collider = gameObject.AddComponent<CircleCollider2D>();
        _collider.isTrigger = true;
        
        _rb = gameObject.AddComponent<Rigidbody2D>();
        _rb.gravityScale = 0;
        _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _rb.simulated = false;

        // 레이어를 FlyingObject로 설정
        int flyingLayer = LayerMask.NameToLayer("FlyingObject");
        if (flyingLayer != -1) gameObject.layer = flyingLayer;

        // [수정] 처음부터 꺼두지 않고, 유닛이 있을 때만 보이도록 설정
        if (visualCircle != null) visualCircle.gameObject.SetActive(false);
    }

    /// <summary>
    /// 던질 유닛들을 클러스터 안으로 모으고 크기를 설정합니다.
    /// </summary>
    public void Setup(List<AllyController> units)
    {
        _units.Clear();
        _units.AddRange(units);

        if (_units.Count == 0)
        {
            if (visualCircle != null) visualCircle.gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);
        if (visualCircle != null) visualCircle.gameObject.SetActive(true);

        // 유닛 수에 비례하여 원의 크기 결정
        float targetRadius = baseRadius + (_units.Count - 1) * radiusPerUnit;
        _collider.radius = targetRadius;

        // 비주얼 원 크기 동기화 (Sprite의 기본 크기가 1x1일 때)
        if (visualCircle != null)
        {
            visualCircle.localScale = new Vector3(targetRadius * 2f, targetRadius * 2f, 1f);
        }

        // 모든 유닛을 클러스터 자식으로 넣고 중앙으로 정렬
        foreach (var unit in _units)
        {
            unit.transform.SetParent(this.transform);
            // 집어들었을 때의 위치 (중앙 근처)
            unit.transform.localPosition = Random.insideUnitCircle * (_collider.radius * 0.3f);
            unit.OnPickedUp(); 
        }
    }

    /// <summary>
    /// 목표 지점을 향해 클러스터를 발사합니다.
    /// </summary>
    public void Launch(Vector2 startPos, Vector2 targetPos, float duration, float maxHeight, bool isDirect, float chargeRatio)
    {
        _isDirectThrow = isDirect;

        // [추가] 모든 유닛에게 투척 데이터 전달 (효과 발동을 위해 필수)
        foreach (var unit in _units)
        {
            if (unit != null) unit.PrepareForClusterThrow(chargeRatio, isDirect);
        }

        transform.SetParent(null); // 플레이어에게서 분리
        transform.position = startPos;
        _rb.simulated = true;

        Vector2 diff = targetPos - startPos;
        float speed = diff.magnitude / duration;
        _rb.linearVelocity = diff.normalized * speed;

        _arcMovement.StartArc(duration, maxHeight);
    }

    private void Update()
    {
        // 비행 중일 때만 높이 애니메이션 적용
        if (_arcMovement != null && _arcMovement.IsFlying)
        {
            float h = _arcMovement.CurrentHeight;
            foreach (var unit in _units)
            {
                if (unit != null)
                {
                    Vector3 lp = unit.transform.localPosition;
                    lp.y = h + (unit.transform.GetSiblingIndex() * 0.01f);
                    unit.transform.localPosition = lp;
                }
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        int wallMask = LayerMask.GetMask("Wall", "Obstacle");
        bool isWall = (wallMask & (1 << other.gameObject.layer)) != 0;

        if (isWall)
        {
            _arcMovement.StopArc();
            return;
        }

        // [추가] 직구(풀차지)일 경우 적이나 오브젝트에 부딪히면 즉시 멈춤
        if (_isDirectThrow)
        {
            int opponentMask = LayerMask.GetMask("Enemy"); 
            int objectMask = LayerMask.GetMask("Object");
            
            bool isTargetHit = ((opponentMask | objectMask) & (1 << other.gameObject.layer)) != 0;
            
            if (isTargetHit)
            {
                _arcMovement.StopArc();
            }
        }
    }

    private bool _isLanded = false;

    private void OnLanded()
    {
        if (_isLanded) return;
        _isLanded = true;

        _rb.simulated = false;
        _rb.linearVelocity = Vector2.zero;

        // [구조 개편] 클러스터 통합 효과 처리
        ProcessClusterImpact();

        foreach (var unit in _units)
        {
            if (unit == null) continue;
            unit.transform.SetParent(null);
            
            // [수정] 유닛의 OnLanded에서는 시각적 효과와 AI 복구만 담당하게 함
            unit.OnLanded(); 
        }

        _units.Clear();
        Destroy(gameObject);
    }

    private void ProcessClusterImpact()
    {
        // 1. 주변 대상 스캔 (딱 한 번만 수행)
        // 아군끼리 데미지를 주지 않도록 opponentMask(Enemy)만 체크
        int opponentMask = LayerMask.GetMask("Enemy");
        float impactRadius = _collider.radius + 1.0f; // 클러스터 크기보다 조금 더 넓게 판정
        Collider2D[] hitTargets = Physics2D.OverlapCircleAll(transform.position, impactRadius, opponentMask);

        // 2. 모든 유닛의 효과를 수집하여 적용
        foreach (var unit in _units)
        {
            if (unit == null || unit.MinionData == null || unit.MinionData.throwImpact == null) continue;

            // 직접 맞은 대상이 있다면 (OnTriggerEnter2D에서 저장 가능) 전달, 없으면 주변 모두에게 적용
            // 여기서는 일단 모든 주변 타겟에게 효과 적용 (전사의 경우 광역으로 변경되는 효과)
            foreach (var targetCol in hitTargets)
            {
                ImpactContext context = new ImpactContext
                {
                    attacker = unit.gameObject,
                    target = targetCol.gameObject,
                    impactPosition = transform.position,
                    chargeRatio = 1.0f // 클러스터 발사 시 저장된 값 사용 가능
                };
                unit.MinionData.throwImpact.Apply(context);
            }
        }
        
        Debug.Log($"<color=orange>[Cluster Impact]</color> {hitTargets.Length} targets hit by {_units.Count} units.");
    }

    public float GetCurrentRadius() => _collider != null ? _collider.radius : baseRadius;
}
