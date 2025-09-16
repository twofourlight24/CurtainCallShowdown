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
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

// 게임 전체를 관리하는 스크립트 (통합/리팩토링 버전)
// 변경: 기존 로직 유지 + 동기화/UI 누락 문제 보완 (씬 스캔으로 다른 플레이어 캐릭터 참조 확보)
[RequireComponent(typeof(PhotonView))]
public class GameManager : MonoBehaviourPunCallbacks
{
    // 싱글톤 (선택적 접근에 편리)
    public static GameManager Instance { get; private set; }

    // UIManager 참조 (인스펙터에 연결)
    public UIManager uiManager;

    [Header("Game Spawning & References")]
    public Transform[] spawnPoints;

    // Resources/Characters 에 있는 프리팹 이름 -> prefab
    private Dictionary<string, GameObject> characterPrefabs = new Dictionary<string, GameObject>();

    // 씬에 존재하는 플레이어 캐릭터들 (ownerNickName -> character GameObject)
    // NOTE: key는 PhotonPlayer.NickName 사용 (닉네임이 변경되면 주의)
    private Dictionary<string, GameObject> playerCharacters = new Dictionary<string, GameObject>();

    // 현재 활성화된 게임 모드 스크립트
    private ShowdownMode currentActiveGameMode;

    private string gameMode;
    private int roundCount;
    private string mapName;
    private GameObject currentMap;

    // 플레이어별 스폰 여부 관리 (NickName -> spawned?)
    private Dictionary<string, bool> playersSpawned = new Dictionary<string, bool>();

    private void Awake()
    {
        // 싱글톤 설정 (씬 전환 시 중복 방지)
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (uiManager == null)
        {
            Debug.LogError("[GameManager] UIManager가 Inspector에 연결되어 있지 않습니다!");
            return;
        }

        // 현재 룸 플레이어들에 대해 spawned false로 초기화
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (!playersSpawned.ContainsKey(p.NickName))
                playersSpawned[p.NickName] = false;
        }

        LoadCharacterPrefabs();
        LoadGameSettings();

        uiManager.InitializeInGameUI();

