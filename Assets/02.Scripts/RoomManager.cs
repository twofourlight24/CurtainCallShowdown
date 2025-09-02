using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Collections; // 추가

// PUN 2의 콜백 인터페이스를 구현하여 Photon 이벤트에 반응
public class RoomManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - Room Info =====")]
    public TMP_Text roomNameText;
    public Button leaveButton; // 방 나가기 버튼

    // 다른 플레이어들에게 보여지는 게임 설정 정보 UI
    public TMP_Text gameModeInfoText;
    public TMP_Text gameRoundInfoText;
    public TMP_Text characterDuplicationInfoText;

    [Header("===== UI - Player List =====")]
    public Transform playerPanel;
    public GameObject playerInfoPrefab;
    private Dictionary<string, GameObject> playerInfoObjects = new Dictionary<string, GameObject>();

    [Header("===== UI - Control Buttons =====")]
    public Button startButton;
    public Button readyButton;
    public Button gameSetupButton;

    [Header("===== UI - Game Setup Panel =====")]
    public GameObject gameSetupPanel;
    public Button gameSetupConfirmButton;
    public Button gameSetupCancelButton;

    [Header("===== UI - Game Setup Options =====")]
    public TMP_Text gameModeText;
    public TMP_Text gameModeGuideText;
    public Button prevModeButton;
    public Button nextModeButton;
    public TMP_Text gameRoundText;
    public Button round3Button;
    public Button round4Button;
    public Button round5Button;
    public TMP_Text characterDuplicationText;
    public Toggle characterDuplicationToggle;
    public TMP_Text mapText;
    public Image mapImage;
    public Button prevMapButton;
    public Button nextMapButton;

    [Header("===== UI - Chat =====")]
    public TMP_InputField chatInput;
    public ScrollRect chatScrollRect;
    public Transform chatContent;
    public GameObject chatMessagePrefab;

    // --- Private Variables ---
    private string[] gameModes = { "Showdown", "King of the Hill" };
    private string[] gameModeDescriptions =
    {
        "각 플레이어당 3개의 목숨이 주어지며, 최후의 생존자가 승리합니다.",
        "특정 영역에 오랫동안 머물러 점수가 가장 높은 플레이어가 승리합니다."
    };
    private string[] mapNames = { "BasicMap", "미구현맵", }; // 맵 이름은 예시
    private int selectedModeIndex = 0;
    private int selectedRoundCount = 3;
    private int selectedMapIndex = 0;

    void Start()
    {
        // 방 이름 설정
        roomNameText.text = PhotonNetwork.CurrentRoom.Name;

        // 버튼 이벤트 연결
        startButton.onClick.AddListener(OnStartButtonClicked);
        readyButton.onClick.AddListener(OnReadyButtonClicked);
        gameSetupButton.onClick.AddListener(OnGameSetupButtonClicked);
        gameSetupConfirmButton.onClick.AddListener(OnGameSetupConfirmButtonClicked);
        gameSetupCancelButton.onClick.AddListener(OnGameSetupCancelButtonClicked);
        leaveButton.onClick.AddListener(OnLeaveButtonClicked); // 방 나가기 버튼 연결
        prevModeButton.onClick.AddListener(() => OnGameModeChanged(-1));
        nextModeButton.onClick.AddListener(() => OnGameModeChanged(1));
        round3Button.onClick.AddListener(() => OnRoundCountChanged(3));
        round4Button.onClick.AddListener(() => OnRoundCountChanged(4));
        round5Button.onClick.AddListener(() => OnRoundCountChanged(5));
        prevMapButton.onClick.AddListener(() => OnMapChanged(-1));
        nextMapButton.onClick.AddListener(() => OnMapChanged(1));

        chatInput.onEndEdit.AddListener(OnChatInputEndEdit);

        // 방장 여부에 따라 UI 활성화/비활성화
        if (PhotonNetwork.IsMasterClient)
        {
            startButton.gameObject.SetActive(true);
            readyButton.gameObject.SetActive(false);
            gameSetupButton.gameObject.SetActive(true);

            // 방장만 게임 설정을 초기화하고 다른 플레이어들에게 동기화
            InitializeGameSettings();
        }
        else
        {
            startButton.gameObject.SetActive(false);
            readyButton.gameObject.SetActive(true);
            gameSetupButton.gameObject.SetActive(false);
        }

        gameSetupPanel.SetActive(false);

        // 모든 플레이어 목록 UI 갱신
        RefreshPlayerList();

        // 방의 커스텀 속성 업데이트를 수신하기 위한 초기 상태 설정
        // 이 코드는 현재 방 설정을 즉시 가져와 UI에 반영합니다.
        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }
    private void Update()
    {
        if (chatInput.isFocused && Input.GetKeyDown(KeyCode.Return))
        {
            string text = chatInput.text;
            if (!string.IsNullOrEmpty(text))
            {
                photonView.RPC("ReceiveChatMessage", RpcTarget.All, PhotonNetwork.LocalPlayer.NickName, text);
                chatInput.text = "";                  // 입력창 비우기
                chatInput.ActivateInputField();       // 다시 포커스 주기 (계속 채팅 입력 가능하게)
            }
        }
    }

    // Photon 콜백 함수들
    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"플레이어 입장: {newPlayer.NickName}");
        RefreshPlayerList();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"플레이어 퇴장: {otherPlayer.NickName}");
        RefreshPlayerList();

        // 방장이 나가면 새로운 방장에게 권한 위임
        if (otherPlayer.IsMasterClient)
        {
            Debug.Log("이전 방장이 나갔습니다. 새로운 방장이 설정됩니다.");
            CheckMasterClientStatus();
        }
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        // 방 설정이 변경될 때마다 UI를 업데이트합니다.
        UpdateGameSetupUI(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        // 방을 나갔을 때 로비 씬으로 이동
        PhotonNetwork.LoadLevel("WatingRoomScene");
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        // 플레이어의 준비 상태가 변경되면 UI를 업데이트합니다.
        if (changedProps.ContainsKey("IsReady"))
        {
            if (playerInfoObjects.ContainsKey(targetPlayer.NickName))
            {
                playerInfoObjects[targetPlayer.NickName].GetComponent<PlayerInfo>().SetReadyState((bool)changedProps["IsReady"]);
            }
        }

        // 모든 플레이어가 준비되었는지 확인 (방장만)
        if (PhotonNetwork.IsMasterClient)
        {
            CheckAllPlayersReady();
        }
    }

    // --- Player & Room Management Logic ---

    /// <summary>
    /// 현재 방의 모든 플레이어 목록 UI를 갱신합니다.
    /// </summary>
    private void RefreshPlayerList()
    {
        // 기존 플레이어 UI 삭제
        foreach (var infoObject in playerInfoObjects.Values)
        {
            Destroy(infoObject);
        }
        playerInfoObjects.Clear();

        // 새로운 플레이어 UI 생성
        foreach (var player in PhotonNetwork.PlayerList)
        {
            GameObject playerObj = Instantiate(playerInfoPrefab, playerPanel);
            PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
            info.Setup(player);
            playerInfoObjects[player.NickName] = playerObj;
        }

        CheckAllPlayersReady();
    }

    /// <summary>
    /// 현재 플레이어의 방장 상태를 확인하고 UI를 갱신합니다.
    /// </summary>
    private void CheckMasterClientStatus()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            startButton.gameObject.SetActive(true);
            readyButton.gameObject.SetActive(false);
            gameSetupButton.gameObject.SetActive(true);
            // 새로운 방장이 된 후 기존의 방 설정을 UI에 로드
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
        }
        else
        {
            startButton.gameObject.SetActive(false);
            readyButton.gameObject.SetActive(true);
            gameSetupButton.gameObject.SetActive(false);
            // 일반 플레이어는 방장이 설정한 정보를 확인
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
        }
    }

    // --- Ready & Start Button Logic ---

    /// <summary>
    /// '준비' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnReadyButtonClicked()
    {
        // 준비 상태를 플레이어 커스텀 속성에 저장하고 동기화합니다.
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        // CustomProperties에 "IsReady" 키가 없으면 false로 초기화
        bool isReady = PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("IsReady") ? !(bool)PhotonNetwork.LocalPlayer.CustomProperties["IsReady"] : true;
        props.Add("IsReady", isReady);
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    /// <summary>
    /// 방장이 '시작' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnStartButtonClicked()
    {
        if (CheckAllPlayersReady())
        {
            // 게임 시작
            PhotonNetwork.LoadLevel("GameScene");
        }
        else
        {
            Debug.Log("모든 플레이어가 준비되지 않았습니다!");
            // TODO: 사용자에게 "모든 플레이어가 준비되어야 게임을 시작할 수 있습니다"와 같은 메시지를 띄우는 UI 추가
        }
    }

    /// <summary>
    /// 모든 플레이어가 준비 상태인지 확인합니다.
    /// </summary>
    /// <returns>모든 플레이어가 준비되었으면 true, 아니면 false</returns>
    private bool CheckAllPlayersReady()
    {
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.IsMasterClient)
            {
                object isReadyObj;
                if (!player.CustomProperties.TryGetValue("IsReady", out isReadyObj) || !(bool)isReadyObj)
                {
                    startButton.interactable = false;
                    return false;
                }
            }
        }
        startButton.interactable = true; // 모든 플레이어가 준비되면 시작 버튼 활성화
        return true;
    }

    /// <summary>
    /// '방 나가기' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnLeaveButtonClicked()
    {
        // 현재 방을 나갑니다.
        PhotonNetwork.LeaveRoom();
    }

    // --- Game Setup Panel Logic ---

    /// <summary>
    /// '게임 설정' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnGameSetupButtonClicked()
    {
        gameSetupPanel.SetActive(true);
    }

    /// <summary>
    /// 게임 설정 값을 초기화하고 방 커스텀 속성에 저장합니다.
    /// 방장만 호출합니다.
    /// </summary>
    private void InitializeGameSettings()
    {
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            {"GameMode", gameModes[selectedModeIndex]},
            {"RoundCount", selectedRoundCount},
            {"AllowDuplication", characterDuplicationToggle.isOn},
            {"MapName", mapNames[selectedMapIndex]}
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    /// <summary>
    /// '확인' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnGameSetupConfirmButtonClicked()
    {
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            {"GameMode", gameModes[selectedModeIndex]},
            {"RoundCount", selectedRoundCount},
            {"AllowDuplication", characterDuplicationToggle.isOn},
            {"MapName", mapNames[selectedMapIndex]}
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        gameSetupPanel.SetActive(false);
    }

    /// <summary>
    /// '돌아가기' 버튼 클릭 시 호출됩니다.
    /// </summary>
    public void OnGameSetupCancelButtonClicked()
    {
        gameSetupPanel.SetActive(false);
        // 패널을 닫을 때 원래 설정으로 UI 복구
        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    /// <summary>
    /// 방 설정이 변경될 때마다 UI를 업데이트합니다. (모든 플레이어)
    /// </summary>
    private void UpdateGameSetupUI(ExitGames.Client.Photon.Hashtable roomProps)
    {
        // 게임 모드 업데이트
        if (roomProps.ContainsKey("GameMode"))
        {
            string mode = (string)roomProps["GameMode"];
            gameModeText.text = mode;
            gameModeInfoText.text = mode; // 추가된 InfoText 업데이트
            selectedModeIndex = System.Array.IndexOf(gameModes, mode);
            gameModeGuideText.text = gameModeDescriptions[selectedModeIndex];
        }

        // 라운드 수 업데이트
        if (roomProps.ContainsKey("RoundCount"))
        {
            selectedRoundCount = (int)roomProps["RoundCount"];
            gameRoundText.text = $"{selectedRoundCount} 라운드";
            gameRoundInfoText.text = $"{selectedRoundCount} 라운드"; // 추가된 InfoText 업데이트
        }

        // 캐릭터 중복 허용 업데이트
        if (roomProps.ContainsKey("AllowDuplication"))
        {
            bool allow = (bool)roomProps["AllowDuplication"];
            characterDuplicationText.text = $"캐릭터 중복 선택 {(allow ? "허용" : "제한")}";
            characterDuplicationInfoText.text = $"단원 중복 {(allow ? "허용" : "제한")}"; // 추가된 InfoText 업데이트
            characterDuplicationToggle.isOn = allow;
            if (PhotonNetwork.IsMasterClient)
            {
                characterDuplicationToggle.gameObject.SetActive(true);
            }
            else
            {
                // 일반 플레이어에게는 토글 UI를 비활성화하고 텍스트만 보여줌
                characterDuplicationToggle.gameObject.SetActive(false);
            }
        }

        // 맵 업데이트
        if (roomProps.ContainsKey("MapName"))
        {
            string map = (string)roomProps["MapName"];
            mapText.text = map;
            selectedMapIndex = System.Array.IndexOf(mapNames, map);
            // TODO: mapImage.sprite = ... 로 실제 맵 이미지 업데이트 로직 추가
        }

        // 방장만 설정 변경 버튼 활성화
        prevModeButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        nextModeButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        prevMapButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        nextMapButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round3Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round4Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round5Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
    }

    /// <summary>
    /// 게임 모드 변경 버튼 클릭 시 호출됩니다.
    /// </summary>
    /// <param name="change">변경량 (-1: 이전, 1: 다음)</param>
    public void OnGameModeChanged(int change)
    {
        selectedModeIndex = (selectedModeIndex + change + gameModes.Length) % gameModes.Length;
        gameModeText.text = gameModes[selectedModeIndex];
        gameModeGuideText.text = gameModeDescriptions[selectedModeIndex];
    }

    /// <summary>
    /// 라운드 수 변경 버튼 클릭 시 호출됩니다.
    /// </summary>
    /// <param name="round">설정할 라운드 수</param>
    public void OnRoundCountChanged(int round)
    {
        selectedRoundCount = round;
        gameRoundText.text = $"{selectedRoundCount} 라운드";
    }

    /// <summary>
    /// 맵 변경 버튼 클릭 시 호출됩니다.
    /// </summary>
    /// <param name="change">변경량 (-1: 이전, 1: 다음)</param>
    public void OnMapChanged(int change)
    {
        selectedMapIndex = (selectedMapIndex + change + mapNames.Length) % mapNames.Length;
        mapText.text = mapNames[selectedMapIndex];
        // TODO: mapImage.sprite = ... 로 실제 맵 이미지 업데이트 로직 추가
    }

    // --- Chat Logic ---

    /// <summary>
    /// 채팅 입력 필드에서 Enter 키 입력 시 호출됩니다.
    /// </summary>
    /// <param name="text">입력된 텍스트</param>
    public void OnChatInputEndEdit(string text)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!string.IsNullOrEmpty(text))
            {
                // RPC를 통해 모든 플레이어에게 채팅 메시지 전송
                photonView.RPC("ReceiveChatMessage", RpcTarget.All, PhotonNetwork.LocalPlayer.NickName, text);
                chatInput.text = ""; // 입력 필드 초기화
                chatInput.ActivateInputField(); // 다시 입력 필드 활성화
            }
        }
    }


    [PunRPC]
    public void ReceiveChatMessage(string senderName, string message)
    {
        GameObject chatMsgObj = Instantiate(chatMessagePrefab, chatContent);
        TMP_Text chatText = chatMsgObj.GetComponent<TMP_Text>();
        chatText.text = $"<color=yellow>{senderName}</color>: {message}";

        // 스크롤을 맨 아래로
        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent.GetComponent<RectTransform>());
        StartCoroutine(ScrollToBottom());
    }

    private IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }
}
