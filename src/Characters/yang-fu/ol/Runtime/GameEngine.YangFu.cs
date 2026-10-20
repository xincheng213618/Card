namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string BingzhongDrawReason = "skill-program.yang-fu.bingzhong.draw", SuyiEquipmentBind = "suyi-equipment";
    private static CardConversionSource YangFuSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_yang_fu_ol_YangFu.YangFuSource(f);
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
