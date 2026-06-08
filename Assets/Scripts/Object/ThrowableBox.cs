using UnityEngine;

/// <summary>
/// 던져서 적에게 피해를 입히고 파괴되는 상자 오브젝트입니다.
/// </summary>
public class ThrowableBox : ThrowableUnit
{
    [Header("Box Settings")]
    [SerializeField] private float damageAmount = 1f;
    public float DamageAmount => damageAmount;
    [SerializeField] private float goldDropChance = 0.5f;
    [SerializeField] private int minGold = 5;
    [SerializeField] private int maxGold = 15;
    [SerializeField] private GameObject breakEffect;

    private bool _isDestroyed = false;

    public override CommandData MinionType => CommandData.None;

    // 상자 자체의 충돌 감지는 무시합니다 (ThrowCluster가 대신 처리함)
    protected override void OnTriggerEnter2D(Collider2D other) { }

    public override void OnLanded()
    {
        // 부모의 OnLanded 호출 (레이어 복구 등)
        base.OnLanded();

        // 클러스터가 적이나 벽에 부딪혔다고 판단했을 때만 파괴 로직 실행
        if (_isImpacted)
        {
            // [임시 수정] 상자가 무조건 파괴되지 않도록 파괴 로직을 주석 처리합니다.
            // DestroyBox();
        }
    }

    private void DestroyBox()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        // 1. 골드 드랍 로직
        if (Random.value <= goldDropChance)
        {
            int gold = Random.Range(minGold, maxGold + 1);
            if (InventoryManager.Instance != null)
            {
                InventoryManager.Instance.AddGold(gold);
            }
            
            if (FloatingTextManager.instance != null)
            {
                var text = FloatingTextManager.instance.GetFromPool();
                text.SetUp($"+{gold} Gold", Color.yellow, transform);
            }
        }

        // 2. 효과음 및 이펙트
        if (SoundManager.Instance != null) SoundManager.Instance.HITSoundPlay(false);
        if (breakEffect != null) Instantiate(breakEffect, transform.position, Quaternion.identity);

        // 3. 오브젝트 제거
        Destroy(gameObject);
    }
}
