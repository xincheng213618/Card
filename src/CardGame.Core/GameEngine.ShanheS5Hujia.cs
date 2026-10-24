namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ClaimShanheS5JianxiongCards(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.SkillId != "shanhe:s5-jianxiong-14198" || frame.TriggerId != "lost-hp-draw-then-claim" ||
            frame.InstructionIndex != 2 || frame.NativeDraw is not null ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied,
                ParentFrameId: var parentId, DamageFrameId: var damageId } ||
            ActiveDamageTrigger is not { } window || window.Id != parentId || window.ParentFrameId != damageId ||
            window.TargetSeat != frame.OwnerSeat)
            throw new InvalidOperationException("S5 Jianxiong must return its native Draw before claiming the original damage's physical cards.");
        // This existing helper rechecks the causal attack and Processing zone,
        // retains group/extra-effect claim proofs, and returns normally if no
        // original material remains after the draw's actual child windows.
        ClaimProgramDamageCards(frame.Id, frame.OwnerSeat, new($"skill-program.{frame.SkillId}.ClaimDamageCards"));
    }

    private void BindShanheS5HujiaReward(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "shanhe:s5-hujia-995" || f.ShanheS5Hujia is not null || f.InstructionIndex != 1 ||
            f.WindowContext is not { OptionalChooserSeat: { } chooser } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramCardTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || context.CardUse?.CardActionId != parent.Action.ActionId ||
            context.Window != SkillProgramTriggerWindow.CardResponseAccepted ||
            parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            parent.Candidates[parent.CandidateIndex] is not { } candidate || candidate.SkillId != f.SkillId ||
            candidate.SkillInstanceId != f.SkillInstanceId || candidate.OwnerSeat != f.OwnerSeat || candidate.TriggerId != f.TriggerId ||
            parent.Action.EffectiveKind != CardKind.Dodge ||
            FactionActionRewardChooser(f.OwnerSeat, _contentRegistry.GetSkill(f.SkillId).Program!.Triggers.Single(t => t.Id == f.TriggerId), parent.Action) != chooser ||
            !HasSkillRoleQualification(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId, Role.Lord))
            throw new InvalidOperationException("S5 Hujia lost its actual qualified Lord, off-turn Wei chooser or native Dodge action.");
        var receipt = new ShanheS5HujiaRewardReceipt(new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId),
            f.GameplayHash, parent.Action.ActionId, parent.Id, chooser, _turnNumber, _currentSeat);
        ReplaceRuntimeTop(f with { ShanheS5Hujia = receipt });
        AdvanceEventRulesAndQueueFact(new ShanheS5HujiaRewardIssuedEvent(f.Id, receipt));
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 59600)]
    private void AssertShanheS5HujiaReward(ProgramSkillFrame f)
    {
        if (f.ShanheS5Hujia is not { } r) return;
        if (f.SkillId != "shanhe:s5-hujia-995" || r.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            f.GameplayHash != r.GameplayHash || f.InstructionIndex is < 1 or > 2 ||
            f.WindowContext?.ParentFrameId != r.WindowFrameId || f.WindowContext.OptionalChooserSeat != r.ChooserSeat ||
            r.CardActionId <= 0 || r.ActualTurnNumber != _turnNumber || r.ActualTurnOwnerSeat != _currentSeat ||
            r.ChooserSeat == f.OwnerSeat || r.ChooserSeat == _currentSeat ||
            ProgramEventHistory<ShanheS5HujiaRewardIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var issued] || issued.Receipt != r)
            throw new InvalidOperationException("S5 Hujia lost its accepted original-chooser reward invoice.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 59600)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 59600)]
    private bool ContinueAcceptedShanheS5HujiaReward(ProgramSkillFrame f)
    {
        if (f.ShanheS5Hujia is null) return false;
        AssertShanheS5HujiaReward(f); return true;
    }

    private sealed partial class ProgramSkillHost : IShanheS5HujiaHost, IShanheS5JianxiongHost
    {
        public void BindShanheS5HujiaReward(ProgramSkillFrame frame) => engine.BindShanheS5HujiaReward(frame);
        public void ClaimShanheS5JianxiongCards(ProgramSkillFrame frame) => engine.ClaimShanheS5JianxiongCards(frame);
    }
}
