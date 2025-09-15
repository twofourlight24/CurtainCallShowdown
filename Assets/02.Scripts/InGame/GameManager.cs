using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

// 게임 전체를 관리하는 스크립트
public class GameManager : MonoBehaviourPunCallbacks
{
    // UIManager 참조
    public UIManager uiManager;

    [Header("Game Spawning & References")]
    public Transform[] spawnPoints;
    // 게임 시작 시 Resource 폴더에서 캐릭터 프리팹을 로드합니다.
    private Dictionary<string, GameObject> characterPrefabs = new Dictionary<string, GameObject>();

    // 캐릭터 오브젝트 참조를 저장할 딕셔너리
    private Dictionary<string, GameObject> playerCharacters = new Dictionary<string, GameObject>();

    // 현재 활성화된 게임 모드 스크립트
    private ShowdownMode currentActiveGameMode;

    private string gameMode;
    private int roundCount;
    private string mapName;
    private GameObject currentMap;

    // ✅ 플레이어별 스폰 여부 관리
    private Dictionary<string, bool> playersSpawned = new Dictionary<string, bool>();

    void Start()
    {
        if (uiManager == null)
        {
            Debug.LogError("UIManager를 찾을 수 없습니다. UIManager 컴포넌트를 GameManager에 연결해주세요.");
            return;
        }

        // ✅ playersSpawned 기본값 주입 (씬 입장 시점)
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!playersSpawned.ContainsKey(p.NickName))
                playersSpawned[p.NickName] = false;
        }

        LoadCharacterPrefabs();
        LoadGameSettings();
        uiManager.InitializeInGameUI();

        // 맵 로드/스폰 대기 -> 그 후 게임 모드 초기화와 스폰 실행
        StartCoroutine(InitializeGameAfterMapLoaded());
    }

    private IEnumerator InitializeGameAfterMapLoaded()
    {
        // 1) 마스터 클라이언트는 맵을 네트워크로 생성
        if (PhotonNetwork.IsMasterClient)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogError("맵 이름이 설정되지 않았습니다.");
            }
            else
            {
                GameObject mapPrefab = Resources.Load<GameObject>(mapName);
                if (mapPrefab != null)
                {
                    Debug.Log("[Master] Instantiating map: " + mapName);
                    currentMap = PhotonNetwork.Instantiate(mapPrefab.name, Vector3.zero, Quaternion.identity);
                    yield return new WaitForSeconds(0.15f);
                    if (photonView != null)
                    {
                        photonView.RPC(nameof(RPC_OnMapInstantiated), RpcTarget.Others);
                    }
                }
                else
                {
                    Debug.LogError($"'Resources/{mapName}'에서 맵 프리팹을 찾을 수 없습니다.");
                }
            }
        }

        // 2) 모든 클라이언트(마스터 포함)는 '스폰포인트가 준비될 때까지' 기다림
        yield return StartCoroutine(WaitForSpawnPoints(timeoutSeconds: 8f));

        // 3) 맵/스폰 준비 끝난 뒤에 게임 모드 초기화
        InitializeGameModeAfterMapLoaded();
    }

    [PunRPC]
    private void RPC_OnMapInstantiated()
    {
        Debug.Log("[Client] Received RPC_OnMapInstantiated from master.");
        StartCoroutine(WaitForSpawnPointsAndThenSpawn());
    }

    private IEnumerator WaitForSpawnPointsAndThenSpawn()
    {
        yield return StartCoroutine(WaitForSpawnPoints(timeoutSeconds: 8f));
        InitializeGameModeAfterMapLoaded();
    }

    private IEnumerator WaitForSpawnPoints(float timeoutSeconds = 5f)
    {
        float timer = 0f;
        GameObject[] spawnPointObjects = null;

        while (timer < timeoutSeconds)
        {
            spawnPointObjects = GameObject.FindGameObjectsWithTag("SpawnPoint");
            if (spawnPointObjects != null && spawnPointObjects.Length > 0)
            {
                break;
            }
            timer += Time.deltaTime;
            yield return null;
        }

        if (spawnPointObjects == null || spawnPointObjects.Length == 0)
        {
            Debug.LogError($"[GameManager] SpawnPoint를 찾지 못했습니다. 타임아웃({timeoutSeconds}s). 현재 Count={(spawnPointObjects == null ? 0 : spawnPointObjects.Length)}");
            yield break;
        }

        spawnPoints = spawnPointObjects.Select(obj => obj.transform).ToArray();
        System.Array.Sort(spawnPoints, (a, b) => string.Compare(a.name, b.name));
    }

    private void InitializeGameModeAfterMapLoaded()
    {
        if (gameMode == "Showdown")
        {
            if (currentActiveGameMode == null)
            {
                currentActiveGameMode = gameObject.AddComponent<ShowdownMode>();
                currentActiveGameMode.Initialize(this);
            }

            // ✅ 이전 씬에서 이미 선택돼 있었으면 즉시 스폰 시도
            TrySpawnIfAlreadySelected();

            // 이후에도 안전망으로 대기 (스폰 완료 시 true로 변경되어 빠져나감)
            StartCoroutine(WaitForCharacterSelectionAndSpawn());
        }
        else
        {
            Debug.LogError("알 수 없는 게임 모드입니다: " + gameMode);
        }
    }

    // ✅ 이미 선택된 상태라면 변경 이벤트 없이도 즉시 스폰
    private void TrySpawnIfAlreadySelected()
    {
        Photon.Realtime.Player lp = PhotonNetwork.LocalPlayer;
        if (lp == null) return;

        if (lp.CustomProperties != null &&
            lp.CustomProperties.TryGetValue("SelectedCharacterName", out object val) &&
            val is string s && !string.IsNullOrEmpty(s) &&
            (!playersSpawned.ContainsKey(lp.NickName) || !playersSpawned[lp.NickName]))
        {
            Debug.Log("[GameManager] TrySpawnIfAlreadySelected: 이미 선택된 캐릭터 감지 → 즉시 스폰");
            SpawnLocalPlayer();
        }
    }

    // ✅ Dictionary 기반 체크 (스폰 완료까지 대기)
    public IEnumerator WaitForCharacterSelectionAndSpawn()
    {
        Photon.Realtime.Player localPlayer = PhotonNetwork.LocalPlayer;

        while (!playersSpawned.ContainsKey(localPlayer.NickName) || !playersSpawned[localPlayer.NickName])
        {
            yield return null;
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        base.OnPlayerPropertiesUpdate(targetPlayer, changedProps);

        // 로컬 플레이어가 선택한 캐릭터 정보가 올라왔을 때
        if (targetPlayer.IsLocal && changedProps.ContainsKey("SelectedCharacterName"))
        {
            if (changedProps["SelectedCharacterName"] != null &&
                (!playersSpawned.ContainsKey(targetPlayer.NickName) || !playersSpawned[targetPlayer.NickName]))
            {
                Debug.Log("[GameManager] SelectedCharacterName 감지. 로컬 플레이어 스폰 시작.");
                SpawnLocalPlayer();
            }
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!playersSpawned.ContainsKey(newPlayer.NickName))
            playersSpawned[newPlayer.NickName] = false;
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (playersSpawned.ContainsKey(otherPlayer.NickName))
            playersSpawned.Remove(otherPlayer.NickName);
    }

    private void SpawnLocalPlayer()
    {
        Photon.Realtime.Player localPlayer = PhotonNetwork.LocalPlayer;

        if (playersSpawned.ContainsKey(localPlayer.NickName) && playersSpawned[localPlayer.NickName])
        {
            Debug.Log("[GameManager] 이미 플레이어를 스폰했습니다. 중복 방지.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[GameManager] SpawnLocalPlayer 호출되었으나 spawnPoints가 비어있습니다.");
            return;
        }

        object selectedCharacterNameObj;
        if (!localPlayer.CustomProperties.TryGetValue("SelectedCharacterName", out selectedCharacterNameObj))
        {
            Debug.LogError($"플레이어 {localPlayer.NickName}의 SelectedCharacterName이 설정되어 있지 않습니다.");
            return;
        }

        string characterPrefabName = (string)selectedCharacterNameObj;
        if (string.IsNullOrEmpty(characterPrefabName) || !characterPrefabs.ContainsKey(characterPrefabName))
        {
            Debug.LogError($"플레이어 {localPlayer.NickName}의 유효하지 않은 캐릭터 프리팹 이름: {characterPrefabName}");
            return;
        }

        int spawnIndex = (localPlayer.ActorNumber - 1) % spawnPoints.Length;
        Vector3 spawnPos = spawnPoints[spawnIndex].position;
        Quaternion spawnRot = spawnPoints[spawnIndex].rotation;

        Debug.Log($"[GameManager] Local player {localPlayer.NickName} (Actor {localPlayer.ActorNumber}) 스폰 인덱스 {spawnIndex} 위치 {spawnPos}.");

        GameObject characterObject = PhotonNetwork.Instantiate("Characters/" + characterPrefabName, spawnPos, spawnRot);
        if (characterObject != null)
        {
            CharacterBase characterBase = characterObject.GetComponent<CharacterBase>();
            if (characterBase != null && characterObject.GetPhotonView().IsMine)
            {
                PlayerInput playerInput = characterObject.AddComponent<PlayerInput>();
                playerInput.controlledCharacter = characterBase;
            }

            playerCharacters[localPlayer.NickName] = characterObject;
            playersSpawned[localPlayer.NickName] = true;

            // 모든 플레이어 UI 갱신
            RefreshAllPlayerUI();
            uiManager.RefreshAllPlayerUI(playerCharacters);

        }
        else
        {
            Debug.LogError("[GameManager] PhotonNetwork.Instantiate가 null을 반환했습니다: " + characterPrefabName);
        }
    }

    //  모든 플레이어의 UI를 갱신하는 함수
    private void RefreshAllPlayerUI()
    {
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (playerCharacters.ContainsKey(p.NickName))
            {
                GameObject obj = playerCharacters[p.NickName];
                if (obj != null)
                {
                    uiManager.UpdatePlayerUI(p, obj.GetComponent<CharacterBase>(), obj.GetComponent<CharacterData>());
                }
            }
        }
    }

    private void LoadCharacterPrefabs()
    {
        // ✅ Characters 폴더에서만 로드
        GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>("Characters");

        foreach (GameObject prefab in loadedPrefabs)
        {
            characterPrefabs[prefab.name] = prefab;
        }

        if (characterPrefabs.Count == 0)
        {
            Debug.LogError("Resources/Characters 폴더에서 캐릭터 프리팹을 찾을 수 없습니다. 게임 시작에 실패했습니다.");
        }
        else
        {
            Debug.Log("[GameManager] Loaded character prefabs: " + string.Join(", ", characterPrefabs.Keys.Take(10)));
        }
    }

    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        ExitGames.Client.Photon.Hashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;

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

        Debug.Log($"[GameManager] GameMode={gameMode}, RoundCount={roundCount}, MapName={mapName}");
    }

    // UI 업데이트 함수들
    public void UpdatePlayerUI(Photon.Realtime.Player targetPlayer)
    {
        if (uiManager == null) return;
        if (!playerCharacters.ContainsKey(targetPlayer.NickName)) return;

        GameObject characterObject = playerCharacters[targetPlayer.NickName];
        if (characterObject == null) return;

        CharacterBase character = characterObject.GetComponent<CharacterBase>();

        CharacterData.PlayerCharacterData characterData = characterObject.GetComponent<CharacterData>().data;

        uiManager.UpdatePlayerUI(targetPlayer, character, characterObject.GetComponent<CharacterData>());
    }

    public IEnumerator RespawnPlayer(Photon.Realtime.Player player)
    {
        if (playerCharacters.ContainsKey(player.NickName) && playerCharacters[player.NickName] != null)
        {
            playerCharacters[player.NickName].SetActive(false);
            yield return new WaitForSeconds(3.0f);
            playerCharacters[player.NickName].SetActive(true);

            CharacterBase character = playerCharacters[player.NickName].GetComponent<CharacterBase>();
            if (character != null)
            {
                character.CurHp = character.MaxHp;
                UpdatePlayerUI(player);
            }
        }
    }

    public void UpdateLifeUI(Photon.Realtime.Player targetPlayer, int currentLives)
    {
        if (uiManager == null) return;
        uiManager.UpdateLifeUI(targetPlayer, currentLives);
    }

    public void EndGame(Photon.Realtime.Player winner)
    {
        if (PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
        }

        if (winner != null)
        {
            uiManager.resultText.text = $"승자: {winner.NickName}";
        }
        else
        {
            uiManager.resultText.text = "무승부!";
        }

        uiManager.resultPanel.SetActive(true);
    }
}
