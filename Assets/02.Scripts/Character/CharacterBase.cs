using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// 모든 플레이어 캐릭터의 기본이 되는 추상 클래스.
/// 공통 속성, 물리 기반 이동, 네트워크 동기화를 담당합니다.
/// 2D 게임 환경에 맞게 Rigidbody2D와 좌우 반전(플립) 기능을 포함합니다.
/// </summary>
public abstract class CharacterBase : MonoBehaviourPun, IPunInstantiateMagicCallback, IPunObservable
{
    public bool IsRunning { get; private set; }

    // 캐릭터의 공통적인 속성
    public float MaxHp = 100f;
    public float CurHp = 100f;
    public float moveSpeed = 0f;
    public float walkSpeed = 5f;
    public float runSpeed = 8f; // 달리기 속도 추가
    public float jumpPower = 10f; // 점프 힘 추가
    public int maxJumpCount = 2; // 최대 점프 횟수 추가
    public int LifeCount = 3;
    public CharacterCommandSet commandSet;

    [Header("Character Properties")]
    public GameObject ShieldObject; // 가드 기능을 위한 쉴드 오브젝트 추가
    public float immobilizedTime = 1f; // 쉴드 파괴 후 움직이지 못하는 시간

    protected Rigidbody2D rb;
    private Vector3 moveDirection;
    private int jumpCount;
    private bool isGrounded = false;
    private bool isImmobilized = false; // 움직이지 못하는 상태
    private Collider2D currentPlatformCollider; // 현재 밟고 있는 플랫폼 콜라이더
    private bool isDead = false;
    private float invincibleUntil = 0f; // Time.time 기준

    // 네트워크 동기화를 위한 변수
    private Vector3 _networkPosition;
    private bool _networkIsFacingRight;
    protected bool isFacingRight = true;
    public GameManager gm;

    //Golden Statue 관련
    public bool IsGoldenStatue { get; private set; } = false;
    private Coroutine goldenCo;

    public void OnPhotonInstantiate(PhotonMessageInfo info)
    {
        StartCoroutine(Co_RegisterAfterManagerReady());
    }

    private IEnumerator Co_RegisterAfterManagerReady()
    {
        // GameManager.Instance가 null일 수 있음 → 준비될 때까지 잠깐 대기
        float t = 0f;
        while (GameManager.Instance == null && t < 2f)
        {
            t += Time.deltaTime;
            yield return null;
        }
        var owner = photonView.Owner;
        if (GameManager.Instance != null && owner != null)
            GameManager.Instance.RegisterCharacter(owner, this.gameObject);
    }

