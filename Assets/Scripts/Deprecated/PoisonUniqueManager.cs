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
        if (inven == null || true /* !inven.HasUniqueEffect(GemUniqueType.PoisonHost) */)
            return;

        // 숙주가 없거나 죽었다면 새로운 숙주 탐색
        if (currentHost == null || currentHost.Stats.Health.IsDead)
        {
            AssignNewHost();
        }

        // 3초마다 숙주의 스택 10% 광역 전염
        if (currentHost != null && !currentHost.Stats.Health.IsDead)
        {
            // 숙주 아이콘을 UI에 띄우기 위해 지속적으로 시간 갱신 (0.5초)
            if (currentHost.Stats != null && currentHost.Stats.Status != null)
            {
                currentHost.Stats.Status.SetDebuffBool(DebuffBoolType.Bleeding, 0.5f);
            }

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
            var visualFeedback = currentHost.GetComponentInChildren<CharacterVisualFeedback>();
            if (visualFeedback != null)
            {
                // 색상을 살짝 독성 띄게 변경 (피격 후에도 유지되도록 원본 색상 자체를 변경)
                visualFeedback.SetBaseColor(new Color(0.7f, 1f, 0.7f));
            }
            else
            {
                var renderer = currentHost.GetComponentInChildren<SpriteRenderer>();
                if (renderer != null) renderer.color = new Color(0.7f, 1f, 0.7f);
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
        int hostStack = status.GetDebuffStack(DebuffStackType.BloodPop);
        
        if (hostStack > 0)
        {
            // [수정] 최소 1스택 이상은 무조건 전염되도록 보장
            float passAmount = Mathf.Max(1f, hostStack * 0.1f);

            LayerMask enemyLayer = Layers.EnemyMask;
            Collider2D[] colls = Physics2D.OverlapCircleAll(currentHost.transform.position, SPREAD_RADIUS, enemyLayer);
            
            foreach (var col in colls)
            {
                if (col.gameObject == currentHost.gameObject) continue;

                var targetStatus = col.GetComponentInChildren<CharacterStatus>();
                if (targetStatus != null)
                {
                    targetStatus.AddDebuffStack(DebuffStackType.BloodPop, passAmount);
                }
            }
        }
    }
}
