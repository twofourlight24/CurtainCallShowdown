using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class SelectCharacterManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - Select Character Panel =====")]
    public GameObject selectCharacterPanel;
    public TMP_Text gameModeText;
    public TMP_Text mapText;
    public TMP_Text timerText;

    [Header("===== UI - Player Info Panels =====")]
    public GameObject[] playerInfoPanels;
    public TMP_Text[] playerNicknameTexts;
    public TMP_Text[] characterNameTexts;
    public Image[] characterIcons;
    public GameObject[] readyCheckImages;

    [Header("===== UI - Character Selection =====")]
    public GameObject[] characterButtons;
    public TMP_Text characterGuideText;
    public TMP_Text selectedCharacterNameText;
    public Button selectButton;

    public CharacterData[] allCharacters;

    private Dictionary<string, int> playerIndices = new Dictionary<string, int>();
    private float timer = 30f;
    private bool selectionComplete = false;
    private bool isInitialized = false;

    private bool allowDuplication = true;
    private readonly HashSet<string> takenPrefabs = new HashSet<string>();

    void Start()
    {
        // 모든 클라가 마스터의 LoadLevel을 따라가도록
        PhotonNetwork.AutomaticallySyncScene = true;

        // 캐릭터 선택 씬 진입 시 로컬 플레이어 속성 초기화
        PhotonHashtable initialProps = new PhotonHashtable {
            { "IsReady", false },
            { "SelectedCharacterName", null }
        };
        PhotonNetwork.LocalPlayer.SetCustomProperties(initialProps);
        Debug.Log("[SelectCharacter] 커스텀 속성 초기화 완료.");

        selectCharacterPanel.SetActive(true);
        InitializeCharacterButtons();
        LoadGameSettings();
        if (PhotonNetwork.CurrentRoom != null &&
        PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AllowDuplication", out object allow) &&
        allow is bool b) allowDuplication = b;
        else allowDuplication = true; // 키가 없으면 허용

        UpdateCharacterButtonsInteractivity();

        // 지연 없이 즉시 초기화
        InitializePlayerPanels();
        isInitialized = true;

        // 경량 주기 리프레시(드리프트 방지) - 필요 없으면 주석처리 가능
        StartCoroutine(AutoRefreshPanels());
    }

    private IEnumerator AutoRefreshPanels()
    {
        var wait = new WaitForSeconds(0.5f);
        while (!selectionComplete)
        {
            RefreshAllPanelsFromSnapshot();
            yield return wait;
        }
    }

    void Update()
    {
        if (!selectionComplete && timer > 0f)
        {
            timer -= Time.deltaTime;
            timerText.text = $"{Mathf.Ceil(timer)}";
            if (timer <= 0f)
            {
                OnTimerEnd();
            }
        }

        // 마스터만 주기적으로 시작 조건을 재검사 (부하 매우 적음)
        if (PhotonNetwork.IsMasterClient)
        {
            CheckAndStartGame();
        }
    }

    private void InitializePlayerPanels()
    {
        // 모든 UI 패널 초기화
        for (int i = 0; i < playerInfoPanels.Length; i++)
        {
            playerInfoPanels[i].SetActive(false);
            characterNameTexts[i].gameObject.SetActive(false);
            characterIcons[i].gameObject.SetActive(false);
            readyCheckImages[i].SetActive(false);
        }

        // 현재 방의 플레이어 수에 맞게 UI 패널 활성화 및 설정
        playerIndices.Clear();
        int index = 0;
        UpdateCharacterButtonsInteractivity();
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (index >= playerInfoPanels.Length) break;

            playerInfoPanels[index].SetActive(true);
            playerNicknameTexts[index].text = player.NickName;
            playerIndices[player.NickName] = index;

            // 이미 선택된 캐릭터 및 준비 상태가 있는지 확인하고 UI 업데이트
            if (player.CustomProperties.TryGetValue("SelectedCharacterName", out object charNameObj)
                && charNameObj is string charName && !string.IsNullOrEmpty(charName))
            {
                CharacterData selectedChar = System.Array.Find(allCharacters, c => c.data.characterPrefab != null && c.data.characterPrefab.name == charName);
                if (selectedChar != null && selectedChar.data.characterPrefab != null)
                {
                    characterNameTexts[index].gameObject.SetActive(true);
                    characterIcons[index].gameObject.SetActive(true);
                    characterNameTexts[index].text = selectedChar.data.characterName;
                    characterIcons[index].sprite = selectedChar.data.characterIcon;
                }
            }

            if (player.CustomProperties.TryGetValue("IsReady", out object isReadyObj) && isReadyObj is bool isReadyVal)
            {
                readyCheckImages[index].SetActive(isReadyVal);
            }

            index++;
        }
    }

    private void RefreshAllPanelsFromSnapshot()
    {
        // 현재 스냅샷으로 전체 UI를 한번에 동기화 (콜백 타이밍 이슈 보정)
        for (int i = 0; i < playerInfoPanels.Length; i++)
        {
            playerInfoPanels[i].SetActive(false);
            characterNameTexts[i].gameObject.SetActive(false);
            characterIcons[i].gameObject.SetActive(false);
            readyCheckImages[i].SetActive(false);
        }
        playerIndices.Clear();
        UpdateCharacterButtonsInteractivity();
        int index = 0;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (index >= playerInfoPanels.Length) break;

            playerInfoPanels[index].SetActive(true);
            playerNicknameTexts[index].text = player.NickName;
            playerIndices[player.NickName] = index;

            if (player.CustomProperties.TryGetValue("SelectedCharacterName", out object charNameObj)
                && charNameObj is string charName && !string.IsNullOrEmpty(charName))
            {
                CharacterData selectedChar = System.Array.Find(allCharacters, c => c.data.characterPrefab != null && c.data.characterPrefab.name == charName);
                if (selectedChar != null && selectedChar.data.characterPrefab != null)
                {
                    characterNameTexts[index].gameObject.SetActive(true);
                    characterIcons[index].gameObject.SetActive(true);
                    characterNameTexts[index].text = selectedChar.data.characterName;
                    characterIcons[index].sprite = selectedChar.data.characterIcon;
                }
            }

            if (player.CustomProperties.TryGetValue("IsReady", out object readyObj) && readyObj is bool readyVal)
            {
                readyCheckImages[index].SetActive(readyVal);
            }

            index++;
        }
    }

    private void InitializeCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            int idx = i;
            characterButtons[idx].GetComponent<Button>().onClick.AddListener(() => OnCharacterSelected(idx));
        }
        selectButton.onClick.AddListener(OnSelectButtonPressed);
    }

    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GameMode", out object mode))
            gameModeText.text = $"게임 모드: {mode}";
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MapName", out object map))
            mapText.text = $"맵: {map}";

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AllowDuplication", out object allow) &&
            allow is bool b) allowDuplication = b;
        else allowDuplication = true; // 키 없으면 허용 기본

        UpdateCharacterButtonsInteractivity(); // ← 추가
    }

    public void OnCharacterSelected(int index)
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("IsReady", out var r) && r is bool rb && rb)
            return;
        var cd = allCharacters[index];
        string prefab = cd.data.characterPrefab?.name;
        if (string.IsNullOrEmpty(prefab)) return;

        if (!allowDuplication)
        {
            RebuildTakenSet();
            string mine = LocalSelectedPrefab();
            if (takenPrefabs.Contains(prefab) && prefab != mine)
            {
                characterGuideText.text = "이미 선택된 캐릭터입니다.";
                return;
            }
        }

        PhotonHashtable props = new PhotonHashtable { { "SelectedCharacterName", prefab } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);

        selectedCharacterNameText.text = $"선택 캐릭터: {cd.data.characterName}";
        characterGuideText.text = cd.data.characterDescription;

        UpdateCharacterButtonsInteractivity();
    }

    public void OnSelectButtonPressed()
    {
        if (selectionComplete) return;

        if (!PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("SelectedCharacterName")
            || !(PhotonNetwork.LocalPlayer.CustomProperties["SelectedCharacterName"] is string s) || string.IsNullOrEmpty(s))
        {
            Debug.Log("[SelectCharacter] 캐릭터를 먼저 선택하세요!");
            return;
        }

        SetReadyState(true);
        // 마스터가 주기적으로 CheckAndStartGame()을 호출하므로 여기서 별도 호출 불필요
    }

    private void SetReadyState(bool isReady)
    {
        selectButton.interactable = !isReady;
        foreach (var go in characterButtons)
        {
            var btn = go.GetComponent<Button>();
            if (btn) btn.interactable = !isReady;
        }
        PhotonHashtable props = new PhotonHashtable { { "IsReady", isReady } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }
    private void RebuildTakenSet()
    {
        takenPrefabs.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (p.CustomProperties.TryGetValue("SelectedCharacterName", out object o) &&
                o is string prefab && !string.IsNullOrEmpty(prefab))
            {
                takenPrefabs.Add(prefab);
            }
        }
    }
    private string LocalSelectedPrefab()
    {
        if (PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("SelectedCharacterName", out object o) &&
            o is string prefab) return prefab;
        return null;
    }

    // ===== [버튼 상호작용 갱신] =====
    private void UpdateCharacterButtonsInteractivity()
    {
        RebuildTakenSet();

        string mine = LocalSelectedPrefab(); // 내 선택은 계속 가능
        for (int i = 0; i < characterButtons.Length; i++)
        {
            var btn = characterButtons[i].GetComponent<Button>();
            if (!btn) continue;

            string prefab = allCharacters[i].data.characterPrefab?.name;
            bool takenByOthers = takenPrefabs.Contains(prefab) && prefab != mine;

            btn.interactable = allowDuplication || !takenByOthers;
        }
    }
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (!isInitialized) return;

        if (!playerIndices.TryGetValue(targetPlayer.NickName, out int pIndex))
        {
            RefreshAllPanelsFromSnapshot();
            return;
        }

        if (changedProps.ContainsKey("SelectedCharacterName"))
        {
            var val = changedProps["SelectedCharacterName"];
            if (val is string charName && !string.IsNullOrEmpty(charName))
            {
                CharacterData selectedChar = System.Array.Find(allCharacters, c => c.data.characterPrefab != null && c.data.characterPrefab.name == charName);
                if (selectedChar != null && selectedChar.data.characterPrefab != null)
                {
                    characterNameTexts[pIndex].gameObject.SetActive(true);
                    characterIcons[pIndex].gameObject.SetActive(true);
                    characterNameTexts[pIndex].text = selectedChar.data.characterName;
                    characterIcons[pIndex].sprite = selectedChar.data.characterIcon;
                }
            }
            else
            {
                // null 또는 빈 값 → 숨김
                characterNameTexts[pIndex].gameObject.SetActive(false);
                characterIcons[pIndex].gameObject.SetActive(false);
            }
        }
        UpdateCharacterButtonsInteractivity();
        if (changedProps.ContainsKey("IsReady"))
        {
            bool isReady = changedProps["IsReady"] is bool b && b;
            readyCheckImages[pIndex].SetActive(isReady);
        }

        // 최종 일관성 보정
        RefreshAllPanelsFromSnapshot();

        // 마스터에서만 씬 전환 조건 재검사
        if (PhotonNetwork.IsMasterClient)
        {
            CheckAndStartGame();
        }
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        // 선택 씬에선 원래 입/퇴장 없지만, 방어적 코드로 유지
        InitializePlayerPanels();
        UpdateCharacterButtonsInteractivity();
        if (PhotonNetwork.IsMasterClient) CheckAndStartGame();
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        // 선택 씬에선 원래 입/퇴장 없지만, 방어적 코드로 유지
        InitializePlayerPanels();
        UpdateCharacterButtonsInteractivity();
    }

    /// <summary>
    /// (마스터만) 모든 플레이어가 유효한 캐릭터를 확정했는지 검증하고, GameScene으로 전환
    /// </summary>
    private void CheckAndStartGame()
    {
        if (selectionComplete) return;
        if (!isInitialized) return;

        // 마스터만 씬 전환 담당
        if (!PhotonNetwork.IsMasterClient) return;

        // 최소 인원(2명) 조건 (필요시 1명 플레이 지원하려면 변경)
        if (PhotonNetwork.PlayerList.Length < 2) return;

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!(player.CustomProperties.TryGetValue("SelectedCharacterName", out object nameObj)
                  && nameObj is string prefabName && !string.IsNullOrEmpty(prefabName)))
                return;

            if (!(player.CustomProperties.TryGetValue("IsReady", out object readyObj)
                  && readyObj is bool isReady && isReady))
                return;
        }

        // 전원 확정 — 마스터 단일 진입
        selectionComplete = true;
        if (selectCharacterPanel != null) selectCharacterPanel.SetActive(false);

        // (선택) 룸 상태 표식 및 방 닫기
        PhotonHashtable roomProps = new PhotonHashtable { { "SelectionCompleted", true } };
        PhotonNetwork.CurrentRoom.IsOpen = false;
        PhotonNetwork.CurrentRoom.SetCustomProperties(roomProps);

        // 마스터만 로드, 나머지는 AutomaticallySyncScene으로 따라옴
        PhotonNetwork.LoadLevel("GameScene");
    }

    private void OnTimerEnd()
    {
        if (selectionComplete) return;

        // 로컬이 아직 선택 안 했다면 랜덤 선택
        if (!PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("SelectedCharacterName")
            || !(PhotonNetwork.LocalPlayer.CustomProperties["SelectedCharacterName"] is string s) || string.IsNullOrEmpty(s))
        {
            int randomIndex = Random.Range(0, allCharacters.Length);
            PhotonHashtable props = new PhotonHashtable {
                { "SelectedCharacterName", allCharacters[randomIndex].data.characterPrefab.name }
            };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }

        // 로컬이 준비 상태가 아니면 준비로
        if (!(PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue("IsReady", out object readyObj)
              && readyObj is bool ready && ready))
        {
            SetReadyState(true);
        }

        // 마스터는 재검사
        if (PhotonNetwork.IsMasterClient)
        {
            CheckAndStartGame();
        }
    }
}
