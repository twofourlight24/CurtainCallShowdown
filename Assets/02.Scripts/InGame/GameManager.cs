using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Linq;
using System.Collections;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
public class GameManager : MonoBehaviourPunCallbacks
{
    public static GameManager Instance { get; private set; }

    // UIManager 참조
    public UIManager uiManager;

    [Header("Game Spawning & References")]
    public Transform[] spawnPoints;

    // 캐릭터 프리팹 캐시(Resources/Characters)
    private readonly Dictionary<string, GameObject> characterPrefabs = new();

    // 플레이어별 캐릭터 오브젝트
    private readonly Dictionary<string, GameObject> playerCharacters = new();

    // 플레이어별 스폰 여부
    private readonly Dictionary<string, bool> playersSpawned = new();

    // 현재 활성 게임모드 (인터페이스)
    public IGameMode currentActiveGameMode; 

    // 내부에서 실제로 AddComponent된 컴포넌트 보관(Enable/Disable 등에 필요할 때를 대비)
    private MonoBehaviour currentGameModeComponent;

    // 룸 설정
    private string gameMode;
    private int roundCount;
    private string mapName;
    private GameObject currentMap;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (uiManager == null)
        {
            Debug.LogError("UIManager를 찾을 수 없습니다. GameManager의 uiManager를 할당해주세요.");
            return;
        }

        // playersSpawned 초기값
        foreach (var p in PhotonNetwork.PlayerList)
            if (!playersSpawned.ContainsKey(p.NickName)) playersSpawned[p.NickName] = false;

        LoadCharacterPrefabs();
        LoadGameSettings();
        uiManager.InitializeInGameUI();

