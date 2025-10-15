// AnimatorRpcHub.cs
using Photon.Pun;
using UnityEngine;

public class AnimatorRpcHub : MonoBehaviourPun
{
    [SerializeField] Animator targetAnimator;

    void Awake()
    {
        if (!targetAnimator) targetAnimator = GetComponentInChildren<Animator>(true);
    }

    public void SendTrigger(string name)
    {
        targetAnimator?.SetTrigger(name);                  // 로컬 재생
        if (photonView.IsMine)
            photonView.RPC(nameof(RPC_AnimTrigger), RpcTarget.Others, name);
    }

    [PunRPC]
    void RPC_AnimTrigger(string name)
    {           // ★ 같은 GO에 반드시 존재
        if (!targetAnimator) targetAnimator = GetComponentInChildren<Animator>(true);
        targetAnimator?.SetTrigger(name);                  // 원격 재생
    }
    public void SendBool(string name, bool v)
    {
        targetAnimator?.SetBool(name, v);
        if (photonView.IsMine) photonView.RPC(nameof(RPC_AnimSetBool), RpcTarget.Others, name, v);
    }
    [PunRPC] void RPC_AnimSetBool(string name, bool v) { targetAnimator?.SetBool(name, v); }

}
