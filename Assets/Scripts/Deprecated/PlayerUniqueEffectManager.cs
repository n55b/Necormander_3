using UnityEngine;
using System.Collections.Generic;

public class PlayerUniqueEffectManager : MonoBehaviour
{
    private void Start()
    {
        // 향후 플레이어 전용 유니크 효과 초기화
    }

    private void Update()
    {
        // 향후 플레이어 전용 유니크 효과 루프 처리
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
