using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ModeButtonView : MonoBehaviour
{
    [Header("Wiring")]
    public Button button;                      // Mode 버튼
    public TMP_Text modeNameText;              // ShowDownText (TMP)
    public Image selectImg;                    // SelectImg (선정 시 On)
    public Transform voterGroup;               // PlayerSelectGamemodePanel
    public GameObject voterEntryTemplate;      // PlayerSelectGamemodeCheck (비활성 템플릿)

    [Header("Runtime")]
    public string modeName;                    // 이 카드가 표현하는 모드 이름

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (selectImg != null) selectImg.gameObject.SetActive(false);
        if (voterEntryTemplate != null) voterEntryTemplate.SetActive(false);
    }

    public void Bind(string mode)
    {
        modeName = mode;
        if (modeNameText != null) modeNameText.text = mode;
        ClearVoters();
        if (selectImg != null) selectImg.gameObject.SetActive(false);
    }

    public void SetOnClick(System.Action<string> onClick)
    {
        if (button == null) return;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke(modeName));
    }

    public void ClearVoters()
    {
        if (voterGroup == null) return;
        for (int i = voterGroup.childCount - 1; i >= 0; i--)
        {
            var child = voterGroup.GetChild(i);
            if (child == voterEntryTemplate?.transform) continue;
            Destroy(child.gameObject);
        }
    }

    public void AddVoter(Player p, Sprite icon, Color? nickColor = null, int? actorNumberTag = null)
    {
        if (voterGroup == null || voterEntryTemplate == null) return;
        var go = Instantiate(voterEntryTemplate, voterGroup);
        go.SetActive(true);

        var view = go.GetComponent<VoterEntryView>();
        if (view == null) view = go.AddComponent<VoterEntryView>();
        view.Set(p, icon, nickColor ?? Color.white);

        // 선택사항: 결과 하이라이트용 ActorNumber 태깅
        var tag = go.GetComponent<VoterActorTag>();
        if (tag == null) tag = go.AddComponent<VoterActorTag>();
        tag.actorNumber = actorNumberTag ?? p.ActorNumber;
    }

    public void SetSelectedVisual(bool on)
    {
        if (selectImg != null) selectImg.gameObject.SetActive(on);
    }

    // 결과 발표 후, 선정자 닉네임을 하늘색으로 바꾸기
    public void AccentWinnerNick(int winnerActor, Color accentColor)
    {
        foreach (Transform t in voterGroup)
        {
            if (t == voterEntryTemplate?.transform) continue;
            var tag = t.GetComponent<VoterActorTag>();
            if (tag != null && tag.actorNumber == winnerActor)
            {
                var nameText = t.Find("PlayerNickName")?.GetComponent<TMP_Text>();
                if (nameText != null) nameText.color = accentColor;
            }
        }
    }
}

