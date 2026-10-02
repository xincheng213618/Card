namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AssertShownGiftReceipt(ProgramSkillFrame frame, IReadOnlyList<SkillProgramEffect> instructions)
    {
        if (frame.ShownGiftReceipt is not { } receipt) return;
        if (receipt.InstructionIndex < 0 || receipt.InstructionIndex >= frame.InstructionIndex ||
            receipt.InstructionIndex >= instructions.Count ||
            instructions[receipt.InstructionIndex].Op != SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit ||
            receipt.BatchId <= 0 || receipt.ActualTurnNumber <= 0 ||
            !IsValidPlayerSeat(receipt.ActualTurnOwnerSeat) || !IsValidPlayerSeat(receipt.RecipientSeat) ||
            receipt.RecipientSeat == frame.OwnerSeat || frame.SelectedTargetSeats is not [var target] || target != receipt.RecipientSeat ||
            receipt.Cards.Select(c => c.CardId).Distinct().Count() != receipt.Cards.Count ||
            receipt.Cards.Any(c => !Enum.IsDefined(c.Category)))
            throw new InvalidOperationException("A shown gift lost its owning instruction or actual receipt.");
        var source = GetProgramCardSet(frame, instructions[receipt.InstructionIndex].SourceBind!);
        if (source.Visibility != SkillProgramCardSetVisibility.Public || receipt.Cards.Any(c =>
            !source.CardIds.Contains(c.CardId) || c.Destination != CardLocation.Hand(receipt.RecipientSeat) ||
            c.Source == c.Destination || c.Source != source.SourceLocations[source.CardIds.ToList().IndexOf(c.CardId)] ||
            !_cardMovements.Any(m => m.Sequence == c.MovementSequence && m.CardId == c.CardId &&
                m.From == c.Source && m.To == c.Destination && m.TurnNumber == receipt.ActualTurnNumber &&
                GetProgramCardCategory(m.CardKind) == c.Category)))
            throw new InvalidOperationException("A shown gift receipt differs from its public selection.");
        var x = receipt.Cards.Select(c => c.Category).Distinct().Count();
        var modifier = _turnCardUseEffects.RuleModifiers.SingleOrDefault(m => m.GrantSequence == receipt.ModifierGrantSequence);
        if (x == 0 ? receipt.ModifierGrantSequence is not null : modifier is null ||
            modifier.ParentFrameId != frame.Id || modifier.EffectIndex != receipt.InstructionIndex ||
            modifier.TurnNumber != receipt.ActualTurnNumber || modifier.TurnSeat != receipt.ActualTurnOwnerSeat ||
            modifier.AffectedSeat != receipt.ActualTurnOwnerSeat || modifier.Query != SkillRuleQuery.HandLimit ||
            modifier.Operation != SkillRuleOperation.Add || modifier.Amount != x || modifier.Source.SkillId != frame.SkillId)
            throw new InvalidOperationException("A shown gift lost its actual-turn hand-limit fact.");
    }

    private sealed partial class ProgramSkillHost : IShownBoundGiftProgramHost
    {
        public SkillProgramStepOutcome GiveShownBoundCardsAndGrantTurnHandLimit(ProgramSkillFrame f,string bind) => engine.GiveProgramShownBoundGift(f,bind);
    }

    private SkillProgramStepOutcome GiveProgramShownBoundGift(ProgramSkillFrame frame,string bind)
    {
        var f=GetActiveProgramFrame(frame.Id);
        ValidateProgramTurnEffectGrant(f);
        if(f.ShownGiftReceipt is not null) throw new InvalidOperationException("A shown gift cannot be paid twice.");
        if(f.SelectedTargetSeats.Count!=1) throw new InvalidOperationException("A shown gift requires one recipient.");
        var recipient=f.SelectedTargetSeats[0];
        if(!IsValidPlayerSeat(recipient)||recipient==f.OwnerSeat||!_players[recipient].IsAlive)
        { CancelProgramBindingAndCleanup(f,"受牌角色失效，赠牌已取消。"); return SkillProgramStepOutcome.AwaitChild; }
        var source=GetProgramCardSet(f,bind);
        if(source.Visibility!=SkillProgramCardSetVisibility.Public||source.CardIds.Count==0||source.CardIds.Count!=source.SourceLocations.Count||
            source.CardIds.Distinct().Count()!=source.CardIds.Count||source.SourceLocations.Any(l=>l.OwnerSeat!=f.OwnerSeat||l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))||
            source.CardIds.Where((id,i)=>_cardZones.GetLocation(id)!=source.SourceLocations[i]).Any())
            throw new InvalidOperationException("A shown gift lost its exact public owned entities.");
        if(_turnProgression.OwnerSeat!=_currentSeat||_turnProgression.TurnNumber!=_turnNumber) throw new InvalidOperationException("A shown gift lost its actual turn.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation=new(f.OwnerSeat,0,null) });
        MoveProgramCardsFromMultipleSources(source.CardIds,CardLocation.Hand(recipient),new($"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveShownBoundCardsAndGrantTurnHandLimit}"),
            (batchId,movements)=>
            {
                var actual=movements.Where(m=>m.To==CardLocation.Hand(recipient)&&m.From!=m.To)
                    .Select(m=>new ProgramShownGiftCard(m.CardId,GetProgramCardCategory(m.CardKind),m.Sequence,m.From,m.To)).ToArray();
                var x=actual.Select(c=>c.Category).Distinct().Count();
                var active=GetActiveProgramFrame(f.Id);
                var receipt=new ProgramShownGiftReceipt(f.InstructionIndex-1,batchId,recipient,_turnNumber,_turnProgression.OwnerSeat,Array.AsReadOnly(actual),null);
                ReplaceRuntimeTop(active with { ShownGiftReceipt=receipt });
                if(x>0)
                {
                    var modifier=_turnCardUseEffects.GrantRuleModifier(receipt.ActualTurnNumber,receipt.ActualTurnOwnerSeat,f.Id,receipt.InstructionIndex,
                        CreateProgramTurnEffectSource(f),SkillRuleQuery.HandLimit,SkillRuleOperation.Add,x,affectedSeat:receipt.ActualTurnOwnerSeat);
                    ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { ShownGiftReceipt=receipt with { ModifierGrantSequence=modifier.GrantSequence } });
                    AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(modifier));
                }
                AdvanceEventRulesAndQueueFact(new ShownBoundGiftCommittedEvent(f.Id,f.SkillId,f.OwnerSeat,recipient,receipt.ActualTurnNumber,receipt.ActualTurnOwnerSeat,actual.Length,x));
            });
        return AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat);
    }
}
