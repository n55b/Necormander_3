using UnityEngine;

/// <summary>
/// 우클릭의 <b>영구</b> 상태 — 무엇이 해금됐는가 + 무엇을 골라뒀는가.
///
/// 왜 SaveData(save_data.json)가 아니라 PlayerPrefs 인가:
/// 런 세이브는 플레이어가 죽으면 통째로 삭제되고(GameManager.Gameover → SaveSystem.DeleteSave),
/// 타이틀에서 '새 게임'을 눌러도 삭제된다(MainMenuManager). 영구 성장요소를 거기 넣으면
/// <b>첫 사망에 해금이 전부 날아간다.</b> 그래서 런 수명과 완전히 분리된 저장소를 쓴다.
///
/// [26/08/15] 해금 조건이 아직 안 정해졌다(재화인지 업적인지 진행도인지).
/// 그래서 지금은 <see cref="UnlockAllForNow"/> 로 전부 열어두고, 조건이 정해지면:
///   1. UnlockAllForNow 를 false 로 내리고
///   2. 조건이 달성되는 지점에서 Unlock(해당에셋) 을 부르면 된다.
/// 저장/조회 배관은 이미 다 돌아가고 있으므로 그 두 줄이면 끝난다.
/// </summary>
public static class RightClickUnlockState
{
    /// <summary>
    /// 해금 조건 미정 상태의 임시 스위치. true 인 동안 <see cref="IsUnlocked"/> 는 항상 참이다.
    /// 저장된 해금 기록 자체는 그대로 살아 있으므로, 이걸 false 로 내리면 즉시 진짜 해금제가 된다.
    /// </summary>
    public const bool UnlockAllForNow = true;

    private const string UnlockKeyPrefix = "RightClick.Unlocked.";
    private const string SelectedKey = "RightClick.Selected";

    // ── 해금 ──────────────────────────────────────────────────────────

    public static bool IsUnlocked(RightClickDataSO rc)
    {
        if (rc == null) return false;
        if (UnlockAllForNow) return true;
        return PlayerPrefs.GetInt(UnlockKeyPrefix + rc.name, 0) != 0;
    }

    /// <summary>이 우클릭을 영구 해금한다. 이미 해금돼 있으면 아무 일도 안 한다.</summary>
    public static void Unlock(RightClickDataSO rc)
    {
        if (rc == null) return;
        if (PlayerPrefs.GetInt(UnlockKeyPrefix + rc.name, 0) != 0) return;

        PlayerPrefs.SetInt(UnlockKeyPrefix + rc.name, 1);
        PlayerPrefs.Save();
        Debug.Log($"<color=cyan>[RightClick]</color> 해금: {rc.ResolveTitle()}");
    }

    /// <summary>해금을 취소한다. 지금은 디버그/테스트용.</summary>
    public static void Lock(RightClickDataSO rc)
    {
        if (rc == null) return;
        PlayerPrefs.DeleteKey(UnlockKeyPrefix + rc.name);
        PlayerPrefs.Save();
    }

    // ── 선택(로드아웃) ────────────────────────────────────────────────
    // 교체는 마을에서만 가능하지만, 고른 값은 런을 넘어 유지돼야 한다("지난번에 쓰던 걸로 시작").
    // 런 세이브에 넣으면 사망 시 같이 지워지므로 이것도 영구 저장소에 둔다.

    /// <summary>마지막으로 고른 우클릭 에셋 이름. 없으면 빈 문자열.</summary>
    public static string SelectedName => PlayerPrefs.GetString(SelectedKey, "");

    public static void SetSelected(RightClickDataSO rc)
    {
        if (rc == null) return;
        PlayerPrefs.SetString(SelectedKey, rc.name);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// 저장된 선택을 실제 에셋으로 해석한다. 저장이 없거나 그 에셋이 사라졌거나 아직 안 해금됐으면
    /// 레지스트리의 기본값(패링)으로 떨어진다 — <b>우클릭이 비는 상태는 만들지 않는다.</b>
    /// 플레이어는 항상 최소한 패링을 들고 있어야 한다는 게 이번 설계의 전제다.
    /// </summary>
    public static RightClickDataSO ResolveSelected(GrowthRegistrySO registry)
    {
        if (registry == null) return null;

        string saved = SelectedName;
        if (!string.IsNullOrEmpty(saved))
        {
            var match = registry.rightClicks.Find(r => r != null && r.name == saved);
            if (match != null && match.IsValid && IsUnlocked(match)) return match;
        }

        return registry.ResolveDefaultRightClick();
    }
}
