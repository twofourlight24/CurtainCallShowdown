using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UI;

public class SelectCharacterManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - Select Character Panel =====")]
    public GameObject selectCharacterPanel;
    public TMP_Text gameModeText;
    public TMP_Text mapText;
    public TMP_Text timerText;

    [Header("===== UI - Player Borders =====")]
    public GameObject[] playerBorders; // P1, P2, P3, P4 Border
    public TMP_Text[] playerNicknameTexts; // Player1NickNameText, etc.
    public TMP_Text[] characterNameTexts; // P1CharacterNameText, etc.
    public Image[] characterIcons; // CharacterIcon, etc.
    public GameObject[] characterButtons; // 캐릭터 선택 버튼 배열
    public TMP_Text characterGuideText; // 캐릭터 설명 텍스트
    public TMP_Text selectedCharacterNameText; // 현재 선택된 캐릭터 이름 텍스트

    // --- Private Variables ---
    private Dictionary<string, int> playerIndices = new Dictionary<string, int>();
    private float timer = 30.0f;
    private bool characterSelectionComplete = false;

    // 이제 CharacterData는 Character 스크립트 내부에 존재합니다.
    // 따라서 'CharacterData' 대신 'Character' 컴포넌트 배열을 사용합니다.
    public CharacterData[] allCharacters;

    void Start()
    {
        // 게임 씬 시작 시 캐릭터 선택 패널 활성화
        selectCharacterPanel.SetActive(true);
        InitializePlayerPanels();
        InitializeCharacterButtons();

        // 게임 설정 정보 로드 및 UI 업데이트
        LoadGameSettings();
    }

    void Update()
    {
        if (timer > 0 && !characterSelectionComplete)
        {
            timer -= Time.deltaTime;
            timerText.text = $"캐릭터 선택 남은 시간: {Mathf.Ceil(timer)}초";

            if (timer <= 0)
            {
                OnTimerEnd();
            }
        }
    }

    /// <summary>
    /// 플레이어 패널 UI를 초기화하고 플레이어 정보를 설정합니다.
    /// </summary>
    private void InitializePlayerPanels()
    {
        // 모든 플레이어 보더를 비활성화
        for (int i = 0; i < playerBorders.Length; i++)
        {
            playerBorders[i].SetActive(false);
        }

        // 현재 방에 있는 플레이어 수만큼 보더를 활성화
        int playerIndex = 0;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (playerIndex < playerBorders.Length)
            {
                playerBorders[playerIndex].SetActive(true);
                playerNicknameTexts[playerIndex].text = player.NickName;
                playerIndices[player.NickName] = playerIndex; // 닉네임과 인덱스 매핑

                // 캐릭터 정보 초기화
                characterNameTexts[playerIndex].gameObject.SetActive(false);
                characterIcons[playerIndex].gameObject.SetActive(false);

                // 로컬 플레이어의 경우 opacity 조절 (투명도)
                if (player.IsLocal)
                {
                    // 투명도 조절 로직 추가 (선택 전)
                    SetBorderOpacity(playerBorders[playerIndex].GetComponent<Image>(), 0.5f);
                }
            }
            playerIndex++;
        }
    }

    /// <summary>
    /// 캐릭터 선택 버튼을 초기화하고 이벤트 리스너를 연결합니다.
    /// </summary>
    private void InitializeCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            int index = i; // 클로저 이슈 방지
            Button button = characterButtons[index].GetComponent<Button>();

            button.onClick.AddListener(() => OnCharacterSelected(index));
        }
    }

    /// <summary>
    /// Photon Custom Properties에서 게임 설정을 불러와 UI를 업데이트합니다.
    /// </summary>
    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("GameMode"))
        {
            gameModeText.text = $"게임 모드: {PhotonNetwork.CurrentRoom.CustomProperties["GameMode"]}";
        }
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("MapName"))
        {
            mapText.text = $"맵: {PhotonNetwork.CurrentRoom.CustomProperties["MapName"]}";
        }
    }

    /// <summary>
    /// 플레이어가 캐릭터를 선택했을 때 호출됩니다.
    /// </summary>
    /// <param name="characterIndex">선택한 캐릭터의 인덱스</param>
    public void OnCharacterSelected(int characterIndex)
    {
        // 이제 'Character' 컴포넌트에서 데이터를 가져옵니다.
        string characterName = allCharacters[characterIndex].data.characterName;
        Sprite characterIcon = allCharacters[characterIndex].data.characterIcon;

        // UI 업데이트
        selectedCharacterNameText.text = $"선택 캐릭터: {characterName}";
        characterGuideText.text = allCharacters[characterIndex].data.characterDescription;

        // Photon Custom Properties를 업데이트하여 다른 플레이어에게 동기화
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
        props["SelectedCharacter"] = characterIndex;
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (changedProps.ContainsKey("SelectedCharacter"))
        {
            int characterIndex = (int)changedProps["SelectedCharacter"];
            if (playerIndices.ContainsKey(targetPlayer.NickName))
            {
                int playerIndex = playerIndices[targetPlayer.NickName];
                // 플레이어의 UI 업데이트
                characterNameTexts[playerIndex].gameObject.SetActive(true);
                characterIcons[playerIndex].gameObject.SetActive(true);
                characterNameTexts[playerIndex].text = allCharacters[characterIndex].data.characterName;
                characterIcons[playerIndex].sprite = allCharacters[characterIndex].data.characterIcon;

                // 확정되지 않은 상태이므로 투명도를 낮춤
                SetBorderOpacity(playerBorders[playerIndex].GetComponent<Image>(), 0.5f);
            }
        }

        if (changedProps.ContainsKey("ConfirmedCharacter"))
        {
            // 캐릭터 확정 시 투명도를 원래대로 되돌림
            if (playerIndices.ContainsKey(targetPlayer.NickName))
            {
                int playerIndex = playerIndices[targetPlayer.NickName];
                SetBorderOpacity(playerBorders[playerIndex].GetComponent<Image>(), 1.0f);
            }
        }

        // 모든 플레이어가 확정했는지 확인
        CheckAllPlayersConfirmed();
    }

    /// <summary>
    /// 타이머가 0초가 되었을 때 호출됩니다.
    /// </summary>
    private void OnTimerEnd()
    {
        if (characterSelectionComplete) return;

        // 아직 확정하지 않은 플레이어는 랜덤으로 캐릭터를 선택
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.CustomProperties.ContainsKey("ConfirmedCharacter"))
            {
                // 랜덤 캐릭터 선택 및 확정
                int randomCharacterIndex = Random.Range(0, allCharacters.Length);
                ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
                props["SelectedCharacter"] = randomCharacterIndex;
                props["ConfirmedCharacter"] = true;
                player.SetCustomProperties(props);
            }
        }

        StartCoroutine(HidePanelAfterDelay(3.0f));
    }

    /// <summary>
    /// 모든 플레이어가 캐릭터를 확정했는지 확인합니다.
    /// </summary>
    private void CheckAllPlayersConfirmed()
    {
        bool allConfirmed = true;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.CustomProperties.ContainsKey("ConfirmedCharacter"))
            {
                allConfirmed = false;
                break;
            }
        }

        if (allConfirmed && !characterSelectionComplete)
        {
            characterSelectionComplete = true;
            StartCoroutine(HidePanelAfterDelay(3.0f));
        }
    }

    /// <summary>
    /// 일정 시간 후 캐릭터 선택 패널을 비활성화합니다.
    /// </summary>
    private IEnumerator HidePanelAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        selectCharacterPanel.SetActive(false);
        // 다음 GameManager로 넘어가는 로직 추가
    }

    /// <summary>
    /// 이미지의 투명도를 설정합니다.
    /// </summary>
    private void SetBorderOpacity(Image image, float opacity)
    {
        Color color = image.color;
        color.a = opacity;
        image.color = color;
    }
}
