using System.Collections;
using Photon.Pun;
using UnityEngine;

public class DancerCharacter : CharacterBase
{
    [Header("Attack - Common")]
    public Collider2D attackCollider;       // 공격 판정용(Trigger)
    public float rehitCooldown = 0.15f;     // 같은 대상 재히트 최소 간격

    [Header("Attack - Basic")]
    public float basicStiffTime = 0.2f;     // 기본공격 경직(이동 불가)
    public float basicAttackTime = 0.18f;   // 콜라이더 on 유지시간
    public float basicDamage = 15f;         // 기본공격 피해

    [Header("Run-Hold (While Running + Hold J)")]
    public float runHoldTickDamage = 12f;   // 홀드 유지 중 접촉 피해
    public float runHoldHitInterval = 0.15f;// 접촉 피해 간격
    public float runHoldMaxDuration = 6f;   // 안전 차단(과도한 유지 방지, 선택)

    [Header("Guard (Magician-like)")]
    public float shieldScaleDecrease = 0.3f;
    public float minShieldScale = 0.1f;
    public float shieldRegenTime = 2.0f;

    // 내부 상태
    private bool isGuarding;
    private bool isAttacking;        // 기본공격 시전 중
    private bool isRunHolding;       // 달리기+J 홀드 활성
    private float currentDamage;     // 공격 콜라이더가 줄 피해
    private float lastTickTime;      // Run-Hold 틱 쿨다운
    private Rigidbody2D _rb;
    private Vector3 initialShieldScale;
    private readonly System.Collections.Generic.Dictionary<int, float> _lastHitAt = new();

    new void Start()
    {
        base.Start();
        _rb = GetComponent<Rigidbody2D>();
        if (ShieldObject) initialShieldScale = ShieldObject.transform.localScale;
        if (attackCollider) attackCollider.enabled = false;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // --- Guard (K) : 마술사 방식 동일 ---
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

        // Run-Hold 유지 틱: 이동은 '달리기 상태' 그대로 허용(조련사와 다르게 이동을 막지 않음)
        if (isRunHolding)
            RunHoldTick();
    }

    // ===== PlayerInput이 직접 호출할 공개 API =====
    public override void Attack()
    {
        if (!isAttacking && !isRunHolding)
            StartCoroutine(BasicAttack());
        Debug.Log("댄서 캐릭터의 기본 공격!");
    }

    // WhileRunningHold 시작(키다운 시 PlayerInput이 호출)
    public void StartDash()
    {
        if (!photonView.IsMine) return;
        if (isAttacking || isRunHolding) return;

        isRunHolding = true;
        lastTickTime = -999f;
        if (attackCollider) attackCollider.enabled = true; // 접촉 판정 on
        StartCoroutine(RunHoldTimeoutGuard());
        Debug.Log("댄서 캐릭터의 달리기+홀드 공격 시작!");
    }

    // WhileRunningHold 종료(키업 시 PlayerInput이 StopDash() 호출)
    public void StopDash()
    {
        isRunHolding = false;
        if (attackCollider) attackCollider.enabled = false;
        Debug.Log("댄서 캐릭터의 달리기+홀드 공격 종료!");
    }

    public override void Guard() { /* 유지 로직은 Update에서 처리 */ }

    // ===== 내부 구현 =====
    private IEnumerator BasicAttack()
    {
        isAttacking = true;
        // 기본공격 동안 이동 불가(경직)
        yield return FreezeSeconds(basicStiffTime);

        currentDamage = basicDamage;
        using (new Scope(this, attackCollider, currentDamage))
            yield return new WaitForSeconds(basicAttackTime);

        isAttacking = false;
        Debug.Log("댄서 캐릭터의 기본 공격 종료!");
    }

    private void RunHoldTick()
    {
        // 달리기 입력/속도는 PlayerInput/CharacterBase가 그대로 유지
        // 접촉 틱 쿨타임만 관리(실제 데미지는 OnTrigger에서 처리)
        if (Time.time - lastTickTime > runHoldHitInterval)
            lastTickTime = Time.time;
    }

    private IEnumerator RunHoldTimeoutGuard()
    {
        float t = 0f;
        while (isRunHolding && t < runHoldMaxDuration)
        {
            t += Time.deltaTime;
            yield return null;
        }
        if (isRunHolding) StopDash();
    }

    private IEnumerator FreezeSeconds(float sec)
    {
        float end = Time.time + sec;
        while (Time.time < end)
        {
            // 이동 차단
            if (_rb) _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            moveSpeed = 0f;
            yield return null;
        }
    }

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

    // === 접촉 데미지 ===
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }
    private void OnTriggerStay2D(Collider2D other)
    {
        TryHit(other);
    }

    private void TryHit(Collider2D other)
    {
        if (!photonView.IsMine) return;
        if (attackCollider == null || !attackCollider.enabled) return;

        var target = other.GetComponentInParent<CharacterBase>();
        var tpv = target.photonView; if (tpv == null || tpv.Owner == photonView.Owner) return;

        // 재히트 쿨타임
        int aid = tpv.Owner != null ? tpv.Owner.ActorNumber : 0;
        if (_lastHitAt.TryGetValue(aid, out float last) && Time.time - last < rehitCooldown) return;
        _lastHitAt[aid] = Time.time;

        float dmg = isRunHolding ? runHoldTickDamage : currentDamage;
        tpv.RPC("RPC_TakeDamage", RpcTarget.All, dmg);
    }

    // 공격 콜라이더 on/off 범위 관리
    private readonly struct Scope : System.IDisposable
    {
        private readonly Collider2D col;
        public Scope(DancerCharacter o, Collider2D c, float dmg)
        {
            if (c) { c.enabled = true; }
            col = c;
        }
        public void Dispose()
        {
            if (col) col.enabled = false;
        }
    }
}
