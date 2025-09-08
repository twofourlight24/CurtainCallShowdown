using UnityEngine;
using UnityEngine.UI;

public class TitleMgr : MonoBehaviour
{
    public Button startButton; 
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        startButton.onClick.AddListener(() =>
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("WatingRoomScene");
        });
    }

    // Update is called once per frame
    void Update()
    {

    }
}
