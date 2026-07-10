using UnityEngine;
using System.Collections.Generic;

public class PlayerUniqueEffectManager : MonoBehaviour
{
    // [일단 던지고 보자] 버프 스택 관련 변수
    private int _justThrowItStacks = 0;
    private float _justThrowItTimer = 0f;
    private const float JustThrowItDuration = 8f;
    private const int MaxJustThrowItStacks = 5;
    private const float SpeedBonusPerStack = 0.08f; // 8%

    public float JustThrowItSpeedBonus
    {
        get
        {
            int gemCount = InventoryManager.Instance != null ? InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.JustThrowIt) : 0;
            return _justThrowItStacks * SpeedBonusPerStack * gemCount;
        }
    }

    private void Start()
    {
        // 향후 플레이어 전용 유니크 효과 초기화
    }

    private void Update()
    {
        // [일단 던지고 보자] 버프 타이머 감소 로직
        if (_justThrowItStacks > 0)
        {
            _justThrowItTimer -= Time.deltaTime;
            if (_justThrowItTimer <= 0f)
            {
                _justThrowItStacks = 0;
                Debug.Log("<color=cyan>[Unique]</color> 일단 던지고 보자 스택 초기화");
            }
        }

        UpdateMobMentality();
    }

    /// <summary>
    /// 포물선 투척 시 호출되는 함수
    /// </summary>
    public void OnParabolicThrow()
    {
        // 인벤토리에 '일단 던지고 보자' 유니크가 있는지 확인 (수치형이므로 개수만큼 효과 증폭)
        if (InventoryManager.Instance != null && InventoryManager.Instance.HasUniqueEffect(GemUniqueType.JustThrowIt))
        {
            int gemCount = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.JustThrowIt);
            if (gemCount > 0)
            {
                _justThrowItStacks = Mathf.Min(_justThrowItStacks + 1, MaxJustThrowItStacks);
                _justThrowItTimer = JustThrowItDuration;
                Debug.Log($"<color=cyan>[Unique]</color> 일단 던지고 보자 버프! (스택: {_justThrowItStacks}, 보유 수량 증폭 대기중)");
            }
        }
    }

    // [군중심리] 버프 스택 관련 변수
    private float _mobMentalityCheckTimer = 0f;
    private const float MobMentalityCheckInterval = 0.2f;
    public float MobMentalitySpeedBonus { get; private set; } = 0f;

    private void UpdateMobMentality()
    {
        if (InventoryManager.Instance == null || !InventoryManager.Instance.HasUniqueEffect(GemUniqueType.MobMentality))
        {
            MobMentalitySpeedBonus = 0f;
            return;
        }

        _mobMentalityCheckTimer += Time.deltaTime;
        if (_mobMentalityCheckTimer >= MobMentalityCheckInterval)
        {
            _mobMentalityCheckTimer = 0f;
            int gemCount = InventoryManager.Instance.GetUniqueEffectCount(GemUniqueType.MobMentality);
            
            float radius = GameManager.Instance.PLAYERCONTROLLER.THROWRANGE;
            Collider2D[] colls = Physics2D.OverlapCircleAll(transform.position, radius, Layers.PlayerArmy);
            
            int armyLayer = Layers.Army;
            int minionCount = 0;
            foreach (var col in colls)
            {
                if (col.gameObject.layer == armyLayer) minionCount++;
            }

            // 마리당 이속 0.1 증가 (기본 공식: 스택 * 0.1 * 마리수)
            // 보유 보석 개수에 비례하여 추가 효과
            MobMentalitySpeedBonus = minionCount * 0.1f * gemCount;
        }
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
                status.AddDebuffStack(DebuffStackType.BloodPop, 1f);
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
                targetsInPuddle[i].AddDebuffStack(DebuffStackType.BloodPop, 1f);
            }
        }
    }
}
