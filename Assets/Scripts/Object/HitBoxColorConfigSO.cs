using UnityEngine;

/// <summary>
/// 아군/적 히트박스(장판)의 외곽선·채움 색과 투명도를 인스펙터에서 공용으로 관리하는 설정입니다.
/// 에셋 하나를 모든 히트박스가 공유합니다 — DamageTextColorConfigSO 와 같은 패턴.
///
/// [읽는 곳] BaseHitBox 가 스폰 시 Resources.Load 로 이 에셋을 읽어(static 캐시) 물들입니다.
/// 그래서 이 에셋은 이름이 'Resources' 인 폴더 아래에 있고 이름이 "HitBoxColorConfig" 여야 합니다.
/// Unity 는 프로젝트 내 모든 'Resources' 폴더를 하나로 합쳐 보므로, 스프라이트용 Assets/Resources 와 섞을
/// 필요 없이 중첩 폴더에 두면 됩니다(예: Assets/SOData/UI/Resources/HitBoxColorConfig.asset).
/// 못 찾으면 BaseHitBox 는 하드코딩 색(파랑/빨강)으로 폴백합니다.
///
/// 색의 알파 채널이 곧 투명도입니다. 채움 = fillingTransform 밑 스프라이트, 나머지 = 외곽선으로 취급됩니다.
/// </summary>
[CreateAssetMenu(fileName = "HitBoxColorConfig", menuName = "Necromancer/Object/HitBoxColorConfig")]
public class HitBoxColorConfigSO : ScriptableObject
{
    [Header("아군 (플레이어/미니언)")]
    [Tooltip("아군 히트박스 외곽선 색+투명도(알파).")]
    public Color allyOutline = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("아군 히트박스 안쪽 채움 색+투명도(알파).")]
    public Color allyFill = new Color(0.2f, 0.6f, 1f, 0.35f);

    [Header("적")]
    [Tooltip("적 히트박스 외곽선 색+투명도(알파).")]
    public Color enemyOutline = new Color(1f, 0.2f, 0.2f, 1f);
    [Tooltip("적 히트박스 안쪽 채움 색+투명도(알파).")]
    public Color enemyFill = new Color(1f, 0.2f, 0.2f, 0.35f);
}
