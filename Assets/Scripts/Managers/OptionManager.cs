using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Localization.Settings;
using System.Collections;

public class OptionManager : MonoBehaviour
{
    // 정적(Static) 변수: 다른 씬(StartScene 등)에서 옵션 씬을 열 때, 설정 패널로 직행할지 여부
    public static bool OpenDirectlyToSettings = false;

    [Header("Menu Panels")]
    public GameObject pauseMenuPanel;
    public GameObject settingsMenuPanel;

    [Header("Audio Sliders")]
    public Slider bgmSlider;
    public Slider sfxSlider;

    [Header("Localization")]
    public TMP_Dropdown languageDropdown;

    private void Start()
    {
        // 옵션 직행 플래그에 따른 패널 초기화
        if (OpenDirectlyToSettings)
        {
            SwitchToSettingsPanel();
            // 직행 후 플래그 초기화 (다음에 인게임에서 ESC 누를 때 정상 작동하도록)
            OpenDirectlyToSettings = false;
        }
        else
        {
            SwitchToPausePanel();
        }

        // 초기 볼륨 설정 (SoundManager가 있으면 거기서, 없으면 PlayerPrefs에서 직접)
        float currentBgmVolume = (SoundManager.Instance != null) ? SoundManager.Instance.globalBgmVolume : PlayerPrefs.GetFloat("BGM_Volume", 1f);
        float currentSfxVolume = (SoundManager.Instance != null) ? SoundManager.Instance.globalSfxVolume : PlayerPrefs.GetFloat("SFX_Volume", 1f);

        if (bgmSlider != null)
        {
            bgmSlider.value = currentBgmVolume;
            bgmSlider.onValueChanged.AddListener(OnBgmSliderChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.value = currentSfxVolume;
            sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
        }

        StartCoroutine(InitLanguageDropdown());
    }

    private IEnumerator InitLanguageDropdown()
    {
        // 로컬라이제이션 초기화 대기
        yield return LocalizationSettings.InitializationOperation;

        if (languageDropdown != null)
        {
            // 현재 선택된 언어의 인덱스를 찾아 드롭다운에 설정
            var locales = LocalizationSettings.AvailableLocales.Locales;
            var currentLocale = LocalizationSettings.SelectedLocale;
            
            for (int i = 0; i < locales.Count; i++)
            {
                if (locales[i] == currentLocale)
                {
                    languageDropdown.SetValueWithoutNotify(i);
                    break;
                }
            }

            // 값이 바뀔 때 언어 변경 이벤트 등록
            languageDropdown.onValueChanged.AddListener(OnLanguageChanged);
        }
    }

    public void OnLanguageChanged(int index)
    {
        StartCoroutine(ChangeLanguageCoroutine(index));
    }

    private IEnumerator ChangeLanguageCoroutine(int index)
    {
        yield return LocalizationSettings.InitializationOperation;
        var locales = LocalizationSettings.AvailableLocales.Locales;
        if (index >= 0 && index < locales.Count)
        {
            LocalizationSettings.SelectedLocale = locales[index];
            Debug.Log($"<color=cyan>[OptionManager]</color> 언어가 변경되었습니다: {locales[index].Identifier.Code}");
        }
    }

    public void SwitchToSettingsPanel()
    {
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(true);
    }

    public void SwitchToPausePanel()
    {
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
        if (settingsMenuPanel != null) settingsMenuPanel.SetActive(false);
    }

    public void OnBgmSliderChanged(float value)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.UpdateBgmVolume(value);
        }
        else
        {
            PlayerPrefs.SetFloat("BGM_Volume", value);
            PlayerPrefs.Save();
        }
    }

    public void OnSfxSliderChanged(float value)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.UpdateSfxVolume(value);
        }
        else
        {
            PlayerPrefs.SetFloat("SFX_Volume", value);
            PlayerPrefs.Save();
        }
    }

public void CloseOption()
    {
        Debug.Log("<color=yellow>[OptionManager]</color> CloseOption() 호출됨");

        if (SceneOptionManager.Instance != null)
        {
            Debug.Log("<color=yellow>[OptionManager]</color> SceneOptionManager를 통해 씬 닫기 시도");
            SceneOptionManager.Instance.CloseOptionScene();
        }
        else
        {
            Debug.Log("<color=yellow>[OptionManager]</color> SceneOptionManager가 없으므로 직접 UnloadSceneAsync 호출");
            if (GameManager.Instance != null) GameManager.Instance.SetTimeStop(false); // 안전장치: timeScale 복구 누락 방지
            UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync("OptionScene");
        }
    }

    // 설정 창의 'X' 또는 'Back' 버튼용 함수
    public void OnBackButtonClicked()
    {
        // SceneOptionManager가 없다는 것은 스타트씬 등에서 직행(Direct)으로 열렸다는 의미
        if (SceneOptionManager.Instance == null)
        {
            Debug.Log("<color=yellow>[OptionManager]</color> 스타트 씬에서 열렸으므로 창을 완전히 닫습니다.");
            CloseOption();
        }
        else
        {
            Debug.Log("<color=yellow>[OptionManager]</color> 인게임에서 열렸으므로 일시정지 메뉴로 돌아갑니다.");
            SwitchToPausePanel();
        }
    }

public void LoadMainMenu()
    {
        Time.timeScale = 1f; // 정지 상태(ESC 메뉴)에서 나갈 경우를 대비해 반드시 복구
        UnityEngine.SceneManagement.SceneManager.LoadScene("StartScene"); // 메인 메뉴 씬으로 이동
    }
}
