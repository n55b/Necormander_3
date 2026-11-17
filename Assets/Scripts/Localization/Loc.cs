using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// 화면 글자 번역 입구. 한국어가 원문이고, 다른 언어는 "Game Text Table"에서 key로 찾는다.
/// 키가 없거나, 에디터 편집 중이거나, 로컬라이제이션 초기화 전이거나, 선택 언어가 ko면 한국어를 그대로 돌려준다.
/// 번역이 비어 있으면 한국어로 돌아간다. 절대 예외를 던지지 않는다.
/// 키는 Tools/번역/번역 표 갱신(LocSync)이 코드·에셋에서 모아 표에 올린다.
/// </summary>
public static class Loc
{
    public const string TableName = "Game Text Table";

    /// <summary>선택 언어가 바뀌면 불린다. 이미 띄운 글자를 다시 그릴 때 구독한다.</summary>
    public static event Action Changed;

    static string s_Code;          // 선택 언어 코드 캐시 (null = 아직 모름)
    static StringTable s_Table;    // s_Code 언어의 표 캐시
    static bool s_TableTried, s_Hooked, s_Waiting;

    // 도메인 리로드를 끈 플레이 모드 진입에서도 이전 플레이의 캐시를 쓰지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_Code = null; s_Table = null;
        s_TableTried = s_Hooked = s_Waiting = false;
        Changed = null;
    }

    public static string T(string key, string ko)
    {
        if (string.IsNullOrEmpty(key)) return ko;
#if UNITY_EDITOR
        if (!Application.isPlaying) return ko;
#endif
        try
        {
            string code = Code();
            if (code == null || code == "ko") return ko;
            if (!s_TableTried)
            {
                s_Table = LocalizationSettings.StringDatabase.GetTable(TableName);
                s_TableTried = true; // 표가 없으면 null로 남아 매번 다시 찾지 않는다.
            }
            string value = s_Table != null ? s_Table.GetEntry(key)?.LocalizedValue : null;
            return string.IsNullOrEmpty(value) ? ko : value;
        }
        catch (Exception) { return ko; }
    }

    /// <summary>string.Format(T(key, ko), args). 번역문의 {0} 등이 깨져 있으면 한국어 원문으로 만든다.</summary>
    public static string F(string key, string ko, params object[] args)
    {
        try { return string.Format(T(key, ko), args); }
        catch (FormatException)
        {
            try { return string.Format(ko, args); }
            catch (FormatException) { Debug.LogWarning($"[Loc] 형식 문자열 오류: {key}"); return ko; }
        }
    }

    static string Code()
    {
        if (s_Code != null) return s_Code;
        if (!LocalizationSettings.HasSettings) return null;
        if (!s_Hooked)
        {
            s_Hooked = true;
            LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
        }
        var init = LocalizationSettings.InitializationOperation;
        if (!init.IsDone)
        {
            // 비동기 초기화(WebGL 등): 끝나면 한 번 다시 그리게 한다.
            if (!s_Waiting)
            {
                s_Waiting = true;
                init.Completed += _ => { s_Waiting = false; if (Code() != null && s_Code != "ko") Changed?.Invoke(); };
            }
            return null;
        }
        s_Code = LocalizationSettings.SelectedLocale?.Identifier.Code;
        return s_Code;
    }

    static void OnLocaleChanged(Locale locale)
    {
        s_Code = locale != null ? locale.Identifier.Code : null;
        s_Table = null; s_TableTried = false;
        Changed?.Invoke();
    }
}
