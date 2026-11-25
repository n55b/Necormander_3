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
    [UnityEngine.InspectorName("튀는 동작")] Punch,
    [UnityEngine.InspectorName("흔들림")] Shake,
    [UnityEngine.InspectorName("파동")] Wave,
    [UnityEngine.InspectorName("무지개")] Rainbow,
    [UnityEngine.InspectorName("흩어짐")] Jitter,
    [UnityEngine.InspectorName("타이핑")] Type
}

/// <summary>
/// 파싱된(태그 제거 후) 텍스트에서 특정 구간에 적용할 이펙트 정보.
/// startIndex/length는 "태그가 제거된 최종 문자열" 기준의 문자 인덱스.
/// </summary>
public struct TextEffectRange
{
    public TextEffectType type;
    public string preset;
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
    private static Regex tagRegex;
    private static Regex TagRegex => tagRegex ??= new Regex(
        @"<(?:(?<end>/)(?<name>punch|shake|wave|rainbow|jitter|type|fx)|(?<name>fx)=(?<id>[\w.-]+)|(?<name>punch|shake|wave|rainbow|jitter|type)(?:=(?<speed>[\d.]+))?)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static bool HasMarkup(string text) =>
        !string.IsNullOrEmpty(text) && text.IndexOf('<') >= 0 && TagRegex.IsMatch(text);

    public static string Parse(string input, List<TextEffectRange> outRanges)
    {
        outRanges.Clear();
        if (string.IsNullOrEmpty(input) || input.IndexOf('<') < 0) return input;
        return ParseTagged(input, outRanges);
    }

    private static string ParseTagged(string input, List<TextEffectRange> outRanges)
    {
        var tags=TagRegex.Matches(input);
        if (tags.Count==0) return input;

        // Only matched pairs are removed. Rich text, sprite tags and unfinished authoring text pass through.
        var stack=new Stack<int>();
        var pairs=new int[tags.Count];
        for(int n=0;n<tags.Count;n++)
        {
            var tag=tags[n];
            if (!tag.Groups["end"].Success) { stack.Push(n); continue; }
            if(stack.Count==0) continue;
            int open=stack.Peek();
            if(!string.Equals(tags[open].Groups["name"].Value,tag.Groups["name"].Value,StringComparison.OrdinalIgnoreCase)) continue;
            stack.Pop();
            pairs[open]=n+1; pairs[n]=open+1;
        }
        var output=new StringBuilder(input.Length);
        var offsets=new int[tags.Count];
        int cursor=0;
        for(int n=0;n<tags.Count;n++)
        {
            if(pairs[n]==0) continue;
            var tag=tags[n];
            output.Append(input,cursor,tag.Index-cursor);
            cursor=tag.Index+tag.Length;
            offsets[n]=output.Length;
        }
        output.Append(input,cursor,input.Length-cursor);
        for(int n=0;n<tags.Count;n++)
        {
            var tag=tags[n];
            if(pairs[n]==0 || tag.Groups["end"].Success) continue;
            var range=new TextEffectRange {startIndex=offsets[n],length=offsets[pairs[n]-1]-offsets[n],param=1f};
            if(tag.Groups["id"].Success) range.preset=tag.Groups["id"].Value;
            else Enum.TryParse(tag.Groups["name"].Value,true,out range.type);
            if(tag.Groups["speed"].Success && float.TryParse(tag.Groups["speed"].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var speed) && speed>0) range.param=speed;
            if(range.length>0) outRanges.Add(range);
        }
        return output.ToString();
    }
}
