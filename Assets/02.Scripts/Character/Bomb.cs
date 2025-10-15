using System.Collections;
using Photon.Pun;
using UnityEngine;

public class Bomb : MonoBehaviourPun
{
    [Header("Damage / Lifetime")]
    public float damage = 15f;
    public float fuseSeconds = 3f;

    [Header("Colliders")]
    public Collider2D bodyCollider;        // 기본 콜라이더(투사체 몸체, Trigger 권장)
    public Collider2D explosionCollider;   // 폭발 콜라이더(Trigger) – 기본 비활성

    [Header("FX")]
   // public GameObject explodeVfx;          // 선택

    private bool exploded = false;
    private readonly System.Collections.Generic.HashSet<int> hitActors = new();

    void OnEnable()
    {
        if (explosionCollider) explosionCollider.enabled = false;

        // 퓨즈는 "오너"만 돈다
        if (photonView.IsMine) StartCoroutine(Fuse());
    }

    private IEnumerator Fuse()
    {
        yield return new WaitForSeconds(fuseSeconds);
        TryExplodeOwnerOnly();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (exploded) return;
        TryExplodeOwnerOnly(); // 닿으면 즉시 폭발 요청
    }

    // 폭발은 오너가 RPC로 전파
    private void TryExplodeOwnerOnly()
    {
        if (!photonView.IsMine) return;
        if (exploded) return;
        photonView.RPC(nameof(RPC_Explode), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_Explode()
    {
        if (exploded) return;
        exploded = true;

      //  if (explodeVfx) Instantiate(explodeVfx, transform.position, Quaternion.identity);
        if (bodyCollider) bodyCollider.enabled = false;

        StartCoroutine(ExplosionPulse());

        // 파괴는 오너만 수행
        if (photonView.IsMine) StartCoroutine(DestroySoon());
    }

    private IEnumerator ExplosionPulse()
    {
        if (!explosionCollider) yield break;
        explosionCollider.enabled = true;
        yield return new WaitForSeconds(0.05f);
        explosionCollider.enabled = false;
    }

    private IEnumerator DestroySoon()
    {
        yield return new WaitForSeconds(0.1f);
        PhotonNetwork.Destroy(gameObject); // 호출 주체는 오너뿐
    }

    // 폭발 콜라이더가 닿은 플레이어에게 데미지
    private void OnCollisionEnter2D(Collision2D _) { /* 사용 안함 */ }
    private void OnTriggerStay2D(Collider2D other)
    {
        if (!exploded) return;
        if (!photonView.IsMine) return;
        var target = other.GetComponentInParent<CharacterBase>();
        if (!target) return;

        var tpv = target.photonView;
        if (tpv == null || tpv.Owner == photonView.Owner) return;

        int aid = tpv.Owner != null ? tpv.Owner.ActorNumber : 0;
        if (hitActors.Contains(aid)) return;
        hitActors.Add(aid);

        tpv.RPC("RPC_TakeDamage", RpcTarget.All, damage);
    }
}
