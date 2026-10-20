using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool CaoYingDamageKind(CardKind kind) => IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault;
    private CaoYingGuessResolvedEvent[] ArmedCaoYingDamage(int source, int target) => CompleteProgramEventHistory().OfType<CaoYingGuessResolvedEvent>().Where(e => e.TurnNumber == _turnNumber && e.TurnOwner == _currentSeat && e.Source.OwnerSeat == source && e.TargetSeat == target && e.CorrectCount >= 1 && !CompleteProgramEventHistory().OfType<CaoYingDamageConsumedEvent>().Any(c => c.ProgramFrameId == e.ProgramFrameId)).ToArray();
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 80)]
    private int CaoYingDamage(IDamageAttempt attack) => attack.IsSourceLess ? 0 : ArmedCaoYingDamage(attack.SourceSeat, attack.TargetSeat).Length;
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 410)]
    private void ObserveCaoYingDamage(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent { Amount: > 0, SourceLess: false } damage) return;
        var armedFacts = ArmedCaoYingDamage(damage.SourceSeat, damage.TargetSeat);
        if (armedFacts.Length == 0) return;
        var frame = _resolutionStack.OfType<DamageFrame>().LastOrDefault(d => d.SourceSeat == damage.SourceSeat && d.TargetSeat == damage.TargetSeat && d.Amount == damage.Amount);
        if (frame is null) throw new InvalidOperationException("Lingren consumption requires its actual applied damage frame.");
        foreach (var armed in armedFacts) AdvanceEventRulesAndQueueFact(new CaoYingDamageConsumedEvent(armed.ProgramFrameId, frame.Id));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 420)]
    private void ObserveCaoYingExpiry(IGameEvent payload)
    {
        foreach (var player in _players)
        foreach (var grant in player.SkillGrants.Grants.Where(g => g.SourceId.StartsWith("acquired:cao-ying:", StringComparison.Ordinal)).ToArray())
        {
            var parts = grant.SourceId["acquired:cao-ying:".Length..].Split(':');
            // Instance IDs can contain colons; the fixed numeric suffix owns the actual grant lifetime.
            if (parts.Length < 4 || !int.TryParse(parts[^2], out var turn)) throw new InvalidOperationException("Cao Ying grant has no frozen issuing turn.");
            if (payload is TurnStartedEvent start && start.ActorSeat == player.Seat && start.TurnNumber > turn || !player.IsAlive)
            { player.SkillGrants.RemoveGrant(grant.GrantId); AdvanceEventRulesAndQueueFact(new CaoYingGrantExpiredEvent(player.Seat, grant.GrantId)); }
        }
    }
}
