using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using AstroNuts.Localization;

namespace AstroNuts.UI
{
    /// <summary>
    /// TMP 텍스트 위에서 마우스가 키워드(<link> 태그) 위에 있는지 감지해서
    /// 공용 시스템인 CommonTooltipUI를 호출합니다.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class KeywordTooltipController : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler
    {
        [SerializeField] private KeywordDictionary dictionary;
        [SerializeField] private Camera uiCamera; // Screen Space - Overlay 캔버스면 비워두세요.

        private TMP_Text _text;
        private int _lastLinkIndex = -1;

        private void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            // 전역 공용 툴팁이 씬에 없으면 아무것도 하지 않습니다.
            if (CommonTooltipUI.Instance == null) return;

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, uiCamera);

            // 같은 키워드 위에 머물고 있다면 매 프레임 무거운 연산을 하지 않도록 방어
            if (linkIndex == _lastLinkIndex)
                return;

            _lastLinkIndex = linkIndex;

            // 마우스가 링크가 없는 일반 글자 위로 갔다면 툴팁을 끕니다.
            if (linkIndex == -1)
            {
                CommonTooltipUI.Instance.Hide();
                return;
            }

            TMP_LinkInfo linkInfo = _text.textInfo.linkInfo[linkIndex];
            string keywordId = linkInfo.GetLinkID();
            KeywordEntry entry = dictionary != null ? dictionary.GetById(keywordId) : null;

            // 사전에 없는 엉뚱한 링크 ID라면 무시하고 끕니다.
            if (entry == null)
            {
                CommonTooltipUI.Instance.Hide();
                return;
            }

            // 🔥 핵심: 기존 CommonTooltipUI의 강력한 기능을 100% 활용합니다.
            // 문자열을 미리 가공해서 넘기는 게 아니라, 번역 에셋(LocalizedString)을 통째로 보냅니다.
            TooltipData data = new TooltipData();
            data.localizedTitle = entry.displayName;
            data.localizedDescription = entry.description;
            data.titleColor = entry.badgeColor; // 사전에 기획자가 지정해둔 타이틀 색상 반영
            
            // 만약 나중에 툴팁에 "종류"나 "부가 효과"도 넣고 싶다면 여기서 채워주면 됩니다.
            data.type = "키워드"; 

            // 공용 툴팁 팝업 짠!
            CommonTooltipUI.Instance.Show(data);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _lastLinkIndex = -1;
            if (CommonTooltipUI.Instance != null) 
                CommonTooltipUI.Instance.Hide();
        }

        private void OnDisable()
        {
            _lastLinkIndex = -1;
            if (CommonTooltipUI.Instance != null) 
                CommonTooltipUI.Instance.Hide();
        }
    }
}