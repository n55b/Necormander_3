using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Localization.Settings;

/// <summary>
/// 대사 한 줄. CSV 한 행이 이거 하나가 된다.
/// </summary>
public class DialogueLine
{
    /// <summary>대화 묶음 id. 같은 값이 연속으로 오면 한 대화다.</summary>
    public string id;

    /// <summary>
    /// 이 줄에서 무대에 세울 캐릭터 키들(표정 접미 포함, 예: "bonemaster/angry").
    /// null 이면 "이전 줄 그대로 유지"라는 뜻이다 — 매 줄 적지 않아도 되게 하려는 것.
    /// </summary>
    public string[] cast;

    /// <summary>말하는 캐릭터 키(표정 접미 가능). 비어 있으면 나레이션.</summary>
    public string speaker;

    /// <summary>화면에 띄울 이름. 비어 있으면 DialogueCastSO 의 기본 이름을 쓴다.</summary>
    public string displayName;

    /// <summary>대사 본문. TMP 태그와 TMPTextEffectPlayer 태그를 그대로 써도 된다.</summary>
    public string text;
}

/// <summary>
/// 대사 CSV 를 물고 있는 테이블. 씬마다 DialogueUI 인스턴스가 따로 있어도
/// 대사 원본은 이 에셋 하나를 공유한다.
///
/// CSV 규격 (첫 줄이 헤더, 칸 순서는 상관없다):
/// <code>
/// id,cast,speaker,name,text_ko
/// bm_intro,player|bonemaster/angry,bonemaster,본 마스터,"드디어 왔군, 애송이."
/// bm_intro,,player,,…준비됐어.
/// bm_intro,,bonemaster/laugh,,&lt;shake&gt;웃기는군.&lt;/shake&gt;
/// bm_intro,,,,뼈들이 일제히 일어섰다.
/// </code>
///
/// · <b>cast</b> 를 비우면 이전 줄의 무대를 그대로 쓴다.
/// · <b>speaker</b> 를 비우면 나레이션(이름칸 숨김, 전원 어둡게).
/// · 쉼표·따옴표·줄바꿈이 든 대사는 큰따옴표로 감싸면 된다("" 두 개가 따옴표 한 개).
/// · '#' 으로 시작하는 줄은 주석으로 건너뛴다.
///
/// <b>다국어</b>: 칸 이름에 로케일 코드를 붙이면(text_ko / text_en) 현재 언어에 맞는 칸을
/// 골라 읽는다. 접미 없는 'text' 칸만 있어도 그대로 동작하므로, 한국어만 쓸 거면
/// 신경 쓸 필요 없다. 나중에 칸 하나 추가하면 그게 곧 번역본이다.
///
/// <b>인코딩 주의</b>: 엑셀은 한글 CSV 를 기본으로 CP949 로 저장한다. 반드시
/// "CSV UTF-8" 로 저장할 것. 구글 시트는 기본이 UTF-8 이라 그냥 받으면 된다.
/// </summary>
[CreateAssetMenu(fileName = "DialogueTable", menuName = "Necromancer/Dialogue/Dialogue Table")]
public class DialogueTableSO : ScriptableObject
{
    [Header("대사 CSV")]
    [Tooltip("대사 CSV 파일들. 여러 장으로 쪼개도 되고, id 만 안 겹치면 된다.\n" +
             "UTF-8 로 저장할 것 (엑셀은 'CSV UTF-8' 로 저장).")]
    [SerializeField] private TextAsset[] csvFiles;

    [Header("다국어")]
    [Tooltip("현재 언어에 해당하는 칸(text_en 등)이 없을 때 대신 읽을 로케일 코드.\n" +
             "칸 이름에 접미가 아예 없으면(text) 그 칸을 그냥 쓴다.")]
    [SerializeField] private string fallbackLocale = "ko";

    // 파싱 결과 캐시. 로케일이 바뀌면 통째로 버린다.
    private Dictionary<string, List<DialogueLine>> _lookup;
    private string _cachedLocale;

    private void OnEnable()
    {
        // 에디터에서 CSV 를 갈아끼웠을 때 옛 캐시가 남지 않게.
        _lookup = null;
    }

