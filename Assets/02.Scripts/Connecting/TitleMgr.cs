using UnityEngine;
using UnityEngine.UI;

public class TitleMgr : MonoBehaviour
{
    public Button startButton;
    public Button OptionButton;
    public GameObject OptionPanel;
    public Button ReturnButton;
    public Button ExitButton;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startButton.onClick.AddListener(() =>
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyScene");
        });
        // Application.Quit()는 에디터에서는 동작하지 않습니다.
        // 빌드된 실행 파일(.exe 등)에서만 정상적으로 게임이 종료됩니다.
        // 에디터에서 테스트하려면 아래처럼 UnityEditor.EditorApplication.isPlaying을 false로 설정하세요.

        ExitButton.onClick.AddListener(() =>
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        });
        OptionButton.onClick.AddListener(() =>
        {
            OptionPanel.SetActive(true);
        });
        ReturnButton.onClick.AddListener(() =>
        {
            OptionPanel.SetActive(false);
        });
    }

    // Update is called once per frame
    void Update()
    {

    }
}
