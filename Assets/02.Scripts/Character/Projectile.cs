using UnityEngine;
using Photon.Pun;

/// <summary>
/// 발사체(카드)의 움직임, 수명, 그리고 충돌을 관리하는 스크립트.
/// </summary>
public class Projectile : MonoBehaviourPun
{
    private Vector3 startPosition;
    private Vector2 direction;
    private float speed;
    private float maxDistance;
    public float damage = 3f; // 발사체의 공격력

    /// <summary>
    /// 발사체를 초기화하는 함수.
    /// </summary>
    /// <param name="initialPosition">발사 위치</param>
    /// <param name="initialDirection">발사 방향</param>
    /// <param name="initialSpeed">속도</param>
    /// <param name="distance">사거리</param>
    public void Initialize(Vector3 initialPosition, Vector2 initialDirection, float initialSpeed, float distance)
    {
        startPosition = initialPosition;
        direction = initialDirection;
        speed = initialSpeed;
        maxDistance = distance;
    }

    void Update()
    {
        // 발사체 이동
        transform.Translate(direction * speed * Time.deltaTime);

        // 사거리 체크 및 제거
        if (Vector3.Distance(startPosition, transform.position) >= maxDistance)
        {
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(gameObject);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (photonView.IsMine && other.CompareTag("Player"))
        {
            PhotonView targetView = other.GetComponent<PhotonView>();
            if (targetView != null)
            {
                // 데미지 전달
                targetView.RPC("RPC_TakeDamage", RpcTarget.AllBuffered, damage);

                // 킬러/희생자 추적
                //var victim = targetView.Owner;
                //var killer = photonView.Owner;

                //if (victim != null && killer != null && killer != victim)
                //{
                //    RoundFlowManager.Instance?.RegisterKill(killer.ActorNumber);
                //}
            }
            PhotonNetwork.Destroy(gameObject);
        }
    }

}
