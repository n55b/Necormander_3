using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 이 Image 를 'UI 스킨 설정'의 한 역할(큰 창, 체력바 테두리 …)로 칠한다.
/// 스킨 설정에서 스프라이트/색을 바꾸면 같은 역할을 가진 모든 이미지가 함께 바뀐다(에디터·플레이 중 모두).
/// </summary>
[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Image))]
[AddComponentMenu("UI 스킨/스킨 이미지 (UI_Panel)")]
public class UISkinPart : MonoBehaviour
{
    [KoLabel("역할"), Tooltip("'UI 스킨 설정'에서 이 역할에 넣은 스프라이트·색·굵기가 적용됩니다.")]
    public UISkinRole role;

    [KoLabel("원래 색 유지"), Tooltip("켜면 색은 건드리지 않고 그림만 바꿉니다.\n코드가 색을 정하는 이미지(아이템 등급색, 체력 빨강, 가드 파랑, 탭 선택색)는 켜 두세요.")]
    public bool keepOwnColor;

    [KoLabel("개별 스킨 설정 (비우면 기본)"), Tooltip("이 이미지만 다른 스킨 설정 파일을 쓰고 싶을 때 넣습니다. 보통은 비워 두세요.")]
    public UISkinSettings settings;

    private Image _image;

    public UISkinSettings Settings => settings != null ? settings : UISkinSettings.Active;

    /// <summary>스킨 설정이 정한 이 이미지의 기본 색 (연출이 번쩍인 뒤 돌아올 색).</summary>
    public Color SkinColor
    {
        get
        {
            var s = Settings;
            var e = s != null ? s.Get(role) : null;
            return e != null ? e.color : Color.white;
        }
    }

    private void OnEnable()
    {
        UISkinSettings.Changed -= Apply;
        UISkinSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        UISkinSettings.Changed -= Apply;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Apply(); };
    }
#endif

    /// <summary>스킨 설정의 값을 이 Image 에 칠한다. 값이 같으면 아무것도 바꾸지 않는다.</summary>
    public void Apply()
    {
        var s = Settings;
        if (s == null) return;
        if (_image == null) _image = GetComponent<Image>();
        if (_image == null) return;
        var e = s.Get(role);
        if (e == null) return;

        if (_image.sprite != e.sprite) _image.sprite = e.sprite;

        Image.Type type = Image.Type.Simple;
        if (e.sprite != null)
        {
            switch (e.drawMode)
            {
                case UISkinSettings.SkinEntry.DrawMode.Sliced: type = e.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple; break;
                case UISkinSettings.SkinEntry.DrawMode.Tiled:  type = Image.Type.Tiled; break;
            }
        }
        if (_image.type != type) _image.type = type;
        if (type == Image.Type.Sliced && !_image.fillCenter) _image.fillCenter = true;

        // 그림 1픽셀 = UI 1칸 × 굵기 배율. 캔버스의 Reference Pixels Per Unit 과 스프라이트 PPU 를 맞춘다.
        float canvasPpu = _image.canvas != null ? _image.canvas.referencePixelsPerUnit : 32f;
        float spritePpu = e.sprite != null ? e.sprite.pixelsPerUnit : 32f;
        float multiplier = canvasPpu / spritePpu / Mathf.Max(0.01f, e.pixelScale);
        if (!Mathf.Approximately(_image.pixelsPerUnitMultiplier, multiplier)) _image.pixelsPerUnitMultiplier = multiplier;

        if (!keepOwnColor && _image.color != e.color) _image.color = e.color;
    }

    /// <summary>지금 켜져 있는 모든 스킨 이미지를 다시 칠한다.</summary>
    public static void ApplyAll() => UISkinSettings.NotifyChanged();

    /// <summary>연출이 번쩍인 뒤 돌아갈 기본 색: 스킨이 색을 정하면 스킨 색, 아니면 원래 색.</summary>
    public static Color BaseColorOf(UISkinPart part, Color ownColor) =>
        part != null && !part.keepOwnColor ? part.SkinColor : ownColor;
}
