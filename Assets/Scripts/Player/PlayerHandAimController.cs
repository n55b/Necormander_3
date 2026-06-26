using UnityEngine;

/// <summary>
/// 공격 애니메이션 재생 중, 기존 스윙 모션(스프라이트 키프레임)은 그대로 둔 채로
/// Hand 오브젝트의 회전값만 추가로 공격 방향(마우스 방향)을 향하도록 틀어줍니다.
/// 애니메이션 클립이 Hand의 로컬 포지션/스프라이트를 굽어놓은 것과는 별개로
/// 로컬 회전(Z)만 매 프레임 덮어쓰기 때문에 기존 클립을 건드리지 않습니다.
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
