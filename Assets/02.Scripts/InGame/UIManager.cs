using Photon.Pun;
using System.Collections.Generic;
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


    public void InitializeInGameUI()
    {
        // 모든 플레이어 UI 패널을 비활성화
        foreach (var panel in playerInfoPanels)
        {
            panel.SetActive(false);
        }
    }
    public void ActivatePanelsForAllPlayers()
    {
        // 모든 패널 비활성화
        foreach (var panel in playerInfoPanels)
            panel.SetActive(false);

        // 현재 룸에 있는 플레이어 수만큼 활성화
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            if (i < playerInfoPanels.Length)
            {
                playerInfoPanels[i].SetActive(true);
                playerNicknames[i].text = PhotonNetwork.PlayerList[i].NickName; // 기본 닉네임 표시
                playerHpBars[i].fillAmount = 1f; // 기본 체력바 풀
            }
        }
    }


    public void UpdatePlayerUI(Photon.Realtime.Player targetPlayer, CharacterBase character, CharacterData characterData)
    {
        // 현재 룸에서 플레이어 순서대로 인덱스 찾기
        int playerIndex = System.Array.FindIndex(PhotonNetwork.PlayerList, p => p == targetPlayer);

        if (playerIndex == -1) return; // 못 찾으면 종료

        if (character != null)
        {
            // 체력바 업데이트 (Image.fillAmount 사용)
            playerHpBars[playerIndex].fillAmount = character.CurHp / character.MaxHp;
        }

        // 캐릭터 아이콘 업데이트
        if (characterData != null && playerIcons[playerIndex] != null)
        {
            playerIcons[playerIndex].sprite = characterData.data.characterIcon;
        }

        // 닉네임 업데이트
        playerNicknames[playerIndex].text = targetPlayer.NickName;

        // 해당 패널 활성화
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


    public void UpdateLifeUI(Photon.Realtime.Player targetPlayer, int currentLives)
    {
        int playerIndex = System.Array.FindIndex(PhotonNetwork.PlayerList, p => p == targetPlayer);

        if (playerIndex != -1)
        {
            if (playerLifeIconsParent[playerIndex] != null)
            {
                for (int j = 0; j < playerLifeIconsParent[playerIndex].transform.childCount; j++)
                {
                    // 목숨 수만큼 아이콘 활성화
                    playerLifeIconsParent[playerIndex].transform.GetChild(j).gameObject.SetActive(j < currentLives);
                }
            }
        }
    }

    public void DisplayEndGameUI(string resultMessage)
    {
        resultPanel.SetActive(true);
        resultText.text = resultMessage;
    }
}
