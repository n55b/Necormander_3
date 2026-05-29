using UnityEngine;
using System.Collections.Generic;

public class PoisonUniqueManager : MonoBehaviour
{
    public static PoisonUniqueManager Instance;

    private BaseEntity currentHost;
    private float spreadTimer = 0f;
    private const float SPREAD_INTERVAL = 3.0f;
    private const float SPREAD_RADIUS = 5.0f;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    private void Update()
    {
        var inven = InventoryManager.Instance;
        if (inven == null || !inven.HasUniqueEffect(GemUniqueType.PoisonHost))
            return;

        // 숙주가 없거나 죽었다면 새로운 숙주 탐색
        if (currentHost == null || currentHost.Stats.Health.IsDead)
        {
            AssignNewHost();
        }

        // 3초마다 숙주의 스택 10% 광역 전염
        if (currentHost != null && !currentHost.Stats.Health.IsDead)
        {
            spreadTimer += Time.deltaTime;
            if (spreadTimer >= SPREAD_INTERVAL)
            {
                spreadTimer = 0f;
                SpreadPoisonFromHost();
            }
        }
    }

    private void AssignNewHost()
    {
        // 씬 내의 적을 찾기 (단순화: BaseEntity를 모두 탐색)
        BaseEntity[] entities = FindObjectsByType<BaseEntity>(FindObjectsSortMode.None);
        List<BaseEntity> aliveEnemies = new List<BaseEntity>();

        foreach (var ent in entities)
        {
            if (ent.team == Team.Enemy && !ent.Stats.Health.IsDead)
            {
                aliveEnemies.Add(ent);
            }
        }

        if (aliveEnemies.Count > 0)
        {
            currentHost = aliveEnemies[Random.Range(0, aliveEnemies.Count)];
            Debug.Log($"<color=green>[PoisonHost]</color> 새로운 숙주 지정: {currentHost.gameObject.name}");
            
            // 시각적 피드백 (선택 사항: 외곽선이나 특수 파티클)
            var renderer = currentHost.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null)
            {
                // 색상을 살짝 독성 띄게 변경
                renderer.color = new Color(0.7f, 1f, 0.7f);
            }
        }
        else
        {
            currentHost = null;
        }
    }

    private void SpreadPoisonFromHost()
    {
        if (currentHost == null) return;
        
        var status = currentHost.Stats.Status;
        int hostStack = status.GetDebuffStack(DebuffStackType.Poison);
        
        if (hostStack > 0)
        {
            float passAmount = hostStack * 0.1f;
            if (passAmount < 1f) return; // 전염량이 너무 적으면 무시 (옵션)

            LayerMask enemyLayer = LayerMask.GetMask("Enemy");
            Collider2D[] colls = Physics2D.OverlapCircleAll(currentHost.transform.position, SPREAD_RADIUS, enemyLayer);
            
            foreach (var col in colls)
            {
                if (col.gameObject == currentHost.gameObject) continue;

                var targetStatus = col.GetComponentInChildren<CharacterStatus>();
                if (targetStatus != null)
                {
                    targetStatus.AddDebuffStack(DebuffStackType.Poison, passAmount);
                }
            }
        }
    }
}
