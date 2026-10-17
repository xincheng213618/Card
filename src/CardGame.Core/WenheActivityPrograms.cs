using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IWenheActivityProgramHost
{
    SkillProgramStepOutcome ExecuteWenheActivity(ProgramSkillFrame frame, SkillProgramEffectOp op);
}
internal abstract class WenheActivityOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new WenheActivityEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ResolveWenheRoundStartDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWenheRoundStart;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)]; }
internal sealed class ResolveWenheTurnStartDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWenheTurnStart;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)]; }
internal sealed class ResolveWenheTurnEndDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWenheTurnEnd;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]; }
internal sealed class ResolveWenheRoundEndDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWenheRoundEnd;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundEnding)]; }
internal sealed class RewardWenheKillerDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.RewardWenheKiller;
  public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)]; }
internal sealed class UseWenheChampionMarkDescriptor : WenheActivityOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.UseWenheChampionMark; }
internal sealed class WenheActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) =>
        h.GetCapability<IWenheActivityProgramHost>().ExecuteWenheActivity(f, op);
}
public sealed record WenheRuleFlowReceipt(SkillProgramEffectOp Operation, int RoundNumber, int TurnNumber,
    int Amount, int Index, int Stage)
{
    private readonly IReadOnlyList<int> _seats = Array.Empty<int>();
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WenheRuleFlowIssuedEvent(long ProgramFrameId, SkillProgramEffectOp Operation, int RoundNumber,
    int TurnNumber, int OwnerSeat, int Count, int Amount) : IGameEvent;
public sealed record WenheRuleParticipantEvent(long ProgramFrameId, int Index, int Seat) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenheRuleFlowReceipt? WenheRuleFlow { get; init; }
}

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class WenheActivityProgramHost(GameEngine engine) : IWenheActivityProgramHost
    { public SkillProgramStepOutcome ExecuteWenheActivity(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.ExecuteWenheActivity(f, op); }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 41000)]
    private bool AllowsWenheActivation(ProgramActivationLegalityContext gate) => gate.SkillId switch
    {
        "wenhe:chaos-resolution" => false,
        "wenhe:champion-mark" => _modeDefinition.WenheActivity is { EnableAspirations: true } &&
            WenheGoalRank(gate.OwnerSeat) == 1 && !WenheChampionMarkUsed(gate.OwnerSeat),
        _ => true
    };
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 41000)]
    private bool AllowsWenheRuleTrigger(ProgramTriggerLegalityContext gate)
    {
        if (gate.SkillId != "wenhe:rules") return true;
        if (_modeDefinition.WenheActivity is null) return false;
        if (gate.WindowContext is not { } c) return true;
        return c.Window switch
        {
            SkillProgramTriggerWindow.RoundStarting => gate.OwnerSeat == _currentSeat && CurrentWenheRule == WenheRuleKind.Chaos &&
                !ProgramEventHistory<WenheRuleFlowIssuedEvent>().Any(e => e.RoundNumber == _roundNumber && e.Operation == SkillProgramEffectOp.ResolveWenheRoundStart),
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => gate.OwnerSeat == _currentSeat && CurrentWenheRule == WenheRuleKind.LastStand,
            SkillProgramTriggerWindow.TurnEnding => (gate.OwnerSeat == _currentSeat || IsWenheDeadOwnerEndingCandidate(gate.Candidate, _currentSeat)) && CurrentWenheRule == WenheRuleKind.Equipment &&
                !ProgramEventHistory<WenheRuleFlowIssuedEvent>().Any(e => e.TurnNumber == _turnNumber && e.Operation == SkillProgramEffectOp.ResolveWenheTurnEnd),
            SkillProgramTriggerWindow.RoundEnding => gate.OwnerSeat == _players.Where(p => p.IsAlive).Select(p => p.Seat).DefaultIfEmpty(-1).Min() && CurrentWenheRule == WenheRuleKind.Famine &&
                !ProgramEventHistory<WenheRuleFlowIssuedEvent>().Any(e => e.RoundNumber == _roundNumber && e.Operation == SkillProgramEffectOp.ResolveWenheRoundEnd),
            SkillProgramTriggerWindow.CharacterDied => c.SourceSeat == gate.OwnerSeat && c.TargetSeat != gate.OwnerSeat,
            _ => false
        };
    }
    private bool HasWenheDeadOwnerEnding(int actualOwner) => _modeDefinition.WenheActivity is not null &&
        CurrentWenheRule == WenheRuleKind.Equipment && !_players[actualOwner].IsAlive && _players.Any(p => p.IsAlive);
    private bool IsWenheDeadOwnerEndingCandidate(ProgramTriggerCandidate c, int actualOwner) =>
        HasWenheDeadOwnerEnding(actualOwner) && c.SkillId == "wenhe:rules" && c.BindingId == "turn-end-rule" &&
        c.OwnerSeat == _players.Where(p => p.IsAlive).Min(p => p.Seat);
    private SkillProgramStepOutcome ExecuteWenheActivity(ProgramSkillFrame frame, SkillProgramEffectOp op)
    {
        if (_modeDefinition.WenheActivity is null || frame.WenheRuleFlow is not null ||
            ProgramEventHistory<WenheRuleFlowIssuedEvent>().Any(e => e.ProgramFrameId == frame.Id))
            throw new InvalidOperationException("Wenhe requires its exact once-issued activity operation.");
        var living = _players.Where(p => p.IsAlive).ToArray();
        var amount = op == SkillProgramEffectOp.ResolveWenheRoundEnd ? _roundNumber :
            op == SkillProgramEffectOp.RewardWenheKiller ? CurrentWenheRule == WenheRuleKind.Bounty ? 2 : 1 : 1;
        var seats = op switch
        {
            SkillProgramEffectOp.ResolveWenheTurnEnd => living.Where(p => GetEquipment(p).Count == living.Min(q => GetEquipment(q).Count)).Select(p => p.Seat).ToArray(),
            SkillProgramEffectOp.ResolveWenheRoundEnd => living.Where(p => GetHand(p).Count == living.Min(q => GetHand(q).Count)).Select(p => p.Seat).ToArray(),
            _ => new[] { frame.OwnerSeat }
        };
        if (op == SkillProgramEffectOp.UseWenheChampionMark)
        {
            if (WenheGoalRank(frame.OwnerSeat) != 1 || WenheChampionMarkUsed(frame.OwnerSeat))
                throw new InvalidOperationException("The private champion mark is unavailable.");
            AdvanceEventRulesAndQueueFact(new WenheChampionMarkUsedEvent(frame.Id, frame.OwnerSeat));
        }
        ReplaceRuntimeTop(frame = frame with { WenheRuleFlow = new(op, _roundNumber, _turnNumber, amount, 0, 0) { Seats = seats } });
        AdvanceEventRulesAndQueueFact(new WenheRuleFlowIssuedEvent(frame.Id, op, _roundNumber, _turnNumber, frame.OwnerSeat, seats.Length, amount));
        for (var index = 0; index < seats.Length; index++) AdvanceEventRulesAndQueueFact(new WenheRuleParticipantEvent(frame.Id, index, seats[index]));
        if (op == SkillProgramEffectOp.ResolveWenheRoundStart)
        {
            ReplaceRuntimeTop(frame = frame with { WenheRuleFlow = frame.WenheRuleFlow! with { Stage = 3 } });
            BeginWenheChaos(frame, null); return SkillProgramStepOutcome.AwaitChild;
        }
        return ResumeWenheRuleFlow(frame.Id) ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 41000)]
    private bool ResumeWenheRuleFlow(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WenheRuleFlow: { } r } frame || frame.Id != id) return false;
        AssertWenheRuleFlow(frame);
        if (r.Stage == 3) throw new InvalidOperationException("Round chaos cannot resume before its issued child returns.");
        if (TryBeginWenheRuleChildren(id)) return true;
        frame = GetActiveProgramFrame(id); r = frame.WenheRuleFlow!;
        while (r.Index < r.Seats.Count)
        {
            var seat = r.Seats[r.Index];
            if (r.Stage == 0)
            {
                r = r with { Stage = 1 }; ReplaceRuntimeTop(frame = frame with { WenheRuleFlow = r });
                if (_players[seat].IsAlive)
                {
                    if (r.Operation == SkillProgramEffectOp.RewardWenheKiller) ChangeProgramMaximumHp(frame, r.Amount);
                    else if (r.Operation != SkillProgramEffectOp.UseWenheChampionMark)
                    {
                        if (new ProgramSkillHost(this).LoseHp(id, frame.SkillId, seat, r.Amount) == SkillProgramStepOutcome.AwaitChild) return true;
                    }
                    if (TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program)) return true;
                }
                frame = GetActiveProgramFrame(id); r = frame.WenheRuleFlow!;
            }
            if (r.Stage == 1)
            {
                r = r with { Stage = 2 }; ReplaceRuntimeTop(frame = frame with { WenheRuleFlow = r });
                if (_players[seat].IsAlive)
                {
                    var draw = r.Operation switch { SkillProgramEffectOp.RewardWenheKiller => r.Amount * 3,
                        SkillProgramEffectOp.ResolveWenheTurnStart => 3, SkillProgramEffectOp.UseWenheChampionMark => 2, _ => 0 };
                    if (draw > 0) { BeginOperationProgramDraw(frame, seat, draw, new("wenhe.rule.draw")); return true; }
                    if (r.Operation == SkillProgramEffectOp.ResolveWenheTurnEnd)
                    {
                        var equipment = _cardZones.CardsAt(CardLocation.DrawPile).Where(c => EquipmentCatalog.IsEquipment(c.Kind) &&
                            _players[seat].EquipmentSlotCapacity(EquipmentCatalog.Get(c.Kind).Slot) > 0).ToArray();
                        if (equipment.Length > 0) PlaceAdvancedEquipment(equipment[_random.Next(equipment.Length)], seat);
                    }
                }
            }
            // Equipment replacement can attach a native recovery receipt to this
            // same frame. Drain it before the next tied participant and never
            // overwrite a producer update with the pre-effect local record.
            if (TryBeginWenheRuleChildren(id)) return true;
            frame = GetActiveProgramFrame(id); r = frame.WenheRuleFlow!;
            r = r with { Stage = 0, Index = r.Index + 1 }; ReplaceRuntimeTop(frame = frame with { WenheRuleFlow = r });
        }
        ReplaceRuntimeTop(frame with { WenheRuleFlow = null }); return false;
    }
    private bool TryBeginWenheRuleChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.RewardWenheKiller, SkillProgramEffectOp.ResolveWenheTurnStart, SkillProgramEffectOp.UseWenheChampionMark)]
    private void ReturnWenheRuleDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        AssertWenheRuleFlow(f);
        var r = f.WenheRuleFlow!;
        var count = r.Operation == SkillProgramEffectOp.RewardWenheKiller ? r.Amount * 3 : r.Operation == SkillProgramEffectOp.ResolveWenheTurnStart ? 3 : 2;
        if (r.Stage != 2 || outcome.RecipientSeat != r.Seats[r.Index] || outcome.RequestedCount != count)
            throw new InvalidOperationException("Wenhe draw lost its already paid participant.");
        CompleteProgramDrawCards(f.Id, outcome.RecipientSeat, null, SkillProgramCardSetVisibility.Private, new("wenhe.rule.draw"), 0, outcome.ActualDrawIds);
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 41000)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 41000)]
    private bool RetainWenheIssuedFlow(ProgramSkillFrame f) => f.WenheRuleFlow is not null || f.WenheChaos is not null && IsExactWenheChaos(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 41000)]
    private void AssertWenheRuleFlow(ProgramSkillFrame f)
    {
        if (f.WenheRuleFlow is not { } r) return;
        if (_modeDefinition.WenheActivity is null || r.RoundNumber != _roundNumber || r.TurnNumber != _turnNumber ||
            r.Index < 0 || r.Index > r.Seats.Count || r.Stage is < 0 or > 3 || r.Seats is not System.Collections.IList { IsReadOnly: true } ||
            ProgramEventHistory<WenheRuleFlowIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.Operation == r.Operation &&
                e.RoundNumber == r.RoundNumber && e.TurnNumber == r.TurnNumber && e.Count == r.Seats.Count && e.Amount == r.Amount && e.OwnerSeat == f.OwnerSeat) != 1 ||
            r.Seats.Where((seat, index) => ProgramEventHistory<WenheRuleParticipantEvent>().Count(e => e.ProgramFrameId == f.Id && e.Index == index && e.Seat == seat) != 1).Any())
            throw new InvalidOperationException("Wenhe rule lost its frozen participants, paid cursor or originating fact.");
    }
}
