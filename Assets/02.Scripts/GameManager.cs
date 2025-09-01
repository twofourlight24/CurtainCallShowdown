using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Linq;
using System.Collections;

// 게임 전체를 관리하는 스크립트
public class GameManager : MonoBehaviourPunCallbacks
{
    // 게임 오브젝트 참조
    public GameObject resultPanel;
    public TMP_Text resultText;
    public Transform[] spawnPoints;
    public GameObject[] characterPrefabs;

    [Header("UI - In-Game Info")]
    public GameObject[] playerInfoPanels;
    public Image[] playerIcons;
    public TMP_Text[] playerNicknames;
    public Image[] playerHpBars; // Slider에서 Image로 변경
    public GameObject[] playerLifeIconsParent;

    // 캐릭터 오브젝트 참조를 저장할 딕셔너리
    private Dictionary<string, GameObject> playerCharacters = new Dictionary<string, GameObject>();

    // 게임 모드 스크립트를 관리할 변수
    private Dictionary<string, GameObject> gameModePrefabs = new Dictionary<string, GameObject>();

    // 현재 활성화된 게임 모드 스크립트
    private ShowdownMode currentActiveGameMode;

    private string gameMode;
    private int roundCount;
    private string mapName;
    private GameObject currentMap;

    void Start()
    {
        // 게임 설정 정보 로드
        LoadGameSettings();

        // 맵 프리팹 생성 및 스폰 포인트 로드
        LoadMapAndSpawnPoints();

        // UI 초기화
        InitializeInGameUI();

        // 게임 모드에 따라 적절한 스크립트 초기화 및 실행
        if (gameMode == "Showdown")
        {
            // ShowdownMode 스크립트를 GameManager에 추가하고 참조를 얻습니다.
            currentActiveGameMode = gameObject.AddComponent<ShowdownMode>();
            currentActiveGameMode.Initialize(this, characterPrefabs);
        }
        else
        {
            Debug.LogError("알 수 없는 게임 모드입니다: " + gameMode);
        }
    }

    /// <summary>
    /// RoomManager의 커스텀 속성에서 게임 설정을 불러옵니다.
    /// </summary>
    private void LoadGameSettings()
    {
        ExitGames.Client.Photon.Hashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties; // 명시적 네임스페이스 사용

        if (roomProps.ContainsKey("GameMode"))
        {
            gameMode = (string)roomProps["GameMode"];
        }
        if (roomProps.ContainsKey("RoundCount"))
        {
            roundCount = (int)roomProps["RoundCount"];
        }
        if (roomProps.ContainsKey("MapName"))
        {
            mapName = (string)roomProps["MapName"];
        }
    }

    /// <summary>
    /// 맵을 생성하고 맵 안의 스폰 포인트를 찾아서 배열에 저장합니다.
    /// </summary>
    private void LoadMapAndSpawnPoints()
    {
        if (string.IsNullOrEmpty(mapName))
        {
            Debug.LogError("맵 이름이 설정되지 않았습니다.");
            return;
        }

        // Resources 폴더에서 맵 프리팹을 로드하고 생성
        GameObject mapPrefab = Resources.Load<GameObject>($"Maps/{mapName}");
        if (mapPrefab != null)
        {
            currentMap = PhotonNetwork.Instantiate(mapPrefab.name, Vector3.zero, Quaternion.identity);

            // "SpawnPoint" 태그를 가진 모든 오브젝트를 찾습니다.
            GameObject[] spawnPointObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (spawnPointObjects.Length > 0)
            {
                spawnPoints = spawnPointObjects.Select(obj => obj.transform).ToArray();
                // 스폰 포인트를 인덱스에 따라 정렬하여 순서를 보장합니다.
                System.Array.Sort(spawnPoints, (a, b) => string.Compare(a.name, b.name));
            }
            else
            {
                Debug.LogError("맵에 'SpawnPoint' 태그를 가진 오브젝트가 없습니다. 모든 스폰 포인트에 태그를 추가해주세요.");
            }
        }
        else
        {
            Debug.LogError($"'Maps/{mapName}' 경로에서 맵 프리팹을 찾을 수 없습니다.");
        }
    }

