using UnityEngine;

/// <summary>
/// 플레이어 전투 사운드 데이터 ScriptableObject.
/// 생성: Assets 우클릭 → Create → Audio → PlayerAttackSoundData
/// </summary>
[CreateAssetMenu(menuName = "Audio/PlayerAttackSoundData", fileName = "PlayerAttackSoundData")]
public class PlayerAttackSoundData : ScriptableObject
{
    [Header("공격 사운드 (콤보 순서대로)")]
    [Tooltip("0=1타, 1=2타... 초과 시 순환")]
    public AudioClip[] attackClips;

    [Header("히트 사운드 (적 피격)")]
    public AudioClip[] hitClips;

    [Header("플레이어 피격 사운드")]
    public AudioClip[] playerHurtClips;

    [Header("콤보 설정")]
    public float comboResetTime = 1.2f;

    [Range(0f, 0.05f)]
    [Tooltip("콤보마다 pitch 상승량")]
    public float pitchIncreasePerCombo = 0.02f;

    [Range(1f, 1.5f)]
    public float maxPitch = 1.2f;

    [Header("구르기 사운드")]
    public AudioClip[] dodgeClips;
    [Range(0f, 1f)] public float dodgeVolume = 0.85f;

    public AudioClip GetDodgeClip() => PickRandom(dodgeClips);

    
[Header("볼륨")]
    [Range(0f, 1f)] public float attackVolume     = 0.8f;
    [Range(0f, 1f)] public float hitVolume        = 0.7f;
    [Range(0f, 1f)] public float playerHurtVolume = 0.9f;

    // ─── 헬퍼 ─────────────────────────────────────────────────────────
    // 공통 랜덤/인덱스 선택 — null·Length 중복 체크를 한 곳에서 처리
    private static AudioClip PickRandom(AudioClip[] arr)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr.Length == 1 ? arr[0] : arr[Random.Range(0, arr.Length)];
    }

    private static AudioClip PickAt(AudioClip[] arr, int index)
    {
        if (arr == null || arr.Length == 0) return null;
        return arr[index % arr.Length];
    }

    public AudioClip GetAttackClip(int comboIndex) => PickAt(attackClips, comboIndex);
    public AudioClip GetHitClip()                  => PickRandom(hitClips);
    public AudioClip GetPlayerHurtClip()           => PickRandom(playerHurtClips);

    // pitch = 1 + (increase * combo), Mathf.Min 대신 삼항으로 분기
    public float GetPitch(int comboIndex)
    {
        float p = 1f + pitchIncreasePerCombo * comboIndex;
        return p < maxPitch ? p : maxPitch;
    }
}
