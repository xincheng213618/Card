namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int QujiaPermanentSlashBonus(int ownerSeat)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd)) return 0;
        var grants=ProgramEventHistory<QujiaSlashBonusGrantedEvent>().Where(e=>e.Source.OwnerSeat==ownerSeat).ToArray();
        foreach(var grant in grants)
            if(ProgramEventHistory<QujiaChoiceIssuedEvent>().Count(e=>e.FrameId==grant.FrameId&&e.Source==grant.Source&&e.GameplayHash==grant.GameplayHash&&e.ActualTurn==grant.ActualTurn&&e.Choice=="quota")!=1 ||
                ProgramEventHistory<YangFengStartedEvent>().Count(e=>e.FrameId==grant.FrameId&&e.Source==grant.Source&&e.GameplayHash==grant.GameplayHash&&e.Op==SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd&&e.ActualTurn==grant.ActualTurn)!=1 ||
                grants.Count(e=>e.FrameId==grant.FrameId)!=1) throw new InvalidOperationException("Qujia's permanent Slash benefit lost its unique original issued choice.");
        return grants.Length;
    }
}
