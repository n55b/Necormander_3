using UnityEngine;

/// <summary>
/// Hand 오브젝트의 회전값을 매 프레임 덮어써, 기존 스프라이트/클립 키프레임은 건드리지 않고
/// 공격 방향(평타) 또는 조준 방향(스킬)을 향하도록 틀어줍니다.
/// - 평타 중: MeleeCombatController.CurrentAttackDir 기준
/// - 스킬 손 모션(HandSkillAnimator) 재생 중: PlayerController.CurrentSkillAimDir 기준 + Pivot 고정
/// </summary>
public class PlayerHandAimController : MonoBehaviour
{
    private MeleeCombatController _melee;
    private PlayerController _player;
    private Transform _root;
    private Animator _handSkillAnimator;
    private Vector3 _fixedLocalPosition;

    private void Awake()
    {
        _melee = GetComponentInParent<MeleeCombatController>();
        _player = GetComponentInParent<PlayerController>();
        _root = _melee != null ? _melee.transform : transform.root;
        _handSkillAnimator = GetComponent<Animator>();
        _fixedLocalPosition = transform.localPosition;
    }

    private void LateUpdate()
    {
        bool skillActive = _handSkillAnimator != null && _handSkillAnimator.enabled;

        if (skillActive)
        {
            // 스킬 손 모션 재생 중에는 클립이 Pivot(Local Position)을 건드릴 수 있으므로, 매 프레임 고정 위치로 되돌려놓습니다.
            transform.localPosition = _fixedLocalPosition;

            // 평타와 완전히 동일한 방식(worldAngle + flip 보정)으로 마우스 전체 각도를 따라감
            Vector2 dir = _player != null ? _player.CurrentSkillAimDir : Vector2.zero;
            if (dir.sqrMagnitude > 0.0001f)
            {
                float worldAngle = Mathf.Atan2(-dir.y, dir.x) * Mathf.Rad2Deg;
                bool flippedSkill = _root.localScale.x < 0f;
                float localAngleSkill = flippedSkill ? worldAngle : (180f - worldAngle);
                transform.localRotation = Quaternion.Euler(0f, 0f, localAngleSkill);
            }
            return;
        }

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
