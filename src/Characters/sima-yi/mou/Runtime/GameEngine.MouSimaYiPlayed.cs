namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsMouSmPlayedActor(CharacterState actor)=>_resolutionStack.LastOrDefault()is ProgramSkillFrame{MouSimaYi:{Operation:SkillProgramEffectOp.ResolveMouSimaYiPingliao,Stage:MouSmStage.Respondent or MouSmStage.PlayedAccepted or MouSmStage.PlayedDeclaration}r}f&&r.Actor==actor.Seat&&MouSimaYi.Valid(f);
    private NativePublicPileCardRequest? MouSmPublicPileRequest(CharacterState actor,CardKind kind)=>IsMouSmPlayedActor(actor)&&CardCatalog.Get(kind).CategoryName=="基本牌"?new(_resolutionStack[^1].Id,_pendingDecision?.PromptId,actor.Seat,actor.Seat,null,actor.Seat,kind,null,NativePublicPileDirection.Played){RequestCursor=((ProgramSkillFrame)_resolutionStack[^1]).MouSimaYi!.Cursor}:null;
    private bool MouSmPlanIsRed(CharacterState actor,MouSmPlayedPlan plan)
    {
        var cards=plan.Cards.Select(GetAdvancedCard).ToArray();if(cards.Length==0)return false;
        var appearance=ApplyProgramUseAppearance(actor,cards[0],plan.Source);
        var suit=UnclaimedDeclarationPayment(actor.Seat,cards[0].Id)?.FrozenSuit??CaptureUsedCardSuit(actor.Seat,plan.Cards,appearance);
        var costs=cards.Select(c=>UnclaimedDeclarationPayment(actor.Seat,c.Id)?.Cost??new CardActionCost(c.Id,c.Kind,FindOwnedCardLocation(actor,c),CapturePhysicalCardColor(actor.Seat,c))).ToArray();
        return CaptureActionColor(costs,suit)==true;
    }
    private IReadOnlyList<MouSmPlayedPlan> MouSmPlayedPlans(ProgramSkillFrame f)
    {
        if(!MouSimaYi.Valid(f)||f.MouSimaYi is not{Operation:SkillProgramEffectOp.ResolveMouSimaYiPingliao}r||!IsValidPlayerSeat(r.Actor)||!_players[r.Actor].IsAlive)return[];
        var actor=_players[r.Actor];var plans=new List<MouSmPlayedPlan>();
        foreach(var required in new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash,CardKind.Dodge,CardKind.Peach,CardKind.Alcohol})
        {
            foreach(var card in GetResponseCards(actor,required).Where(c=>CanUseCardAsResponse(actor,c,required)))
            {
                var kind=GetEffectiveResponseKind(actor,card,required);var identity=HasProgramCardIdentity(actor,card);
                if(!identity&&IsNativeResponseCard(card,required)&&(!IsOwnPublicPileCard(actor,card)||IsNativePublicPileOriginalChoice(actor,card,kind,true)))plans.Add(new(kind,null,false){Cards=[card.Id]});
                foreach(var source in identity?GetProgramCardIdentitySources(actor,card,kind,true):GetProgramViewAsConversions(actor,card,kind,true))plans.Add(new(kind,source,false){Cards=[card.Id]});
            }
            plans.AddRange(GetProgramMultiCardViewAsSelections(actor,required,true).Where(p=>!IsXieweiSelection(p)).Select(p=>new MouSmPlayedPlan(p.OutputKind,p.Source,false){Cards=p.Cards.Select(c=>c.Id).ToArray()}));
        }
        plans.AddRange(GetZhangbaSlashPairs(actor).Select(p=>new MouSmPlayedPlan(CardKind.Slash,null,true){Cards=p.Select(c=>c.Id).ToArray()}));
        return Array.AsReadOnly(plans.Where(p=>MouSmPlanIsRed(actor,p)).DistinctBy(p=>(p.Kind,p.Source,p.Zhangba,string.Join(",",p.Cards))).ToArray());
    }
    private static bool SameMouSmPlayedPlan(MouSmPlayedPlan a,MouSmPlayedPlan b) => a.Kind == b.Kind && a.Source == b.Source && a.Zhangba == b.Zhangba && a.Cards.SequenceEqual(b.Cards);
    private void IssueMouSmPlayed(ProgramSkillFrame f)
    {
        if (!MouSimaYi.Valid(f) || f.MouSimaYi is not { Stage: MouSmStage.PlayedAccepted, Played: { } plan, Response: null, ResponseActionId: 0 } r || !MouSmPlayedPlans(f).Any(p => SameMouSmPlayedPlan(p,plan))) throw new InvalidOperationException("Pingliao lost its exact currently legal Played Slash.");
        var actor = _players[r.Actor]; CardActionContext action;
        if (plan.Source is { } multiSource && plan.Cards.Count > 1)
        {
            var selection = FindProgramMultiCardViewAsSelection(actor,plan.Cards,plan.Kind,true,multiSource) ?? throw new InvalidOperationException("Pingliao lost its original multi-card response materials.");
            action = MoveProgramMultiCardResponse(actor,selection,f.Id,f.OwnerSeat);
        }
        else
        {
            var cards = plan.Cards.Select(GetAdvancedCard).ToArray(); var costs = cards.Select(c => UnclaimedDeclarationPayment(actor.Seat,c.Id)?.Cost??new CardActionCost(c.Id,c.Kind,FindOwnedCardLocation(actor,c),CapturePhysicalCardColor(actor.Seat,c))).ToArray();
            var preparedSuit=FreezeCompletedResponseUseSuit(actor,cards,plan.Kind);var suit=UnclaimedDeclarationPayment(actor.Seat,cards[0].Id)?.FrozenSuit??preparedSuit??CaptureUsedCardSuit(actor.Seat,plan.Cards,ApplyProgramUseAppearance(actor,cards[0],plan.Source));
            PrepareWuZhangFeiResponseRanks(actor,plan.Kind,cards,plan.Source); PrepareLeCaoZhiWenlanResponse(actor,plan.Kind,cards); PrepareMouGszHuResponse(actor,cards,plan.Kind,plan.Source,null);
            if (cards is [var one]) PrepareNativePublicPileResponsePayment(actor,one,plan.Kind,plan.Source);
            if (plan.Source is { } source) { ValidateLastZoneConversionCommit(source,actor,cards,true); ValidateOwnPublicPileConversionCommit(source,actor,cards,true,plan.Kind); ConsumeProgramViewAsUsage([source]); }
            foreach (var card in cards) { MoveCard(card,FindOwnedCardLocation(actor,card),CardLocation.Processing,CardMoveReasons.Respond,disclosure:CardMovementDisclosure.FaceUp); AdvanceEventRulesAndQueueFact(new CardRespondedEvent(card.Id,actor.Seat,f.OwnerSeat,plan.Kind)); }
            action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,null,CardActionType.Response,actor.Seat,actor.Seat,null,actor.Seat,f.OwnerSeat,plan.Kind,[],costs,plan.Source is { } conversion ? [conversion] : [],effectiveSuit:suit,effectiveIsRed:CaptureActionColor(costs,suit)));
            BindWuZhangFeiResponseRanks(action); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
            if (plan.Source is { } converted) AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(f.Id,converted.SkillId,converted.BindingId,actor.Seat,plan.Cards,plan.Kind,false,[]));
        }
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat,plan.Kind);
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MouSimaYi = f.MouSimaYi! with { Response = action, ResponseActionId = action.ActionId, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouSmPlayedEvent(f.Id,r.Cursor,r.Actor,action.ActionId,false));
        if (!TryBeginProgramCardWindow(null,action,SkillProgramTriggerWindow.CardResponseAccepted,[f.OwnerSeat],null)) FinishMouSmPlayedAccepted(f);
    }
    private static bool SameMouSmAction(CardActionContext a,CardActionContext b)=>a.ActionId==b.ActionId&&a.ParentActionId==b.ParentActionId&&a.Type==b.Type&&a.ActorSeat==b.ActorSeat&&a.ProviderSeat==b.ProviderSeat&&a.RequesterSeat==b.RequesterSeat&&a.ResponderSeat==b.ResponderSeat&&a.OpponentSeat==b.OpponentSeat&&a.EffectiveKind==b.EffectiveKind&&a.EffectiveSuit==b.EffectiveSuit&&a.EffectiveRank==b.EffectiveRank&&a.EffectiveIsRed==b.EffectiveIsRed&&a.FactionOrigin==b.FactionOrigin&&a.TargetSeats.SequenceEqual(b.TargetSeats)&&(a.DesignatedTargetSeats is null?b.DesignatedTargetSeats is null:b.DesignatedTargetSeats is{}t&&a.DesignatedTargetSeats.SequenceEqual(t))&&a.PhysicalCards.SequenceEqual(b.PhysicalCards)&&a.ConversionChain.SequenceEqual(b.ConversionChain);
    private bool IsMouSmPlayedWindow(CardActionContext action,SkillProgramTriggerWindow window) => window is SkillProgramTriggerWindow.CardResponseAccepted or SkillProgramTriggerWindow.CardResponseCompleted && _resolutionStack.LastOrDefault() is ProgramSkillFrame { MouSimaYi: { Response: { } accepted } r } f && MouSimaYi.Valid(f) && action.ActionId == accepted.ActionId && action.Type == CardActionType.Response && action.ActorSeat == r.Actor && action.ProviderSeat == r.Actor && action.RequesterSeat is null && action.TargetSeats.Count == 0 && CardCatalog.Get(action.EffectiveKind).CategoryName == "基本牌" && action.EffectiveIsRed == true && r.ResponseActionId == action.ActionId && r.Stage == (window == SkillProgramTriggerWindow.CardResponseAccepted ? MouSmStage.PlayedAccepted : MouSmStage.PlayedCompleted);
    private MouSmPlayedReturn? MouSmPlayedWindowReturn(CardActionContext action,SkillProgramTriggerWindow window) => IsMouSmPlayedWindow(action,window) ? new(_resolutionStack[^1].Id,action.ActionId,window) : null;
    private bool ValidMouSmPlayedWindow(ProgramCardTriggerWindowFrame window) => window.MouSimaYiPlayed is { } back && window.Continuation is null && window.CompletedResponseReturn is null && window.AttackOwnerFrameId is null && window.ParentFrameId == back.FrameId && _resolutionStack.SingleOrDefault(x => x.Id == back.FrameId) is ProgramSkillFrame { MouSimaYi: { Response: { } response, Played: { } plan } r } f && MouSimaYi.Valid(f) && r.ResponseActionId == back.ActionId && window.Action.ActionId == back.ActionId && SameMouSmAction(response,window.Action) && window.Action.Type == CardActionType.Response && window.Action.ActorSeat == r.Actor && window.Action.ProviderSeat == r.Actor && window.Action.RequesterSeat is null && window.Action.TargetSeats.Count == 0 && window.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(plan.Cards) && back.Window == (r.Stage == MouSmStage.PlayedAccepted ? SkillProgramTriggerWindow.CardResponseAccepted : r.Stage == MouSmStage.PlayedCompleted ? SkillProgramTriggerWindow.CardResponseCompleted : (SkillProgramTriggerWindow)(-1)) && ProgramEventHistory<MouSmPlayedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor && e.ActionId == back.ActionId && e.Actor == r.Actor && !e.Completed) == 1;
    private bool TryReturnMouSmPlayedWindow(ProgramCardTriggerWindowFrame window)
    {
        if (window.MouSimaYiPlayed is not { } returned) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouSimaYi: { } r } f || f.Id != returned.FrameId || window.ParentFrameId != f.Id || window.Action.ActionId != returned.ActionId || r.ResponseActionId != returned.ActionId || !IsMouSmPlayedWindow(window.Action,returned.Window)) throw new InvalidOperationException("Pingliao response observer returned to another native Played receipt.");
        if (returned.Window == SkillProgramTriggerWindow.CardResponseAccepted) FinishMouSmPlayedAccepted(f); else FinishMouSmPlayedCompleted(f); return true;
    }
    private void FinishMouSmPlayedAccepted(ProgramSkillFrame f)
    {
        var r = f.MouSimaYi!; var action = r.Response!; if (!IsMouSmPlayedWindow(action,SkillProgramTriggerWindow.CardResponseAccepted)) throw new InvalidOperationException("Pingliao must complete its exact accepted response before disposing it.");
        ReplaceRuntimeTop(f = f with { MouSimaYi = r with { Stage = MouSmStage.PlayedCostChildren }, PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        foreach (var cost in action.PhysicalCards)
        {
            if (IsProgramAlternativeCost(action,cost.CardId) || IsExchangedCardClaim(action.ActionId,cost.CardId) || TryGetSiruiDestroyedResponseCost(action,cost.CardId,out _)) continue;
            if (_cardZones.GetLocation(cost.CardId) != CardLocation.Processing) throw new InvalidOperationException("Pingliao cannot silently dispose a different response entity.");
            MoveCard(GetAdvancedCard(cost.CardId),CardLocation.Processing,CardLocation.DiscardPile,CardMoveReasons.ResponseFinished);
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MouSimaYi = f.MouSimaYi! with { After = _movementSequence } }); MouSimaYi.Continue(f.Id);
    }
    private void BeginMouSmPlayedCompleted(ProgramSkillFrame f)
    {
        if (!MouSimaYi.Valid(f) || f.MouSimaYi is not { Stage: MouSmStage.PlayedCostChildren, Response: { } action } r || f.PendingMovementContinuation is not null) throw new InvalidOperationException("Pingliao must drain its actual Played payment before the Completed window.");
        ReplaceRuntimeTop(f = f with { MouSimaYi = r with { Stage = MouSmStage.PlayedCompleted } });
        if (!TryBeginProgramCardWindow(null,action,SkillProgramTriggerWindow.CardResponseCompleted,[f.OwnerSeat],null)) FinishMouSmPlayedCompleted(f);
    }
    private void FinishMouSmPlayedCompleted(ProgramSkillFrame f)
    {
        var r=f.MouSimaYi!;if(!IsMouSmPlayedWindow(r.Response!,SkillProgramTriggerWindow.CardResponseCompleted))throw new InvalidOperationException("Pingliao cannot complete another Played action.");
        AdvanceEventRulesAndQueueFact(new MouSmPlayedEvent(f.Id,r.Cursor,r.Actor,r.ResponseActionId,true));MouSimaYi.PlayedCompleted(f);
    }
    private bool IsMouSmPlayedCostFinished(ProgramCardTriggerWindowFrame w,CardActionCost cost) => w.MouSimaYiPlayed is { Window: SkillProgramTriggerWindow.CardResponseCompleted } back && ValidMouSmPlayedWindow(w) && _resolutionStack.SingleOrDefault(f => f.Id == back.FrameId) is ProgramSkillFrame { MouSimaYi: { } r } && w.Action.PhysicalCards.Contains(cost) && _cardMovements.Any(m => m.CardId == cost.CardId && m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished);
}
