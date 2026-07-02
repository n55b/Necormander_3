using System.Text.RegularExpressions;
using UnityEngine;

namespace AstroNuts.Localization
{
    /// <summary>
    /// 로컬라이즈된 평문 텍스트를 받아서, 사전에 등록된 키워드가 문장 속에 있으면
    /// TMP의 &lt;link&gt;/&lt;sprite&gt;/&lt;color&gt; 태그를 자동으로 씌워준다.
    /// 기획자는 "취약 스택을 소모합니다" 처럼 평문만 쓰면 되고,
    /// 태그를 손으로 넣을 필요가 없다.
    /// </summary>
    public static class KeywordHighlighter
    {
        public static string ApplyTags(string rawText, KeywordDictionary dictionary)
        {
            if (dictionary == null || string.IsNullOrEmpty(rawText))
                return rawText;

            string result = rawText;

            foreach (var entry in dictionary.entries)
            {
                if (string.IsNullOrEmpty(entry.id) || entry.displayName == null)
                    continue;

                // 주의: GetLocalizedString()은 테이블이 이미 로드되어 있어야 즉시 값을 반환한다.
                // 초기 로딩 타이밍 이슈가 보이면, LocalizedString.StringChanged 이벤트로
                // 캐싱하는 방식으로 바꿀 것 (KeywordDictionary에 캐시 딕셔너리 추가).
                string surface = entry.displayName.IsEmpty ? null : entry.displayName.GetLocalizedString();
                if (string.IsNullOrEmpty(surface))
                    continue;

                // 이미 태그가 씌워진 결과 안에서 다시 매칭되지 않도록,
                // "이 표면형이 <link 안에 이미 있는지"는 검사하지 않는다 -
                // 대신 원문(raw) 한 번에 대해서만 이 함수를 호출하는 것을 전제로 한다.
                string pattern = Regex.Escape(surface);
                string colorHex = ColorUtility.ToHtmlStringRGB(entry.badgeColor);
                string spriteTag = string.IsNullOrEmpty(entry.spriteName)
                    ? string.Empty
                    : $"<sprite name=\"{entry.spriteName}\">";

                string replacement =
                    $"<link=\"{entry.id}\"><color=#{colorHex}>{spriteTag}{surface}</color></link>";

                result = Regex.Replace(result, pattern, replacement);
            }

            return result;
        }
    }
}
