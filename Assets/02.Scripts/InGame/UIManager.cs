using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class UIManager : MonoBehaviourPunCallbacks
{
    [Header("UI - In-Game Info")]
    public GameObject[] playerInfoPanels;
    public Image[] playerIcons;
    public TMP_Text[] playerNicknames;
    public Image[] playerHpBars;
    public Color defaultHpBarColor = Color.green;
    public GameObject[] playerLifeIconsParent;

    [Header("UI - Other")]
    public GameObject respawnOverlayPanel;  // 회색 이미지 패널 (Canvas 안)
    public TMP_Text respawnCountdownText;   // 남은 시간 표시

    [Header("UI - Result Panels")]
    public RoundResultPanel roundResultPanel;
    public TotalScorePanel totalScorePanel;
    public GamemodeVotePanel votePanel;



    private int GetPlayerIndex(Player target)
    {
        if (target == null) return -1;
        var ordered = PhotonNetwork.PlayerList.OrderBy(p => p.ActorNumber).ToArray();
        for (int i = 0; i < ordered.Length; i++)
            if (ordered[i] == target) return i;
        return -1;
    }


    public void InitializeInGameUI()
    {
        // 모든 플레이어 UI 패널을 비활성화
        foreach (var panel in playerInfoPanels)
        {
            panel.SetActive(false);
        }
    }
    // 선택: 모든 패널을 "정렬된 순서"로 활성화
    public void ActivatePanelsForAllPlayers()
    {
        foreach (var panel in playerInfoPanels)
            panel.SetActive(false);

        var ordered = PhotonNetwork.PlayerList.OrderBy(p => p.ActorNumber).ToArray();
        for (int i = 0; i < ordered.Length && i < playerInfoPanels.Length; i++)
        {
            playerInfoPanels[i].SetActive(true);
            playerNicknames[i].text = ordered[i].NickName;
            playerHpBars[i].fillAmount = 1f;
        }
    }


    public void UpdatePlayerUI(Player targetPlayer, CharacterBase character, CharacterData characterData)
    {
        int playerIndex = GetPlayerIndex(targetPlayer);
        if (playerIndex < 0 || playerIndex >= playerInfoPanels.Length) return;

        // 체력
        if (character != null && character.MaxHp > 0f)
        {
            float ratio = Mathf.Clamp01(character.CurHp / character.MaxHp);
            playerHpBars[playerIndex].fillAmount = ratio;
            // Debug.Log($"[UIManager] {targetPlayer.NickName} HP {character.CurHp}/{character.MaxHp} ({ratio})");
        }

        // 아이콘
        if (characterData != null && playerIcons[playerIndex] != null)
            playerIcons[playerIndex].sprite = characterData.data.characterIcon;

        // 닉네임(안정화)
        playerNicknames[playerIndex].text = targetPlayer.NickName;

        // 패널 활성
        playerInfoPanels[playerIndex].SetActive(true);
    }
    public void RefreshAllPlayerUI(Dictionary<string, GameObject> playerCharacters)
    {
        // 모든 패널 비활성화
        for (int i = 0; i < playerInfoPanels.Length; i++)
        {
            playerInfoPanels[i].SetActive(false);
        }

        // 현재 방 플레이어 전부 UI 세팅
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            var player = PhotonNetwork.PlayerList[i];
            if (playerCharacters.TryGetValue(player.NickName, out GameObject characterObj) && characterObj != null)
            {
                CharacterBase character = characterObj.GetComponent<CharacterBase>();
                CharacterData characterData = characterObj.GetComponent<CharacterData>();
                UpdatePlayerUI(player, character, characterData);
            }
        }
    }


    // 기존 UpdateLifeUI 교체: 같은 인덱싱 규칙 사용
    public void UpdateLifeUI(Player targetPlayer, int currentLives)
    {
        int playerIndex = GetPlayerIndex(targetPlayer);
        if (playerIndex < 0 || playerIndex >= playerLifeIconsParent.Length) return;

        var parent = playerLifeIconsParent[playerIndex];
        if (parent == null) return;

        int childCount = parent.transform.childCount;
        for (int j = 0; j < childCount; j++)
            parent.transform.GetChild(j).gameObject.SetActive(j < currentLives);
    }
    // 라운드 결과 보여주기
    public void ShowRoundResultUI(List<Player> ranking, Dictionary<int, int> roundPoints)
    {
        roundResultPanel.gameObject.SetActive(true);
        totalScorePanel.gameObject.SetActive(false);
        roundResultPanel.Bind(ranking, roundPoints, PhotonNetwork.IsMasterClient);
    }

    // 총점 패널 보여주기
    public void ShowTotalScoreUI(Dictionary<int, int> totalPoints, int roundsLeft, string eventsCsv)
    {
        if (totalScorePanel != null)
        {
            roundResultPanel.gameObject.SetActive(false);
            totalScorePanel.gameObject.SetActive(true);

            totalScorePanel.Bind(totalPoints, roundsLeft, eventsCsv, PhotonNetwork.IsMasterClient);
        }
    }

    public void SetHpBarColor(Player target, Color c)
    {
        int idx = System.Array.FindIndex(Photon.Pun.PhotonNetwork.PlayerList, p => p == target);
        if (idx < 0 || idx >= playerHpBars.Length) return;
        if (playerHpBars[idx] != null) playerHpBars[idx].color = c;
    }

    public void ResetHpBarColor(Player target)
    {
        SetHpBarColor(target, defaultHpBarColor);
    }

    public void OpenGameModeVotePanel()
    {
        if (votePanel == null) return;
        votePanel.gameObject.SetActive(true);
        StartCoroutine(Co_OpenVoteWhenPropsReady());
        votePanel.InitializeFromRoom();
    }
    private System.Collections.IEnumerator Co_OpenVoteWhenPropsReady()
    {
        float timeout = 2.0f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (PhotonNetwork.CurrentRoom != null &&
                PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("VoteOptions") &&
                PhotonNetwork.CurrentRoom.CustomProperties["VoteOptions"] is string voteStr &&
                !string.IsNullOrEmpty(voteStr))
            {
                // 옵션이 세팅됨 → 이제 패널 초기화 가능
                Debug.Log("[UI] VoteOptions ready. Opening panel.");
                votePanel.InitializeFromRoom();
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[UI] VoteOptions not ready; fallback initialize.");
        votePanel.InitializeFromRoom(); // 그래도 열어줌 (디버그용)
    }

    public void ShowRespawnOverlay(float seconds)
    {
        if (respawnOverlayPanel != null) respawnOverlayPanel.SetActive(true);
        if (respawnCountdownText != null) respawnCountdownText.text = Mathf.CeilToInt(seconds).ToString();
        StopCoroutine("RespawnOverlayRoutine");
        StartCoroutine(RespawnOverlayRoutine(seconds));
    }
    public void CloseResultsAndVotePanels()
    {
        try
        {
            if (roundResultPanel) roundResultPanel.gameObject.SetActive(false);
            if (totalScorePanel) totalScorePanel.gameObject.SetActive(false);
            if (votePanel) votePanel.gameObject.SetActive(false);   // GamemodeVotePanel 참조 필드
        }
        catch { }
    }

    public void HideRespawnOverlay()
    {
        StopCoroutine("RespawnOverlayRoutine");
        if (respawnOverlayPanel != null) respawnOverlayPanel.SetActive(false);
    }

    private IEnumerator RespawnOverlayRoutine(float seconds)
    {
        float t = seconds;
        while (t > 0f)
        {
            if (respawnCountdownText != null)
                respawnCountdownText.text = Mathf.CeilToInt(t).ToString();
            t -= Time.deltaTime;
            yield return null;
        }
        // 리스폰 직전에 자동 숨기지는 말고, 리스폰 완료 시 GameManager가 Hide 호출
    }
}