    /// <summary>id 에 해당하는 대사 줄들. 없으면 null.</summary>
    public List<DialogueLine> Get(string id)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(id)) return null;
        return _lookup.TryGetValue(id, out var lines) ? lines : null;
    }

    /// <summary>테이블에 들어 있는 모든 대화 id. 에디터 검증/드롭다운용.</summary>
    public IEnumerable<string> AllIds
    {
        get
        {
            EnsureBuilt();
            return _lookup.Keys;
        }
    }

    /// <summary>캐시를 버리고 다음 조회에서 다시 읽게 한다.</summary>
    public void Invalidate()
    {
        _lookup = null;
    }

    private void EnsureBuilt()
    {
        string locale = CurrentLocaleCode();
        if (_lookup != null && _cachedLocale == locale) return;

        _cachedLocale = locale;
        _lookup = new Dictionary<string, List<DialogueLine>>();
        if (csvFiles == null) return;

        foreach (var csv in csvFiles)
        {
            if (csv == null) continue;
            BuildFrom(csv, locale);
        }
    }

    private void BuildFrom(TextAsset csv, string locale)
    {
        var rows = ParseCsv(csv.text);
        if (rows.Count == 0) return;

        string[] header = rows[0];
        int cId      = Column(header, "id", locale);
        int cCast    = Column(header, "cast", locale);
        int cSpeaker = Column(header, "speaker", locale);
        int cName    = Column(header, "name", locale);
        int cText    = Column(header, "text", locale);

        if (cId < 0 || cText < 0)
        {
            Debug.LogError($"<color=orange>[DialogueTable]</color> {csv.name}: 헤더에 'id' 또는 'text' 칸이 없다. " +
                           "첫 줄이 헤더여야 한다.");
            return;
        }

        for (int r = 1; r < rows.Count; r++)
        {
            string[] row = rows[r];
            string id = Cell(row, cId);
            if (string.IsNullOrEmpty(id)) continue;   // 빈 줄 / 구분용 공백 줄

            string castRaw = Cell(row, cCast);
            var line = new DialogueLine
            {
                id          = id,
                cast        = string.IsNullOrEmpty(castRaw) ? null : castRaw.Split('|'),
                speaker     = Cell(row, cSpeaker),
                displayName = Cell(row, cName),
                text        = Cell(row, cText)
            };

            // cast 항목 앞뒤 공백은 기획자가 보기 좋으라고 넣는 경우가 많아 여기서 털어둔다.
            if (line.cast != null)
            {
                for (int i = 0; i < line.cast.Length; i++) line.cast[i] = line.cast[i].Trim();
            }

            if (!_lookup.TryGetValue(id, out var list))
            {
                list = new List<DialogueLine>();
                _lookup[id] = list;
            }
            list.Add(line);
        }
    }

    // ─── 로케일 ──────────────────────────────────────────────────────
    /// <summary>
    /// 지금 언어 코드("ko" / "en"). Localization 이 아직 초기화 전이면 fallback 을 쓴다.
    /// 여기서는 스트링 테이블을 조회하지 않고 코드만 읽으므로 KeywordHighlighter 가 겪는
    /// '테이블 로드 전 빈 문자열' 문제는 생기지 않는다.
    /// </summary>
    private string CurrentLocaleCode()
    {
        try
        {
            var locale = LocalizationSettings.SelectedLocale;
            if (locale != null)
            {
                string code = locale.Identifier.Code;
                if (!string.IsNullOrEmpty(code)) return code;
            }
        }
        catch
        {
            // 로컬라이제이션 초기화 전이면 조용히 기본값으로 간다.
        }
        return fallbackLocale;
    }

    /// <summary>
    /// 논리 칸 이름 하나를 실제 칸 번호로 푼다.
    /// text_ko 처럼 로케일 접미가 붙은 칸이 있으면 그쪽이 이기고,
    /// "ko-KR" 같은 코드는 앞 두 글자("ko")로도 한 번 더 찾아본다.
    /// 마지막으로 접미 없는 칸(text)을 본다.
    /// </summary>
    private int Column(string[] header, string logical, string locale)
    {
        int idx = IndexOf(header, logical + "_" + locale);
        if (idx >= 0) return idx;

        int dash = locale.IndexOf('-');
        if (dash > 0)
        {
            idx = IndexOf(header, logical + "_" + locale.Substring(0, dash));
            if (idx >= 0) return idx;
        }

        idx = IndexOf(header, logical + "_" + fallbackLocale);
        if (idx >= 0) return idx;

        return IndexOf(header, logical);
    }

    private static int IndexOf(string[] header, string name)
    {
        for (int i = 0; i < header.Length; i++)
        {
            if (string.Equals(header[i].Trim(), name, System.StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static string Cell(string[] row, int idx)
    {
        if (idx < 0 || idx >= row.Length) return "";
        return row[idx].Trim();
    }

    // ─── CSV 파서 ────────────────────────────────────────────────────
    /// <summary>
    /// 큰따옴표를 제대로 처리하는 최소 CSV 파서.
    /// 대사에는 쉼표가 반드시 들어가므로 string.Split(',') 로는 안 된다 —
    /// "안녕, 반가워" 한 줄이 두 칸으로 쪼개진다.
    ///
    /// 처리하는 것: "" 이스케이프, 인용 구간 안의 쉼표·줄바꿈, CRLF/LF, BOM, '#' 주석줄.
    /// </summary>
    public static List<string[]> ParseCsv(string src)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(src)) return rows;

        if (src[0] == '\uFEFF') src = src.Substring(1);   // UTF-8 BOM

        var field = new StringBuilder();
        var row = new List<string>();
        bool inQuotes = false;

        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // "" 는 따옴표 한 개, 홑따옴표는 인용 구간의 끝.
                    if (i + 1 < src.Length && src[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else if (c != '\r')
                {
                    field.Append(c);   // 인용 안의 줄바꿈은 대사에 그대로 살린다
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    AddRow(rows, row);
                    row.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            AddRow(rows, row);
        }
        return rows;
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        // 완전히 빈 줄과 '#' 주석줄은 버린다. 헤더 번호가 밀리면 안 되므로 헤더 앞에서도 동일.
        bool empty = true;
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i])) { empty = false; break; }
        }
        if (empty) return;
        if (row.Count > 0 && row[0].TrimStart().StartsWith("#")) return;

        rows.Add(row.ToArray());
    }
}
