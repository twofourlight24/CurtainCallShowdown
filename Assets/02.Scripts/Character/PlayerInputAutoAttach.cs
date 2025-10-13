// 새 파일: PlayerInputAutoAttach.cs
using Photon.Pun;
using UnityEngine;

/// <summary>
/// 로컬 소유 캐릭터에만 PlayerInput을 정확히 1개 부착/활성화하고,
/// 원격 캐릭터에는 비활성화합니다.
/// 프리팹에 붙여두거나, 스폰 직후 AddComponent로 붙여도 됩니다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerInputAutoAttach : MonoBehaviour
{

    void OnEnable() => AttachOnce();

    public void AttachOnce()
    {
        var pv = GetComponent<PhotonView>();
        var cb = GetComponent<CharacterBase>();

        // 중복 제거: 하나만 남김
        var inputs = GetComponents<PlayerInput>();
        for (int i = 1; i < inputs.Length; i++)
            Destroy(inputs[i]);

        var input = (inputs.Length > 0) ? inputs[0] : null;

        if (pv.IsMine)
        {
            if (input == null) input = gameObject.AddComponent<PlayerInput>();
            input.enabled = true;
            // ★ 명시 주입
            input.SetCharacter(cb);
        }
        else
        {
            if (input != null) input.enabled = false;
        }
    }
}
