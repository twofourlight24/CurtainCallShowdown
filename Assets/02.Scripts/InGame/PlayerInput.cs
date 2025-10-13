using Photon.Pun;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

[DisallowMultipleComponent]
public class PlayerInput : MonoBehaviourPun, IPunObservable
{
    public CharacterBase controlledCharacter;
    public CharacterBase Character { get; private set; }
    private PhotonView pv;

    [Header("Network Sync")]
    private Vector3 networkPosition;
    private Vector3 networkVelocity;
    private float lastReceivedTime;
    private float lerpSpeed = 10f;

    [Header("Run Detection")]
    public float doubleTapTime = 0.5f; // ������ ����
    private float lastATapTime = -1f;
    private float lastDTapTime = -1f;
    private bool isRunning = false;

    [Header("Attack Detection")]
    private Dictionary<KeyCode, float> lastPressTime = new();
    private Dictionary<KeyCode, float> holdStartTime = new();
    private Dictionary<string, float> skillCooldowns = new(); // 스킬별 쿨타임 관리

    private Rigidbody2D rb;
    public void SetCharacter(CharacterBase cb)
    {
        Character = cb;
        controlledCharacter = cb;
    }
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        networkPosition = transform.position;
        pv = GetComponent<PhotonView>();

        // 안전망: 주입이 아직 없으면 스스로 찾아서 바인딩
        if (Character == null)
            Character = GetComponent<CharacterBase>();
        if (controlledCharacter == null)
            controlledCharacter = Character;
    }
    void OnEnable()
    {
        // 혹시 씬 리로드/리스폰 등으로 참조가 끊겼을 때 재바인딩
        if (Character == null)
            Character = GetComponent<CharacterBase>();
        if (controlledCharacter == null)
            controlledCharacter = Character;
    }

    void Update()
    {
        if (pv == null && !pv.IsMine) return;
        if (controlledCharacter == null) return;

        if (photonView.IsMine)
        {
            HandleInput();
            HandleCommandInput(KeyCode.J);
        }
        else
        {
            HandleNetworkInterpolation();
        }
    }

    #region --- Input Handling ---
    private void HandleInput()
    {
        Vector3 moveDir = Vector3.zero;

        bool aPressed = Input.GetKey(KeyCode.A);
        bool dPressed = Input.GetKey(KeyCode.D);

        // --- �̵� ���� ���� ---
        if (aPressed) moveDir = Vector3.left;
        else if (dPressed) moveDir = Vector3.right;

        // --- �޸��� ������ ���� ---
        if (Input.GetKeyDown(KeyCode.A))
        {
            if (Time.time - lastATapTime <= doubleTapTime)
                isRunning = true;
            lastATapTime = Time.time;
        }
        if (Input.GetKeyDown(KeyCode.D))
        {
            if (Time.time - lastDTapTime <= doubleTapTime)
                isRunning = true;
            lastDTapTime = Time.time;
        }

        // --- Ű ���� �޸��� ���� ---
        if (Input.GetKeyUp(KeyCode.A) || Input.GetKeyUp(KeyCode.D))
            isRunning = false;

        // --- ���� ĳ���� �̵� ���� ---
        if (controlledCharacter != null)
        {
            controlledCharacter.SetMoveDirection(moveDir);
            controlledCharacter.SetRunState(isRunning);
        }

        if (Input.GetKeyDown(KeyCode.K))
            controlledCharacter?.Guard();

        if (Input.GetKeyDown(KeyCode.L))
            controlledCharacter?.UseSkill();

        // --- ���� ---
        if (Input.GetKeyDown(KeyCode.Space))
            controlledCharacter?.Jump();

        // --- ���� ���: S + Space ���ÿ� ������ ���� ---
        if (Input.GetKey(KeyCode.S) && Input.GetKey(KeyCode.Space))
            controlledCharacter?.DropThroughPlatform();
    }
    private void HandleCommandInput(KeyCode key)
    {
        // 모든 커맨드 중 key가 J인 것만 필터링
        var cmds = controlledCharacter.commandSet?.commands;
        if (cmds == null) return;

        bool anyExecuted = false;

        foreach (var cmd in cmds)
        {
            if (cmd.key != key) continue;
            if (IsOnCooldown(cmd)) continue; // 쿨타임 중이면 무시

            switch (cmd.type)
            {
                case CharacterCommandSet.CommandType.SinglePress:
                    if (Input.GetKeyDown(key))
                    {
                        ExecuteCommand(cmd);
                        anyExecuted = true;
                    }
                    break;

                case CharacterCommandSet.CommandType.DoublePress:
                    if (Input.GetKeyDown(key))
                    {
                        if (Time.time - lastPressTime.GetValueOrDefault(key) <= cmd.timeWindow)
                        {
                            ExecuteCommand(cmd);
                            anyExecuted = true;
                        }
                        lastPressTime[key] = Time.time;
                    }
                    break;

                case CharacterCommandSet.CommandType.Hold:
                    if (Input.GetKeyDown(key))
                        holdStartTime[key] = Time.time;

                    if (Input.GetKeyUp(key))
                    {
                        if (holdStartTime.ContainsKey(key) &&
                            Time.time - holdStartTime[key] >= cmd.timeWindow)
                        {
                            ExecuteCommand(cmd);
                            anyExecuted = true;
                        }
                    }
                    break;

                case CharacterCommandSet.CommandType.WhileRunning:
                    if (controlledCharacter.IsRunning && Input.GetKeyDown(key))
                    {
                        ExecuteCommand(cmd);
                        anyExecuted = true;
                    }
                    break;
            }
        }

        // 커맨드가 하나도 발동하지 않았다면 → 기본 공격 발동
        if (!anyExecuted && Input.GetKeyDown(key))
            controlledCharacter.Attack();
    }

    private void ExecuteCommand(CharacterCommandSet.Command cmd)
    {
        if (IsOnCooldown(cmd)) return;

        var method = controlledCharacter.GetType().GetMethod(cmd.skillMethod,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        if (method != null)
        {
            method.Invoke(controlledCharacter, null);
            skillCooldowns[cmd.commandName] = Time.time + cmd.cooldown;
        }
        else
        {
            Debug.LogWarning($"[Command] '{cmd.skillMethod}' not found on {controlledCharacter.name}");
        }
    }

    private bool IsOnCooldown(CharacterCommandSet.Command cmd)
    {
        return skillCooldowns.TryGetValue(cmd.commandName, out float endTime) && Time.time < endTime;
    }


    #endregion

    #region --- Network Interpolation ---
    private void HandleNetworkInterpolation()
    {
        float lag = Mathf.Max(0f, (float)(PhotonNetwork.Time - lastReceivedTime));
        Vector3 targetPos = networkPosition + networkVelocity * lag;
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * lerpSpeed);
    }
    #endregion

    #region --- Photon Sync ---
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(transform.position);
            stream.SendNext(rb != null ? rb.linearVelocity : Vector3.zero);
        }
        else
        {
            networkPosition = (Vector3)stream.ReceiveNext();
            networkVelocity = (Vector3)stream.ReceiveNext();
            lastReceivedTime = (float)PhotonNetwork.Time;
        }
    }
    #endregion
}
