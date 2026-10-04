namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanPlaceCapturedEquipmentCost(int owner,int recipient,int id)
    {
        var card=GetAdvancedCard(id);var from=_cardZones.GetLocation(id);
        return recipient!=owner &&!card.IsGeneralWeapon &&CanPlaceOwnedEquipment(owner,recipient,card,from) &&
            !IsActiveProgramSourceEquipmentCard(owner,_resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.SkillId??"",
                _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()?.SkillInstanceId??"",card);
    }

    private bool CanPlaceCapturedEquipmentActivation(int owner,int recipient,int id,string skillId)
    {
        var card=GetAdvancedCard(id);var from=_cardZones.GetLocation(id);
        return recipient!=owner &&!card.IsGeneralWeapon &&CanPlaceOwnedEquipment(owner,recipient,card,from) &&
            !_players[owner].SkillGrants.Grants.Any(b=>b.IsEnabled &&b.SkillId==skillId &&IsActiveProgramSourceEquipmentCard(owner,skillId,b.SkillInstanceId,card));
    }

    private SkillProgramStepOutcome BeginCapturedEquipmentAndDraw(ProgramSkillFrame f,int recipient,string bind,int requested)
    {
        var set=GetProgramCardSet(f,bind);
        if(f.CapturedEquipmentDraw is not null ||f.TriggerId is not null ||_resolutionStack.LastOrDefault()?.Id!=f.Id ||
            set.CardIds is not [var id] ||set.SourceLocations is not [var from] ||!set.CardIds.SequenceEqual(f.SelectedCardIds) ||
            f.SelectedTargetSeats is not [var target] ||target!=recipient ||target==f.OwnerSeat ||from.OwnerSeat!=f.OwnerSeat ||
            from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("Captured equipment placement lost its unpaid exact active source/material/recipient.");
        if(_currentSeat!=f.OwnerSeat ||_phase!=TurnPhase.Play ||!CanPlaceCapturedEquipmentCost(f.OwnerSeat,recipient,id) ||
            _cardZones.GetLocation(id)!=from ||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))
        {CancelProgramBindingAndCleanup(f,"直置前参与者、所选实体或技能来源已失效，未支付装备。");return SkillProgramStepOutcome.AwaitChild;}
        var card=GetAdvancedCard(id);var slot=EquipmentCatalog.Get(card.Kind).Slot;
        var equipped=GetEquipment(_players[recipient]).Where(c=>EquipmentCatalog.Get(c.Kind).Slot==slot).ToArray();
        var replaced=equipped.Length>=_players[recipient].EquipmentSlotCapacity(slot) ? equipped[0] : null;
        var before=_cardMovements.LastOrDefault()?.Sequence??0;
        var source=new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
        var receipt=new ProgramCapturedEquipmentDrawReceipt(f.InstructionIndex,bind,source,f.GameplayHash,_turnNumber,recipient,id,from,slot,
            replaced?.Id,replaced?.IsGeneralWeapon??false,before,before,0,CapturedEquipmentDrawStage.Placed);
        ReplaceRuntimeTop(f with { PendingMovementContinuation=new(f.OwnerSeat,0,null),CapturedEquipmentDraw=receipt });
        if(replaced is not null)
        { CopyFirstReplacedWeapon(card,replaced);MoveCard(replaced,CardLocation.Equipment(recipient),CardLocation.DiscardPile,CardMoveReasons.EquipmentReplace); }
        MoveCard(card,from,CardLocation.Equipment(recipient),CardMoveReasons.EquipmentEnter);
        var after=_cardMovements.LastOrDefault()?.Sequence??before;
        var entry=_cardMovements.Single(m=>m.Sequence>before &&m.Sequence<=after &&m.CardId==id &&m.From==from &&m.To==CardLocation.Equipment(recipient) &&m.Reason==CardMoveReasons.EquipmentEnter);
        var active=GetActiveProgramFrame(f.Id);
        receipt=receipt with { SequenceAfter=after,EntryMovementSequence=entry.Sequence };
        ReplaceRuntimeTop(active with { CapturedEquipmentDraw=receipt });
        AdvanceEventRulesAndQueueFact(new EquipmentChangedEvent(f.Id,recipient,slot,id,card.Kind,replaced?.Id));
        AdvanceEventRulesAndQueueFact(new CapturedEquipmentPlacedEvent(f.Id,source,f.GameplayHash,recipient,id,from,slot,
            replaced?.Id,replaced?.IsGeneralWeapon??false,before,after,entry.Sequence));
        AddLog("EquipmentChanged",$"{_players[f.OwnerSeat].Name} 将【{card.DisplayName}】置入 {_players[recipient].Name} 的装备区。",recipient);
        AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeCapturedEquipmentAndDraw(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f ||f.Id!=id ||f.CapturedEquipmentDraw is not { } r)return false;
        AssertCapturedEquipmentAndDraw(f);
        if(DrainCapturedEquipmentOrDiscardChildren(f))return true;
        f=GetActiveProgramFrame(id);r=f.CapturedEquipmentDraw!;
        if(f.PendingMovementContinuation is not null)ReplaceRuntimeTop(f=f with { PendingMovementContinuation=null });
        if(r.Stage==CapturedEquipmentDrawStage.Placed &&_players[f.OwnerSeat].IsAlive &&_winner==Winner.None)
        {
            var effect=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
            var before=_cardMovements.LastOrDefault()?.Sequence??0;
            ReplaceRuntimeTop(f with { PendingMovementContinuation=new(f.OwnerSeat,0,null),CapturedEquipmentDraw=r with
                { Stage=CapturedEquipmentDrawStage.Drawn,DrawSequenceBefore=before } });
            var reason=new CardMoveReason($"skill-program.{f.SkillId}.captured-equipment-reward");
            DrawCards(_players[f.OwnerSeat],effect.Amount,true,reason);
            var after=_cardMovements.LastOrDefault()?.Sequence??before;
            var actual=_cardMovements.Count(m=>m.Sequence>before &&m.Sequence<=after &&m.From==CardLocation.DrawPile &&m.To==CardLocation.Hand(f.OwnerSeat) &&m.Reason==reason);
            var active=GetActiveProgramFrame(id);
            ReplaceRuntimeTop(active with { CapturedEquipmentDraw=active.CapturedEquipmentDraw! with { DrawSequenceAfter=after,ActualDrawCount=actual } });
            AdvanceEventRulesAndQueueFact(new CapturedEquipmentRewardDrawnEvent(id,f.OwnerSeat,effect.Amount,actual,before,after));
            AdvanceRuntimeProgram(id);return true;
        }
        ReplaceRuntimeTop(f with { CapturedEquipmentDraw=null });AdvanceRuntimeProgram(id);return true;
    }

    private void AssertCapturedEquipmentAndDraw(ProgramSkillFrame f)
    {
        if(f.CapturedEquipmentDraw is not { } r)return;
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!);
        var op=plan.GetPausedInstruction(f.InstructionIndex).Effect;var set=GetProgramCardSet(f,r.SourceBind);
        if(op.Op!=SkillProgramEffectOp.PlaceCapturedEquipmentAndDraw ||op.SourceBind!=r.SourceBind ||r.InstructionIndex!=f.InstructionIndex ||
            r.Source!=new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId) ||r.GameplayHash!=f.GameplayHash ||
            f.TriggerId is not null ||r.TurnNumber!=_turnNumber ||f.SelectedTargetSeats is not [var target] ||target!=r.RecipientSeat ||target==f.OwnerSeat ||
            !set.CardIds.SequenceEqual([r.CardId]) ||!set.SourceLocations.SequenceEqual([r.OriginalFrom]) ||!f.SelectedCardIds.SequenceEqual(set.CardIds) ||
            r.OriginalFrom.OwnerSeat!=f.OwnerSeat ||r.OriginalFrom.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            GetAdvancedCard(r.CardId).IsGeneralWeapon ||!EquipmentCatalog.IsEquipment(GetAdvancedCard(r.CardId).Kind) ||
            EquipmentCatalog.Get(GetAdvancedCard(r.CardId).Kind).Slot!=r.Slot ||!Enum.IsDefined(r.Stage) ||r.SequenceAfter<=r.SequenceBefore ||
            r.SequenceBefore<0 ||r.Stage==CapturedEquipmentDrawStage.Placed &&(r.DrawSequenceBefore!=0 ||r.DrawSequenceAfter!=0 ||r.ActualDrawCount!=0) ||
            r.EntryMovementSequence<=r.SequenceBefore ||r.EntryMovementSequence>r.SequenceAfter ||
            _cardMovements.Count(m=>m.Sequence==r.EntryMovementSequence &&m.CardId==r.CardId &&m.From==r.OriginalFrom &&
                m.To==CardLocation.Equipment(target) &&m.Reason==CardMoveReasons.EquipmentEnter)!=1 ||
            CompleteProgramEventHistory().OfType<CapturedEquipmentPlacedEvent>().Count(e=>e.ProgramFrameId==f.Id &&e.Source==r.Source &&
                e.GameplayHash==r.GameplayHash &&e.RecipientSeat==target &&e.CardId==r.CardId &&e.OriginalFrom==r.OriginalFrom &&e.Slot==r.Slot &&
                e.ReplacedCardId==r.ReplacedCardId &&e.ReplacedGeneralWeapon==r.ReplacedGeneralWeapon &&e.SequenceBefore==r.SequenceBefore &&
                e.SequenceAfter==r.SequenceAfter &&e.EntryMovementSequence==r.EntryMovementSequence)!=1)
            throw new InvalidOperationException("Captured equipment placement lost its exact actual cost/entry and frozen original recipient.");
        var replacements=_cardMovements.Where(m=>m.Sequence>r.SequenceBefore &&m.Sequence<=r.SequenceAfter &&
            m.From==CardLocation.Equipment(target) &&m.Reason==CardMoveReasons.EquipmentReplace).ToArray();
        if(r.ReplacedCardId is { } replaced)
        {
            if(replacements is not [var movement] ||movement.CardId!=replaced ||replaced==r.CardId ||
                GetAdvancedCard(replaced).IsGeneralWeapon!=r.ReplacedGeneralWeapon ||movement.To!=(r.ReplacedGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile) ||
                !EquipmentCatalog.IsEquipment(movement.CardKind) ||EquipmentCatalog.Get(movement.CardKind).Slot!=r.Slot)
                throw new InvalidOperationException("Captured equipment placement lost its first replaced physical/generated identity.");
        }
        else if(replacements.Length>0 ||r.ReplacedGeneralWeapon)throw new InvalidOperationException("An unoccupied original slot cannot carry a replacement receipt.");
        var payment=_cardMovements.Where(m=>m.Sequence>r.SequenceBefore &&m.Sequence<=r.SequenceAfter).ToArray();
        var movedOx=GetAdvancedCard(r.CardId).Kind==CardKind.WoodenOx &&r.OriginalFrom==CardLocation.Equipment(f.OwnerSeat);
        var replacedOx=r.ReplacedCardId is { } oldOx &&GetAdvancedCard(oldOx).Kind==CardKind.WoodenOx;
        if(payment.Any(m=>!(m.Sequence==r.EntryMovementSequence &&m.CardId==r.CardId &&m.From==r.OriginalFrom &&
                m.To==CardLocation.Equipment(target) &&m.Reason==CardMoveReasons.EquipmentEnter) &&
            !(m.CardId==r.ReplacedCardId &&m.From==CardLocation.Equipment(target) &&
                m.To==(r.ReplacedGeneralWeapon ? CardLocation.OutsideGame : CardLocation.DiscardPile) &&m.Reason==CardMoveReasons.EquipmentReplace) &&
            !(movedOx &&m.From==CardLocation.WoodenOxGrain(f.OwnerSeat) &&m.To==CardLocation.WoodenOxGrain(target) &&m.Reason==CardMoveReasons.WoodenOxTransfer) &&
            !(replacedOx &&m.From==CardLocation.WoodenOxGrain(target) &&m.To==CardLocation.DiscardPile &&m.Reason==CardMoveReasons.WoodenOxGrainDiscard)))
            throw new InvalidOperationException("Captured equipment placement contains a movement outside its exact equipment/WoodenOx payment.");
        if(r.Stage==CapturedEquipmentDrawStage.Drawn)
        {
            if(r.ActualDrawCount<0 ||r.ActualDrawCount>op.Amount ||r.DrawSequenceBefore<r.SequenceAfter ||r.DrawSequenceAfter<r.DrawSequenceBefore ||
                _cardMovements.Count(m=>m.Sequence>r.DrawSequenceBefore &&m.Sequence<=r.DrawSequenceAfter &&m.From==CardLocation.DrawPile &&
                    m.To==CardLocation.Hand(f.OwnerSeat) &&m.Reason.Value==$"skill-program.{f.SkillId}.captured-equipment-reward")!=r.ActualDrawCount ||
                CompleteProgramEventHistory().OfType<CapturedEquipmentRewardDrawnEvent>().Count(e=>e.ProgramFrameId==f.Id &&e.OwnerSeat==f.OwnerSeat &&
                    e.Requested==op.Amount &&e.Actual==r.ActualDrawCount &&e.SequenceBefore==r.DrawSequenceBefore &&e.SequenceAfter==r.DrawSequenceAfter)!=1 ||
                _cardMovements.Any(m=>m.Sequence>r.DrawSequenceBefore &&m.Sequence<=r.DrawSequenceAfter &&
                    !(m.From==CardLocation.DrawPile &&m.To==CardLocation.Hand(f.OwnerSeat) &&m.Reason.Value==$"skill-program.{f.SkillId}.captured-equipment-reward") &&
                    !(m.From==CardLocation.DiscardPile &&m.To==CardLocation.DrawPile &&m.Reason==CardMoveReasons.Reshuffle)))
                throw new InvalidOperationException("Captured equipment placement lost its one-time real draw tail.");
        }
    }
}
