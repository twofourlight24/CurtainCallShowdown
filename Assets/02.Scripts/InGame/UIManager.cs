using Photon.Pun;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 인게임 UI를 관리하는 스크립트.
/// 게임 매니저와 분리하여 UI 로직을 담당합니다.
/// </summary>
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

    /// <summary>
    /// 인게임 UI를 초기화합니다.
    /// </summary>
    public void InitializeInGameUI()
    {
        // 모든 플레이어 UI 패널을 비활성화
        foreach (var panel in playerInfoPanels)
        {
            panel.SetActive(false);
        }
    }

    /// <summary>
    /// 플레이어의 UI를 업데이트합니다.
    /// 이 함수는 GameManager에서 호출됩니다.
    /// </summary>
    /// <param name="targetPlayer">UI를 업데이트할 플레이어</param>
    /// <param name="character">플레이어 캐릭터의 CharacterBase 컴포넌트</param>
    /// <param name="characterData">플레이어 캐릭터의 CharacterData 컴포넌트</param>
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

    /// <summary>
    /// 플레이어의 목숨 UI를 업데이트합니다.
    /// 이 함수는 GameManager에서 호출됩니다.
    /// </summary>
    /// <param name="targetPlayer">UI를 업데이트할 플레이어</param>
    /// <param name="currentLives">현재 목숨 수</param>
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

    /// <summary>
    /// 게임 종료 UI를 표시합니다.
    /// </summary>
    /// <param name="resultMessage">표시할 결과 메시지</param>
    public void DisplayEndGameUI(string resultMessage)
    {
        resultPanel.SetActive(true);
        resultText.text = resultMessage;
    }
}
