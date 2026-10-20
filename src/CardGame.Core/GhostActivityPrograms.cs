using System.Globalization;

namespace CardGame.Core;

internal interface IGhostActivityProgramHost
{
    SkillProgramStepOutcome ExecuteGhostActivity(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class GhostActivityOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new GhostActivityEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class DiscardRandomJudgmentDescriptor : GhostActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardRandomJudgment;
}

internal sealed class RewardOtherRecoverySourceDescriptor : GhostActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardOtherRecoverySource;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpRecovered)];
}

internal sealed class DiscardDeathSourceCardsDescriptor : GhostActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardDeathSourceCards;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "condition");
        var zones = r.RequiredEnumArray<CardZoneKind>("zones");
        if (zones.Count == 0 || zones.Distinct().Count() != zones.Count ||
            zones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)))
            throw new InvalidOperationException($"{r.Path}: death-source discard requires distinct hand/equipment areas.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), zones: zones);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.WindowContext?.Window == SkillProgramTriggerWindow.OwnerDied;
}

internal sealed class SelectRedJudgmentFireTargetDescriptor : GhostActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectRedJudgmentFireTarget;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget,
        static (effect, context) => context.SelectTarget(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), targetKind: SkillProgramTargetKind.OtherLiving);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadCardSet(effect.SourceBind!), new SelectSingleTarget(), new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}

internal sealed class GhostActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IGhostActivityProgramHost>().ExecuteGhostActivity(frame, effect);
}

public sealed record RandomJudgmentDiscardedEvent(long ProgramFrameId, int OwnerSeat, int CardId) : IGameEvent;
public sealed record DeathSourceCardsDiscardedEvent(long ProgramFrameId, int OwnerSeat, int SourceSeat, int Count) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class GhostActivityProgramHost(GameEngine engine) : IGhostActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteGhostActivity(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ExecuteGhostActivity(frame, effect);
    }

    private SkillProgramStepOutcome ExecuteGhostActivity(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(input.Id);
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        switch (effect.Op)
        {
            case SkillProgramEffectOp.DiscardRandomJudgment:
            {
                var cards = GetJudgment(_players[frame.OwnerSeat]);
                if (cards.Count == 0) return SkillProgramStepOutcome.Continue;
                var card = cards[_random.Next(cards.Count)];
                MoveLogicalDiscard(frame.OwnerSeat, frame.OwnerSeat, card, CardLocation.Judgment(frame.OwnerSeat), reason,
                    parentFrameId: frame.Id);
                AdvanceEventRulesAndQueueFact(new RandomJudgmentDiscardedEvent(frame.Id, frame.OwnerSeat, card.Id));
                return SkillProgramStepOutcome.Continue;
            }
            case SkillProgramEffectOp.RewardOtherRecoverySource:
            {
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery } change } ||
                    change.TargetSeat != frame.OwnerSeat)
                    throw new InvalidOperationException("A recovery-source reward lost its actual recovery.");
                if (change.SourceSeat is { } source && source != frame.OwnerSeat && IsValidPlayerSeat(source) && _players[source].IsAlive)
                    DrawProgramCards(frame.Id, source, 1, null, null, SkillProgramCardSetVisibility.Private, reason);
                return SkillProgramStepOutcome.Continue;
            }
            case SkillProgramEffectOp.DiscardDeathSourceCards:
            {
                if (frame.WindowContext?.Window != SkillProgramTriggerWindow.OwnerDied)
                    throw new InvalidOperationException("A death-source discard lost its actual death window.");
                if (frame.WindowContext.SourceSeat is not { } source || source == frame.OwnerSeat || !IsValidPlayerSeat(source) || !_players[source].IsAlive)
                    return SkillProgramStepOutcome.Continue;
                var ids = effect.Zones.SelectMany(zone => _cardZones.CardsAt(new(zone, source))
                    .Where(card => !IsSelfHandCategoryDiscardForbidden(source, card, new(zone, source), OwnedCardMoveIntent.Discard)))
                    .Select(card => card.Id).ToArray();
                if (ids.Length != 0) MoveProgramLogicalDiscard(frame, source, ids, reason);
                AdvanceEventRulesAndQueueFact(new DeathSourceCardsDiscardedEvent(frame.Id, frame.OwnerSeat, source, ids.Length));
                return SkillProgramStepOutcome.Continue;
            }
            case SkillProgramEffectOp.SelectRedJudgmentFireTarget:
            {
                var red = GetProgramCardSet(frame, effect.SourceBind!);
                if (red.CardIds.Count == 0)
                {
                    ReplaceRuntimeTop(frame with { SelectedTargetSeats = [] });
                    return SkillProgramStepOutcome.Continue;
                }
                if (red.CardIds.Count != 1) throw new InvalidOperationException("A red-judgment target requires one filtered judgment card.");
                var targets = GetProgramTargetSeats(frame.OwnerSeat, SkillProgramTargetKind.OtherLiving, frame.WindowContext);
                if (targets.Count == 0)
                {
                    ReplaceRuntimeTop(frame with { SelectedTargetSeats = [] });
                    return SkillProgramStepOutcome.Continue;
                }
                var skill = _contentRegistry.GetSkill(frame.SkillId);
                _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "选择受到1点火焰伤害的角色。", [], targets, frame.OwnerSeat)
                {
                    PromptId = CreatePromptId(),
                    SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description),
                    Choices = Array.AsReadOnly(targets.Select(target => new PromptChoice(
                        new ChoiceId($"red-judgment-fire.{frame.Id}.{target}"), $"对 {_players[target].Name} 造成1点火焰伤害。", [], [target],
                        new Dictionary<string, string> { ["program-action"] = "red-judgment-fire-target", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture) })).ToArray())
                };
                _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
                return SkillProgramStepOutcome.AwaitChoice;
            }
            default: throw new InvalidOperationException("Unsupported ghost activity operation.");
        }
    }

    [ProgramAnswer("red-judgment-fire-target")]
    private void ResolveRedJudgmentFireTarget(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The red judgment lost its program.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SelectRedJudgmentFireTarget || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(CultureInfo.InvariantCulture) || choice.Cards.Count != 0 ||
            choice.Targets is not [var target] || GetProgramCardSet(frame, effect.SourceBind!).CardIds.Count != 1 ||
            !GetProgramTargetSeats(frame.OwnerSeat, SkillProgramTargetKind.OtherLiving, frame.WindowContext).Contains(target))
            throw new InvalidOperationException("The selected red-judgment target lost its exact instruction or legal target.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { SelectedTargetSeats = Array.AsReadOnly(new[] { target }) });
        AdvanceRuntimeProgram(frame.Id);
    }
}
