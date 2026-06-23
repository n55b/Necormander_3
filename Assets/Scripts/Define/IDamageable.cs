/// <summary>
/// 모든 피격 가능한 오브젝트(캐릭터, 함정 등)가 구현해야 하는 데미지 처리 인터페이스입니다.
/// </summary>
public interface IDamageable
{
    void TakeDamage(DamageInfo info);
    bool IsDead { get; }
}
