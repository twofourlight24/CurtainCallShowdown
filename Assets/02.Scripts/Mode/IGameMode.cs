using System.Collections.Generic;

public interface IGameMode
{
    string ModeName { get; }

    void Initialize(GameManager gm);
    void StartRound();
    void EndRound();
    void OnPlayerEliminated(Photon.Realtime.Player player);
    void OnRoundComplete(List<Photon.Realtime.Player> ranking);
}