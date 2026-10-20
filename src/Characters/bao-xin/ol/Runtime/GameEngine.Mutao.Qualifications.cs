namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsIssuedBaoXinMutaoPile(PublicPersistentPileSource source)=>_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.BaoXin is{Paid:true,Pile:{}pile}r&&pile==source&&r.Op==SkillProgramEffectOp.ResolveBaoXinMutao&&r.Source==BaoXinSource(f)&&r.GameplayHash==f.GameplayHash&&f.InstructionIndex==1&&f.SkillId=="ol:mutao"&&r.Original.Count>0&&_publicPersistentPiles.GetValueOrDefault((source.OwnerSeat,source.SkillId,source.SkillInstanceId))==source&&ProgramEventHistory<BaoXinStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Holder==r.Holder)==1&&r.Original.Select((c,i)=>(c,i)).All(x=>ProgramEventHistory<BaoXinOriginalEntityEvent>().Count(e=>e.FrameId==f.Id&&e.Index==x.i&&e.CardId==x.c.CardId&&e.Kind==x.c.Kind&&e.EffectiveKind==x.c.EffectiveKind&&e.From==x.c.From)==1));
}
