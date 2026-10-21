namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ProgramSkillFrame? MouKrLiwenSlotParent(EquipmentSlotNamedUseDraft slot)
    {
        if(slot.Intent!=EquipmentSlotNamedUseIntent.Play||slot.OriginalParentId is not{}id||
            _resolutionStack.SingleOrDefault(x=>x.Id==id)is not ProgramSkillFrame{MouKrLiwen:{Stage:MouKrLiwenStage.Issuing,UseId:0,SelectedAction:{}action,OriginalDecision:{}original}r}f||
            !MouKongRong.Valid(f)||slot.Source!=action.ConversionSource||slot.EffectiveKind!=action.PlayedCardKind||r.Schedule[r.Cursor].Seat!=slot.Source.OwnerSeat||
            slot.OriginalDecision.PromptId!=original.PromptId||slot.PlayAction is null||System.Text.Json.JsonSerializer.Serialize(slot.PlayAction)!=System.Text.Json.JsonSerializer.Serialize(action))return null;
        return f;
    }
    private bool MouKrLiwenPaymentParent(long paymentId,long? parentId,long? requestId,int cursor,CardConversionSource source,CardKind kind,IReadOnlyList<int> targets)
    {
        var index=_resolutionStack.FindIndex(x=>x.Id==paymentId);
        return index>0&&parentId==requestId&&_resolutionStack[index-1]is ProgramSkillFrame f&&f.Id==parentId&&f.InstructionIndex==cursor&&
            f.MouKrLiwen is{Stage:MouKrLiwenStage.Issuing,SelectedAction:{}action,OriginalDecision:{}original}r&&MouKongRong.Valid(f)&&r.UseId==0&&r.ActionId==0&&
            r.Schedule[r.Cursor].Seat==source.OwnerSeat&&action.ConversionSource==source&&action.PlayedCardKind==kind&&action.TargetSeats.SequenceEqual(targets)&&
            original.PlayerSeat==source.OwnerSeat&&original.PromptId.Value>0&&original.SkillPrompt?.SkillId==f.SkillId;
    }
    private bool IsMouKrLiwenPaymentFrame(DrawFundedDistinctBasicFrame paid)=>paid.Payment.Intent==DrawFundedDistinctBasicIntent.Play&&
        MouKrLiwenPaymentParent(paid.Id,paid.Payment.ParentFrameId,paid.Payment.RequestFrameId,paid.Payment.Cursor,paid.Payment.Source,paid.Payment.EffectiveKind,paid.TargetSeats);
    private bool IsMouKrLiwenPaymentFrame(ChainedStateBasicFrame paid)=>paid.Payment.Intent==ChainedStateBasicIntent.Play&&
        MouKrLiwenPaymentParent(paid.Id,paid.Payment.ParentFrameId,paid.Payment.RequestFrameId,paid.Payment.Cursor,paid.Payment.Source,paid.Payment.EffectiveKind,paid.TargetSeats);
    private bool TryCancelMouKrLiwenPayment(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouKrLiwen:{Stage:MouKrLiwenStage.Issuing,UseId:0}r}f||f.Id!=id||!MouKongRong.Valid(f))return false;
        // A paid source whose target ceased to be legal consumes this one use
        // opportunity. It neither repays the cost nor pretends to have used a card.
        ReplaceRuntimeTop(f with{MouKrLiwen=r with{Stage=MouKrLiwenStage.Children,NativeReturned=true,After=_movementSequence},PendingMovementContinuation=new(r.Schedule[r.Cursor].Seat,0,null)});
        MouKongRong.Continue(id);return true;
    }
}
