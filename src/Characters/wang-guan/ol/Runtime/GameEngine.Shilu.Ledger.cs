namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasShiluCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShiluDrawRevealIdentity);

    private ProgramCardIdentityMatch? ShiluIdentity(CharacterState owner,Card card)
    {
        if(!HasShiluCapability||_cardZones.GetLocation(card.Id)!=CardLocation.Hand(owner.Seat)||CurrentHandEntry(owner.Seat,card.Id)is not{}entry)return null;
        var e=ProgramEventHistory<ShiluHandIdentityGrantedEvent>().LastOrDefault(e=>e.HolderSeat==owner.Seat&&e.CardId==card.Id&&e.HandEntry==entry&&
            !_cardMovements.Any(m=>m.CardId==card.Id&&m.Sequence>e.MovementCutoff&&m.From==CardLocation.Hand(owner.Seat)&&m.To!=m.From));
        if(e is null)return null;
        AssertShiluIdentity(e);
        var p=_contentRegistry.GetSkill(e.Source.SkillId).Program!;
        return new(p,new SkillProgramCardIdentity(e.Source.BindingId,[CardZoneKind.Hand],[],[],CardKind.Slash,new(SkillProgramConditionKind.Always,0,[])),e.Source);
    }
    private void AssertShiluIdentity(ShiluHandIdentityGrantedEvent e)
    {
        if(e.Source.SkillId!="ol:shilu"||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash||
            ProgramEventHistory<ShiluHandIdentityGrantedEvent>().Count(v=>v.FrameId==e.FrameId)!=1||
            ProgramEventHistory<WangGuanStartedEvent>().Where(v=>v.FrameId==e.FrameId).ToArray()is not[var started]||started.Source!=e.Source||started.GameplayHash!=e.GameplayHash||
            started.NativeId!=e.DamageFrameId||started.Op!=SkillProgramEffectOp.ResolveShiluDrawRevealIdentity||
            ProgramEventHistory<ShiluCandidateFrozenEvent>().Count(v=>v.FrameId==e.FrameId&&v.Entity.CardId==e.CardId&&v.Entity.HolderSeat==e.HolderSeat&&v.Entity.Entry==e.HandEntry)!=1||
            ProgramEventHistory<ShiluHolderChosenEvent>().Count(v=>v.FrameId==e.FrameId&&v.HolderSeat==e.HolderSeat&&v.MovementCutoff==e.MovementCutoff)!=1||
            ProgramEventHistory<ProgramCardsRevealedEvent>().Count(v=>v.FrameId==e.FrameId&&v.SkillId==e.Source.SkillId&&v.BindingId==e.Source.BindingId&&v.Bind==ShiluRevealBind&&v.Cards is[var card]&&card.Id==e.CardId)!=1)
            throw new InvalidOperationException("Shilu's persistent hand identity lost its exact actual damage, private entity selection or genuine public reveal.");
    }
}