        StartCoroutine(InitializeGameAfterMapLoaded());
    }
    public void RegisterCharacter(Photon.Realtime.Player owner, GameObject go)
    {
        if (owner == null || go == null) return;

        // 캐시 갱신
        playerCharacters[owner.NickName] = go;

        // 로컬이면 스폰 완료 표시
        if (owner.IsLocal)
            playersSpawned[owner.NickName] = true;

        StartCoroutine(DeferredUpdateUI(owner));
    }
    private IEnumerator DeferredUpdateUI(Photon.Realtime.Player p)
    {
        // 한 프레임 대기: 컴포넌트 초기화/PhotonView.Owner 세팅 대기
        yield return null;
        UpdatePlayerUI(p);  // ← 이 호출이 “처음부터 모두 보이게” 만듭니다.
    }

    private IEnumerator InitializeGameAfterMapLoaded()
    {
        // 1) 마스터가 맵 네트워크 생성
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
                        photonView.RPC(nameof(RPC_OnMapInstantiated), RpcTarget.Others);
                }
                else
                {
                    Debug.LogError($"'Resources/{mapName}'에서 맵 프리팹을 찾을 수 없습니다.");
                }
            }
        }

        // 2) 스폰 포인트 준비 대기
        yield return StartCoroutine(WaitForSpawnPoints(timeoutSeconds: 8f));

        // 3) 모드 초기화 + 라운드 시작 준비
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
            if (spawnPointObjects != null && spawnPointObjects.Length > 0) break;
            timer += Time.deltaTime; yield return null;
        }

        if (spawnPointObjects == null || spawnPointObjects.Length == 0)
        {
            Debug.LogError($"[GameManager] SpawnPoint를 찾지 못했습니다. 타임아웃({timeoutSeconds}s)");
            yield break;
        }

        spawnPoints = spawnPointObjects.Select(obj => obj.transform).ToArray();
        System.Array.Sort(spawnPoints, (a, b) => string.Compare(a.name, b.name));
        Debug.Log($"[GameManager] SpawnPoints 준비 완료. Count={spawnPoints.Length}");
    }

    private void InitializeGameModeAfterMapLoaded()
    {
        // 모드 컴포넌트 준비
        currentActiveGameMode = CreateOrGetGameModeComponent(gameMode, out currentGameModeComponent);
        if (currentActiveGameMode == null)
        {
            Debug.LogError($"알 수 없는 게임 모드입니다: {gameMode}");
            return;
        }

        currentActiveGameMode.Initialize(this);

        // 선택 씬에서 이미 캐릭터 선택 완료 상태면 즉시 스폰 시도
        TrySpawnIfAlreadySelected();

        // 안전망: 선택 이벤트 기다리기
        StartCoroutine(WaitForCharacterSelectionAndSpawn());

        // 라운드 시작(모드가 필요 시 타이머/룰 세팅)
        currentActiveGameMode.StartRound();
    }

    private IGameMode CreateOrGetGameModeComponent(string modeName, out MonoBehaviour rawComponent)
    {
        rawComponent = null;
        IGameMode found = GetComponents<MonoBehaviour>().OfType<IGameMode>().FirstOrDefault(m => m.ModeName == modeName);
        if (found != null) { rawComponent = (MonoBehaviour)found; return found; }

        switch (modeName)
        {
            case "Showdown":
                rawComponent = gameObject.AddComponent<ShowdownMode>();
                break;
            // TODO: 다른 모드 추가 시 여기에 연결 (e.g., KingOfTheHillMode 등)
            default:
                Debug.LogError($"지원되지 않는 모드: {modeName}");
                return null;
        }
        return (IGameMode)rawComponent;
    }

    // === 캐릭터 스폰 관련 ===
    private void TrySpawnIfAlreadySelected()
    {
        Player lp = PhotonNetwork.LocalPlayer;
        if (lp == null) return;

        if (lp.CustomProperties != null &&
            lp.CustomProperties.TryGetValue("SelectedCharacterName", out object val) &&
            val is string s && !string.IsNullOrEmpty(s) &&
            (!playersSpawned.ContainsKey(lp.NickName) || !playersSpawned[lp.NickName]))
        {
            Debug.Log("[GameManager] TrySpawnIfAlreadySelected → 즉시 스폰");
            SpawnLocalPlayer();
        }
    }
    private int GetRandomSpawnIndex()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return 0;
        return Random.Range(0, spawnPoints.Length);
    }

    // ShowdownMode에서 lives[player] > 0 일 때 호출하도록 공개 API
    public void OrderRespawn(Player target, float delay, float invincibleTime)
    {
        if (target == null) return;
        int spawnIndex = GetRandomSpawnIndex();
        // 소유자에게만 지시 (Owner)
        photonView.RPC(nameof(RPC_OrderRespawn), target, spawnIndex, delay, invincibleTime);
    }

    [PunRPC]
    public void RPC_OrderRespawn(int spawnIndex, float delay, float invincibleTime)
    {
        // 로컬(= 소유자)만 처리
        StartCoroutine(LocalRespawnCoroutine(spawnIndex, delay, invincibleTime));
    }

    private IEnumerator LocalRespawnCoroutine(int spawnIndex, float delay, float invincibleTime)
    {
        // 오버레이 표시
        uiManager?.ShowRespawnOverlay(delay);

        // 기존 내 캐릭터가 씬에 남아 있다면 파괴(안전)
        GameObject myChar = GetCharacterObject(PhotonNetwork.LocalPlayer);
        if (myChar != null)
        {
            // Owner가 파괴 권한 보유 → 안전하게 Destroy
            var pv = myChar.GetComponent<PhotonView>();
            if (pv != null && pv.IsMine)
                PhotonNetwork.Destroy(myChar);
            else
                myChar.SetActive(false);
        }

        // 카운트다운 대기
        yield return new WaitForSeconds(delay);

        // 스폰 위치
        Vector3 pos = spawnPoints != null && spawnIndex >= 0 && spawnIndex < spawnPoints.Length
            ? spawnPoints[spawnIndex].position
            : Vector3.zero;
        Quaternion rot = spawnPoints != null && spawnIndex >= 0 && spawnIndex < spawnPoints.Length
            ? spawnPoints[spawnIndex].rotation
            : Quaternion.identity;

        // 내 선택 캐릭터 프리팹 이름
        string prefabName = "";
        if (PhotonNetwork.LocalPlayer.CustomProperties != null &&
            PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SelectedCharacterName", out object v) &&
            v is string s && !string.IsNullOrEmpty(s))
        {
            prefabName = s;
        }
        else
        {
            Debug.LogError("[GM] SelectedCharacterName not set on respawn.");
            uiManager?.HideRespawnOverlay();
            yield break;
        }

        // 재생성
        GameObject newChar = PhotonNetwork.Instantiate("Characters/" + prefabName, pos, rot);
        if (newChar != null)
        {
            var cb = newChar.GetComponent<CharacterBase>();
            if (cb != null && newChar.GetPhotonView().IsMine)
            {
                // 무적 부여
                cb.SetInvincible(invincibleTime);
            }
        }

        // UI 갱신/오버레이 끄기
        uiManager?.HideRespawnOverlay();
        UpdatePlayerUI(PhotonNetwork.LocalPlayer);
    }
    public IEnumerator WaitForCharacterSelectionAndSpawn()
    {
        Player localPlayer = PhotonNetwork.LocalPlayer;
        while (!playersSpawned.ContainsKey(localPlayer.NickName) || !playersSpawned[localPlayer.NickName])
            yield return null;
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, PhotonHashtable changedProps)
    {
        base.OnPlayerPropertiesUpdate(targetPlayer, changedProps);

        if (changedProps.ContainsKey("SelectedCharacterName"))
        {
            // 로컬: 스폰 트리거 (기존 로직 유지)
            if (targetPlayer.IsLocal)
            {
                if (changedProps["SelectedCharacterName"] != null &&
                    (!playersSpawned.ContainsKey(targetPlayer.NickName) || !playersSpawned[targetPlayer.NickName]))
                {
                    SpawnLocalPlayer();
                }
            }
            else
            {
                // 원격: 캐릭터가 이미 등록되어 있으면 UI 즉시 갱신
                if (playerCharacters.ContainsKey(targetPlayer.NickName) && playerCharacters[targetPlayer.NickName] != null)
                {
                    UpdatePlayerUI(targetPlayer);
                }
                else
                {
                    // 아직 네트워크 인스턴스가 도착 안했을 수 있으니 한 프레임 뒤 재시도
                    StartCoroutine(DeferredUpdateUI(targetPlayer));
                }
            }
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (!playersSpawned.ContainsKey(newPlayer.NickName)) playersSpawned[newPlayer.NickName] = false;
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (playersSpawned.ContainsKey(otherPlayer.NickName)) playersSpawned.Remove(otherPlayer.NickName);
    }

    private void SpawnLocalPlayer()
    {
        Player localPlayer = PhotonNetwork.LocalPlayer;
        if (playersSpawned.ContainsKey(localPlayer.NickName) && playersSpawned[localPlayer.NickName])
        {
            Debug.Log("[GameManager] 이미 스폰됨 (중복 방지)");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[GameManager] spawnPoints 비어있음");
            return;
        }

        if (!localPlayer.CustomProperties.TryGetValue("SelectedCharacterName", out object selectedCharacterNameObj))
        {
            Debug.LogError($"플레이어 {localPlayer.NickName}의 SelectedCharacterName이 설정되어 있지 않습니다.");
            return;
        }

        string characterPrefabName = (string)selectedCharacterNameObj;
        if (string.IsNullOrEmpty(characterPrefabName) || !characterPrefabs.ContainsKey(characterPrefabName))
        {
            Debug.LogError($"유효하지 않은 캐릭터 프리팹 이름: {characterPrefabName}");
            return;
        }

        int spawnIndex = (localPlayer.ActorNumber - 1) % spawnPoints.Length;
        Vector3 spawnPos = spawnPoints[spawnIndex].position;
        Quaternion spawnRot = spawnPoints[spawnIndex].rotation;

        Debug.Log($"[GameManager] Spawn {localPlayer.NickName} at {spawnIndex}");

        GameObject characterObject = PhotonNetwork.Instantiate("Characters/" + characterPrefabName, spawnPos, spawnRot);
        if (characterObject != null)
        {
            var characterBase = characterObject.GetComponent<CharacterBase>();
            if (characterBase != null && characterObject.GetPhotonView().IsMine)
            {
                characterBase.CurHp = characterBase.MaxHp;
                characterBase.SetInvincible(2);
            }

            playerCharacters[localPlayer.NickName] = characterObject;
            playersSpawned[localPlayer.NickName] = true;
            UpdatePlayerUI(localPlayer);
        }
        else
        {
            Debug.LogError("[GameManager] PhotonNetwork.Instantiate가 null 반환");
        }
    }

    private void LoadCharacterPrefabs()
    {
        var loadedPrefabs = Resources.LoadAll<GameObject>("Characters");
        foreach (GameObject prefab in loadedPrefabs)
            characterPrefabs[prefab.name] = prefab;

        if (characterPrefabs.Count == 0)
            Debug.LogError("Resources/Characters 폴더에서 캐릭터 프리팹을 찾을 수 없습니다.");
        else
            Debug.Log("[GameManager] Loaded character prefabs: " + string.Join(", ", characterPrefabs.Keys.Take(10)));
    }

    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom == null) return;
        PhotonHashtable roomProps = PhotonNetwork.CurrentRoom.CustomProperties;

        if (roomProps.ContainsKey("GameMode")) gameMode = (string)roomProps["GameMode"];
        if (roomProps.ContainsKey("RoundCount")) roundCount = (int)roomProps["RoundCount"];
        if (roomProps.ContainsKey("MapName")) mapName = (string)roomProps["MapName"];
        Debug.Log($"[GameManager] GameMode={gameMode}, RoundCount={roundCount}, MapName={mapName}");
    }

    // === 공개 유틸 ===
    public void UpdatePlayerUI(Player targetPlayer)
    {
        if (uiManager == null) return;
        if (!playerCharacters.ContainsKey(targetPlayer.NickName)) return;
        GameObject characterObject = playerCharacters[targetPlayer.NickName];
        if (characterObject == null) return;
        CharacterBase character = characterObject.GetComponent<CharacterBase>();
        CharacterData characterData = characterObject.GetComponent<CharacterData>();
        uiManager.UpdatePlayerUI(targetPlayer, character, characterData);
    }

    public IEnumerator RespawnPlayer(Player player)
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

    public void UpdateLifeUI(Player targetPlayer, int currentLives)
    {
        if (uiManager == null) return;
        uiManager.UpdateLifeUI(targetPlayer, currentLives);
    }

    public void EndGame(Player winner)
    {
        if (PhotonNetwork.CurrentRoom != null)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
        }

        if (winner != null) uiManager.resultText.text = $"승자: {winner.NickName}";
        else uiManager.resultText.text = "무승부!";
        uiManager.resultPanel.SetActive(true);
    }

    // 모드가 라운드 결과를 알릴 때 사용 (추후 RoundFlowManager 연동 예정)
    public void EndRound(List<Player> ranking)
    {
        // 모드 정리
        currentActiveGameMode?.EndRound();
        currentActiveGameMode?.OnRoundComplete(ranking);

        // TODO: RoundFlowManager.Instance?.HandleRoundComplete(ranking); // 라운드 루프 붙일 때 활성화
    }
    [PunRPC]
    public void RPC_PlayerEliminated(int actorNumber /*, PhotonMessageInfo info 可*/ )
    {
        var player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
        if (player == null)
        {
            Debug.LogWarning($"[GM] RPC_PlayerEliminated: player not found for actor #{actorNumber}");
            return;
        }

        if (currentActiveGameMode is ShowdownMode showdown)
            showdown.OnPlayerEliminated(player);
    }
    public GameObject GetCharacterObject(Player p)
    {
        // playerCharacters 사전에서 먼저 찾기
        if (playerCharacters.TryGetValue(p.NickName, out var go) && go != null) return go;
        // 혹시 모를 누락 대비: 씬에서 소유자 기반으로 탐색
        var all = GameObject.FindObjectsByType<CharacterBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var cb in all)
        {
            var pv = cb.GetComponent<PhotonView>();
            if (pv?.Owner == p) return cb.gameObject;
        }
        return null;
    }
}
