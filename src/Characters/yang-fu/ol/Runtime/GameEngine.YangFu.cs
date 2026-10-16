namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string BingzhongDrawReason = "skill-program.yang-fu.bingzhong.draw", SuyiEquipmentBind = "suyi-equipment";
    private static CardConversionSource YangFuSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int BingzhongCurrentX() => checked(1 + _players.Count(p => p.IsAlive && GetHand(p).Count == 0));
    private YangFuReceipt NewYangFuReceipt(ProgramSkillFrame f, SkillProgramEffectOp op, YangFuStage stage, int target, int x,
        int? ordinal = null, CardUseFrame? use = null) => new(YangFuSource(f), f.GameplayHash, op, _turnNumber, f.WindowContext?.ParentFrameId ?? 0,
            target, x, ordinal, use?.Id, use?.Action?.ActionId, stage, use?.TargetSeats ?? [], [], _movementSequence, _movementSequence);
    private void StartYangFu(ProgramSkillFrame f, YangFuReceipt r)
    {
        if (f.YangFu is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0) throw new InvalidOperationException("Yang Fu cannot replace an issued owning receipt.");
        ReplaceRuntimeTop(f with { YangFu = r });
        AdvanceEventRulesAndQueueFact(new YangFuStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ActualTurn, r.ParentId, r.TargetSeat, r.X, r.Ordinal, r.UseId, r.ActionId, r.OriginalTargets.Count));
        for (var i = 0; i < r.OriginalTargets.Count; i++) AdvanceEventRulesAndQueueFact(new BingzhongOriginalTargetEvent(f.Id, i, r.OriginalTargets[i]));
    }
    private SkillProgramStepOutcome BeginBingzhongDraw(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:bingzhong" || f.TriggerId != "actual-damage-dynamic-x" || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: not null } c || c.TargetSeat != f.OwnerSeat)
            throw new InvalidOperationException("Bingzhong requires its true whole positive damage occurrence.");
        var r = NewYangFuReceipt(f, SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw, YangFuStage.DrawChildren, f.OwnerSeat, BingzhongCurrentX());
        StartYangFu(f, r); f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var count = DrawCards(_players[f.OwnerSeat], r.X, true, new(BingzhongDrawReason)).Count;
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { YangFu = f.YangFu! with { After = _movementSequence, DrawCount = count } });
        AdvanceEventRulesAndQueueFact(new BingzhongDrawPaidEvent(f.Id, r.X, count, r.Before, _movementSequence));
        AdvanceRuntimeProgram(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private SkillProgramStepOutcome BeginSuyi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:suyi" || f.TriggerId is not null || f.WindowContext is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
            f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Suyi requires its actual one-other-target native Play activation.");
        StartYangFu(f, NewYangFuReceipt(f, SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash, YangFuStage.EquipmentUse, target, 0));
        f = GetActiveProgramFrame(f.Id);
        var outcome = UseProgramRandomDeckEquipment(f, target, SuyiEquipmentBind);
        if (outcome == SkillProgramStepOutcome.Continue)
        {
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { YangFu = f.YangFu! with { Stage = YangFuStage.EquipmentChildren }, PendingMovementContinuation = new(target, 0, null) });
            AdvanceEventRulesAndQueueFact(new SuyiNoEquipmentEvent(f.Id, target)); AdvanceRuntimeProgram(f.Id);
        }
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainYangFuChildren(long id) => TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 27310)]
    private bool ResumeYangFu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { YangFu: { } r } f || f.Id != id) return false;
        AssertYangFu(f);
        if (r.Stage is YangFuStage.Targets or YangFuStage.EquipmentUse or YangFuStage.SlashUse) return true;
        if (DrainYangFuChildren(id)) return true;
        f = GetActiveProgramFrame(id); r = f.YangFu!;
        if (r.Stage != YangFuStage.EquipmentChildren || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.TargetSeat].IsAlive ||
            !CanUseVirtualSlashTarget(_players[f.OwnerSeat], _players[r.TargetSeat]) || IsDirectedCardTargetProhibited(f.OwnerSeat, r.TargetSeat, CardKind.Slash) ||
            IsCardTargetProhibited(_players[r.TargetSeat], CardKind.Slash, Suit.None, null)) { FinishYangFu(f); return true; }
        ReplaceRuntimeTop(f = f with { YangFu = r with { Stage = YangFuStage.SlashUse, NativeOrigin = null, NativeCompleted = false, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = null });
        _ = UseTriggeredProgramVirtualSlash(f, r.TargetSeat); return true;
    }
    private void FinishYangFu(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new YangFuCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { YangFu = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27310)]
    private bool ReturnYangFuMovement(ProgramSkillFrame f) => f.YangFu is not null && ResumeYangFu(f.Id);
    [ProgramCapability] private sealed class YangFuCapability(GameEngine e) : IYangFuHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveBingzhongExtraTargets => e.BeginBingzhongTargets(f),
            SkillProgramEffectOp.ResolveBingzhongAfterDamageDraw => e.BeginBingzhongDraw(f),
            SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash => e.BeginSuyi(f),
            _ => throw new InvalidOperationException("Unknown Yang Fu native operation.")
        };
    }
}
