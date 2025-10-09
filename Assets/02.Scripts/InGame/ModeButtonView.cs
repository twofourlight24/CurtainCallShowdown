using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ModeButtonView : MonoBehaviour
{
    [Header("Refs")]
    public Button button;
    public TMP_Text modeNameText;
    public Image selectImg;
    public Transform voterGroup;
    public GameObject voterEntryTemplate;

    [Header("Runtime")]
    public string modeName;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();

        // 자동 바인딩
        if (modeNameText == null)
            modeNameText = transform.Find("ShowDownText")?.GetComponent<TMP_Text>();

        if (selectImg == null)
            selectImg = transform.Find("SelectImg")?.GetComponent<Image>();

        if (voterGroup == null)
        {
            var t = transform.Find("PlayerSelectGamemodePanel");
            voterGroup = t != null ? t : transform;
        }

        if (voterEntryTemplate == null && voterGroup != null)
        {
            var t = voterGroup.Find("PlayerSelectGamemodeCheck");
            voterEntryTemplate = t ? t.gameObject : null;
        }

        if (selectImg != null) selectImg.gameObject.SetActive(false);
        if (voterEntryTemplate != null) voterEntryTemplate.SetActive(false);
    }

    public void Bind(string mode)
    {
        modeName = mode;
        if (modeNameText != null) modeNameText.text = mode;
        ClearVoters();
        SetSelectedVisual(false);
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
            if (voterEntryTemplate != null && child == voterEntryTemplate.transform) continue;
            Destroy(child.gameObject);
        }
    }

    /// 같은 배우가 이미 있으면 업데이트, 없으면 새로 추가
    public void AddOrUpdateVoter(Player p, Sprite icon, Color nameColor, int actorNumber, bool showX2)
    {
        if (voterGroup == null || voterEntryTemplate == null)
        {
            Debug.LogError($"[ModeButtonView] voterGroup or voterEntryTemplate missing on {name}");
            return;
        }

        // 기존 찾기
        Transform existing = null;
        for (int i = 0; i < voterGroup.childCount; i++)
        {
            var child = voterGroup.GetChild(i);
            if (child == voterEntryTemplate.transform) continue;
            var tag = child.GetComponent<VoterActorTag>();
            if (tag != null && tag.actorNumber == actorNumber)
            {
                existing = child;
                break;
            }
        }

        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = Instantiate(voterEntryTemplate, voterGroup);
            go.SetActive(true);
            var tag = go.GetComponent<VoterActorTag>() ?? go.AddComponent<VoterActorTag>();
            tag.actorNumber = actorNumber;
        }

        // View 세팅
        var view = go.GetComponent<VoterEntryView>();
        if (view == null) view = go.AddComponent<VoterEntryView>();
        view.Set(p, icon, nameColor);

        view.SetX2Suffix(showX2);
    }

    /// 이 버튼에서 해당 배우의 표 UI 제거
    public void RemoveVoter(int actorNumber)
    {
        if (voterGroup == null) return;
        for (int i = voterGroup.childCount - 1; i >= 0; i--)
        {
            var child = voterGroup.GetChild(i);
            if (child == voterEntryTemplate?.transform) continue;
            var tag = child.GetComponent<VoterActorTag>();
            if (tag != null && tag.actorNumber == actorNumber)
            {
                Destroy(child.gameObject);
            }
        }
    }

    public void SetSelectedVisual(bool on)
    {
        if (selectImg != null) selectImg.gameObject.SetActive(on);
    }

    public void AccentWinnerNick(int winnerActor, Color accentColor)
    {
        if (voterGroup == null) return;
        for (int i = 0; i < voterGroup.childCount; i++)
        {
            var child = voterGroup.GetChild(i);
            if (child == voterEntryTemplate?.transform) continue;
            var tag = child.GetComponent<VoterActorTag>();
            if (tag != null && tag.actorNumber == winnerActor)
            {
                var nameText = child.Find("PlayerNickName")?.GetComponent<TMP_Text>();
                if (nameText != null) nameText.color = accentColor;
            }
        }
    }
}

public class VoterActorTag : MonoBehaviour
{
    public int actorNumber;
}
