using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public enum SceneNames { StartScene, BattleScene }

namespace UnityNote
{
    public class SceneLoader : MonoBehaviour
    {
        public static SceneLoader Instance {get; private set; }

        [SerializeField] private GameObject loadingScreen; // 로딩 화면
        [SerializeField] private Image loadingBackGround; // 로딩 화면에 출력되는 배경 이미지
        [SerializeField] private Sprite[] loadingSprites; // 로딩 화면에 출력되는 배경 이미지 배열
        [SerializeField] private float waitChangeDelay; // 씬이 완전히 로드된 후, 로딩 화면이 사라지기까지의 딜레이

        private void Awake()
        {
            if(Instance != null && Instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                Instance = this;

                DontDestroyOnLoad(gameObject);
            }
        }

        public void LoadScene(string name)
        {
            int index = Random.Range(0, loadingSprites.Length);
            loadingBackGround.sprite = loadingSprites[index];
            loadingScreen.SetActive(true);

            StartCoroutine(LoadSceneAsync(name));
        }

        public void LoadScene(SceneNames name)
        {
            LoadScene(name.ToString());
        }

        private IEnumerator LoadSceneAsync(string name)
        {
            AsyncOperation asyncLoad = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(name);

            // 비동기 작업(씬 불러오기)이 완료될 때까지 반복
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            // 새 씬의 Awake/Start가 한 프레임 안에 실행될 수 있도록 여유를 둡니다.
            yield return null;

            MapGenerator mapGenerator = FindLoadedMapGenerator();
            if (mapGenerator != null)
            {
                if (!mapGenerator.IsMapGenerationCompleted)
                {
                    bool generationCompleted = mapGenerator.IsMapGenerationCompleted;
                    System.Action onGenerated = null;
                    onGenerated = () => generationCompleted = true;
                    mapGenerator.OnMapGenerated += onGenerated;

                    while (!generationCompleted)
                    {
                        yield return null;
                    }

                    mapGenerator.OnMapGenerated -= onGenerated;
                }
            }

            if (waitChangeDelay > 0f)
                yield return new WaitForSeconds(waitChangeDelay);

            loadingScreen.SetActive(false);
        }

        private MapGenerator FindLoadedMapGenerator()
        {
            return Object.FindFirstObjectByType<MapGenerator>();
        }
    }
}