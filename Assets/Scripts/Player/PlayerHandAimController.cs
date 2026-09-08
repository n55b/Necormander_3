using UnityEngine;

/// <summary>
/// Hand 오브젝트의 회전값을 매 프레임 덮어써, 기존 스프라이트/클립 키프레임은 건드리지 않고
/// 공격 방향(평타) 또는 조준 방향(스킬)을 향하도록 틀어줍니다.
/// - 평타 중: MeleeCombatController.CurrentAttackDir 기준
/// </summary>
public class PlayerHandAimController : MonoBehaviour
{
    private MeleeCombatController _melee;
    private Transform _root;

    private void Awake()
    {
        _melee = GetComponentInParent<MeleeCombatController>();
        _root = _melee != null ? _melee.transform : transform.root;
    }

    private void LateUpdate()
    {
        if (_melee == null) return;

        if (_melee.IsAttacking)
        {
            Vector2 dir = _melee.CurrentAttackDir;
            if (dir.sqrMagnitude < 0.0001f) return;

            float worldAngle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;

            // 루트(플레이어 본체)가 좌우 반전(localScale.x < 0)되어 있으면
            // 그 반전을 상쇄하기 위해 각도를 좌우 대칭으로 미러링합니다.
            bool flipped = _root.localScale.x < 0f;
            float localAngle = flipped ? worldAngle : (180f - worldAngle);

            transform.localRotation = Quaternion.Euler(0f, 0f, localAngle);
        }
        else
        {
            transform.localRotation = Quaternion.identity;
        }
    }
}
