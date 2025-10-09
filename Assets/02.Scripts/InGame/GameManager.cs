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
        // DontDestroyOnLoad를 쓰지 않음: PhotonView ID 중복 방지 (씬마다 존재하는 씬 오브젝트로 운영)
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
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

    // === 맵/스폰 세팅 ===

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

    // === 캐릭터 등록(UI 즉시 표시) ===

    public void RegisterCharacter(Player owner, GameObject go)
    {
        if (owner == null || go == null) return;

        playerCharacters[owner.NickName] = go;

        if (owner.IsLocal)
            playersSpawned[owner.NickName] = true;

        StartCoroutine(DeferredUpdateUI(owner)); // 한 프레임 뒤 UI 안정 갱신
    }

    private IEnumerator DeferredUpdateUI(Player p)
    {
        yield return null; // PhotonView.Owner 세팅 등 대기
        UpdatePlayerUI(p);
    }

    // === 스폰 / 리스폰 ===

    private void TrySpawnIfAlreadySelected()
    {
        var lp = PhotonNetwork.LocalPlayer;
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
        photonView.RPC(nameof(RPC_OrderRespawn), target, spawnIndex, delay, invincibleTime);
    }

    [PunRPC]
    public void RPC_OrderRespawn(int spawnIndex, float delay, float invincibleTime)
    {
        StartCoroutine(LocalRespawnCoroutine(spawnIndex, delay, invincibleTime));
    }

    private IEnumerator LocalRespawnCoroutine(int spawnIndex, float delay, float invincibleTime)
    {
        // 사망자 오버레이(회색+카운트다운)
        uiManager?.ShowRespawnOverlay(delay);

        // 기존 내 캐릭터 파괴(네트워크 인스턴스만 Destroy)
        GameObject myChar = GetCharacterObject(PhotonNetwork.LocalPlayer);
        if (myChar != null)
        {
            var pv = myChar.GetComponent<PhotonView>();
            if (pv != null && pv.IsMine) PhotonNetwork.Destroy(myChar);
            else myChar.SetActive(false);
        }

        yield return new WaitForSeconds(delay);

        // 스폰 위치
        Vector3 pos = spawnPoints != null && spawnIndex >= 0 && spawnIndex < spawnPoints.Length
            ? spawnPoints[spawnIndex].position
            : Vector3.zero;
        Quaternion rot = spawnPoints != null && spawnIndex >= 0 && spawnIndex < spawnPoints.Length
            ? spawnPoints[spawnIndex].rotation
            : Quaternion.identity;

        // 내 선택 캐릭터 프리팹 이름
        if (!(PhotonNetwork.LocalPlayer.CustomProperties?.TryGetValue("SelectedCharacterName", out object v) ?? false) ||
             v is not string prefabName || string.IsNullOrEmpty(prefabName))
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
                cb.CurHp = cb.MaxHp;
                cb.SetInvincible(invincibleTime);
            }
            playerCharacters[PhotonNetwork.LocalPlayer.NickName] = newChar;
        }

        uiManager?.HideRespawnOverlay();
        UpdatePlayerUI(PhotonNetwork.LocalPlayer);
    }

    public IEnumerator WaitForCharacterSelectionAndSpawn()
    {
        var local = PhotonNetwork.LocalPlayer;
        while (!playersSpawned.ContainsKey(local.NickName) || !playersSpawned[local.NickName])
            yield return null;
    }

    // === 포톤 콜백 ===

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        base.OnPlayerPropertiesUpdate(targetPlayer, changedProps);

        if (changedProps.ContainsKey("SelectedCharacterName"))
        {
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
                if (playerCharacters.ContainsKey(targetPlayer.NickName) && playerCharacters[targetPlayer.NickName] != null)
                {
                    UpdatePlayerUI(targetPlayer);
                }
                else
                {
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

    // === 실제 스폰 ===

    private void SpawnLocalPlayer()
    {
        var local = PhotonNetwork.LocalPlayer;

        if (playersSpawned.TryGetValue(local.NickName, out bool already) && already)
        {
            Debug.Log("[GameManager] 이미 스폰됨 (중복 방지)");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("[GameManager] spawnPoints 비어있음");
            return;
        }

        if (!local.CustomProperties.TryGetValue("SelectedCharacterName", out object selectedObj))
        {
            Debug.LogError($"플레이어 {local.NickName}의 SelectedCharacterName이 설정되어 있지 않습니다.");
            return;
        }

        string characterPrefabName = (string)selectedObj;
        if (string.IsNullOrEmpty(characterPrefabName) || !characterPrefabs.ContainsKey(characterPrefabName))
        {
            Debug.LogError($"유효하지 않은 캐릭터 프리팹 이름: {characterPrefabName}");
            return;
        }

        int spawnIndex = (local.ActorNumber - 1) % spawnPoints.Length;
        Vector3 spawnPos = spawnPoints[spawnIndex].position;
        Quaternion spawnRot = spawnPoints[spawnIndex].rotation;

        Debug.Log($"[GameManager] Spawn {local.NickName} at {spawnIndex}");

        GameObject characterObject = PhotonNetwork.Instantiate("Characters/" + characterPrefabName, spawnPos, spawnRot);
        if (characterObject != null)
        {
            var characterBase = characterObject.GetComponent<CharacterBase>();
            if (characterBase != null && characterObject.GetPhotonView().IsMine)
            {
                characterBase.CurHp = characterBase.MaxHp;
                characterBase.SetInvincible(2);
            }

            playerCharacters[local.NickName] = characterObject;
            playersSpawned[local.NickName] = true;
            UpdatePlayerUI(local);
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

    // === UI/유틸 ===

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

    // === 라운드 종료 흐름 ===

    public void EndRound(List<Player> ranking)
    {
        // 모드 종료 콜백
        currentActiveGameMode?.EndRound();

        // 점수/투표 등 라운드 종결은 RoundFlowManager가 담당
        RoundFlowManager.Instance?.HandleRoundComplete(ranking);
    }

    // === 킬/사망 ===

    [PunRPC]
    public void RPC_PlayerEliminated(int actorNumber /*, PhotonMessageInfo info*/ )
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

    public void OpenGameModeVotePanelForAll()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(RPC_OpenGameModeVotePanelForAll), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_OpenGameModeVotePanelForAll()
    {
        uiManager?.OpenGameModeVotePanel();   // 내부에서 votePanel.InitializeFromRoom() 호출
    }

    // === 다음 라운드 시작(투표 종료 이후) ===

    /// <summary>
    /// 투표가 끝나고(결과 확정 + 5초 대기) 라운드 재시작을 트리거.
    /// 마스터만 호출 / 모든 클라이언트에 RPC로 브로드캐스트.
    /// </summary>
    public void BeginNextRoundAfterVote()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 룸 속성에서 선정된 게임모드 읽기 (없으면 Showdown)
        string modeName = "Showdown";
        var props = PhotonNetwork.CurrentRoom?.CustomProperties;
        if (props != null && props.TryGetValue("GameMode", out object gmNameObj))
        {
            if (gmNameObj is string gmName && !string.IsNullOrEmpty(gmName))
                modeName = gmName;
        }

        photonView.RPC(nameof(RPC_BeginNextRound), RpcTarget.All, modeName, RoundFlowManager.Instance?.currentRoundIndex ?? 0);
    }

    [PunRPC]
    private void RPC_BeginNextRound(string modeName, int roundIndex)
    {
        Debug.Log($"[GameManager] BeginNextRound: round={roundIndex + 1}, mode={modeName}");

        // 1) UI 정리
        try
        {
            if (uiManager != null)
            {
                // UIManager에 CloseResultsAndVotePanels() 유틸을 추가해 두는 것을 권장
                uiManager.CloseResultsAndVotePanels();
                uiManager.ActivatePanelsForAllPlayers();
            }
        }
        catch { }

        // 2) 라운드 데이터 리셋 (킬 카운트 등)
        RoundFlowManager.Instance?.ResetRoundData();

        // 3) 선택된 모드로 세팅
        SetupGameModeByName(modeName);

        // 4) 라운드 이벤트 스택 재적용 (있다면)
        try
        {
            RoundEventManager.Instance?.RefreshContext();
            RoundEventManager.Instance?.EnableStackedEvents(RoundFlowManager.Instance?.stackedRoundEventIds);
        }
        catch { }

        // 5) 전원 리스폰 (마스터만)
        if (PhotonNetwork.IsMasterClient)
        {
            foreach (var p in PhotonNetwork.PlayerList)
                OrderRespawn(p, 0.1f, 2f); // 소폭 지연 + 2초 무적
        }
    }
    public void OpenTotalScoreForAllClients()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        photonView.RPC(nameof(RPC_OpenTotalScoreForAllClients), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_OpenTotalScoreForAllClients()
    {
        var rf = RoundFlowManager.Instance;
        if (rf == null || uiManager == null) return;

        var totals = rf.GetTotalPoints();
        var roundsLeft = rf.GetRoundsLeft();
        var eventsCsv = rf.GetStackedEventsCsv();

        uiManager.ShowTotalScoreUI(totals, roundsLeft, eventsCsv);
    }
    /// <summary>
    /// 문자열 모드 이름으로 현재 게임모드(IGameMode) 세팅/초기화.
    /// 없는 모드는 Showdown으로 폴백.
    /// </summary>
    private void SetupGameModeByName(string modeName)
    {
        // 기존 모드 종료/정리
        if (currentActiveGameMode != null)
        {
            try { currentActiveGameMode.EndRound(); } catch { }
            currentActiveGameMode = null;
        }

        // 기존 IGameMode 컴포넌트 제거(필요 시)
        foreach (var m in GetComponents<MonoBehaviour>())
        {
            if (m is IGameMode && m.GetType().Name != "ShowdownMode")
            {
                Destroy(m);
            }
        }

        // 새 모드 바인딩
        IGameMode mode = null;
        switch (modeName)
        {
            case "Showdown":
                mode = GetComponent<ShowdownMode>();
                if (mode == null) mode = gameObject.AddComponent<ShowdownMode>();
                break;
            // case "King of the Hill":
            //     var koth = GetComponent<KingOfTheHillMode>() ?? gameObject.AddComponent<KingOfTheHillMode>();
            //     mode = koth;
            //     break;
            default:
                // 알 수 없는 모드면 Showdown으로 폴백
                mode = GetComponent<ShowdownMode>();
                if (mode == null) mode = gameObject.AddComponent<ShowdownMode>();
                break;
        }

        currentActiveGameMode = mode;
        currentActiveGameMode?.Initialize(this);

        Debug.Log($"[GameManager] GameMode set → {modeName}");
    }

    // === 조회 유틸 ===

    public GameObject GetCharacterObject(Player p)
    {
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
