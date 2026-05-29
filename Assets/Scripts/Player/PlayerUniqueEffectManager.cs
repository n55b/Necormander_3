using UnityEngine;
using System.Collections.Generic;

public class PlayerUniqueEffectManager : MonoBehaviour
{
    [Header("부식석 발자취 (Poison Footprint)")]
    public float poisonSpawnDistance = 0.5f; // 충돌체 생성 간격
    private Vector2 _lastPoisonSpawnPosition;
    private TrailRenderer _poisonTrail;

    private void Start()
    {
        InitPoisonFootprint();
    }

    private void Update()
    {
        HandlePoisonFootprint();
        
        // 향후 추가될 다른 유니크 효과들은 여기에 함수 단위로 추가합니다.
        // 예: HandleFireAura();
    }

    private void InitPoisonFootprint()
    {
        _lastPoisonSpawnPosition = transform.position;
        
        // 그림판처럼 끊임없이 이어지는 시각 효과(TrailRenderer) 생성
        _poisonTrail = gameObject.GetComponent<TrailRenderer>();
        if (_poisonTrail == null)
        {
            _poisonTrail = gameObject.AddComponent<TrailRenderer>();
        }
        
        _poisonTrail.time = 5.0f; // 5초 유지 후 꼬리부터 서서히 사라짐
        _poisonTrail.startWidth = 1.0f;
        _poisonTrail.endWidth = 1.0f;
        
        // 스프라이트 기본 머티리얼 사용 (URP/Standard 모두 호환)
        Material trailMat = new Material(Shader.Find("Sprites/Default"));
        _poisonTrail.material = trailMat;
        
        // 독성 느낌의 초록색 그라데이션 (시작은 반투명, 끝은 완전 투명)
        _poisonTrail.startColor = new Color(0f, 0.8f, 0f, 0.6f);
        _poisonTrail.endColor = new Color(0f, 0.8f, 0f, 0.0f);
        
        _poisonTrail.sortingLayerName = "Background"; // 캐릭터 뒤에 그려지도록
        _poisonTrail.sortingOrder = 10;
        _poisonTrail.emitting = false;
    }

    private void HandlePoisonFootprint()
    {
        var inven = InventoryManager.Instance;
        bool hasUnique = (inven != null && inven.HasUniqueEffect(GemUniqueType.PoisonFootprint));
        
        // 유니크 효과가 있을 때만 궤적 그리기 활성화
        if (_poisonTrail != null)
        {
            _poisonTrail.emitting = hasUnique;
        }

        if (!hasUnique)
            return;

        // 투명한 충돌체(Trigger)를 촘촘하게 생성하여 적에게 독 부여
        float dist = Vector2.Distance(transform.position, _lastPoisonSpawnPosition);
        if (dist >= poisonSpawnDistance)
        {
            SpawnInvisibleTrigger(transform.position);
            _lastPoisonSpawnPosition = transform.position;
        }
    }

    private void SpawnInvisibleTrigger(Vector3 pos)
    {
        // 렌더러 없이 보이지 않는 빈 게임오브젝트 생성
        GameObject footprint = new GameObject("PoisonFootprint_Trigger");
        footprint.transform.position = pos;

        // 원형 충돌체 촘촘하게 설정
        var circle = footprint.AddComponent<CircleCollider2D>();
        circle.radius = 0.6f;
        circle.isTrigger = true;

        // 독성 부여 스크립트 추가
        footprint.AddComponent<PoisonPuddle>();

        Destroy(footprint, 5.0f); // 5초 뒤 보이지 않는 충돌체 소멸
    }
}

public class PoisonPuddle : MonoBehaviour
{
    private float tickTimer = 0f;
    private const float TICK_INTERVAL = 3.0f;
    private List<CharacterStatus> targetsInPuddle = new List<CharacterStatus>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var stat = collision.GetComponentInParent<CharacterStat>();
        if (stat == null) stat = collision.GetComponentInChildren<CharacterStat>();
        
        if (stat != null && stat.IsEnemy)
        {
            var status = stat.Status;
            if (status != null && !targetsInPuddle.Contains(status))
            {
                targetsInPuddle.Add(status);
                // 들어오자마자 1스택
                status.AddDebuffStack(DebuffStackType.Poison, 1f);
            }
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        var stat = collision.GetComponentInParent<CharacterStat>();
        if (stat == null) stat = collision.GetComponentInChildren<CharacterStat>();
        
        if (stat != null)
        {
            var status = stat.Status;
            if (status != null && targetsInPuddle.Contains(status))
            {
                targetsInPuddle.Remove(status);
            }
        }
    }

    private void Update()
    {
        tickTimer += Time.deltaTime;
        if (tickTimer >= TICK_INTERVAL)
        {
            tickTimer = 0f;
            // 남아있는 적들에게 독 스택 추가
            for (int i = targetsInPuddle.Count - 1; i >= 0; i--)
            {
                if (targetsInPuddle[i] == null || targetsInPuddle[i].gameObject == null)
                {
                    targetsInPuddle.RemoveAt(i);
                    continue;
                }
                targetsInPuddle[i].AddDebuffStack(DebuffStackType.Poison, 1f);
            }
        }
    }
}
