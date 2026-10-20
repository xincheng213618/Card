using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record BoundDyingAlcoholUseReceipt(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, long DyingFrameId, int VictimSeat,
    long CardUseFrameId, long ActionId, int CardId, CardLocation From, CardMoveReason Reason, long SequenceBefore);
public sealed record BoundDyingAlcoholPaymentReceipt(long? RecoveryAttemptId, int Recovered, int RemainingHp);
public sealed record ProgramBoundDyingAlcoholIssuedEvent(BoundDyingAlcoholUseReceipt Receipt) : IGameEvent;
public sealed record ProgramBoundDyingAlcoholPaidEvent(long CardUseFrameId, long ProgramFrameId,
    BoundDyingAlcoholPaymentReceipt Payment) : IGameEvent;

public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundDyingAlcoholUseReceipt? BoundDyingAlcoholUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundDyingAlcoholPaymentReceipt? BoundDyingAlcoholPayment { get; init; }
}

public sealed partial class GameEngine
{
    private void IssueBoundDyingAlcoholReturn(long useId, ProgramSkillFrame program, DyingFrame dying,
        Card card, CardLocation from, CardMoveReason reason)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Bound Alcohol lost its issued native use.");
        if (use.BoundDyingAlcoholUse is not null || use.Action is not { } action)
            throw new InvalidOperationException("Bound Alcohol cannot issue a second native return.");
        var token = new BoundDyingAlcoholUseReceipt(program.Id, program.InstructionIndex,
            new(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId), program.GameplayHash,
            dying.Id, dying.VictimSeat, useId, action.ActionId, card.Id, from, reason, _movementSequence);
        ReplaceRuntimeTop(use with { BoundDyingAlcoholUse = token });
        AdvanceEventRulesAndQueueFact(new ProgramBoundDyingAlcoholIssuedEvent(token));
    }

    private void RecordBoundDyingAlcoholPayment(long useId, int recovered, long? recoveryAttemptId = null)
    {
        var use = LifecycleCardUse(useId) ?? throw new InvalidOperationException("Bound Alcohol payment lost its native use.");
        var token = use.BoundDyingAlcoholUse ?? throw new InvalidOperationException("Bound Alcohol payment has no owned return.");
        RequireBoundDyingAlcoholUse(use, requirePayment: false, requireFinished: false);
        if (use.BoundDyingAlcoholPayment is not null || recovered < 0)
            throw new InvalidOperationException("Bound Alcohol recovery was already paid or has an invalid actual amount.");
        var payment = new BoundDyingAlcoholPaymentReceipt(recoveryAttemptId, recovered, _players[token.VictimSeat].Hp);
        ReplaceRuntimeFrame(useId, use with { BoundDyingAlcoholPayment = payment });
        AdvanceEventRulesAndQueueFact(new ProgramBoundDyingAlcoholPaidEvent(useId, token.ProgramFrameId, payment));
        // This existing fact describes actual recovery payment. It precedes
        // completion observers, while the use token owns their eventual return.
        AdvanceEventRulesAndQueueFact(new ProgramDyingRescueEvent(token.DyingFrameId, token.Source.SkillId,
            token.Source.OwnerSeat, token.VictimSeat, token.CardId, recovered, payment.RemainingHp));
        var skill = _contentRegistry.GetSkill(token.Source.SkillId);
        AddLog("SkillTriggered", $"{_players[token.Source.OwnerSeat].Name} 发动【{skill.Name}】，令 {_players[token.VictimSeat].Name} 视为使用【酒】并回复{recovered}点体力。",
            token.Source.OwnerSeat, token.VictimSeat);
    }

    private void RequireBoundDyingAlcoholUse(CardUseFrame use, bool requirePayment, bool requireFinished)
    {
        if (use.BoundDyingAlcoholUse is not { } t) throw new InvalidOperationException("Bound Alcohol has no native return token.");
        var program = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == t.ProgramFrameId);
        var dying = _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == t.DyingFrameId);
        var programIndex = _resolutionStack.FindIndex(f => f.Id == t.ProgramFrameId);
        var useIndex = _resolutionStack.FindIndex(f => f.Id == use.Id);
        if (program is null || dying is null || program.InstructionIndex != t.InstructionIndex ||
            programIndex < 1 || _resolutionStack[programIndex - 1].Id != dying.Id ||
            (useIndex >= 0 ? useIndex != programIndex + 1 : !requireFinished || _resolutionStack.LastOrDefault()?.Id != program.Id) ||
            t.Source != new CardConversionSource(program.SkillId, GetProgramBindingId(program), program.OwnerSeat, program.SkillInstanceId) ||
            t.GameplayHash != program.GameplayHash || _contentRegistry.GetSkill(program.SkillId).Program is not { } definition ||
            definition.GameplayHash != t.GameplayHash || program.WindowContext is not { Window: SkillProgramTriggerWindow.DyingResponse } context ||
            context.ParentFrameId != dying.Id || context.TargetSeat != t.VictimSeat || context.OwnerSeat != program.OwnerSeat ||
            dying.VictimSeat != t.VictimSeat || dying.ResponderSeat != program.OwnerSeat ||
            t.InstructionIndex < 1 || t.CardUseFrameId != use.Id || use.CardKind != CardKind.Alcohol || use.CardId != t.CardId ||
            use.SourceSeat != t.VictimSeat || use.DyingResponse is not null || !use.TargetSeats.SequenceEqual([t.VictimSeat]) ||
            !(use.PhysicalCardIds ?? [use.CardId]).SequenceEqual([t.CardId]) || t.From.OwnerSeat != program.OwnerSeat ||
            t.From.Zone is not (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao) ||
            t.Reason.Value != $"skill-program.{program.SkillId}.{SkillProgramEffectOp.UseBoundCardAsDyingAlcohol}" ||
            use.Action is not { Type: CardActionType.Use } action || action.ActionId != t.ActionId ||
            action.ActorSeat != t.VictimSeat || action.ProviderSeat != program.OwnerSeat || action.EffectiveKind != CardKind.Alcohol ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || !action.EffectiveDesignatedTargetSeats.SequenceEqual(use.TargetSeats) ||
            !action.ConversionChain.SequenceEqual([t.Source]) || action.PhysicalCards is not [var cost] ||
            cost.CardId != t.CardId || cost.From != t.From ||
            ProgramInstructionResolver.Default.Resolve(program, definition).GetPausedInstruction(program.InstructionIndex).Effect is not
                { Op: SkillProgramEffectOp.UseBoundCardAsDyingAlcohol, SourceBind: { } bind } ||
            program.CardSetBindings.SingleOrDefault(b => b.Name == bind) is not { CardIds: [var bound], SourceLocations: [var location] } ||
            bound != t.CardId || location != t.From ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == program.Id && e.SkillId == program.SkillId &&
                e.BindingId == program.TriggerId && e.SkillInstanceId == program.SkillInstanceId && e.OwnerSeat == program.OwnerSeat &&
                e.Window == SkillProgramTriggerWindow.DyingResponse) != 1 ||
            ProgramEventHistory<ProgramBoundDyingAlcoholIssuedEvent>().Count(e => e.Receipt == t) != 1 ||
            ProgramEventHistory<CardUseDeclaredEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == t.CardId &&
                e.CardKind == CardKind.Alcohol && e.SourceSeat == t.VictimSeat) != 1 ||
            t.SequenceBefore < 0 || t.SequenceBefore > _movementSequence ||
            _cardMovements.Count(m => m.Sequence > t.SequenceBefore && m.CardId == t.CardId && m.From == t.From && m.To == CardLocation.Processing && m.Reason == t.Reason) != 1)
            throw new InvalidOperationException("Bound Alcohol lost its exact source, program, dying occurrence, native use or physical payment.");
        var payments = ProgramEventHistory<ProgramBoundDyingAlcoholPaidEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        if (use.BoundDyingAlcoholPayment is { } p)
        {
            if (p.Recovered < 0 || payments is not [var payment] || payment.ProgramFrameId != t.ProgramFrameId || payment.Payment != p ||
                CompleteProgramEventHistory().SkipWhile(e => e is not ProgramBoundDyingAlcoholIssuedEvent issued || issued.Receipt != t)
                    .Skip(1).OfType<ProgramDyingRescueEvent>().Count(e => e.DyingFrameId == t.DyingFrameId && e.SkillId == t.Source.SkillId &&
                    e.OwnerSeat == t.Source.OwnerSeat && e.VictimSeat == t.VictimSeat && e.CardId == t.CardId &&
                    e.RecoveredHp == p.Recovered && e.VictimHp == p.RemainingHp) != 1)
                throw new InvalidOperationException("Bound Alcohol lost its once-paid native recovery fact.");
        }
        else if (requirePayment || payments.Length != 0)
            throw new InvalidOperationException("Bound Alcohol cannot return without its actual recovery payment.");
        if (requireFinished && (ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id &&
                e.CardId == t.CardId && e.CardKind == CardKind.Alcohol) != 1 ||
            _cardMovements.Count(m => m.Sequence > t.SequenceBefore && m.CardId == t.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == t.Reason) != 1))
            throw new InvalidOperationException("Bound Alcohol must finish its original use and physical discard before returning its program.");
    }

    [NativeUseValidation(275)]
    private void ValidateBoundDyingAlcoholReturn(CardUseFrame use)
    { if (use.BoundDyingAlcoholUse is not null) RequireBoundDyingAlcoholUse(use, requirePayment: true, requireFinished: true); }

    [NativeUseReturn(275)]
    private bool ReturnBoundDyingAlcoholUse(CardUseFrame use)
    {
        if (use.BoundDyingAlcoholUse is not { } t) return false;
        RequireBoundDyingAlcoholUse(use, requirePayment: true, requireFinished: true);
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame program || program.Id != t.ProgramFrameId)
            throw new InvalidOperationException("Bound Alcohol returned outside its exact paused program.");
        AdvanceRuntimeProgram(program.Id); return true;
    }
}
