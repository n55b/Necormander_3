using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 엘리트 보스가 소환한 기둥(<see cref="EliteMonsterPillar"/>) 무리의 <b>수명과 질의를 캡슐화</b>한다.
/// 브레인(SO)이 raw 리스트를 직접 만지는 대신, 이 객체에 "숨었나 / 가까운 기둥 때리기 / 전부 정리"를 묻는다.
///
/// <para><b>#6 수정</b>: 기둥은 보스의 자식이 아니라 월드에 스폰되므로 보스가 죽어도 자동으로 사라지지 않았다.
/// 브레인이 <c>CharacterHealth.OnDeath</c>(또는 SO.OnDestroy)에서 <see cref="DestroyAll"/>을 호출해
/// 확실히 정리한다. 이 한 지점만 지키면 기둥 누수/유령 콜라이더가 구조적으로 사라진다.</para>
/// </summary>
public class PillarField
{
    private readonly List<EliteMonsterPillar> _pillars = new List<EliteMonsterPillar>();

    public IReadOnlyList<EliteMonsterPillar> All => _pillars;
    public int Count => _pillars.Count;

    /// <summary>이미 계산된 위치들에 기둥을 스폰한다. (방 배치 계산은 호출측 책임 — 이 클래스는 수명만 담당)</summary>
    public void Spawn(GameObject owner, GameObject prefab, IList<Vector2> positions, int maxHP)
    {
        foreach (var pos in positions)
        {
            GameObject obj = prefab != null
                ? Object.Instantiate(prefab, pos, Quaternion.identity)
                : CreateFallbackPillar(pos);

            EliteMonsterPillar pillar = obj.GetComponent<EliteMonsterPillar>();
            if (pillar == null) pillar = obj.AddComponent<EliteMonsterPillar>();
            pillar.SetMaxHP(maxHP);
            pillar.Owner = owner;
            _pillars.Add(pillar);
        }
    }

    /// <summary>worldPos가 (숨을 수 있는) 기둥의 안전지대 안이면 그 기둥을 반환. 없으면 null.</summary>
    public EliteMonsterPillar FindSheltering(Vector2 worldPos)
    {
        foreach (var p in _pillars)
            if (p != null && p.IsSheltering(worldPos)) return p;
        return null;
    }

    /// <summary>center 반경 radius 안의 살아있는 기둥들에 내구도 피해.</summary>
    public void DamageInRadius(Vector2 center, float radius, int amount)
    {
        foreach (var p in _pillars)
        {
            if (p == null || !p.IsAlive) continue;
            if (Vector2.Distance(center, p.transform.position) <= radius) p.DamagePattern(amount);
        }
    }

    /// <summary>보스 사망/재초기화 시 호출: 남은 기둥을 전부 파괴하고 목록을 비운다. (#6 정리)</summary>
    public void DestroyAll()
    {
        foreach (var p in _pillars)
            if (p != null) Object.Destroy(p.gameObject);
        _pillars.Clear();
    }

    // 프리팹이 없을 때 코드로 만드는 임시 원기둥. (기존 EliteCharger.CreateFallbackPillar 이관)
    private GameObject CreateFallbackPillar(Vector2 pos)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.name = "EliteMonster_Pillar";
        obj.transform.position = pos;
        obj.transform.localScale = new Vector3(1f, 1.4f, 1f);

        var col = obj.AddComponent<CircleCollider2D>();
        col.radius = 0.55f;

        // "Obstacle" 레이어는 이 프로젝트에 없으므로 실제 존재하는 "Object" 레이어를 쓴다.
        int objectLayer = LayerMask.NameToLayer("Object");
        if (objectLayer >= 0) obj.layer = objectLayer;

        var renderer = obj.GetComponent<Renderer>();
        if (renderer != null) renderer.material.color = new Color(0.55f, 0.4f, 0.25f);
        return obj;
    }
}
