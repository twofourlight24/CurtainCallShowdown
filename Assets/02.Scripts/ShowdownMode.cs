using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Linq;
using System.Collections;

// 쇼다운 게임 모드를 관리하는 스크립트
public class ShowdownMode : MonoBehaviourPunCallbacks
{
    private GameManager gameManager;
    private GameObject[] characterPrefabs;

    private int lifeCount = 3;
    private Dictionary<string, int> playerLives = new Dictionary<string, int>();

    /// <summary>
    /// 게임 모드를 초기화하고 필요한 참조를 설정합니다.
    /// </summary>
    public void Initialize(GameManager manager, GameObject[] prefabs)
    {
        gameManager = manager;
        characterPrefabs = prefabs;
        InitializeShowdownMode();
    }

    /// <summary>
    /// 쇼다운 모드를 초기화하고 플레이어들을 스폰시킵니다.
    /// </summary>
    private void InitializeShowdownMode()
    {
        gameManager.resultPanel.SetActive(false);

        // characterPrefabs 배열이 비어있는지 확인
        if (characterPrefabs == null || characterPrefabs.Length == 0)
        {
            Debug.LogError("Character prefabs array is null or empty. Cannot spawn any characters.");
            return;
        }

        // playerInfoPanels의 크기 확인
        int maxPlayers = gameManager.playerInfoPanels.Length;

        // 현재 방의 모든 플레이어 리스트 가져오기
        var playerList = PhotonNetwork.PlayerList;

        // 모든 플레이어 정보 패널 비활성화 (초기화)
        for (int i = 0; i < maxPlayers; i++)
        {
            gameManager.playerInfoPanels[i].SetActive(false);
        }

        // 플레이어 수만큼만 UI 활성화 및 스폰 로직 실행
        for (int i = 0; i < playerList.Length; i++)
        {
            var player = playerList[i];

            // UI 패널 배열의 인덱스 범위를 초과하는지 확인하여 오류 방지
            if (i >= maxPlayers)
            {
                Debug.LogError("Player count exceeds the size of player info panels array. Some players will not have UI panels.");
                break;
            }

            if (i < gameManager.spawnPoints.Length)
            {
                // UI 활성화 및 닉네임 설정
                gameManager.playerInfoPanels[i].SetActive(true);
                gameManager.playerNicknames[i].text = player.NickName;

                // 캐릭터 정보 로드 및 스폰
                object selectedCharacterIndexObj;
                int characterIndex = 0;
                if (player.CustomProperties.TryGetValue("SelectedCharacter", out selectedCharacterIndexObj))
                {
                    characterIndex = (int)selectedCharacterIndexObj;
                }
                else
                {
                    Debug.LogWarning($"Player {player.NickName} did not select a character. Assigning default character.");
                }

                GameObject selectedCharacterPrefab = null;
                if (characterIndex >= 0 && characterIndex < characterPrefabs.Length)
                {
                    selectedCharacterPrefab = characterPrefabs[characterIndex];
                }
                else
                {
                    Debug.LogError($"Invalid character index for player {player.NickName}. Assigning default character.");
                    selectedCharacterPrefab = characterPrefabs[0];
                }

                // 캐릭터 프리팹이 여전히 유효한지 최종 확인
                if (selectedCharacterPrefab == null)
                {
                    Debug.LogError($"No valid character prefab found for player {player.NickName}. Cannot spawn character.");
                    continue; // 다음 플레이어로 넘어감
                }

                // 플레이어의 캐릭터를 네트워크 상에 생성
                PhotonNetwork.Instantiate(selectedCharacterPrefab.name, gameManager.spawnPoints[i].position, gameManager.spawnPoints[i].rotation);

                // 목숨 초기화
                playerLives[player.NickName] = lifeCount;
                UpdateLifeUI(player.NickName, lifeCount);
            }
        }
    }

    /// <summary>
    /// 캐릭터의 체력이 0이 되었을 때 호출됩니다.
    /// </summary>
    /// <param name="player">체력을 잃은 플레이어</param>
    public void OnPlayerDied(Photon.Realtime.Player player)
    {
        if (!playerLives.ContainsKey(player.NickName)) return;

        playerLives[player.NickName]--;
        UpdateLifeUI(player.NickName, playerLives[player.NickName]);

        if (playerLives[player.NickName] <= 0)
        {
            // 목숨이 0이면 게임에서 탈락
            player.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "IsEliminated", true } });
            CheckForWinner();
        }
        else
        {
            // 목숨이 남아있으면 부활
            gameManager.StartCoroutine(RespawnPlayer(player));
        }
    }

    /// <summary>
    /// 플레이어의 목숨 UI를 업데이트합니다.
    /// </summary>
    /// <param name="playerName">플레이어 닉네임</param>
    /// <param name="currentLives">현재 목숨 수</param>
    private void UpdateLifeUI(string playerName, int currentLives)
    {
        // UI에서 해당 플레이어를 찾고 목숨 이미지 업데이트
        int playerIndex = System.Array.FindIndex(gameManager.playerNicknames, t => t.text == playerName);
        if (playerIndex >= 0)
        {
            for (int i = 0; i < gameManager.playerLifeIconsParent[playerIndex].transform.childCount; i++)
            {
                if (i < currentLives)
                {
                    gameManager.playerLifeIconsParent[playerIndex].transform.GetChild(i).gameObject.SetActive(true);
                }
                else
                {
                    gameManager.playerLifeIconsParent[playerIndex].transform.GetChild(i).gameObject.SetActive(false);
                }
            }
        }
    }

    /// <summary>
    /// 플레이어를 부활시킵니다.
    /// </summary>
    private IEnumerator RespawnPlayer(Photon.Realtime.Player player)
    {
        yield return new WaitForSeconds(3.0f); // 3초 부활 대기

        // TODO: 체력 회복 로직 추가

        // TODO: 캐릭터를 스폰 포인트로 이동 (P1의 스폰 포인트로 임시 설정)
    }

    /// <summary>
    /// 최후의 1인이 남았는지 확인하고 게임을 종료합니다.
    /// </summary>
    private void CheckForWinner()
    {
        var activePlayers = PhotonNetwork.PlayerList.Where(p => !(p.CustomProperties.ContainsKey("IsEliminated") && (bool)p.CustomProperties["IsEliminated"])).ToList();

        if (activePlayers.Count <= 1)
        {
            // 게임 종료 및 결과 패널 활성화
            gameManager.resultPanel.SetActive(true);
            if (activePlayers.Count == 1)
            {
                gameManager.resultText.text = $"승자: {activePlayers[0].NickName}";
            }
            else
            {
                gameManager.resultText.text = "무승부!"; // 모든 플레이어가 동시에 사망한 경우
            }
        }
    }
}
