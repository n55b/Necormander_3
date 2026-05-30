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
                var registry = GameManager.Instance != null && GameManager.Instance.dataManager != null ? GameManager.Instance.dataManager.THROW_EFFECT_REGISTRY : null;
                
                if (registry != null && registry.goryeojangAuraPrefab != null)
                {
                    _goryeojangAuraVFX = Instantiate(registry.goryeojangAuraPrefab);
                }
                else
                {
                    _goryeojangAuraVFX = new GameObject("GoryeojangAura_Temp");
                    var sr = _goryeojangAuraVFX.AddComponent<SpriteRenderer>();
                    sr.sprite = CreateCircleSprite();
                    sr.color = new Color(0f, 0f, 0f, 0.5f); // 50% 반투명 검정색
                }
            }
            _goryeojangAuraVFX.SetActive(true);
            _goryeojangAuraVFX.transform.position = HighestAgingEnemy.transform.position;
            
            // 임시 크기 적용 (비폭 기본 폭발 반경이 2.0f 이므로 지름 4.0f) - 프리팹이면 자체 크기를 쓰거나 맞게 스케일
            _goryeojangAuraVFX.transform.localScale = Vector3.one * 4.0f; 
        }
        else
        {
            if (_goryeojangAuraVFX != null) _goryeojangAuraVFX.SetActive(false);
        }
    }

    private Sprite CreateCircleSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color transparent = new Color(0, 0, 0, 0);
        Color black = Color.white; 
        float radius = size / 2f;
        Vector2 center = new Vector2(radius, radius);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (Vector2.Distance(center, new Vector2(x, y)) <= radius)
                    texture.SetPixel(x, y, black);
                else
                    texture.SetPixel(x, y, transparent);
            }
        }
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
