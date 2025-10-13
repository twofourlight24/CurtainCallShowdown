using System.Collections;
using TMPro;
using UnityEngine;

public class StartGamePanel : MonoBehaviour
{
    [Header("Wiring")]
    public GameObject root;             // 패널 루트
    public TMP_Text guideText;          // "공연 장르 : {게임모드}"
    public TMP_Text smallGuideText;     // 모드 간략 설명
    public TMP_Text countdownText;      // Ready.. / Action!

    [Header("Timing")]
    public float guideShowSec = 3f;     // 가이드 표시 시간
    public float readySec = 3f;         // "Ready.." 유지
    public float actionFadeSec = 1f;    // "Action!" 페이드

    private Coroutine running;

    private void Awake()
    {
        if (root != null) root.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }

    /// <summary>
    /// 패널을 강제로 켠 뒤, 가이드/카운트다운 플로우를 실행한다.
    /// </summary>
    public void ShowAndRun(string modeName, string brief)
    {
        // 비활성 상태에서 코루틴이 안 도는 문제 → 내부에서 강제 활성
        if (root != null && !root.activeSelf) root.SetActive(true);
        gameObject.SetActive(true);

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Co_Run(modeName, brief));
    }

    private IEnumerator Co_Run(string modeName, string brief)
    {
        // 1) 가이드 표시
        if (guideText != null) guideText.text = $"공연 장르 : <b>{modeName}</b>";
        if (smallGuideText != null) smallGuideText.text = brief;

        if (guideText != null) guideText.gameObject.SetActive(true);
        if (smallGuideText != null) smallGuideText.gameObject.SetActive(true);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        yield return new WaitForSeconds(guideShowSec);

        // 2) 가이드 끄고 Ready.. 카운트다운
        if (guideText != null) guideText.gameObject.SetActive(false);
        if (smallGuideText != null) smallGuideText.gameObject.SetActive(false);

        if (countdownText != null)
        {
            countdownText.alpha = 1f;
            countdownText.text = "Ready..";
            countdownText.gameObject.SetActive(true);
        }
        yield return new WaitForSeconds(readySec);

        // 3) Action! → 페이드 아웃
        if (countdownText != null) countdownText.text = "Action!";

        float t = 0f;
        while (t < actionFadeSec)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / actionFadeSec);
            if (countdownText != null) countdownText.alpha = k;
            yield return null;
        }

        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (root != null) root.SetActive(false);

        // 4) 게임 시작 알림 (중복 방지 로직은 GameManager쪽에서 처리)
        GameManager.Instance?.NotifyStartPanelFinished();
    }
}