    /// <summary>
    /// 인게임 UI를 초기화합니다.
    /// </summary>
    private void InitializeInGameUI()
    {
        // 모든 플레이어 UI 패널을 비활성화
        foreach (var panel in playerInfoPanels)
        {
            panel.SetActive(false);
        }

        // 현재 방에 있는 플레이어 수만큼 UI 패널 활성화
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            Photon.Realtime.Player player = PhotonNetwork.PlayerList[i];

            if (i < playerInfoPanels.Length)
            {
                playerInfoPanels[i].SetActive(true);
                playerNicknames[i].text = player.NickName;
            }
        }
    }

    /// <summary>
    /// 플레이어의 UI를 업데이트합니다.
    /// </summary>
    /// <param name="targetPlayer">UI를 업데이트할 플레이어</param>
    public void UpdatePlayerUI(Photon.Realtime.Player targetPlayer)
    {
        int playerIndex = -1;
        for (int i = 0; i < PhotonNetwork.PlayerList.Length; i++)
        {
            if (PhotonNetwork.PlayerList[i] == targetPlayer)
            {
                playerIndex = i;
                break;
            }
        }

        if (playerIndex != -1 && playerCharacters.ContainsKey(targetPlayer.NickName))
        {
            GameObject characterObject = playerCharacters[targetPlayer.NickName];
            CharacterBase character = characterObject.GetComponent<CharacterBase>();

            if (character != null)
            {
                // 체력바 업데이트 (Image fillAmount 사용)
                playerHpBars[playerIndex].fillAmount = character.CurHp / character.MaxHp;

                // 목숨 UI 업데이트
                if (playerLifeIconsParent[playerIndex] != null)
                {
                    for (int j = 0; j < playerLifeIconsParent[playerIndex].transform.childCount; j++)
                    {
                        // 목숨 수만큼 아이콘 활성화
                        playerLifeIconsParent[playerIndex].transform.GetChild(j).gameObject.SetActive(j < character.LifeCount);
                    }
                }

                // CharacterData 스크립트에서 아이콘을 가져와 UI에 적용
                CharacterData characterData = characterObject.GetComponent<CharacterData>();
                if (characterData != null && playerIcons[playerIndex] != null)
                {
                    playerIcons[playerIndex].sprite = characterData.data.characterIcon;
                }
            }
        }
    }

    /// <summary>
    /// 플레이어 캐릭터를 스폰 포인트에 생성하고 PlayerInput을 연결합니다.
    /// 이 함수는 ShowdownMode.cs에서 호출됩니다.
    /// </summary>
    /// <param name="player">생성할 플레이어</param>
    /// <param name="characterPrefab">생성할 캐릭터 프리팹</param>
    /// <param name="spawnPoint">스폰 위치</param>
    public void SpawnPlayerCharacter(Photon.Realtime.Player player, GameObject characterPrefab, Transform spawnPoint)
    {
        // 플레이어의 캐릭터를 네트워크 상에 생성
        GameObject characterObject = PhotonNetwork.Instantiate(characterPrefab.name, spawnPoint.position, spawnPoint.rotation);

        // 생성된 캐릭터의 PlayerInput 컴포넌트를 가져와 제어할 캐릭터를 할당
        if (characterObject != null)
        {
            CharacterBase characterBase = characterObject.GetComponent<CharacterBase>();
            if (characterBase != null)
            {
                // 로컬 플레이어일 경우에만 입력 스크립트 활성화
                if (characterObject.GetPhotonView().IsMine)
                {
                    PlayerInput playerInput = characterObject.AddComponent<PlayerInput>();
                    playerInput.controlledCharacter = characterBase;
                }

                // 생성된 캐릭터 오브젝트를 딕셔너리에 저장
                playerCharacters[player.NickName] = characterObject;

                // UI 업데이트
                UpdatePlayerUI(player);
            }
        }
    }
}
