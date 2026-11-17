using System;

/// <summary>
/// 기획자가 적는 한국어 원문(ko) + 번역 키(key). 문자열이 필요한 곳에 그대로 넣으면 선택 언어로 바뀐다.
/// 저장·비교·검색처럼 언어와 무관해야 하는 곳은 .ko를 직접 읽는다.
/// SO의 key는 비워 두면 "Tools/번역/번역 표 갱신"이 자동으로 채운다(프리팹·씬 컴포넌트는 인스펙터에서 직접 넣는다).
/// </summary>
[Serializable]
public struct LocText
{
    public string ko;
    public string key;

    public static implicit operator string(LocText t) => Loc.T(t.key, t.ko);
    public override string ToString() => Loc.T(key, ko);
}
