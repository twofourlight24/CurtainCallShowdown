using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

/// <summary>
/// 모든 플레이어 캐릭터의 기본이 되는 추상 클래스.
/// 공통 속성, 물리 기반 이동, 네트워크 동기화를 담당합니다.
/// 2D 게임 환경에 맞게 Rigidbody2D와 좌우 반전(플립) 기능을 포함합니다.
/// </summary>
public abstract class CharacterBase : MonoBehaviourPun, IPunObservable
{
    // 캐릭터의 공통적인 속성
    public float MaxHp = 100f;
    public float CurHp = 100f;
    public float moveSpeed = 5f;
    public int LifeCount = 3;

    private Rigidbody2D rb;
    private Vector3 moveDirection;

    // 네트워크 동기화를 위한 변수
    private Vector3 _networkPosition;
    private bool _networkIsFacingRight;
    private bool isFacingRight = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("CharacterBase: Rigidbody2D 컴포넌트가 필요합니다!");
        }

        // 동기화 변수 초기화
        _networkPosition = transform.position;
    }

    void FixedUpdate()
    {
        // 핵심: 로컬 플레이어만 물리 연산을 수행하도록 보장
        if (photonView.IsMine && rb != null)
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

    // 데미지를 받는 함수
    public void TakeDamage(float damage)
    {
        if (photonView.IsMine)
        {
            CurHp -= damage;
            if (CurHp <= 0)
            {
                // 목숨 차감 및 리스폰 로직
            }
        }
    }

    // 각 캐릭터별로 다르게 구현할 추상 함수들
    public abstract void Attack();
    public abstract void Guard();
    public abstract void UseSkill();
}
