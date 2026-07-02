using TMPro;
using UnityEngine;

namespace AstroNuts.Localization
{
    /// <summary>
    /// 스킬 설명 등, 키워드 하이라이팅이 필요한 TMP 텍스트에 붙인다.
    /// 씬의 LocalizeStringEvent 컴포넌트의 OnUpdateString(string) 이벤트에
    /// 이 컴포넌트의 OnLocalizedTextUpdated(string)를 인스펙터에서 연결하면,
    /// 언어가 바뀌거나 처음 로드될 때마다 자동으로 키워드 태그가 다시 씌워진다.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedSkillText : MonoBehaviour
    {
        [SerializeField] private KeywordDictionary dictionary;

        private TMP_Text _text;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        /// <summary>LocalizeStringEvent.OnUpdateString에 연결할 콜백.</summary>
        public void OnLocalizedTextUpdated(string rawTranslatedText)
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            _text.text = KeywordHighlighter.ApplyTags(rawTranslatedText, dictionary);
        }
    }
}
