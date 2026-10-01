namespace CardGame.Core;
public sealed record SlashTargetsReplacedEvent(long FrameId,string SkillId,int OwnerSeat,long CardUseFrameId,IReadOnlyList<int> PreviousTargets,IReadOnlyList<int> Targets) : IGameEvent;
public sealed partial class GameEngine
{
    private bool HasCardActionPindianContinuation()
    {
        if(ActiveCardAttack is not { } attack || _resolutionStack.LastOrDefault() is not PindianFrame child || _resolutionStack.Count<3 ||
           _resolutionStack[^2] is not ProgramSkillFrame parent || child.ParentFrameId!=parent.Id ||
           parent.WindowContext is not {Window:SkillProgramTriggerWindow.CardUseBeforeTargetEffects or SkillProgramTriggerWindow.SlashBeforeResponse,CardUse:{ } use} context ||
           _resolutionStack[^3] is not ProgramCardTriggerWindowFrame window || window.Id!=context.ParentFrameId || window.ParentFrameId!=attack.ResolutionId ||
           window.Action.ActionId!=use.CardActionId || window.AttackOwnerFrameId != attack.ResolutionId) return false;
        var program=_contentRegistry.GetSkill(parent.SkillId).Program!;
        var plan=ProgramInstructionResolver.Default.Resolve(parent,program);
        return parent.InstructionIndex>0 && plan.Instructions[parent.InstructionIndex-1].Op==SkillProgramEffectOp.StartCardActionPindian &&
               child.ProgramResultBind==plan.Instructions[parent.InstructionIndex-1].ResultBind;
    }
    private sealed partial class ProgramSkillHost : ICardActionContestProgramHost
    {
        public void ReplaceAllSlashTargets(ProgramSkillFrame f,string bind)=>engine.ReplaceAllProgramSlashTargets(f,bind);
        public SkillProgramStepOutcome StartCardActionPindian(ProgramSkillFrame f,SkillProgramEffect e)=>engine.StartProgramCardActionPindian(f,e);
    }
    private bool CanOfferCardActionContest(CharacterState owner,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.ReplaceAllSlashTargets))
        {
            var use=_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Id==context.CardUse?.ParentCardUseFrameId);
            if(context.Window!=SkillProgramTriggerWindow.CardUseCommitted || use?.Action is not { } action || !IsSlashCard(action.EffectiveKind) || action.ActorSeat==owner.Seat || use.TargetSeats.Count==0 || use.TargetSeats.Contains(owner.Seat) || !IsWithinAttackRange(action.ActorSeat,owner.Seat) || GetHand(owner).Count==0) return false;
        }
        foreach(var effect in trigger.Effects.Where(e=>e.Op==SkillProgramEffectOp.StartCardActionPindian))
        {
            var opponent=effect.OpponentReference!.Kind==ProgramParticipantRef.Actor?context.CardUse?.ActorSeat:context.TargetSeat;
            if(context.CardUse is not { } action || !IsSlashCard(action.EffectiveKind) || opponent is not { } seat || seat==owner.Seat || !_players[seat].IsAlive || GetHand(owner).Count==0 || GetHand(_players[seat]).Count==0) return false;
        }
        return true;
    }
    private SkillProgramStepOutcome StartProgramCardActionPindian(ProgramSkillFrame frame,SkillProgramEffect effect)
    {
        var context=frame.WindowContext??throw new InvalidOperationException("Card contest lost its boundary.");
        var opponent=effect.OpponentReference!.Kind==ProgramParticipantRef.Actor?context.CardUse!.ActorSeat:context.TargetSeat!.Value;
        var use=_resolutionStack.OfType<CardUseFrame>().Single(f=>f.Id==context.CardUse!.ParentCardUseFrameId);
        if(!IsSlashCard(use.CardKind) || use.Action?.ActionId!=context.CardUse!.CardActionId || opponent==frame.OwnerSeat || GetHand(_players[frame.OwnerSeat]).Count==0 || GetHand(_players[opponent]).Count==0 || frame.PindianResultBindings.Any(b=>b.Name==effect.ResultBind)) throw new InvalidOperationException("Card contest lost its exact Slash or participants.");
        var skill=_contentRegistry.GetSkill(frame.SkillId);
        BeginSharedPindian(frame.Id,new(frame.SkillId,skill.Name,skill.Name+" · 拼点",skill.Description),frame.OwnerSeat,opponent,programResultBind:effect.ResultBind!,programResultVisibility:effect.Visibility);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void ReplaceAllProgramSlashTargets(ProgramSkillFrame frame,string bind)
    {
        var context=frame.WindowContext??throw new InvalidOperationException("Replacement lost its boundary.");
        var index=_resolutionStack.FindIndex(f=>f.Id==context.CardUse!.ParentCardUseFrameId);
        var use=(CardUseFrame)_resolutionStack[index];var action=use.Action!;
        var payment=frame.CardSetBindings.Single(b=>b.Name==bind);
        if(context.Window!=SkillProgramTriggerWindow.CardUseCommitted || !IsSlashCard(use.CardKind) || action.ActionId!=context.CardUse!.CardActionId || action.ActorSeat==frame.OwnerSeat || use.TargetSeats.Count==0 || use.TargetSeats.Contains(frame.OwnerSeat) || payment.CardIds.Count!=1 || payment.SourceLocations is not [var location] || location!=CardLocation.DrawPile) throw new InvalidOperationException("Replacement lost its actual owner-hand payment or Slash.");
        var targets = Array.AsReadOnly(action.EffectiveSuit is Suit.Spade or Suit.Club ? Array.Empty<int>() : new[] { frame.OwnerSeat });
        var updated=new CardActionContext(action.ActionId,action.ParentActionId,action.Type,action.ActorSeat,action.ProviderSeat,action.RequesterSeat,action.ResponderSeat,action.OpponentSeat,action.EffectiveKind,targets,action.PhysicalCards,action.ConversionChain,targets,action.EffectiveSuit,action.EffectiveRank);
        var attack = ProgramCardAttack ?? throw new InvalidOperationException("Replacement lost Slash continuation.");
        if (attack.ResolutionId != use.Id)
            throw new InvalidOperationException("Replacement lost its exact Slash owner.");
        ReplaceRuntimeFrame(use.Id, use with
        {
            TargetSeats = targets,
            TargetIndex = 0,
            Action = updated,
            SlashTargetsCancelled = use.SlashTargetsCancelled || targets.Count == 0,
            Continuations = use.Continuations with
            {
                FangtianHalberd = use.Continuations.FangtianHalberd is { Active: true } multi
                    ? multi with { TargetSeats = targets }
                    : use.Continuations.FangtianHalberd
            }
        });
        if (targets.Count > 0)
        {
            attack.SetDamageParticipants(attack.SourceSeat, frame.OwnerSeat);
            attack.SetIgnoresArmor(HasArmorBypass(_players[action.ActorSeat]) ||
                HasCardArmorBypass(_players[action.ActorSeat], _players[frame.OwnerSeat], use.CardKind));
        }
        AdvanceEventRulesAndQueueFact(new SlashTargetsReplacedEvent(frame.Id,frame.SkillId,frame.OwnerSeat,use.Id,use.TargetSeats,targets));
    }
}
