using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MagicianCharacter : CharacterBase
{
    [Header("Magician Attack Properties")]
    public Transform ShootPos; // 카드를 발사할 위치
    public GameObject cardPrefab; // 발사할 카드 프리팹
    public float cardSpeed = 20f; // 카드의 속도
    public float attackRange = 10f; // 카드의 사거리

    public float attackCooldown = 0.25f;   // 연타 방지 쿨타임
    bool attackBusy = false;
    float nextAttackTime = 0f;

    [Header("Magician Guard Properties")]
    public float shieldScaleDecrease = 0.3f; // 가드 시 쉴드 크기 감소량
    public float minShieldScale = 0.1f; // 쉴드가 파괴되는 최소 크기
    public float shieldRegenTime = 2.0f; // 쉴드 재생성 시간
    private Vector3 initialShieldScale;

    [Header("Dash Hit")]
    public Collider2D DashCollider;       // 트리거
    public float dashDamage = 12f;         // 대쉬 공격력
    public float dashRehitCooldown = 1f;   // 동일 대상 재타격 쿨타임
    private bool dashHitActive = false;
    private readonly Dictionary<int, float> _dashLastHit = new();

    private bool isGuarding = false;

    new void Start()
    {
        base.Start();
        if (ShieldObject != null)
            initialShieldScale = ShieldObject.transform.localScale;
    }

    void Update()
    {
        if (photonView.IsMine)
        {
            // 가드 (K 키)
            if (Input.GetKey(KeyCode.K))
            {
                if (!isGuarding)
                {
                    isGuarding = true;
                    StartGuard();
                }
                UpdateGuard();
            }
            else if (isGuarding)
            {
                isGuarding = false;
                EndGuard();
            }
        }
    }

    private void StartGuard()
    {
        if (ShieldObject != null)
        {
            ShieldObject.SetActive(true);
            SetGuarding(true);
        }
    }

    private void UpdateGuard()
    {
        if (ShieldObject != null)
        {
            Vector3 newScale = ShieldObject.transform.localScale - new Vector3(shieldScaleDecrease, shieldScaleDecrease, 0) * Time.deltaTime;
            ShieldObject.transform.localScale = newScale;

            if (newScale.x <= minShieldScale)
                BreakShield();
        }
    }

    private void EndGuard()
    {
        if (ShieldObject != null)
        {
            ShieldObject.SetActive(false);
            SetGuarding(false);
        }
    }

    private void BreakShield()
    {
        ShieldObject.SetActive(false);
        isGuarding = false;
        StartCoroutine(ImmobilizeCharacter());
        StartCoroutine(RegenerateShield());
    }

    private IEnumerator RegenerateShield()
    {
        yield return new WaitForSeconds(shieldRegenTime);
        if (ShieldObject != null)
            ShieldObject.transform.localScale = initialShieldScale;
    }

    /// <summary>기본 공격: 연타 방지(쿨타임+락)로 1회만 발사</summary>
    public override void Attack()
    {
        if (!photonView.IsMine) return;
        if (attackBusy) return;
        if (Time.time < nextAttackTime) return;

        StartCoroutine(Co_AttackOnce());
    }

    private IEnumerator Co_AttackOnce()
    {
        attackBusy = true;
        nextAttackTime = Time.time + attackCooldown;

        // 애니 트리거 전파
        AnimTrigger("Attack");

        // 발사 타이밍(애니 타이밍에 맞춰 약간 딜레이 — 필요시 조정)
        yield return new WaitForSeconds(0.06f);
        FireCardOnce();

        // 약간의 후딜(선택)
        yield return new WaitForSeconds(0.12f);

        attackBusy = false;
    }

    private void FireCardOnce()
    {
        if (!photonView.IsMine) return;
        if (cardPrefab == null || ShootPos == null) return;

        GameObject card = PhotonNetwork.Instantiate(cardPrefab.name, ShootPos.position, ShootPos.rotation);
        Projectile projectile = card.GetComponent<Projectile>();
        if (projectile != null)
        {
            float direction = isFacingRight ? 1f : -1f;
            projectile.Initialize(transform.position, new Vector2(direction, 0f), cardSpeed, attackRange);
        }
    }

    protected override void OnDamaged(float finalDamage)
    {
        AnimTrigger("Hit");
    }

    public override void Guard() { /* Update에서 처리 */ }

    public void HoldAttack()
    {
        StartCoroutine(HoldAttackRoutine());
    }

    private IEnumerator HoldAttackRoutine()
    {
        anim?.SetBool("IsHolding", true);
        yield return new WaitForSeconds(0.1f);

        float direction = isFacingRight ? 1f : -1f;
        AnimTrigger("Attack");
        anim?.SetBool("IsHolding", false);

        // 홀드 공격은 의도적으로 연사(타다닥)
        for (int i = 0; i < 4; i++)
        {
            GameObject card = PhotonNetwork.Instantiate(cardPrefab.name, ShootPos.position, Quaternion.identity);
            Projectile proj = card.GetComponent<Projectile>();
            if (proj != null)
                proj.Initialize(transform.position, new Vector2(direction, 0f), cardSpeed, attackRange);

            yield return new WaitForSeconds(0.1f);
        }
    }

    public void DashAttack()
    {
        if (!photonView.IsMine) return;
        AnimTrigger("Dash");
        StartCoroutine(DashAttackRoutine());
    }

    private IEnumerator DashAttackRoutine()
    {
        dashHitActive = true;
        if (DashCollider) DashCollider.enabled = true;

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

        dashHitActive = false;
        if (DashCollider) DashCollider.enabled = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        HandleDashHit(other);

        if (other.CompareTag("Card"))
        {
            if (ShieldObject != null && ShieldObject.activeSelf)
                PhotonNetwork.Destroy(other.gameObject);
        }
    }
    private void OnTriggerStay2D(Collider2D other) => HandleDashHit(other);

    private void HandleDashHit(Collider2D other)
    {
        if (!photonView.IsMine) return;
        if (!dashHitActive) return;
        if (DashCollider == null || !DashCollider.enabled) return;
        if (other == null) return;

        var target = other.GetComponentInParent<CharacterBase>();
        if (target == null) return;

        var tpv = target.photonView;
        if (tpv == null || tpv.Owner == photonView.Owner) return;

        int aid = tpv.Owner != null ? tpv.Owner.ActorNumber : 0;
        if (_dashLastHit.TryGetValue(aid, out var t) && Time.time - t < dashRehitCooldown) return;
        _dashLastHit[aid] = Time.time;

        tpv.RPC("RPC_TakeDamage", RpcTarget.All, dashDamage);
    }
}
