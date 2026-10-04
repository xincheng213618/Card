using System.Text.Json.Serialization;

namespace CardGame.Core;

// Optional ViewAs policy. A missing policy preserves the original positive
// input-count rules, configured conversion tiers and instance-scoped quotas.
public sealed record ProgramTieredRoundConversionPolicy(string StateId, string UsageId);

public sealed record TieredRoundConversionUseReceipt(
    CardConversionSource Source, string GameplayHash, string StateId, string UsageId,
    int FrozenTier, int RoundNumber, int ActualTurnNumber, int ActualTurnOwnerSeat,
    int PhaseInstanceId, long CardActionId, long OwnerFrameId, int ActorSeat,
    CardKind EffectiveKind, int MaterialCount, long? DyingFrameId = null);

public sealed record TrueRoundCardNameUsedEvent(int RoundNumber, int ActorSeat,
    long OriginFrameId, long? CardActionId, CardKind EffectiveKind, bool NullificationUse) : IGameEvent;
public sealed record TieredRoundConversionUseIssuedEvent(TieredRoundConversionUseReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksTrueRoundCardNames => _contentRegistry.ProgramDependencies.UsesTieredRoundConversions;

    private static bool IsTieredRoundOutput(CardKind kind) =>
        CardCatalog.Get(kind).CategoryName == "基本牌" || IsOrdinaryTrick(kind);

    private int TieredRoundTier(int ownerSeat, string stateId) =>
        _configuredConversionTiers.GetValueOrDefault((ownerSeat, stateId));

    private static string TieredRoundUsage(ProgramTieredRoundConversionPolicy policy) =>
        "tiered-round-conversion:" + policy.StateId + ":" + policy.UsageId;

    private bool TrueRoundHasUsedName(CardKind kind) => CompleteProgramEventHistory()
        .OfType<TrueRoundCardNameUsedEvent>().Any(e => e.RoundNumber == _roundNumber &&
            ProgramBasicCardName(e.EffectiveKind) == ProgramBasicCardName(kind));

    private bool CanUseTieredRoundConversion(CharacterState actor, IndexedSkillProgramInstance instance,
        SkillProgramViewAs rule, bool responseUse, bool dyingUse)
    {
        if (rule.TieredRoundConversion is not { } policy) return true;
        var tier = TieredRoundTier(actor.Seat, policy.StateId);
        if (_winner != Winner.None || !actor.IsAlive || _roundNumber < 1 || !IsTieredRoundOutput(rule.OutputKind) ||
            tier is < 0 or > 2 || tier < rule.MinimumTier || tier > rule.MaximumTier ||
            rule.ConversionStateId != policy.StateId || rule.InputCount != (tier == 2 ? 0 : 1)) return false;
        // Response grammar is admitted only for a real use-qualified conversion;
        // ordinary Slash/Dodge response paths never become round-use facts.
        if (responseUse && !rule.UseOnly) return false;
        if (tier == 0 && (_phase != TurnPhase.Play || actor.Seat != _currentSeat || TrueRoundHasUsedName(rule.OutputKind))) return false;
        var scope = tier == 0 ? SkillUsageScope.Phase : SkillUsageScope.Round;
        return _skillRuntimeState.GetUsage(actor.Seat, instance.SkillId, TieredRoundUsage(policy), scope) == 0;
    }

    private void ConsumeTieredRoundConversion(CardConversionSource source)
    {
        if (ViewAsRule(source) is not { TieredRoundConversion: { } policy } rule) return;
        var actor = _players[source.OwnerSeat];
        var instance = GetSkillBindingShard(actor).ProgramInstances.SingleOrDefault(i =>
            i.SkillId == source.SkillId && i.SkillInstanceId == source.SkillInstanceId);
        if (instance is null || !CanUseTieredRoundConversion(actor, instance, rule, false, false))
            throw new InvalidOperationException("A tiered round conversion lost its published source, current level or shared allowance before acceptance.");
        var tier = TieredRoundTier(actor.Seat, policy.StateId);
        var scope = tier == 0 ? SkillUsageScope.Phase : SkillUsageScope.Round;
        if (!_skillRuntimeState.TryConsumeUsage(actor.Seat, source.SkillId, TieredRoundUsage(policy), scope, 1))
            throw new InvalidOperationException("A tiered conversion allowance cannot be paid twice.");
        AdvanceEventRulesAndQueueFact(new SkillUsageConsumedEvent(actor.Seat, source.SkillId, source.BindingId, scope, 1));
    }

    private void ObserveTrueRoundCardNames(IGameEvent payload)
    {
        if (!TracksTrueRoundCardNames || _roundNumber < 1) return;
        if (payload is CardActionAcceptedEvent accepted)
        {
            var acceptedAction = accepted.Action;
            if (TryRecordTrueRoundDodgeUse(acceptedAction)) return;
            if (acceptedAction.Type != CardActionType.Response || acceptedAction.EffectiveKind != CardKind.Nullification ||
                acceptedAction.ActorSeat != acceptedAction.ProviderSeat || acceptedAction.ResponderSeat != acceptedAction.ActorSeat || acceptedAction.RequesterSeat is not null) return;
            var window = _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault();
            var parent = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == window?.ParentFrameId);
            if (window is null || parent is null || acceptedAction.ParentActionId != parent.Action?.ActionId ||
                acceptedAction.OpponentSeat != window.SourceSeat || !IsValidPlayerSeat(acceptedAction.ActorSeat)) return;
            RecordTrueRoundCardName(window.Id, acceptedAction.ActorSeat, acceptedAction.ActionId, acceptedAction.EffectiveKind, true); return;
        }
        if (payload is not CardUseDeclaredEvent declared) return;
        var use = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Id == declared.ResolutionId);
        if (use is null || use.SourceSeat != declared.SourceSeat || use.CardId != declared.CardId || use.CardKind != declared.CardKind ||
            !IsValidPlayerSeat(use.SourceSeat) || !IsTieredRoundOutput(use.CardKind)) return;
        if (use.Action is { } action)
        {
            if (action.Type != CardActionType.Use || action.ActorSeat != use.SourceSeat || action.EffectiveKind != use.CardKind) return;
            // A declaration reaches this point only after the real producer
            // accepted it and transferred its frozen declaration payment.
            if (use.AcceptedDeclarationPayment is { } payment &&
                (payment.OwnerFrameId != use.Id || payment.DeclaredKind != use.CardKind || payment.ProviderSeat != action.ProviderSeat))
                throw new InvalidOperationException("Round-use tracking lost the accepted declared-use producer.");
            RecordTrueRoundCardName(use.Id, action.ActorSeat, action.ActionId, use.CardKind, false);
        }
        else if (use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && IsActualTurnLegacyVirtualUse(use))
            RecordTrueRoundCardName(use.Id, use.SourceSeat, null, use.CardKind, false);
    }

    private void RecordTrueRoundCardName(long origin, int actor, long? action, CardKind kind, bool counter)
    {
        if (CompleteProgramEventHistory().OfType<TrueRoundCardNameUsedEvent>().Any(e =>
            e.RoundNumber == _roundNumber && (action is not null ? e.CardActionId == action : e.CardActionId is null && e.OriginFrameId == origin))) return;
        AdvanceEventRulesAndQueueFact(new TrueRoundCardNameUsedEvent(_roundNumber, actor, origin, action, kind, counter));
    }

    private bool TryRecordTrueRoundDodgeUse(CardActionContext action)
    {
        if (action.Type != CardActionType.Response || action.EffectiveKind != CardKind.Dodge || action.ActorSeat != action.ProviderSeat ||
            action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null || ActiveGroupCard is not null || ActiveFactionDefense is not null ||
            ActiveCardAttack is not { } attack || !IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash) || attack.TargetSeat != action.ActorSeat ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || action.ParentActionId != use.Action?.ActionId || action.OpponentSeat != attack.SourceSeat ||
            !IsValidPlayerSeat(action.ActorSeat) || !IsProgramResponseCardUse(_players[action.ActorSeat], CardKind.Dodge)) return false;
        // This observer runs at the real Accepted boundary. Native, converted
        // and Bagua Dodge uses share the same mature Slash-defense classification;
        // a new zero-material conversion additionally retains its own issuance.
        if (action.PhysicalCards.Count == 0 && action.ConversionChain.Any(source =>
                ViewAsRule(source) is { InputCount: 0, TieredRoundConversion: not null }) &&
            !IsIssuedTieredRoundZeroResponse(use, action)) return false;
        RecordTrueRoundCardName(use.Id, action.ActorSeat, action.ActionId, CardKind.Dodge, false); return true;
    }
}
