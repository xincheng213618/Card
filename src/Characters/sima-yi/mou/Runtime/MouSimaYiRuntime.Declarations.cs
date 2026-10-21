namespace CardGame.Core;
internal sealed partial class MouSimaYiRuntime
{
    internal void DeclarationReturned(ProgramSkillFrame f,long declarationId,bool succeeded)
    {
        if(!Valid(f)||f.MouSimaYi is not{Stage:MouSmStage.PlayedDeclaration,Played:{}plan,Response:null,ResponseActionId:0}r||!facts.Declaration(plan)||facts.History<MouSmDeclarationReturnedEvent>().Any(e=>e.DeclarationId==declarationId))throw new InvalidOperationException("Pingliao requires the unique actual native declaration return.");
        events.AdvanceFact(new MouSmDeclarationReturnedEvent(f.Id,r.Cursor,declarationId,succeeded));
        frames.ReplaceTop(f=f with{MouSimaYi=r with{Stage=succeeded?MouSmStage.PlayedAccepted:MouSmStage.PlayedChildren,After=facts.Sequence},PendingMovementContinuation=succeeded?null:new(f.OwnerSeat,0,null)});
        if(succeeded)native.Play(f);else Continue(f.Id);
    }
}
