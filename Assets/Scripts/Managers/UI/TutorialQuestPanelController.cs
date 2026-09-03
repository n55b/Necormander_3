using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌상단 튜토리얼 퀘스트 패널(Panel_Quest)의 내용을 채우는 컨트롤러입니다.
/// Object_Quest(템플릿, 비활성 상태로 보관)를 단계별 문구 줄 수만큼 복제해서 보여줍니다.
/// RoomType/IRoomEvent 시스템과는 별개로 동작하며, 외부에서 ShowStage()를 직접 호출해서 사용합니다.
/// (튜토리얼 던전은 별도 시퀀스라 일반 방 타입 시스템에 묶지 않았습니다.)
/// </summary>
public class TutorialQuestPanelController : MonoBehaviour
{
    public static TutorialQuestPanelController Instance { get; private set; }

    public enum TutorialStage
    {
        Spawn,
        Combat,
        Reward,
        Augment,
        Dodge,
        Shop,
        Descend
    }

    [Header("Object_Quest 템플릿 (자식 0번, 비활성 상태로 유지)")]
    [SerializeField] private RectTransform objectQuestTemplate;

    [Header("Layout")]
    [SerializeField] private RectTransform panelRect;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private static readonly Dictionary<TutorialStage, string[]> StageLines = new Dictionary<TutorialStage, string[]>
    {
        { TutorialStage.Spawn, new[]
            {
                "<sprite=\"Tutorial\" name=\"Keyboard_WASD\">로 이동하세요",
                "파란색 점이 향하는 곳이 다음 맵 방향이에요",
            }
        },
        { TutorialStage.Combat, new[]
            {
                "<sprite=\"Tutorial\" name=\"Mouse_Left\">로 적을 공격하세요",
                "<sprite=\"Tutorial\" name=\"Mouse_Right\">로 날아오는 투사체를 튕겨낼 수 있어요",
                "<sprite=\"Tutorial\" name=\"Keyboard_LShift\">로 대시(회피)할 수 있어요",
                "<sprite=\"Tutorial\" name=\"Keyboard_Tab\">을 누르면 이미 클리어한 맵으로 즉시 이동해요",
            }
        },
        { TutorialStage.Reward, new[]
            {
                "<sprite=\"Tutorial\" name=\"Keyboard_F\">로 상호작용하세요",
                "미니언을 획득하면 바로 장착돼요",
                "하단 버튼을 누르면 미니언 대신 체력을 회복해요",
                "<sprite=\"Tutorial\" name=\"Keyboard_R\">로 미니언 스킬을 사용해요",
                "<sprite=\"Tutorial\" name=\"Keyboard_Tab\">로 미니언 능력을 확인해요",
            }
        },
        { TutorialStage.Augment, new[]
            {
                "증강을 선택하면 보상은 늘지만 더 강한 전투가 시작돼요",
            }
        },
        { TutorialStage.Dodge, new[]
            {
                "<sprite=\"Tutorial\" name=\"Keyboard_LShift\">로 대시할 수 있어요",
                "대시로 걸어서 못 가는 지형도 통과할 수 있어요",
            }
        },
        { TutorialStage.Shop, new[]
            {
                "골드로 상인에게서 아이템을 살 수 있어요",
                "<sprite=\"Tutorial\" name=\"Keyboard_F\">로 상호작용, 구매할 수 있어요",
                "건틀릿(장비)을 사면 스킬이 생겨요",
                "<sprite=\"Tutorial\" name=\"Keyboard_Tab\">로 장비/스킬을 확인해요",
                "<sprite=\"Tutorial\" name=\"Keyboard_Q\">, <sprite=\"Tutorial\" name=\"Keyboard_E\">로 장비 스킬을 사용해요",
                "<sprite=\"Tutorial\" name=\"Keyboard_Tab\">로 장착 중인 아이템을 확인해요",
            }
        },
        { TutorialStage.Descend, new[]
            {
                "마을로 이동합니다 (튜토리얼 종료)",
            }
        },
    };

    private void Awake()
    {
        Instance = this;

        if (panelRect == null) panelRect = GetComponent<RectTransform>();

        if (objectQuestTemplate == null && transform.childCount > 0)
        {
            objectQuestTemplate = transform.GetChild(0) as RectTransform;
        }

        if (objectQuestTemplate != null)
        {
            objectQuestTemplate.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>미리 정의된 단계 문구로 패널을 채웁니다.</summary>
    public void ShowStage(TutorialStage stage)
    {
        if (!StageLines.TryGetValue(stage, out var lines))
        {
            Debug.LogWarning($"[TutorialQuestPanelController] '{stage}' 단계에 등록된 문구가 없어요.");
            return;
        }

        ShowLines(lines);
    }

    /// <summary>임의의 문구 목록으로 패널을 채웁니다. (필요하면 외부에서 직접 사용 가능)</summary>
    public void ShowLines(IReadOnlyList<string> lines)
    {
        if (objectQuestTemplate == null)
        {
            Debug.LogError("[TutorialQuestPanelController] Object_Quest 템플릿이 연결되어 있지 않아요.");
            return;
        }

        Clear();
        gameObject.SetActive(true);

        foreach (var line in lines)
        {
            RectTransform instance = Instantiate(objectQuestTemplate, objectQuestTemplate.parent);
            instance.gameObject.SetActive(true);

            TextMeshProUGUI text = instance.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) text.text = line;

            _spawned.Add(instance.gameObject);
        }

        if (panelRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
    }

    /// <summary>패널을 비우고 숨깁니다. (예: 내려가는방에서 튜토리얼 종료 시 호출)</summary>
    public void Hide()
    {
        Clear();
        gameObject.SetActive(false);
    }

    private void Clear()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }
}
