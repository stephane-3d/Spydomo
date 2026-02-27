namespace Spydomo.DTO
{
    public sealed record IntentResult(string Lang, IReadOnlyList<IntentHit> Intents);
}
