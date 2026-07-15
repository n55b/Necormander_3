using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 지원하는 커스텀 텍스트 이펙트 종류.
/// </summary>
public enum TextEffectType
{
    Punch,
    Shake,
    Wave,
    Rainbow,
    Jitter
}

/// <summary>
/// 파싱된(태그 제거 후) 텍스트에서 특정 구간에 적용할 이펙트 정보.
/// startIndex/length는 "태그가 제거된 최종 문자열" 기준의 문자 인덱스.
/// </summary>
public struct TextEffectRange
{
    public TextEffectType type;
    public int startIndex;
    public int length;
    public float param; // 세기/속도 배율 (기본 1)
}

/// <summary>
/// "&lt;punch&gt;100&lt;/punch&gt;", "&lt;shake=1.5&gt;Critical!&lt;/shake&gt;" 같은 커스텀 리치텍스트 태그를 해석한다.
/// TMP 자체 태그(&lt;color&gt;, &lt;b&gt; 등)와는 별개로, 여기서 정의한 태그만 걷어내고
/// 나머지 텍스트(그리고 TMP 태그)는 그대로 통과시킨다.
///
/// 사용 예)
///   "&lt;punch&gt;100&lt;/punch&gt; Damage!"            -&gt; "100"에 Punch
///   "&lt;shake&gt;MISS&lt;/shake&gt;"                    -&gt; 전체 텍스트에 Shake (범위가 전체 문자열과 같으면 통짜 이펙트처럼 보임)
///   "&lt;rainbow=2&gt;BONUS&lt;/rainbow&gt; Combo"       -&gt; "BONUS"에 2배 속도 Rainbow
/// </summary>
public static class TextEffectParser
{
    // <punch>, <shake=1.5>, <wave>, <rainbow=2>, <jitter> 형태를 매칭
    private static readonly Regex TagRegex = new Regex(
        @"<(punch|shake|wave|rainbow|jitter)(?:=([\d.]+))?>(.*?)</\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// 원본 문자열에서 커스텀 이펙트 태그를 제거한 최종 문자열을 반환하고,
    /// outRanges에 각 이펙트가 적용될 구간(제거된 문자열 기준 인덱스)을 채운다.
    /// </summary>
    public static string Parse(string input, List<TextEffectRange> outRanges)
    {
        outRanges.Clear();

        if (string.IsNullOrEmpty(input))
            return input;

        var matches = TagRegex.Matches(input);
        if (matches.Count == 0)
            return input;

        var sb = new StringBuilder(input.Length);
        int lastIndex = 0;

        foreach (Match m in matches)
        {
            // 태그 이전의 일반 텍스트를 그대로 붙인다.
            sb.Append(input, lastIndex, m.Index - lastIndex);

            int start = sb.Length;
            string inner = m.Groups[3].Value;
            sb.Append(inner);
            int length = inner.Length;

            if (Enum.TryParse(m.Groups[1].Value, true, out TextEffectType type))
            {
                float param = 1f;
                if (m.Groups[2].Success)
                    float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out param);

                if (length > 0)
                {
                    outRanges.Add(new TextEffectRange
                    {
                        type = type,
                        startIndex = start,
                        length = length,
                        param = param <= 0f ? 1f : param
                    });
                }
            }

            lastIndex = m.Index + m.Length;
        }

        sb.Append(input, lastIndex, input.Length - lastIndex);
        return sb.ToString();
    }
}
