using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AstroNuts.Localization
{
    /// <summary>
    /// 로컬라이즈된 평문 텍스트를 받아서, 사전에 등록된 키워드가 문장 속에 있으면
    /// TMP의 &lt;link&gt;/&lt;u&gt;/&lt;color&gt; 태그를 자동으로 씌워준다.
    /// 기획자는 "취약 스택을 소모합니다" 처럼 평문만 쓰면 되고,
    /// 태그를 손으로 넣을 필요가 없다.
    /// </summary>
    public static class KeywordHighlighter
    {
        public static string ApplyTags(string rawText, KeywordDictionary dictionary)
        {
            if (dictionary == null || string.IsNullOrEmpty(rawText))
                return rawText;

            var terms = new Dictionary<string, KeywordEntry>();
            foreach (var entry in dictionary.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id) || entry.displayName == null)
                    continue;

                // 주의: GetLocalizedString()은 테이블이 이미 로드되어 있어야 즉시 값을 반환한다.
                // 초기 로딩 타이밍 이슈가 보이면, LocalizedString.StringChanged 이벤트로
                // 캐싱하는 방식으로 바꿀 것 (KeywordDictionary에 캐시 딕셔너리 추가).
                string surface = entry.displayName.IsEmpty ? null : entry.displayName.GetLocalizedString();
                if (string.IsNullOrEmpty(surface))
                    continue;

                terms.TryAdd(surface, entry);
            }
            if (terms.Count == 0) return rawText;
            // '충격파' 속 '격파'나 기존 태그를 다시 감싸지 않는다. 한국어 조사는 뒤에 붙을 수 있다.
            string keywords = string.Join("|", terms.Keys.OrderByDescending(s => s.Length).Select(Regex.Escape));
            string pattern = @"<link\b[^>]*>.*?</link>|<[^>]+>|(?<![\p{L}\p{N}_])(?:" + keywords + @")(?![A-Za-z0-9_])";
            return Regex.Replace(rawText, pattern, match => {
                if (!terms.TryGetValue(match.Value, out var entry)) return match.Value;
                string color = ColorUtility.ToHtmlStringRGB(entry.badgeColor);
                // 공용 Sprite Asset에 없는 배지 대신 텍스트 링크만 사용한다.
                return $"<link=\"{entry.id}\"><u><color=#{color}>{match.Value}</color></u></link>";
            }, RegexOptions.Singleline);
        }
    }
}
