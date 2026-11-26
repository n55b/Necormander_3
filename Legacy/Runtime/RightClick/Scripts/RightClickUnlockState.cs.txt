using UnityEngine;

/// <summary>
/// 우클릭 기술의 해금과 선택을 PlayerPrefs에 저장한다.
/// 사망하거나 새 게임을 시작해 런 세이브를 지울 때도 이 설정은 유지된다.
/// </summary>
public static class RightClickUnlockState
{
    /// <summary>
    /// 켜져 있으면 모든 우클릭 기술을 사용할 수 있다. 저장된 해금 기록은 보존한다.
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
        if (WorkshopTrial.Active) return;
        if (rc == null) return;
        if (PlayerPrefs.GetInt(UnlockKeyPrefix + rc.name, 0) != 0) return;

        PlayerPrefs.SetInt(UnlockKeyPrefix + rc.name, 1);
        PlayerPrefs.Save();
        Debug.Log($"<color=cyan>[RightClick]</color> 해금: {rc.ResolveTitle()}");
    }

    /// <summary>해금을 취소한다. 지금은 디버그/테스트용.</summary>
    public static void Lock(RightClickDataSO rc)
    {
        if (WorkshopTrial.Active) return;
        if (rc == null) return;
        PlayerPrefs.DeleteKey(UnlockKeyPrefix + rc.name);
        PlayerPrefs.Save();
    }

    // ── 선택(로드아웃) ────────────────────────────────────────────────
    // 교체는 마을에서만 가능하지만, 고른 값은 런을 넘어 유지돼야 한다("지난번에 쓰던 걸로 시작").
    // 런 세이브에 넣으면 사망 시 같이 지워지므로 이것도 영구 저장소에 둔다.

    /// <summary>마지막으로 고른 우클릭 에셋 이름. 없으면 빈 문자열.</summary>
    public static string SelectedName => WorkshopTrial.Active ? WorkshopTrial.SelectedSkill ?? "" : PlayerPrefs.GetString(SelectedKey, "");

    public static void SetSelected(RightClickDataSO rc)
    {
        if (rc == null) return;
        if (WorkshopTrial.Active) { WorkshopTrial.SelectedSkill = rc.name; return; }
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
