namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasTurnRedSlashCapability=>_contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GrantTurnRedSlashBenefits)==true;
    private bool CapturesActionColor=>TracksActionDiscardColor||HasTurnRedSlashCapability;
    private void GrantTurnRedSlashBenefits(ProgramSkillFrame frame)
    {
        ValidateProgramTurnEffectGrant(frame);
        var change=frame.WindowContext?.HpChange;
        if(change?.Kind!=HpChangeKind.Loss || change.TargetSeat!=frame.OwnerSeat) throw new InvalidOperationException("Red Slash benefits require the owner's actual Loss.");
        if(change.LossOccurrence is not {ActualTurnNumber:>0,Phase:TurnPhase.Play} occurrence || occurrence.TurnOwnerSeat!=frame.OwnerSeat) return;
        if(occurrence.ActualTurnNumber!=_turnNumber || occurrence.TurnOwnerSeat!=_currentSeat) throw new InvalidOperationException("A Loss benefit crossed its actual turn.");
        var source=CreateProgramTurnEffectSource(frame);
        var modifier=_turnCardUseEffects.GrantRuleModifier(_turnNumber,_currentSeat,frame.Id,frame.InstructionIndex-1,source,SkillRuleQuery.SlashLimit,SkillRuleOperation.Add,1);
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(modifier));
        var policy=_turnCardUseEffects.GrantRedSlashPolicy(_turnNumber,_currentSeat,frame.Id,frame.InstructionIndex-1,source);
        AdvanceEventRulesAndQueueFact(new TurnRedSlashPolicyGrantedEvent(policy));
    }
    private bool HasTurnRedSlashPolicy(int actor,CardKind kind,Suit? suit)=>HasTurnRedSlashPolicyForColor(actor,kind,SuitColor(suit));
    private bool HasTurnRedSlashPolicyForColor(int actor,CardKind kind,bool? color)=>IsSlashCard(kind)&&color==true&&_turnCardUseEffects.HasRedSlashPolicy(_turnNumber,_currentSeat,actor);
    private bool? PhysicalGroupColor(CharacterState owner,IReadOnlyList<Card> cards)
    { var colors=cards.Select(c=>SuitColor(EffectiveSuit(owner,c))).Distinct().ToArray();return colors.Length==1?colors[0]:null; }
    private bool IsRedSlashProviderPaymentLegal(FactionCardRequestHandle pending,CardKind kind,IReadOnlyList<Card> cards)
    {
        if(!IsFactionSlashUse(pending) || pending.IsAssistedProgramUse || pending.TargetSeat is not {} target || !_turnCardUseEffects.HasRedSlashPolicy(_turnNumber,_currentSeat,pending.OwnerSeat))return true;
        var actor=_players[pending.OwnerSeat];var color=PhysicalGroupColor(actor,cards);
        if(pending.IsBorrowedSwordUse)return IsLegalBorrowedSwordSlashTarget(actor,_players[target],kind,allowAnyPhysicalSuit:false,effectiveColor:color);
        if(pending.IsQinglongCrescentBladeUse)return CanUseQinglongCrescentBladeTarget(actor,_players[target],kind,color,allowAnyColor:false);
        return CanUseProvidedSlashTarget(actor,_players[target],kind,allowAnyPhysicalSuit:false,effectiveColor:color);
    }
    private sealed partial class ProgramSkillHost : ITurnRedSlashProgramHost
    { public void GrantTurnRedSlashBenefits(ProgramSkillFrame frame)=>engine.GrantTurnRedSlashBenefits(frame); }
}