        StartCoroutine(InitializeGameAfterMapLoaded());
    }

    #region --- 맵 로드 및 SpawnPoint 대기 ---
    private IEnumerator InitializeGameAfterMapLoaded()
    {
        // 마스터가 맵을 생성
        if (PhotonNetwork.IsMasterClient)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogWarning("[GameManager] 맵 이름이 비어있음 (mapName).");
            }
            else
            {
                GameObject mapPrefab = Resources.Load<GameObject>(mapName);
                if (mapPrefab != null)
                {
                    Debug.Log("[Master] Instantiating map: " + mapName);
                    currentMap = PhotonNetwork.Instantiate(mapPrefab.name, Vector3.zero, Quaternion.identity);
                    // 짧은 딜레이로 다른 클라이언트가 수신할 시간 제공
                    yield return new WaitForSeconds(0.15f);
                    if (photonView != null)
                        photonView.RPC(nameof(RPC_OnMapInstantiated), RpcTarget.Others);
                }
                else
                {
                    Debug.LogError($"[GameManager] Resources/{mapName} 에서 맵 프리팹을 찾을 수 없습니다.");
                }
            }
        }

        // 모든 클라이언트는 SpawnPoint 준비될 때까지 대기
        yield return StartCoroutine(WaitForSpawnPoints(timeoutSeconds: 8f));

        // 스폰포인트 확보 후 게임 모드 초기화
        InitializeGameModeAfterMapLoaded();
    }
    public void RegisterCharacter(Photon.Realtime.Player owner, GameObject characterObj)
    {
        if (!playerCharacters.ContainsKey(owner.NickName))
        {
            playerCharacters[owner.NickName] = characterObj;
            Debug.Log($"[GameManager] {owner.NickName} 캐릭터 등록 완료");

            // UI 갱신도 즉시 실행
            UpdatePlayerUI(owner);
        }
    }

    [PunRPC]
    private void RPC_OnMapInstantiated()
    {
        Debug.Log("[GameManager] RPC_OnMapInstantiated 수신 (Master가 맵 생성)");
        // 마스터가 맵을 생성했다는 신호를 받으면 스폰포인트 대기 이후 초기화 진입
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
                break;

            timer += Time.deltaTime;
            yield return null;
        }

        if (spawnPointObjects == null || spawnPointObjects.Length == 0)
        {
            Debug.LogError($"[GameManager] SpawnPoint를 찾지 못했습니다. 타임아웃({timeoutSeconds}s). Count={ (spawnPointObjects==null?0:spawnPointObjects.Length)}");
            yield break;
        }

        spawnPoints = spawnPointObjects.Select(o => o.transform).ToArray();
        System.Array.Sort(spawnPoints, (a, b) => string.Compare(a.name, b.name));
        Debug.Log($"[GameManager] SpawnPoints 준비 완료. Count={spawnPoints.Length}. 예: {string.Join(", ", spawnPoints.Select(t => t.name).Take(5))}");
    }
    #endregion

    #region --- 게임 모드 초기화 & 캐릭터 스폰 관련 ---
    private void InitializeGameModeAfterMapLoaded()
    {
        // Showdown만 우선 지원 (확장 가능)
        if (gameMode == "Showdown")
        {
            if (currentActiveGameMode == null)
            {
                currentActiveGameMode = gameObject.AddComponent<ShowdownMode>();
                currentActiveGameMode.Initialize(this);
            }

            // UI 패널 초기 활성화
            uiManager.ActivatePanelsForAllPlayers();

            // 씬에 이미 생성된(네트워크로 생성된) 캐릭터 오브젝트를 수집해서 playerCharacters 채우기
            CollectCharactersInScene();

            // 로컬 플레이어가 이미 선택 상태이면 즉시 스폰 시도
            TrySpawnIfAlreadySelected();

            // 안전망: 로컬 스폰이 끝날 때까지 대기 (Coroutine 내부에서 playersSpawned 검사)
            StartCoroutine(WaitForCharacterSelectionAndSpawn());
        }
        else
        {
            Debug.LogWarning("[GameManager] 알 수 없는 게임 모드: " + gameMode);
        }
    }

    // 로컬 플레이어가 이미 이전 씬에서 캐릭터 선택을 해 둔 경우 즉시 스폰
    private void TrySpawnIfAlreadySelected()
    {
        Player lp = PhotonNetwork.LocalPlayer;
        if (lp == null) return;

        if (lp.CustomProperties != null &&
            lp.CustomProperties.TryGetValue("SelectedCharacterName", out object val) &&
            val is string s && !string.IsNullOrEmpty(s) &&
            (!playersSpawned.ContainsKey(lp.NickName) || !playersSpawned[lp.NickName]))
        {
            Debug.Log("[GameManager] TrySpawnIfAlreadySelected: 즉시 스폰 시도");
            SpawnLocalPlayer();
        }
    }

    // 로컬 플레이어가 스폰될 때까지 대기 (playersSpawned[local] == true)
    public IEnumerator WaitForCharacterSelectionAndSpawn()
    {
        Player local = PhotonNetwork.LocalPlayer;
        if (local == null) yield break;

        // 안전: 만약 로컬이 이미 spawned이면 즉시 리턴
        while (!playersSpawned.ContainsKey(local.NickName) || !playersSpawned[local.NickName])
        {
            yield return null;
        }
    }

    // 로컬 플레이어 스폰
    private void SpawnLocalPlayer()
    {
        Player localPlayer = PhotonNetwork.LocalPlayer;
        if (localPlayer == null)
        {
            Debug.LogError("[GameManager] LocalPlayer가 없음");
            return;
        }

        if (playersSpawned.ContainsKey(localPlayer.NickName) && playersSpawned[localPlayer.NickName])
        {
            Debug.Log("[GameManager] 이미 로컬 플레이어 스폰됨. 중복 방지.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[GameManager] SpawnLocalPlayer 호출되었으나 spawnPoints가 비어있음.");
            return;
        }

        if (!localPlayer.CustomProperties.TryGetValue("SelectedCharacterName", out object selectedNameObj))
        {
            Debug.LogError($"[{localPlayer.NickName}] SelectedCharacterName이 설정되지 않음. 선택 후 재시도.");
            return;
        }

        string characterPrefabName = selectedNameObj as string;
        if (string.IsNullOrEmpty(characterPrefabName) || !characterPrefabs.ContainsKey(characterPrefabName))
        {
            Debug.LogError($"[{localPlayer.NickName}] 유효하지 않은 캐릭터 이름: {characterPrefabName}");
            return;
        }

        int spawnIndex = (localPlayer.ActorNumber - 1) % spawnPoints.Length;
        Vector3 spawnPos = spawnPoints[spawnIndex].position;
        Quaternion spawnRot = spawnPoints[spawnIndex].rotation;

        Debug.Log($"[GameManager] 스폰 시도: {localPlayer.NickName} -> {characterPrefabName} @ index {spawnIndex}");

        // Characters/ 폴더 아래 이름으로 Instantiate
        GameObject characterObject = PhotonNetwork.Instantiate("Characters/" + characterPrefabName, spawnPos, spawnRot);
        if (characterObject == null)
        {
            Debug.LogError("[GameManager] PhotonNetwork.Instantiate 실패: " + characterPrefabName);
            return;
        }

        // 로컬 오브젝트라면 입력 스크립트 붙이기
        CharacterBase cb = characterObject.GetComponent<CharacterBase>();
        if (cb != null && characterObject.GetPhotonView().IsMine)
        {
            // PlayerInput이 곡예사에만 붙는 문제를 막기 위해 AddComponent 조건을 확인하고 중복 추가 방지
            if (characterObject.GetComponent<PlayerInput>() == null)
            {
                PlayerInput pi = characterObject.AddComponent<PlayerInput>();
                pi.controlledCharacter = cb;
            }
        }

        // 씬 내에 다른 클라이언트들이 Instantiate한 오브젝트도 포함하도록 수집
        StartCoroutine(DelayedCollectAndRefresh(0.1f));

        // spawned 마킹
        playersSpawned[localPlayer.NickName] = true;

        // 모든 플레이어 UI 갱신 (playerCharacters 딕셔너리에 반영된 결과로)
        RefreshAllPlayerUI();
    }

    // Instantiate 직후 잠시 기다린 뒤 씬 스캔 & UI 갱신
    private IEnumerator DelayedCollectAndRefresh(float delay)
    {
        yield return new WaitForSeconds(delay);
        CollectCharactersInScene();
        RefreshAllPlayerUI();
    }

    // 씬에 생성된 CharacterBase(또는 태그 등)를 스캔해서 playerCharacters 딕셔너리를 채움
    private void CollectCharactersInScene()
    {
        // null 체크
        if (playerCharacters == null)
            playerCharacters = new Dictionary<string, GameObject>();

        // 기존 매핑을 갱신(존재하지 않으면 제거)
        // 대신 새로 채우는 방식으로 간단히 처리
        var found = new Dictionary<string, GameObject>();

        // 모든 CharacterBase 컴포넌트를 찾아 PhotonView의 Owner.NickName으로 매핑코드
        var allCharacters = GameObject.FindObjectsByType<CharacterBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var charBase in allCharacters)
        {
            var pv = charBase.GetComponent<PhotonView>();
            if (pv == null || pv.Owner == null) continue;

            string ownerName = pv.Owner.NickName;
            if (string.IsNullOrEmpty(ownerName)) continue;

            // 우선 덮어쓰기 (가장 최근 것이 valid)
            found[ownerName] = charBase.gameObject;
        }

        playerCharacters = found;

        Debug.Log($"[GameManager] 씬 스캔으로 playerCharacters 채움. Count={playerCharacters.Count}. Keys={string.Join(", ", playerCharacters.Keys.Take(8))}");
    }

    // 모든 플레이어의 UI 갱신 (playerCharacters의 현재 상태 기반)
    private void RefreshAllPlayerUI()
    {
        // 씬에서 캐릭터들을 다시 한번 수집 (보수적)
        CollectCharactersInScene();

        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (playerCharacters.TryGetValue(p.NickName, out GameObject charObj) && charObj != null)
            {
                var cb = charObj.GetComponent<CharacterBase>();
                var cd = charObj.GetComponent<CharacterData>();
                uiManager.UpdatePlayerUI(p, cb, cd);
            }
            else
            {
                // 해당 플레이어의 캐릭터가 아직 씬에 없다면 패널은 기본 세팅(비활성/닉네임만)으로 둔다
                // uiManager가 제공하는 기본 활성화 로직 호출 (선택)
            }
        }
    }
    #endregion

    #region --- Helpers: 리소스 로드, 룸 프로퍼티 등 ---
    private void LoadCharacterPrefabs()
    {
        characterPrefabs.Clear();

        // Characters 폴더만 로드 (명확)
        GameObject[] loadedPrefabs = Resources.LoadAll<GameObject>("Characters");
        foreach (var pf in loadedPrefabs)
        {
            if (pf == null) continue;
            if (!characterPrefabs.ContainsKey(pf.name))
                characterPrefabs[pf.name] = pf;
        }

        if (characterPrefabs.Count == 0)
        {
            Debug.LogError("[GameManager] Resources/Characters에서 프리팹을 찾지 못했습니다.");
        }
        else
        {
            Debug.Log("[GameManager] 로드된 캐릭터 프리팹: " + string.Join(", ", characterPrefabs.Keys.Take(10)));
        }
    }

    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        PhotonHashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;

        if (roomProps.ContainsKey("GameMode"))
            gameMode = (string)roomProps["GameMode"];
        if (roomProps.ContainsKey("RoundCount"))
            roundCount = (int)roomProps["RoundCount"];
        if (roomProps.ContainsKey("MapName"))
            mapName = (string)roomProps["MapName"];

        Debug.Log($"[GameManager] Settings: Mode={gameMode}, Rounds={roundCount}, Map={mapName}");
    }
    #endregion

    #region --- Photon 콜백들 (플레이어 입장/퇴장/프로퍼티 변화) ---
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        base.OnPlayerEnteredRoom(newPlayer);

        if (!playersSpawned.ContainsKey(newPlayer.NickName))
            playersSpawned[newPlayer.NickName] = false;

        // 새 플레이어가 들어올 때 씬에 이미 instantiate된 캐릭터가 있을 수 있으므로 한번 수집/갱신
        StartCoroutine(DelayedCollectAndRefresh(0.05f));
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        base.OnPlayerLeftRoom(otherPlayer);

        if (playersSpawned.ContainsKey(otherPlayer.NickName))
            playersSpawned.Remove(otherPlayer.NickName);

        if (playerCharacters.ContainsKey(otherPlayer.NickName))
            playerCharacters.Remove(otherPlayer.NickName);

        // UI 갱신
        RefreshAllPlayerUI();
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        base.OnPlayerPropertiesUpdate(targetPlayer, changedProps);

        // 로컬 플레이어가 자신의 SelectedCharacterName을 세팅했을 때 로컬 스폰 트리거
        if (targetPlayer.IsLocal && changedProps.ContainsKey("SelectedCharacterName"))
        {
            if (changedProps["SelectedCharacterName"] != null &&
                (!playersSpawned.ContainsKey(targetPlayer.NickName) || !playersSpawned[targetPlayer.NickName]))
            {
                Debug.Log("[GameManager] 로컬 SelectedCharacterName 변경 감지 -> SpawnLocalPlayer()");
                SpawnLocalPlayer();
            }
        }

        // 다른 플레이어가 캐릭터를 세팅/변경했을 경우 (UI 갱신 필요)
        if (!targetPlayer.IsLocal && changedProps.ContainsKey("SelectedCharacterName"))
        {
            // 다른 플레이어가 자신의 캐릭터를 선택하면, 언젠가 그 플레이어가 Instantiate를 수행해서 씬에 오브젝트가 생기므로
            // 잠시 후 씬 스캔으로 캐릭터를 수집하고 UI 갱신.
            StartCoroutine(DelayedCollectAndRefresh(0.1f));
        }

        // 준비/라이프 등 다른 플레이어의 프로퍼티 변화시 UI 갱신
        if (!targetPlayer.IsLocal && (changedProps.ContainsKey("IsReady") || changedProps.ContainsKey("IsEliminated")))
        {
            StartCoroutine(DelayedCollectAndRefresh(0.05f));
        }
    }
    #endregion

    #region --- 게임 종료 / UI 업데이트 래퍼 ---
    // 외부에서 사용 가능한 UI 업데이트 래퍼
    public void UpdatePlayerUI(Photon.Realtime.Player targetPlayer)
    {
        if (uiManager == null) return;

        // 캐릭터 매핑이 있으면 업데이트
        if (playerCharacters.TryGetValue(targetPlayer.NickName, out GameObject obj) && obj != null)
        {
            var cb = obj.GetComponent<CharacterBase>();
            var cd = obj.GetComponent<CharacterData>();
            uiManager.UpdatePlayerUI(targetPlayer, cb, cd);
        }
        else
        {
            // 씬에서 다시 한번 수집 시도
            CollectCharactersInScene();
            if (playerCharacters.TryGetValue(targetPlayer.NickName, out GameObject obj2) && obj2 != null)
            {
                var cb2 = obj2.GetComponent<CharacterBase>();
                var cd2 = obj2.GetComponent<CharacterData>();
                uiManager.UpdatePlayerUI(targetPlayer, cb2, cd2);
            }
        }
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
            uiManager.resultText.text = $"승자: {winner.NickName}";
        else
            uiManager.resultText.text = "무승부!";

        uiManager.resultPanel.SetActive(true);
    }
    #endregion
}
