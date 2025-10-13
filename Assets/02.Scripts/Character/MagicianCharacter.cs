using UnityEngine;
using Photon.Pun;
using System.Collections;

public class MagicianCharacter : CharacterBase
{
    [Header("Magician Attack Properties")]
    public Transform ShootPos; // 카드를 발사할 위치
    public GameObject cardPrefab; // 발사할 카드 프리팹
    public float cardSpeed = 20f; // 카드의 속도
    public float attackRange = 10f; // 카드의 사거리

    [Header("Magician Guard Properties")]
    public float shieldScaleDecrease = 0.3f; // 가드 시 쉴드 크기 감소량
    public float minShieldScale = 0.1f; // 쉴드가 파괴되는 최소 크기
    public float shieldRegenTime = 2.0f; // 쉴드 재생성 시간
    private Vector3 initialShieldScale;

    private bool isGuarding = false;

    new void Start()
    {
        // CharacterBase의 Start() 함수를 먼저 호출
        base.Start();

        // 쉴드 오브젝트의 초기 크기 저장
        if (ShieldObject != null)
        {
            initialShieldScale = ShieldObject.transform.localScale;
        }
    }

    void Update()
    {
        // 로컬 플레이어만 입력을 받습니다.
        if (photonView.IsMine)
        {
            // 가드 (K 키) - 키를 누르는 동안만 작동
            if (Input.GetKey(KeyCode.K))
            {
                if (!isGuarding)
                {
                    isGuarding = true;
                    // 가드 시작 로직
                    StartGuard();
                }
                UpdateGuard();
            }
            else if (isGuarding)
            {
                // 가드 종료 로직
                isGuarding = false;
                EndGuard();
            }
        }
    }

    private void StartGuard()
    {
        Debug.Log("마술사 캐릭터의 가드 시작!");
        if (ShieldObject != null)
        {
            // 가드 시작 시 쉴드 활성화
            ShieldObject.SetActive(true);
        }
    }

    private void UpdateGuard()
    {
        if (ShieldObject != null)
        {
            // 쉴드 크기 감소
            Vector3 newScale = ShieldObject.transform.localScale - new Vector3(shieldScaleDecrease, shieldScaleDecrease, 0) * Time.deltaTime;
            ShieldObject.transform.localScale = newScale;

            // 쉴드 파괴 조건
            if (newScale.x <= minShieldScale)
            {
                BreakShield();
            }
        }
    }

    private void EndGuard()
    {
        Debug.Log("마술사 캐릭터의 가드 종료!");
        if (ShieldObject != null)
        {
            ShieldObject.SetActive(false);
        }
    }

    private void BreakShield()
    {
        Debug.Log("쉴드 파괴! 2초간 이동 불가!");
        // 쉴드 파괴 시 비활성화 및 이동 불가 상태로 전환
        ShieldObject.SetActive(false);
        isGuarding = false; // 가드 상태를 즉시 종료
        StartCoroutine(ImmobilizeCharacter());

        // 2초 뒤 쉴드 재생성 코루틴 시작
        StartCoroutine(RegenerateShield());
    }

    private IEnumerator RegenerateShield()
    {
        yield return new WaitForSeconds(shieldRegenTime);
        Debug.Log("쉴드 재생성!");
        if (ShieldObject != null)
        {
            ShieldObject.transform.localScale = initialShieldScale;
        }
    }

    /// <summary>
    /// 공격 행동을 재정의합니다.
    /// ShootPos 위치에서 카드를 발사합니다.
    /// </summary>
    public override void Attack()
    {
        Debug.Log("마술사 캐릭터의 강한 공격!");
        if (cardPrefab != null && ShootPos != null)
        {
            // 네트워크로 발사체 생성
            GameObject card = PhotonNetwork.Instantiate(cardPrefab.name, ShootPos.position, ShootPos.rotation);

            // Projectile 스크립트에 필요한 정보 전달
            Projectile projectile = card.GetComponent<Projectile>();
            if (projectile != null)
            {
                // 발사 방향은 캐릭터의 현재 방향을 따름
                float direction = isFacingRight ? 1f : -1f;
                projectile.Initialize(transform.position, new Vector2(direction, 0f), cardSpeed, attackRange);
            }
        }
    }


    public override void Guard()
    {
        // Update()에서 가드 로직을 처리하므로 이 함수는 비워둡니다.
    }

    public void HoldAttack()
    {
        Debug.Log("[Magician] HoldAttack 발동!");
        StartCoroutine(HoldAttackRoutine());
    }

    private IEnumerator HoldAttackRoutine()
    {
        yield return new WaitForSeconds(0.1f);
        float direction = isFacingRight ? 1f : -1f;

        for (int i = 0; i < 4; i++)
        {
            GameObject card = PhotonNetwork.Instantiate(cardPrefab.name, ShootPos.position, Quaternion.identity);
            Projectile proj = card.GetComponent<Projectile>();
            proj.Initialize(transform.position, new Vector2(direction, 0f), cardSpeed, attackRange);
            yield return new WaitForSeconds(0.1f); // 타다닥
        }
    }

    public void DashAttack()
    {
        if (!photonView.IsMine) return;
        Debug.Log("[Magician] DashAttack 돌진 시작!");
        StartCoroutine(DashAttackRoutine());
    }

    private IEnumerator DashAttackRoutine()
    {
        float dashDistance = 3f;
        float dashTime = 0.2f;
        float elapsed = 0f;

        Vector2 start = transform.position;
        Vector2 target = start + new Vector2(isFacingRight ? dashDistance : -dashDistance, 0);

        while (elapsed < dashTime)
        {
            transform.position = Vector2.Lerp(start, target, elapsed / dashTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
        // 관통 데미지 판정
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, isFacingRight ? Vector2.right : Vector2.left, dashDistance);
        foreach (var hit in hits)
        {
            if (hit.collider != null && hit.collider.CompareTag("Player"))
            {
                var enemytarget = hit.collider.GetComponent<CharacterBase>();
                if (enemytarget != null)
                {
                    enemytarget.photonView.RPC("RPC_TakeDamage", RpcTarget.All, 20f);
                }
            }
        }
    }
    public override void UseSkill()
    {
        Debug.Log("마술사 캐릭터의 스킬 사용!");
        // 마술사 고유의 스킬 로직 추가
    }

    /// <summary>
    /// 쉴드 콜라이더에 다른 오브젝트가 닿았을 때 호출됩니다.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 'Card' 태그를 가진 오브젝트에 맞았을 때
        if (other.CompareTag("Card"))
        {
            // 내 쉴드 오브젝트가 활성화되어 있는 상태에서만 카드 제거
            if (ShieldObject != null && ShieldObject.activeSelf)
            {
                Debug.Log("가드로 카드 발사체 방어!");
                // 발사체 파괴
                PhotonNetwork.Destroy(other.gameObject);
            }
        }
    }
}
