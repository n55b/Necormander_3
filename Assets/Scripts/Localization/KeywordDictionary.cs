using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;

namespace AstroNuts.Localization
{
    /// <summary>
    /// 트리거 키워드(취약, 기절, 강타 등) 사전.
    /// 노션 "태그 사전" 문서와 동일한 스키마: id / 표시이름 / 설명 / 아이콘 / 색상.
    /// id는 언어와 무관한 고정 키이며 절대 번역하지 않는다.
    /// </summary>
    [CreateAssetMenu(fileName = "KeywordDictionary", menuName = "AstroNuts/Keyword Dictionary")]
    public class KeywordDictionary : ScriptableObject
    {
        public List<KeywordEntry> entries = new List<KeywordEntry>();

        private Dictionary<string, KeywordEntry> _byId;

        /// <summary>id로 항목을 찾는다. 예: "vulnerable_stack" → 취약 항목.</summary>
        public KeywordEntry GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_byId == null) BuildLookup();
            _byId.TryGetValue(id, out var entry);
            return entry;
        }

        private void BuildLookup()
        {
            _byId = new Dictionary<string, KeywordEntry>();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.id)) continue;
                if (!_byId.ContainsKey(e.id))
                    _byId.Add(e.id, e);
            }
        }

        private void OnEnable()
        {
            // 에디터에서 entries를 수정했을 수 있으므로 캐시를 비워 다음 조회 때 다시 빌드하게 함
            _byId = null;
        }
    }

    [System.Serializable]
    public class KeywordEntry
    {
        [Tooltip("언어와 무관한 고정 키. 절대 번역하지 말 것. 예: vulnerable_stack")]
        public string id;

        [Tooltip("화면에 표시되는 단어 (로케일별로 다름: 취약 / Vulnerable)")]
        public LocalizedString displayName;

        [Tooltip("툴팁에 표시되는 설명 (로케일별)")]
        public LocalizedString description;

        [Tooltip("TMP Sprite Asset 안에 등록된 스프라이트 이름. 언어와 무관하게 공통.")]
        public string spriteName;

        [Tooltip("태그 사전 문서의 색상 규칙과 동일하게 맞출 것 (예: 소모 계열 = 빨강)")]
        public Color badgeColor = Color.white;
    }
}
