using UnityEngine;
using System.Collections.Generic;

public class PoisonFootprintSpawner : MonoBehaviour
{
    private Vector2 lastSpawnPosition;
    public float spawnDistance = 1.5f; // 이동 거리 기준 스폰

    private void Start()
    {
        lastSpawnPosition = transform.position;
    }

    private void Update()
    {
        var inven = InventoryManager.Instance;
        if (inven == null || !inven.HasUniqueEffect(GemUniqueType.PoisonFootprint))
            return;

        float dist = Vector2.Distance(transform.position, lastSpawnPosition);
        if (dist >= spawnDistance)
        {
            SpawnFootprint(transform.position);
            lastSpawnPosition = transform.position;
        }
    }

    private void SpawnFootprint(Vector3 pos)
    {
        var registry = GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY;
        GameObject footprint = null;

        if (registry != null && registry.poisonFootprintPrefab != null)
        {
            // 프리팹이 연결된 경우
            footprint = Instantiate(registry.poisonFootprintPrefab, pos, Quaternion.identity);
            
            // 만약 프리팹 자체에 콜라이더와 PoisonPuddle 컴포넌트가 없다면 붙여줌
            if (footprint.GetComponent<PoisonPuddle>() == null)
            {
                var col = footprint.GetComponent<Collider2D>();
                if (col == null)
                {
                    var circle = footprint.AddComponent<CircleCollider2D>();
                    circle.radius = 0.5f;
                    circle.isTrigger = true;
                }
                else
                {
                    col.isTrigger = true;
                }
                footprint.AddComponent<PoisonPuddle>();
            }
        }
        else
        {
            // 프리팹이 없을 경우 임시 방법 (Quad 생성)
            footprint = GameObject.CreatePrimitive(PrimitiveType.Quad);
            footprint.name = "PoisonFootprint";
            footprint.transform.position = pos;
            footprint.transform.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            footprint.transform.localScale = Vector3.one * 1.5f;

            // 투명하고 초록색인 마테리얼 (시각적 피드백)
            var renderer = footprint.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0f, 0.8f, 0f, 0.4f);
            }

            // 트리거 설정
            var col = footprint.GetComponent<Collider2D>();
            if (col == null) 
            {
                var circle = footprint.AddComponent<CircleCollider2D>();
                circle.radius = 0.5f;
                circle.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }

            footprint.AddComponent<PoisonPuddle>();
        }

        Destroy(footprint, 5.0f); // 5초 뒤 사라짐
    }
}

public class PoisonPuddle : MonoBehaviour
{
    private float tickTimer = 0f;
    private const float TICK_INTERVAL = 3.0f;
    private List<CharacterStatus> targetsInPuddle = new List<CharacterStatus>();

    private void OnTriggerEnter2D(Collider2D collision)
    {
        var stat = collision.GetComponentInChildren<CharacterStat>();
        if (stat != null && stat.IsEnemy)
        {
            var status = collision.GetComponentInChildren<CharacterStatus>();
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
        var status = collision.GetComponentInChildren<CharacterStatus>();
        if (status != null && targetsInPuddle.Contains(status))
        {
            targetsInPuddle.Remove(status);
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
