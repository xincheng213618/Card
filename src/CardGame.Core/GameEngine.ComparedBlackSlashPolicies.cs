namespace CardGame.Core;

// Scalar-only, target-owned conclusion. No material IDs or concealed Hand
// identity is added to public history or snapshot.
public sealed record ComparedBlackSlashReceipt(long CardUseFrameId, long ActionId, int ActualTurnNumber,
    int ActualTurnOwnerSeat, int ActorSeat, int ProviderSeat, int TargetSeat, CardKind EffectiveKind,
    Suit EffectiveSuit, bool EffectiveIsRed, int ActorHp, int TargetHp, int ActorHandCount, int TargetHandCount,
    CardConversionSource? TargetPolicySource, string? TargetPolicyHash, CardConversionSource? ActorPolicySource,
    string? ActorPolicyHash, bool Ineffective, bool CannotRespond);
public sealed record ComparedBlackSlashAppliedEvent(ComparedBlackSlashReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private bool HasComparedBlackSlashPolicies => _contentRegistry.Skills.Values.Any(s => s.Program?.CardPolicies.Any(p =>
        p.Kind is SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp or SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand) == true);
    private bool ApplyComparedBlackSlashPolicies(CardAttackHandle attack)
    {
        if (!HasComparedBlackSlashPolicies || attack.IsSourceLess || attack.IsChainPropagation ||
            attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            LifecycleCardUse(attack.ResolutionId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActionId < 1 || action.ActorSeat != use.SourceSeat || attack.CardUserSeat != use.SourceSeat ||
            action.EffectiveKind != kind || !use.TargetSeats.Contains(attack.TargetSeat) ||
            action.EffectiveIsRed != false ||
            (action.PhysicalCards.Count == 0 && action.EffectiveSuit is not (Suit.Spade or Suit.Club))) return false;
        var existing = GetCardAttackState(attack.ResolutionId).ComparedBlackSlash;
        if (existing is not null)
        {
            if (existing.CardUseFrameId != use.Id || existing.ActionId != action.ActionId || existing.ActorSeat != attack.CardUserSeat ||
                existing.TargetSeat != attack.TargetSeat || existing.EffectiveKind != kind ||
                CompleteProgramEventHistory().OfType<ComparedBlackSlashAppliedEvent>().Count(e => e.Receipt == existing) != 1)
                throw new InvalidOperationException("A black Slash comparison lost its exact original use and current target.");
            if (existing.CannotRespond && existing.ActorPolicySource is { } paidSource)
                attack.ProhibitDodgeBy(_contentRegistry.GetSkill(paidSource.SkillId).Name);
            return existing.Ineffective;
        }
        var actor = _players[attack.CardUserSeat]; var target = _players[attack.TargetSeat];
        var incoming = target.IsAlive ? CardPolicies(target, SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp, kind).FirstOrDefault() : default;
        var outgoing = actor.IsAlive ? CardPolicies(actor, SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand, kind).FirstOrDefault() : default;
        if (incoming.Source is null && outgoing.Source is null) return false;
        var actorHand = GetHand(actor).Count; var targetHand = GetHand(target).Count;
        CardConversionSource? incomingSource = incoming.Source is null ? null : new(incoming.Source.SkillId, incoming.Policy.Id, target.Seat, incoming.Source.SkillInstanceId);
        CardConversionSource? outgoingSource = outgoing.Source is null ? null : new(outgoing.Source.SkillId, outgoing.Policy.Id, actor.Seat, outgoing.Source.SkillInstanceId);
        var receipt = new ComparedBlackSlashReceipt(use.Id, action.ActionId, _turnNumber, _currentSeat, actor.Seat, action.ProviderSeat,
            target.Seat, kind, action.EffectiveSuit ?? Suit.None, false, actor.Hp, target.Hp, actorHand, targetHand,
            incomingSource, incoming.Source?.Program.GameplayHash, outgoingSource, outgoing.Source?.Program.GameplayHash,
            incomingSource is not null && actor.Hp >= target.Hp, outgoingSource is not null && targetHand <= actorHand);
        UpdateCardAttackState(attack.ResolutionId, state => (state ?? throw new InvalidOperationException("Missing Slash owner.")) with { ComparedBlackSlash = receipt });
        AdvanceEventRulesAndQueueFact(new ComparedBlackSlashAppliedEvent(receipt));
        if (receipt.CannotRespond && outgoingSource is { } source) attack.ProhibitDodgeBy(_contentRegistry.GetSkill(source.SkillId).Name);
        if (receipt.Ineffective) MarkCardEffectIneffective(use.Id, target.Seat);
        return receipt.Ineffective;
    }
    internal static void ValidateComparedBlackSlashPolicy(string path, SkillProgramCardPolicy policy)
    {
        if (policy.Kind is not (SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp or SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand)) return;
        if (!policy.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) || policy.Value != 0 ||
            policy.RequiredCardKinds.Count != 0 || policy.InputSuit is not null || policy.OutputSuit is not null ||
            policy.Condition.Kind != SkillProgramConditionKind.Always || policy.FactionId is not null || policy.OwnerRole is not null)
            throw new InvalidOperationException($"{path}: comparison policy requires exactly the three frozen black Slash kinds and no alternate condition.");
    }
    private void AssertComparedBlackSlashReceipt(CardUseFrame use)
    {
        if (use.CardAttack?.ComparedBlackSlash is not { } r) return;
        var attack = use.CardAttack;
        var action = use.Action;
        if (action is null || action.Type != CardActionType.Use || action.EffectiveIsRed != false ||
            r.CardUseFrameId != use.Id || r.ActionId != action.ActionId || r.ActorSeat != use.SourceSeat ||
            r.ActorSeat != action.ActorSeat || r.ProviderSeat != action.ProviderSeat ||
            !IsValidPlayerSeat(r.TargetSeat) || !use.TargetSeats.Contains(r.TargetSeat) ||
            r.EffectiveKind != action.EffectiveKind || !IsSlashCard(r.EffectiveKind) ||
            r.EffectiveSuit != (action.EffectiveSuit ?? Suit.None) || r.EffectiveIsRed ||
            r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            r.ActorHandCount < 0 || r.TargetHandCount < 0 || r.ActorHp <= 0 || r.TargetHp <= 0 ||
            r.TargetPolicySource is null && r.ActorPolicySource is null ||
            (action.PhysicalCards.Count == 0 && r.EffectiveSuit is not (Suit.Spade or Suit.Club)) ||
            r.Ineffective != (r.TargetPolicySource is not null && r.ActorHp >= r.TargetHp) ||
            r.CannotRespond != (r.ActorPolicySource is not null && r.TargetHandCount <= r.ActorHandCount) ||
            !ComparedBlackSlashPolicyMatches(r.TargetPolicySource, r.TargetPolicyHash, r.TargetSeat,
                SkillProgramCardPolicyKind.NullifyBlackSlashByCurrentHp) ||
            !ComparedBlackSlashPolicyMatches(r.ActorPolicySource, r.ActorPolicyHash, r.ActorSeat,
                SkillProgramCardPolicyKind.ProhibitBlackSlashResponseByCurrentHand) ||
            CompleteProgramEventHistory().OfType<ComparedBlackSlashAppliedEvent>().Count(e => e.Receipt == r) != 1 ||
            r.CannotRespond && (!attack.ProhibitsDodge || !attack.ResponseProhibitingSkillNames.Contains(
                _contentRegistry.GetSkill(r.ActorPolicySource!.SkillId).Name, StringComparer.Ordinal)) ||
            r.Ineffective && use.IneffectiveTargetSeats?.Contains(r.TargetSeat) != true ||
            attack.TargetSeat != r.TargetSeat && (!attack.DamageRedirected ||
                !CompleteProgramEventHistory().OfType<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == use.Id &&
                    e.OwnerSeat == r.TargetSeat && e.TargetSeat == attack.TargetSeat && e.SourceSeat == attack.SourceSeat)))
            throw new InvalidOperationException("A black Slash conclusion changed its frozen same-use appearance, current actor, source metadata or original target.");
    }
    private bool ComparedBlackSlashPolicyMatches(CardConversionSource? source, string? hash, int owner,
        SkillProgramCardPolicyKind kind)
    {
        if (source is null) return hash is null;
        return source.OwnerSeat == owner && !string.IsNullOrWhiteSpace(source.SkillInstanceId) &&
            _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program is { } program && program.GameplayHash == hash &&
            program.CardPolicies.Any(p => p.Id == source.BindingId && p.Kind == kind);
    }
}
