namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? HuanpeiActiveDraft() => _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.HuanShuJun is { Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, Request.Intent: not HuanpeiIntent.Play });
    private PendingDecision? HuanpeiInvariantDecision(PendingDecision? ordinary)
    { if (HuanpeiActiveDraft() is not { } f) return ordinary is null ? null : HuanpeiNativeDecision(ordinary); AssertHuanShuJun(f); return HuanpeiNativeDecision(f.HuanShuJun!.OriginalDecision!); }
    private ResolutionFrame? HuanpeiInvariantTop(ResolutionFrame? ordinary)
    { if (HuanpeiActiveDraft() is not { } f) return ordinary; AssertHuanShuJun(f); return _resolutionStack.Single(x => x.Id == f.HuanShuJun!.Request!.RequestFrameId); }
    private bool HuanpeiDraftRidesOn(ProgramSkillFrame draft, ProgramSkillFrame parent) =>
        draft.HuanShuJun is { Op: SkillProgramEffectOp.ResolveHuanpeiHandAdjustmentAndBasicUse, Request: { } n, OriginalDecision: { } decision } r && HuanShuJunPaidOrPreparing(draft) &&
        n.ParentFrameId == parent.Id && n.RequestFrameId == parent.Id && n.Cursor == parent.InstructionIndex && n.ParentSkillId == parent.SkillId && n.ParentSkillInstanceId == parent.SkillInstanceId && n.ParentGameplayHash == parent.GameplayHash && n.ParentOperation == GetPausedPrivateOfferEffect(parent)?.Op && n.ActorSeat == draft.OwnerSeat && decision.PlayerSeat == draft.OwnerSeat && decision.PromptId == n.OriginalPromptId && decision.Revision == n.OriginalRevision &&
        draft.Id > parent.Id && ProgramEventHistory<HuanpeiRequestCapturedEvent>().Count(e => e.FrameId == draft.Id && e.Request == n && e.DecisionHash == HuanShuJunDecisionHash(decision)) == 1;
}
