using System.Collections;
using Photon.Pun;
using UnityEngine;

public class BreakerCharacter : CharacterBase
{
    [Header("Attack - Common")]
    public Collider2D attackCollider;
    public float rehitCooldown = 0.15f;

    [Header("Attack - Basic")]
    public float basicStiffTime = 0.2f;
    public float basicAttackTime = 0.18f;
    public float basicDamage = 6f;

    [Header("Attack - Hold (tiers)")]
    public float tier1HoldSec = 1.0f, tier2HoldSec = 2.0f, tier3HoldSec = 3.0f;
    public float tier1Damage = 10f, tier2Damage = 15f, tier3Damage = 20f;
    public float tier1ActiveTime = 0.25f, tier2ActiveTime = 0.4f, tier3ActiveTime = 0.6f;
    public Vector2 tier1ColliderOffset = new(0.9f, 0f), tier2ColliderOffset = new(1.3f, 0f), tier3ColliderOffset = new(1.8f, 0f);
    public Vector2 tier1ColliderScale = new(1f, 1f), tier2ColliderScale = new(1.4f, 1.1f), tier3ColliderScale = new(1.8f, 1.2f);

    [Header("Attack - Dash (While Running + Hold J)")]
    public float dashSpeedMultiplier = 1.5f;
    public float dashTickDamage = 18f;
    public float dashHitInterval = 0.18f;
    public float dashMaxDuration = 3.5f;

    [Header("Guard (Magician-like)")]
    public float shieldScaleDecrease = 0.3f;
    public float minShieldScale = 0.1f;
    public float shieldRegenTime = 2.0f;

    // 내부 상태
    private bool isGuarding, isAttacking, isCharging, isDashing;
    private float currentDamage, lastDashHitTime;
    private Rigidbody2D _rb;
    private Vector3 initialShieldScale;
    private readonly System.Collections.Generic.Dictionary<int, float> _lastHitAt = new();

    new void Start()
    {
        base.Start(); // CharacterBase 초기화
        _rb = GetComponent<Rigidbody2D>();
        if (ShieldObject) initialShieldScale = ShieldObject.transform.localScale;
        if (attackCollider) attackCollider.enabled = false;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // --- Guard (K) : 마술사 방식과 동일 ---
        if (Input.GetKey(KeyCode.K))
        {
            if (!isGuarding)
            {
                isGuarding = true;
                if (ShieldObject) ShieldObject.SetActive(true);
            }
            GuardTick();
        }
        else if (isGuarding)
        {
            isGuarding = false;
            if (ShieldObject) ShieldObject.SetActive(false);
        }

        // 대쉬 유지(키홀드 중 이동/타격은 PlayerInput에서 시작/종료, 유지 틱은 여기서 처리)
        if (isDashing) DashTick();

        // 공격/차지 중에는 이동 차단
        if (isAttacking || isCharging) FreezeFrame();
    }

    // ====== PlayerInput에서 직접 호출할 공개 API ======

    /// <summary>기본공격(탭) – 0.2초 경직 + 짧은 콜라이더 on</summary>
    public override void Attack()
    {
        if (!isAttacking && !isCharging && !isDashing)
            StartCoroutine(BasicAttack());
    }
    public void HoldAttackTier1() { HoldAttackTier(1); }
    public void HoldAttackTier2() { HoldAttackTier(2); }
    public void HoldAttackTier3() { HoldAttackTier(3); }
    /// <summary>홀드 공격 – 티어(1/2/3)에 따라 범위/데미지/시간 다름</summary>
    public void HoldAttackTier(int tier)
    {
        if (!photonView.IsMine) return;
        if (isAttacking || isDashing) return;

        // PlayerInput이 KeyUp에서 티어를 판정해 호출
        StartCoroutine(HoldAttackTierRoutine(Mathf.Clamp(tier, 1, 3)));
    }

    /// <summary>달리기+홀드 – 시작(키다운)</summary>
    public void StartDash()
    {
        if (!photonView.IsMine) return;
        if (isAttacking || isDashing) return;

        isCharging = false; // 대시가 우선
        isDashing = true;
        lastDashHitTime = -999f;

        ForceDashMove();
        if (attackCollider) attackCollider.enabled = true; // 접촉 판정 on
        // (대시 최대 시간 제한을 두고 싶다면 코루틴으로 감시 가능)
        StartCoroutine(DashTimeoutGuard());
    }

    /// <summary>달리기+홀드 – 종료(키업)</summary>
    public void StopDash()
    {
        isDashing = false;
        if (_rb) _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        if (attackCollider) attackCollider.enabled = false;
    }

    public override void Guard() { /* 유지 로직은 Update에서 처리 */ }

    // ====== 내부 구현 ======

    private IEnumerator BasicAttack()
    {
        isAttacking = true;
        yield return FreezeSeconds(basicStiffTime);

        currentDamage = basicDamage;
        using (new Scope(this, attackCollider, currentDamage))
            yield return new WaitForSeconds(basicAttackTime);

        isAttacking = false;
    }