    protected void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("CharacterBase: Rigidbody2D 컴포넌트가 필요합니다!");
        }
        gm = GameManager.Instance;

        if (CurHp <= 0f) CurHp = MaxHp;
        jumpCount = maxJumpCount;

        // 동기화 변수 초기화
        _networkPosition = transform.position;

        // 시작 시 쉴드 오브젝트 비활성화
        if (ShieldObject != null)
        {
            ShieldObject.SetActive(false);
        }
    }

    void FixedUpdate()
    {
        // 핵심: 로컬 플레이어만 물리 연산을 수행하도록 보장
        if (photonView.IsMine && rb != null)
        {
            if (isImmobilized)
            {
                rb.linearVelocity = Vector2.zero; // 캐릭터가 움직이지 못하도록 함
            }
            else
            {
                // 로컬 플레이어 이동
                rb.linearVelocity = new Vector2(moveDirection.x * moveSpeed, rb.linearVelocity.y);

                // 캐릭터 좌우 방향 전환
                if (moveDirection.x > 0 && !isFacingRight)
                {
                    Flip();
                }
                else if (moveDirection.x < 0 && isFacingRight)
                {
                    Flip();
                }
            }
        }
        else
        {
            // 다른 플레이어 캐릭터의 위치를 부드럽게 동기화
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.fixedDeltaTime * 10.0f);

            // 네트워크로 받은 방향에 따라 로컬 캐릭터의 스케일을 뒤집습니다.
            if (isFacingRight != _networkIsFacingRight)
            {
                Vector3 theScale = transform.localScale;
                theScale.x *= -1;
                transform.localScale = theScale;
                isFacingRight = _networkIsFacingRight;
            }
        }
    }

    /// <summary>
    /// PlayerInput 스크립트로부터 이동 방향을 설정하는 함수
    /// </summary>
    public void SetMoveDirection(Vector3 direction)
    {
        moveDirection = direction;
    }
    public void SetRunState(bool running)
    {
        IsRunning = running;    
        moveSpeed = running ? runSpeed : walkSpeed;
    }

    /// <summary>
    /// 점프 함수 (더블 점프 포함)
    /// </summary>
    public void Jump()
    {
        if (jumpCount > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0); // 기존 y 속도 초기화
            rb.AddForce(new Vector2(0, jumpPower), ForceMode2D.Impulse);
            jumpCount--;
        }
    }

    /// <summary>
    /// 아래 방향키로 발판을 통과하는 함수
    /// </summary>
    public void DropThroughPlatform()
    {
        // 현재 밟고 있는 플랫폼이 있다면
        if (currentPlatformCollider != null)
        {
            // 2D 플랫폼 콜라이더를 일시적으로 무시하도록 설정
            Physics2D.IgnoreCollision(GetComponent<Collider2D>(), currentPlatformCollider, true);
            StartCoroutine(RestorePlatformCollision(currentPlatformCollider));
        }
    }

    /// <summary>
    /// 일정 시간 후 플랫폼 콜라이더 충돌을 다시 활성화하는 코루틴
    /// </summary>
    private System.Collections.IEnumerator RestorePlatformCollision(Collider2D platformCollider)
    {
        yield return new WaitForSeconds(0.5f); // 0.5초 후 다시 활성화
        if (platformCollider != null)
        {
            Physics2D.IgnoreCollision(GetComponent<Collider2D>(), platformCollider, false);
        }
    }

    /// <summary>
    /// 캐릭터를 좌우로 뒤집는 함수
    /// </summary>
    private void Flip()
    {
        isFacingRight = !isFacingRight;
        Vector3 theScale = transform.localScale;
        theScale.x *= -1;
        transform.localScale = theScale;
    }

    /// <summary>
    /// 포톤이 데이터를 송수신하기 위해 호출하는 함수
    /// </summary>
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            // 내 캐릭터이므로, 내 위치와 좌우 방향을 보냅니다.
            stream.SendNext(transform.position);
            stream.SendNext(isFacingRight);
        }
        else
        {
            // 다른 플레이어 캐릭터이므로, 위치와 좌우 방향 정보를 받습니다.
            _networkPosition = (Vector3)stream.ReceiveNext();
            _networkIsFacingRight = (bool)stream.ReceiveNext();
        }
    }

    // 콜라이더 이벤트 처리
    private void OnCollisionEnter2D(Collision2D collision)
    {
        // 바닥에 닿았을 때 점프 횟수 초기화
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = true;
            jumpCount = maxJumpCount;
        }

        // 현재 밟고 있는 플랫폼 저장 (통과 점프를 위해)
        if (collision.gameObject.CompareTag("Platform"))
        {
            currentPlatformCollider = collision.collider;
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground") || collision.gameObject.CompareTag("Platform"))
        {
            isGrounded = false;
        }

        if (collision.gameObject.CompareTag("Platform"))
        {
            currentPlatformCollider = null;
        }
    }

    // 데미지를 받는 함수

    [PunRPC]
    public void RPC_TakeDamage(float dmg)
    {
        if (GameManager.Instance?.currentActiveGameMode is ShowdownMode sd &&
       sd.IsInvincible(photonView.Owner))
            return;
        if (isDead) return; // 이미 죽은 상태면 무시
        if(Time.time < invincibleUntil) return; // 무적 상태면 무시

        CurHp -= dmg;
        if (CurHp <= 0)
        {
            CurHp = 0;
            Die();
        }
        if (GameManager.Instance != null)
        {
            GameManager.Instance.UpdatePlayerUI(photonView.Owner);
        }

        Debug.Log($"{photonView.Owner.NickName} took {dmg} damage. HP={CurHp}");
    }
    public void Die()
    {
        if(isDead) return; // 이미 죽은 상태면 무시  
        isDead = true;

        gameObject.SetActive(false); // 캐릭터 비활성화

        PhotonView pv = GetComponent<PhotonView>();
        if (!PhotonNetwork.IsMasterClient) return;     
        if (pv == null || pv.Owner == null) return;

        //  GameManager와 PhotonView 안전하게 확보
        var gmInst = GameManager.Instance;
        if (gmInst == null) { Debug.LogError("[Die] GameManager.Instance is null"); return; }

        var gmPV = gmInst.GetComponent<PhotonView>();
        if (gmPV == null) { Debug.LogError("[Die] GameManager has no PhotonView"); return; }

        //  Player 객체 대신 ActorNumber(int) 전송
        int actorNumber = pv.Owner.ActorNumber;
        gmPV.RPC(nameof(GameManager.RPC_PlayerEliminated), RpcTarget.All, actorNumber);

    }
    public void ApplyGoldenStatue(float duration, bool refillHP)
    {
        if (goldenCo != null) StopCoroutine(goldenCo);
        goldenCo = StartCoroutine(Co_Golden(duration, refillHP));
    }

    private IEnumerator Co_Golden(float duration, bool refillHP)
    {
        IsGoldenStatue = true;
        isImmobilized = true;
        if (refillHP) CurHp = MaxHp;

        float t = 0f;
        while (t < duration)
        {
            if (CurHp <= 0)
            {
                Die();
                yield break;
            }
            t += Time.deltaTime;
            yield return null;
        }

        IsGoldenStatue = false;
        isImmobilized = false;
    }

    public void SetInvincible(float second)
    {
        invincibleUntil = Time.time + second; // 예: 2초간 무적
    }

    protected IEnumerator ImmobilizeCharacter()
    {
        isImmobilized = true;
        yield return new WaitForSeconds(immobilizedTime);
        isImmobilized = false;
    }

    // 각 캐릭터별로 다르게 구현할 추상 함수들
    public abstract void Attack();
    public abstract void Guard();
}
