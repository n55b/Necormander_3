using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 노화(Aging) 관련 유니크 효과를 관리하는 매니저입니다.
/// </summary>
public class AgingUniqueManager : MonoBehaviour
{
    public static CharacterStatus HighestAgingEnemy { get; private set; }

    private float _updateInterval = 0.5f;
    private float _timer = 0f;

    // 장판 시각 효과용 (필요 시 임시 오브젝트 사용)
    private GameObject _goryeojangAuraVFX;

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer >= _updateInterval)
        {
            _timer = 0f;
            UpdateGoryeojangTarget();
        }
    }

    private void UpdateGoryeojangTarget()
    {
        var inven = InventoryManager.Instance;
        if (inven == null || !inven.HasUniqueEffect(GemUniqueType.Goryeojang))
        {
            HighestAgingEnemy = null;
            if (_goryeojangAuraVFX != null) _goryeojangAuraVFX.SetActive(false);
            return;
        }

        CharacterStatus target = null;
        float maxStack = 0;

        foreach (var enemy in CharacterStatus.ActiveEnemies)
        {
            if (enemy == null) continue;
            float currentStack = enemy.GetDebuffStack(DebuffStackType.Aging);
            if (currentStack > maxStack)
            {
                maxStack = currentStack;
                target = enemy;
            }
        }

        HighestAgingEnemy = target;

        if (HighestAgingEnemy != null)
        {
            if (_goryeojangAuraVFX == null)
            {
                _goryeojangAuraVFX = new GameObject("GoryeojangAura_Temp");
                // 임시로 시각적 표현이 필요하다면 SpriteRenderer 등을 붙일 수 있습니다.
            }
            _goryeojangAuraVFX.SetActive(true);
            _goryeojangAuraVFX.transform.position = HighestAgingEnemy.transform.position;
            
            // 임시 크기 적용 (비폭 기본 폭발 반경이 2.0f 이므로 지름 4.0f)
            _goryeojangAuraVFX.transform.localScale = Vector3.one * 4.0f; 
        }
        else
        {
            if (_goryeojangAuraVFX != null) _goryeojangAuraVFX.SetActive(false);
        }
    }
}
