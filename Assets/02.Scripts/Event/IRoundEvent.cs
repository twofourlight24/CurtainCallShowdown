public interface IRoundEvent
{
    string Id { get; }                    // "Spotlight", "GoldenAward" 등
    string DisplayName { get; }           // UI 노출 이름
    void EnableEvent(RoundEventContext ctx);
    void DisableEvent(RoundEventContext ctx);
}