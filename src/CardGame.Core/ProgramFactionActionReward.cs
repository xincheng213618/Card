namespace CardGame.Core;

public sealed record CardActionFactionOrigin(int ActualTurnNumber, int ActualTurnOwnerSeat, string? ActorFactionId, string? ProviderFactionId);
public sealed record ProgramFactionRewardOffer(int OwnerSeat, string SkillId);

internal sealed class RewardOutOfTurnFactionSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardOutOfTurnFactionSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new RewardOutOfTurnFactionSlashHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "providerFactionId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition(), providerFactionId: r.RequiredIdentifier("providerFactionId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseBeforeTargetEffects, SkillProgramTriggerWindow.CardResponseAccepted])];
}

public sealed class RewardOutOfTurnFactionSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RewardOutOfTurnFactionSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host)
    {
        host.Draw(frame.Id, frame.OwnerSeat, frame.OwnerSeat, 1, null, null, SkillProgramCardSetVisibility.Private, new($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed partial class GameEngine
{
    private bool TracksFactionActionOrigin => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RewardOutOfTurnFactionSlash);
    private CardActionContext CaptureFactionAction(CardActionContext action)
    {
        if (!TracksFactionActionOrigin || action.FactionOrigin is not null || !IsSlashCard(action.EffectiveKind) || action.ActionId == 0) return action;
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
            action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain, action.DesignatedTargetSeats, action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed,
            new(_turnNumber, _currentSeat, GetEffectiveFactionId(_players[action.ActorSeat]), GetEffectiveFactionId(_players[action.ProviderSeat])));
    }

    private CardActionContext CaptureReplacedFactionActor(CardActionContext action, int actorSeat)
    {
        if (action.FactionOrigin is not { } origin) return action;
        return new(action.ActionId, action.ParentActionId, action.Type, actorSeat, action.ProviderSeat,
            action.RequesterSeat, action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats,
            action.PhysicalCards, action.ConversionChain, action.EffectiveDesignatedTargetSeats, action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed,
            origin with { ActorFactionId = GetEffectiveFactionId(_players[actorSeat]) });
    }

    private int? FactionActionRewardChooser(int owner, SkillProgramTrigger trigger, CardActionContext action)
    {
        var effect = trigger.Effects.SingleOrDefault(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionSlash);
        if (effect is null || action.FactionOrigin is not { } origin || origin.ActualTurnNumber != _turnNumber || !IsSlashCard(action.EffectiveKind)) return null;
        var actualSeat = action.ActorSeat == owner ? action.ProviderSeat : action.ActorSeat;
        var faction = action.ActorSeat == owner ? origin.ProviderFactionId : origin.ActorFactionId;
        if (actualSeat == owner || actualSeat == origin.ActualTurnOwnerSeat || faction != effect.ProviderFactionId || !_players[actualSeat].IsAlive) return null;
        return actualSeat;
    }

    private bool WasFactionRewardOffered(int owner, string skillId, CardActionContext action) => action.Type == CardActionType.Use &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Action?.ActionId == action.ActionId)?.FactionRewardOffers?.Contains(new(owner, skillId)) == true;

    private void RecordFactionRewardOffers(CardActionContext action, IReadOnlyList<ProgramCardTriggerCandidate> candidates)
    {
        if (action.Type != CardActionType.Use || !TracksFactionActionOrigin) return;
        var offers = candidates.Where(candidate => _contentRegistry.GetSkill(candidate.SkillId).Program!.Triggers
            .Single(trigger => trigger.Id == candidate.TriggerId).Effects.Any(effect => effect.Op == SkillProgramEffectOp.RewardOutOfTurnFactionSlash))
            .Select(candidate => new ProgramFactionRewardOffer(candidate.OwnerSeat, candidate.SkillId)).Distinct().ToArray();
        if (offers.Length == 0) return;
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.ActionId == action.ActionId);
        UpdateLifecycleCardUse(use.Id, frame => frame with { FactionRewardOffers = Array.AsReadOnly((frame.FactionRewardOffers ?? []).Concat(offers).Distinct().ToArray()) });
    }

    private bool CanRunFactionActionReward(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RewardOutOfTurnFactionSlash)) return true;
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId);
        return window is not null && context.OptionalChooserSeat is { } chooser && FactionActionRewardChooser(candidate.OwnerSeat, trigger, window.Action) == chooser &&
            HasSkillRoleQualification(_players[candidate.OwnerSeat], candidate.SkillId, candidate.SkillInstanceId, Role.Lord);
    }
}
