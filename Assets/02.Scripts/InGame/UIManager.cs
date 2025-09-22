using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;


public class UIManager : MonoBehaviourPunCallbacks
{
    [Header("UI - In-Game Info")]
    public GameObject resultPanel;
    public TMP_Text resultText;
    public GameObject[] playerInfoPanels;
    public Image[] playerIcons;
    public TMP_Text[] playerNicknames;
    public Image[] playerHpBars;
    public GameObject[] playerLifeIconsParent;

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
    public void ShowRoundResultUI(List<Player> ranking)
    {
        if (resultPanel != null) resultPanel.SetActive(true);

    }

    public void DisplayEndGameUI(string resultMessage)
    {
        resultPanel.SetActive(true);
        resultText.text = resultMessage;
    }
}
