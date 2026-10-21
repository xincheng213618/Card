namespace CardGame.Core;
public sealed partial class GameEngine
{
    private ProgramSkillFrame? MouLuSuSlotParent(EquipmentSlotNamedUseDraft slot) => slot.Intent == EquipmentSlotNamedUseIntent.Play && slot.OriginalParentId is { } id && _resolutionStack.SingleOrDefault(x => x.Id == id) is ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.Issuing, UseId: 0, SelectedAction: { } action, OriginalDecision: { } original } r } f && MouLuSuAnniversary.Valid(f) && slot.Source == action.ConversionSource && slot.EffectiveKind == action.PlayedCardKind && slot.Source.OwnerSeat == r.Other && slot.OriginalDecision.PromptId == original.PromptId && slot.PlayAction is not null && System.Text.Json.JsonSerializer.Serialize(slot.PlayAction) == System.Text.Json.JsonSerializer.Serialize(action) ? f : null;
    private bool MouLuSuPaymentParent(long paymentId,long? parent,long? request,int cursor,CardConversionSource source,CardKind kind,IReadOnlyList<int> targets)
    {
        var at = _resolutionStack.FindIndex(x => x.Id == paymentId);
        return at > 0 && parent == request && _resolutionStack[at - 1] is ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.Issuing, UseId: 0, ActionId: 0, SelectedAction: { } a, OriginalDecision: { } original } r } f && f.Id == parent && f.InstructionIndex == cursor && MouLuSuAnniversary.Valid(f) && r.Other == source.OwnerSeat && a.ConversionSource == source && a.PlayedCardKind == kind && a.TargetSeats.SequenceEqual(targets) && original.PlayerSeat == r.Other && original.PromptId.Value > 0;
    }
    private bool IsMouLuSuPaymentFrame(DrawFundedDistinctBasicFrame p) => p.Payment.Intent == DrawFundedDistinctBasicIntent.Play && MouLuSuPaymentParent(p.Id,p.Payment.ParentFrameId,p.Payment.RequestFrameId,p.Payment.Cursor,p.Payment.Source,p.Payment.EffectiveKind,p.TargetSeats);
    private bool IsMouLuSuPaymentFrame(ChainedStateBasicFrame p) => p.Payment.Intent == ChainedStateBasicIntent.Play && MouLuSuPaymentParent(p.Id,p.Payment.ParentFrameId,p.Payment.RequestFrameId,p.Payment.Cursor,p.Payment.Source,p.Payment.EffectiveKind,p.TargetSeats);
    private bool TryCancelMouLuSuPayment(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuSuAnniversary: { Stage: MouLuSuAnniversaryStage.Issuing, UseId: 0 } r } f || f.Id != id || !MouLuSuAnniversary.Valid(f)) return false;
        ReplaceRuntimeTop(f with { MouLuSuAnniversary = r with { Stage = MouLuSuAnniversaryStage.UseChildren, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat,0,null) }); MouLuSuAnniversary.Continue(id); return true;
    }
}
