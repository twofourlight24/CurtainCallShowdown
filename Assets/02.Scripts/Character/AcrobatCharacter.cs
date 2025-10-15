using System.Collections;
using Photon.Pun;
using UnityEngine;

public class AcrobatCharacter : CharacterBase
{
    [Header("Melee (기본공격)")]
    public Collider2D attackCollider;          // Trigger
    public float basicStiffTime = 0.2f;
    public float basicActiveTime = 0.18f;
    public float basicDamage = 15f;
    public float rehitCooldown = 0.15f;

    [Header("Bomb (홀드공격/던지기)")]
    public GameObject bombPrefab;              // Bomb.cs가 붙은 프리팹 (아래)
    public Transform throwOrigin;
    public Vector2 throwVelocity = new(8f, 10f); // 포물선 초기 속도

    [Header("Run-Attack (달리면서 공격)")]
    public int trailBombCount = 3;
    public float trailInterval = 0.18f;
    public float behindDropSpeedX = 5f;        // 뒤로 던질 X속도(좌/우 반전)

    [Header("Guard (Magician-like)")]
    public float shieldScaleDecrease = 0.3f;
    public float minShieldScale = 0.1f;
    public float shieldRegenTime = 2.0f;

    private float currentDamage;
    private bool isGuarding;
    private readonly System.Collections.Generic.Dictionary<int, float> lastHitAt = new();
    private Vector3 shieldInitScale;

    new void Start()
    {
        base.Start();
        rb = GetComponent<Rigidbody2D>();
        if (ShieldObject) { shieldInitScale = ShieldObject.transform.localScale; ShieldObject.SetActive(false); }
        if (attackCollider) attackCollider.enabled = false;
    }

    void Update()
    {
        if (!photonView.IsMine) return;

        // Guard 유지
        if (Input.GetKey(KeyCode.K))
        {
            if (!isGuarding) { isGuarding = true; if (ShieldObject) ShieldObject.SetActive(true); }
            GuardTick();
        }
        else if (isGuarding)
        {
            isGuarding = false;
            if (ShieldObject) ShieldObject.SetActive(false);
        }
    }

    public override void Attack()
    {
        if (IsRunning)
        {
            // 달리기 상태에서 공격 → 뒤로 폭탄 3개 드랍
            StartCoroutine(Co_DropTrailBombs());
        }
        else
        {
            // 근접 기본공격
            StartCoroutine(Co_BasicMelee());
        }
    }

    // 커맨드셋: Hold → 이 메서드에 매핑(무인자)
    public void ThrowBomb()
    {
        if (!photonView.IsMine || bombPrefab == null) return;
        var pos = throwOrigin ? throwOrigin.position : transform.position;
        var go = PhotonNetwork.Instantiate(bombPrefab.name, pos, Quaternion.identity);
        var rb2 = go.GetComponent<Rigidbody2D>();
        if (rb2)
        {
            float dir = isFacingRight ? 1f : -1f;
            rb2.linearVelocity = new Vector2(throwVelocity.x * dir, throwVelocity.y);
        }
    }

    public override void Guard() { /* Update에서 유지 */ }

    // ===== 구현 =====
    private IEnumerator Co_BasicMelee()
    {
        // 경직(이동 불가)
        float end = Time.time + basicStiffTime;
        while (Time.time < end)
        {
            if (rb) rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
            moveSpeed = 0f;
            yield return null;
        }

        currentDamage = basicDamage;
        if (attackCollider) attackCollider.enabled = true;
        yield return new WaitForSeconds(basicActiveTime);
        if (attackCollider) attackCollider.enabled = false;
    }

    private IEnumerator Co_DropTrailBombs()
    {
        for (int i = 0; i < trailBombCount; i++)
        {
            if (!photonView.IsMine || bombPrefab == null) yield break;

            var pos = throwOrigin ? throwOrigin.position : transform.position;
            var go = PhotonNetwork.Instantiate(bombPrefab.name, pos, Quaternion.identity);
            var rb2 = go.GetComponent<Rigidbody2D>();
            if (rb2)
            {
                float dir = isFacingRight ? -1f : 1f; // 뒤쪽으로 던짐
                rb2.linearVelocity = new Vector2(behindDropSpeedX * dir, 0.5f * throwVelocity.y);
            }
            yield return new WaitForSeconds(trailInterval);
        }
    }

    private void GuardTick()
    {
        if (!ShieldObject) return;
        var s = ShieldObject.transform.localScale - new Vector3(shieldScaleDecrease, shieldScaleDecrease, 0f) * Time.deltaTime;
        ShieldObject.transform.localScale = s;
        if (s.x <= minShieldScale)
        {
            ShieldObject.SetActive(false);
            isGuarding = false;
            StartCoroutine(ImmobilizeCharacter());
            StartCoroutine(RegenShield());
        }
    }
    private IEnumerator RegenShield()
    {
        yield return new WaitForSeconds(shieldRegenTime);
        if (ShieldObject) ShieldObject.transform.localScale = shieldInitScale;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryHit(other);
    private void OnTriggerStay2D(Collider2D other) => TryHit(other);

    private void TryHit(Collider2D other)
    {
        if (!photonView.IsMine) return;
        if (attackCollider == null || !attackCollider.enabled) return;
        if (other == null) return;

        var target = other.GetComponentInParent<CharacterBase>();
        if (!target) return;

        var tpv = target.photonView;
        if (tpv == null || tpv.Owner == photonView.Owner) return;

        int aid = tpv.Owner != null ? tpv.Owner.ActorNumber : 0;
        if (lastHitAt.TryGetValue(aid, out float t) && Time.time - t < rehitCooldown) return;
        lastHitAt[aid] = Time.time;

        tpv.RPC("RPC_TakeDamage", RpcTarget.All, basicDamage);
    }
}
