using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class LightingDebugPanel : MonoBehaviour
{
    public static LightingDebugPanel Instance { get; private set; }
    private InputAction toggle;
    private Canvas overlay;
    private RectTransform window, rows;
    private TMP_Text status;
    private Image swatch;
    private LightingPreview draft;
    private int page, row;
    private bool dragging;
    private string context;

    private static readonly Color Trim = new Color32(132,76,100,255);
    private static readonly Color Rail = new Color32(91,73,98,255);
    private static readonly Color Cyan = new Color32(127,218,208,255);
    private static readonly Color Paper = new Color32(246,231,217,255);
    public bool IsOpen => overlay != null && overlay.gameObject.activeSelf;

    private void Awake()
    {
        Instance = this;
        toggle = new InputAction("Lighting debug",InputActionType.Button,"<Keyboard>/f8");
        toggle.performed += Toggle;
    }
    private void OnEnable() => toggle.Enable();
    private void OnDisable() { toggle.Disable(); SetOpen(false); }
    private void Toggle(InputAction.CallbackContext _) => SetOpen(!IsOpen);
    private void OnDestroy()
    {
        toggle.performed -= Toggle;
        toggle.Dispose();
        if (Instance == this) { Instance = null; LightingPreview.Active = null; }
        if (overlay != null) Destroy(overlay.gameObject);
    }

    private void Update()
    {
        if (!IsOpen) return;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame) dragging = ContainsPointer();
            if (!mouse.leftButton.isPressed) dragging = false;
        }
        string current = WorldAtmosphere.Current?.ProfileSource ?? "메뉴";
        if (current != context) { context = current; RefreshStatus(); }
    }

    public static bool CapturesPointer => Instance != null && Instance.IsOpen &&
        (Instance.dragging || Instance.ContainsPointer());

    private bool ContainsPointer()
    {
        var mouse = Mouse.current;
        return mouse != null && RectTransformUtility.RectangleContainsScreenPoint(window,
            PixelViewport.PointerForCanvas(overlay, mouse.position.ReadValue()), PixelViewport.CanvasCamera(overlay));
    }

    public void SetOpen(bool open)
    {
        if (!open)
        {
            if (overlay != null) overlay.gameObject.SetActive(false);
            dragging = false;
            return;
        }
        if (CameraSettingsSO.Instance == null) return;
        if (overlay == null) Create();
        overlay.gameObject.SetActive(true);
        PixelArtUpscaleRig.BindCanvas(overlay);
        draft = LightingPreview.Active ?? LightingPreview.From(CameraSettingsSO.Instance);
        ShowPage(page);
    }

    private void Create()
    {
        var root = new GameObject("Lighting debug canvas",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        DontDestroyOnLoad(root);
        root.layer = LayerMask.NameToLayer("UI");
        overlay = root.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceCamera;
        overlay.planeDistance = .5f;
        overlay.sortingLayerName = "UI";
        overlay.sortingOrder = short.MaxValue-2;

        window = Box("Lighting debug", (RectTransform)root.transform, 0, 0, 218, 264, Trim).rectTransform;
        window.anchorMin = window.anchorMax = window.pivot = Vector2.one;
        window.anchoredPosition = new Vector2(-5,-5);
        UIChrome.Panel(Rect("Panel",window,1,1,216,262));
        Label(window,"조명 디버그",8,4,100,19,12);
        Button(window,"닫기",174,5,38,16,() => SetOpen(false));
        status = Label(window,"",8,24,202,14);
        Button(window,"태양",7,44,65,18,() => ShowPage(0));
        Button(window,"분위기",76,44,65,18,() => ShowPage(1));
        Button(window,"외곽 안개",145,44,66,18,() => ShowPage(2));
        Button(window,"장면 기본값 복원",8,240,124,18,ResetLook);
        Label(window,"F8  닫기",146,240,65,18);
    }

    private void ResetLook()
    {
        LightingPreview.Active = null;
        draft = LightingPreview.From(CameraSettingsSO.Instance);
        ShowPage(page);
    }

    private void RefreshStatus()
    {
        if (status != null) status.text = (context ?? "월드") + "  /  " +
            (LightingPreview.Active == null ? "장면 기본값" : "조정 중");
        if (swatch != null) swatch.color = draft.sun;
    }

    private void ShowPage(int next)
    {
        page = next;
        if (rows != null) { rows.gameObject.SetActive(false); Destroy(rows.gameObject); }
        rows = Rect("Controls",window,8,68,202,168);
        swatch = null;
        row = 0;
        if (page == 0)
        {
            Add("태양 방향",0,359,draft.azimuth,v => draft.azimuth=v,"0°",true);
            Add("태양 고도",20,90,draft.elevation,v => draft.elevation=v,"0°",true);
            Add("빛 강도",0,2,draft.intensity,v => draft.intensity=v);
            Add("그림자 강도",0,1,draft.shadows,v => draft.shadows=v);
            Add("하이라이트",0,1.5f,draft.highlight,v => draft.highlight=v);
            Add("빨강",0,255,draft.sun.r*255,v => draft.sun.r=v/255,"0",true);
            Add("초록",0,255,draft.sun.g*255,v => draft.sun.g=v/255,"0",true);
            Add("파랑",0,255,draft.sun.b*255,v => draft.sun.b=v/255,"0",true);
            swatch = Box("Sun color",rows,46,137,24,8,draft.sun);
            Add("구름 그림자",0,1,1-draft.cloudSuppression,v => draft.cloudSuppression=1-v);
        }
        else if (page == 1)
        {
            Add("블룸 강도",0,2,draft.bloom,v => draft.bloom=v);
            Add("블룸 밝기 기준",.05f,.95f,draft.threshold,v => draft.threshold=v);
            Add("블룸 반경",.5f,3,draft.radius,v => draft.radius=v);
            Add("지면 안개",0,1,draft.mist,v => draft.mist=v);
            Add("빛줄기 배율",0,3,draft.shafts,v => draft.shafts=v);
            Add("안개 바람 배율",0,3,draft.wind,v => draft.wind=v);
            Add("공간 깊이",0,1,1-draft.depthSuppression,v => draft.depthSuppression=1-v);
            Add("환경 입자량",0,1,1-draft.airSuppression,v => draft.airSuppression=1-v);
            Add("나무 바람",0,4,draft.canopy,v => draft.canopy=v,"0 px",true);
        }
        else
        {
            Add("외곽 강도",0,.65f,draft.borderOpacity,v => draft.borderOpacity=v);
            Add("외곽 폭",0,48,draft.borderWidth,v => draft.borderWidth=v,"0 px",true);
            Add("안개 속도",0,2,draft.borderSpeed,v => draft.borderSpeed=v);
        }
        RefreshStatus();
    }

    private void Add(string name,float min,float max,float initial,Action<float> apply,string format="0.00",bool integer=false)
    {
        int y = row++ * 19;
        Label(rows,name,0,y,77,15);
        var value = Label(rows,initial.ToString(format),167,y,35,15);
        value.alignment = TextAlignmentOptions.Right;
        var hit = Box(name,rows,78,y,88,15,Color.clear);
        Box("Track",hit.rectTransform,3,6,82,3,Rail);
        var area = Rect("Travel",hit.rectTransform,3,1,82,13);
        var handle = Box("Handle",area,0,0,4,11,Cyan);
        handle.rectTransform.pivot = new Vector2(.5f,.5f);
        handle.rectTransform.sizeDelta = new Vector2(4,-2);
        handle.rectTransform.anchoredPosition = Vector2.zero;
        handle.raycastTarget = false;
        var slider = hit.gameObject.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.handleRect = handle.rectTransform;
        slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = integer;
        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(v =>
        {
            apply(v);
            LightingPreview.Active = draft;
            value.text = v.ToString(format);
            RefreshStatus();
        });
    }

    private static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
    {
        var child = new GameObject(name,typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        var rect = (RectTransform)child.transform;
        rect.SetParent(parent,false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
        rect.sizeDelta = new Vector2(width,height);
        rect.anchoredPosition = new Vector2(x,-y);
        return rect;
    }
    private static Image Box(string name,Transform parent,float x,float y,float w,float h,Color color)
    {
        var image = Rect(name,parent,x,y,w,h).gameObject.AddComponent<Image>();
        image.color = color;
        if (color.a > 0) RoundedPlate.ApplyFlat(image);
        return image;
    }
    private static TMP_Text Label(Transform parent,string caption,float x,float y,float w,float h,int size=10)
    {
        var text = Rect("Label",parent,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = caption; text.color = Paper; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        PixelText.Configure(text,size);
        return text;
    }
    private static void Button(Transform parent,string caption,int x,int y,int w,int h,Action click)
    {
        var button = UIChrome.Button(Rect(caption,parent,x,y,w,h));
        button.onClick.AddListener(() => click());
        Label(button.transform,caption,0,0,w,h).alignment = TextAlignmentOptions.Center;
    }
}