    private IEnumerator HoldAttackTierRoutine(int tier)
    {
        isAttacking = true;

        float active; Vector2 off, scl;
        switch (tier)
        {
            case 3: currentDamage = tier3Damage; active = tier3ActiveTime; off = tier3ColliderOffset; scl = tier3ColliderScale; break;
            case 2: currentDamage = tier2Damage; active = tier2ActiveTime; off = tier2ColliderOffset; scl = tier2ColliderScale; break;
            default: currentDamage = tier1Damage; active = tier1ActiveTime; off = tier1ColliderOffset; scl = tier1ColliderScale; break;
        }

        var freeze = StartCoroutine(FreezeSeconds(active));
        using (new Scope(this, attackCollider, currentDamage, off, scl))
            yield return new WaitForSeconds(active);
        if (freeze != null) yield return freeze;

        isAttacking = false;
    }

    private IEnumerator DashTimeoutGuard()
    {
        float t = 0f;
        while (isDashing && t < dashMaxDuration)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (isDashing) StopDash();
    }

    private void DashTick()
    {
        // 강제 이동 유지
        ForceDashMove();

        // 접촉 데미지 간격 관리(충돌 이벤트에서 실제 데미지 처리)
        if (Time.time - lastDashHitTime > dashHitInterval)
            lastDashHitTime = Time.time;
    }

    private void ForceDashMove()
    {
        if (_rb == null) return;
        float sign = isFacingRight ? 1f : -1f;
        float v = runSpeed * dashSpeedMultiplier;
        _rb.linearVelocity = new Vector2(sign * v, _rb.linearVelocity.y);
    }

    private IEnumerator FreezeSeconds(float sec)
    {
        float originalSpeed = runSpeed; // 원래 이동속도 저장
        runSpeed = 0f; // 움직임 차단

        yield return new WaitForSeconds(sec);

        runSpeed = originalSpeed;
    }

    private void FreezeFrame()
    {
        if (_rb) _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
         // CharacterBase 이동 차단
    }

    // === Guard shrinking (마술사와 동일 패턴) ===
    private void GuardTick()
    {
        if (!ShieldObject) return;
        var s = ShieldObject.transform.localScale
              - new Vector3(shieldScaleDecrease, shieldScaleDecrease, 0f) * Time.deltaTime;
        ShieldObject.transform.localScale = s;
        if (s.x <= minShieldScale)
        {
            ShieldObject.SetActive(false);
            StartCoroutine(ImmobilizeCharacter());
            StartCoroutine(RegenShield());
            isGuarding = false;
        }
    }

    private IEnumerator RegenShield()
    {
        yield return new WaitForSeconds(shieldRegenTime);
        if (ShieldObject) ShieldObject.transform.localScale = initialShieldScale;
    }

    // === Attack hit ===
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!photonView.IsMine) return;
        if (attackCollider == null || !attackCollider.enabled) return;
        if (other == null || !other.CompareTag("Player")) return;

        var target = other.GetComponent<CharacterBase>(); if (target == null) return;
        var tpv = target.photonView; if (tpv == null || tpv.Owner == photonView.Owner) return;

        // 재히트 쿨타임
        int aid = tpv.Owner != null ? tpv.Owner.ActorNumber : 0;
        if (_lastHitAt.TryGetValue(aid, out float t) && Time.time - t < rehitCooldown) return;
        _lastHitAt[aid] = Time.time;

        tpv.RPC("RPC_TakeDamage", RpcTarget.All, isDashing ? dashTickDamage : currentDamage);
    }

    // 공격 콜라이더 범위/오프셋 수명 관리용
    private readonly struct Scope : System.IDisposable
    {
        private readonly Collider2D col;
        private readonly Vector3? pos;
        private readonly Vector3? scale;
        private readonly BreakerCharacter owner;

        public Scope(BreakerCharacter o, Collider2D c, float dmg, Vector2? offset = null, Vector2? scl = null)
        {
            owner = o; col = c; owner.currentDamage = dmg;
            if (c)
            {
                pos = c.transform.localPosition; scale = c.transform.localScale;
                if (offset.HasValue)
                {
                    float dir = o.isFacingRight ? 1f : -1f;
                    c.transform.localPosition = new Vector3(offset.Value.x * dir, offset.Value.y, c.transform.localPosition.z);
                }
                if (scl.HasValue)
                {
                    c.transform.localScale = new Vector3(scl.Value.x, scl.Value.y, c.transform.localScale.z);
                }
                c.enabled = true;
            }
            else { pos = null; scale = null; }
        }

        public void Dispose()
        {
            if (col)
            {
                if (pos.HasValue) col.transform.localPosition = pos.Value;
                if (scale.HasValue) col.transform.localScale = scale.Value;
                col.enabled = false;
            }
        }
    }
}
