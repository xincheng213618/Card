namespace CardGame.Core;

internal sealed partial class MouXiaoQiaoOlRuntime
{
    internal CardActionContext PrepareNativeAction(CardActionContext action)
    {
        if (frames.Top is { MouXiaoQiaoJueyanResponseGrant: { } g } requestedParent &&
            g.Request.ParentFrameId == requestedParent.Id && g.Request.DecisionKind is DecisionKind.RescueDying or DecisionKind.RespondSlash or DecisionKind.QinglongCrescentBlade or DecisionKind.ProgramTrigger && action.Type == CardActionType.Use && action.ActorSeat == g.Source.OwnerSeat &&
            action.ProviderSeat == action.ActorSeat && action.PhysicalCards.Count == 0 && action.EffectiveKind == g.Request.OutputKind &&
            action.ConversionChain.SequenceEqual([g.Source]))
            return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat,
                action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain,
                action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null);
        if (frames.Top is not ProgramSkillFrame { MouXiaoQiaoOl: { Stage: MouXiaoQiaoOlStage.NativeIssuing } r } f) return action;
        if (!Valid(f) || !r.Paid || r.Plan is not { } p || action.Type != CardActionType.Use || action.ActorSeat != f.OwnerSeat ||
            action.ProviderSeat != f.OwnerSeat || action.RequesterSeat is not null || action.PhysicalCards.Count != 0 || action.EffectiveKind != p.Kind ||
            !action.ConversionChain.SequenceEqual([r.Source])) throw new InvalidOperationException("Jueyan cannot normalize another producer or physical material.");
        return new(action.ActionId, action.ParentActionId, action.Type, action.ActorSeat, action.ProviderSeat, action.RequesterSeat,
            action.ResponderSeat, action.OpponentSeat, action.EffectiveKind, action.TargetSeats, action.PhysicalCards, action.ConversionChain,
            action.DesignatedTargetSeats, effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: null);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 34002)]
    internal void BindNative(CardUseDeclaredEvent declaration)
    {
        if (frames.Find(declaration.ResolutionId) is CardUseFrame { Action: { Type: CardActionType.Use } rescueAction } rescueUse &&
            frames.ParentOf(rescueUse.Id) is { MouXiaoQiaoJueyanResponseGrant: { } grant } requestedParent &&
            grant.Request.ParentFrameId == requestedParent.Id && grant.Request.DecisionKind is DecisionKind.RescueDying or DecisionKind.RespondSlash or DecisionKind.QinglongCrescentBlade or DecisionKind.ProgramTrigger &&
            rescueAction.ActorSeat == grant.Source.OwnerSeat && rescueAction.PhysicalCards.Count == 0 &&
            rescueAction.ConversionChain.SequenceEqual([grant.Source]))
        {
            frames.Replace(rescueUse.Id, rescueUse with { MouXiaoQiaoJueyanRescueOrigin = grant with { ActionId = rescueAction.ActionId } });
            frames.Replace(requestedParent.Id, requestedParent with { MouXiaoQiaoJueyanResponseGrant = grant with { ActionId = rescueAction.ActionId } });
            events.AdvanceFact(new MouXiaoQiaoJueyanResponseIssuedEvent(rescueUse.Id, grant.ProgramFrameId, rescueAction.ActionId, rescueAction.ActorSeat, rescueAction.EffectiveKind));
            return;
        }
        if (frames.Find(declaration.ResolutionId) is not CardUseFrame { Action: { } a } use ||
            frames.ParentOf(use.Id) is not ProgramSkillFrame { MouXiaoQiaoOl: { Stage: MouXiaoQiaoOlStage.NativeIssuing } r } f) return;
        if (!Valid(f) || !r.Paid || r.Plan is not { } plan || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } ||
            a.PhysicalCards.Count != 0 || a.ActorSeat != f.OwnerSeat || a.ProviderSeat != f.OwnerSeat || a.RequesterSeat is not null ||
            a.EffectiveKind != r.OutputKind || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 || a.EffectiveIsRed is not null ||
            !a.ConversionChain.SequenceEqual([r.Source])) throw new InvalidOperationException("Jueyan's actual zero-material Use changed its frozen actor, provider or source.");
        var origin = new MouXiaoQiaoOlNativeOrigin(f.Id, r.Source, f.GameplayHash, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind);
        frames.Replace(use.Id, use with { MouXiaoQiaoOlOrigin = origin });
        frames.Replace(f.Id, f with { MouXiaoQiaoOl = r with { Stage = MouXiaoQiaoOlStage.NativeUse, UseId = use.Id, ActionId = a.ActionId } });
        events.AdvanceFact(new MouXiaoQiaoJueyanNativeIssuedEvent(f.Id, use.Id, a.ActionId, f.OwnerSeat, r.OutputKind));
    }
    internal bool IsIssuedUse(CardUseFrame use)
    {
        if (use.MouXiaoQiaoJueyanRescueOrigin is { } g && use.Action is { Type: CardActionType.Use } rescue &&
            frames.ParentOf(use.Id) is { } nativeParent && nativeParent.Id == g.Request.ParentFrameId &&
            (g.Request.DecisionKind==DecisionKind.RescueDying ? nativeParent is DyingFrame :
                g.Request.DecisionKind==DecisionKind.ProgramTrigger ? nativeParent is ProgramSkillFrame requested && g.Request.RequestSource==new CardConversionSource(requested.SkillId,requested.TriggerId??requested.ActivationId,requested.OwnerSeat,requested.SkillInstanceId)&&g.Request.RequestHash==requested.GameplayHash :
                nativeParent is CardUseFrame forced&&forced.MouXiaoQiaoJueyanResponseGrant==g&&
                (g.Request.DecisionKind==DecisionKind.RespondSlash ? forced.CardKind==CardKind.BorrowedSword&&forced.Continuations.BorrowedSword is{}borrowed&&borrowed.WeaponOwnerSeat==g.Request.ActorSeat&&borrowed.SlashTargetSeat==g.Request.TargetSeat :
                    g.Request.DecisionKind==DecisionKind.QinglongCrescentBlade&&forced.CardAttack is{}attack&&attack.SourceSeat==g.Request.ActorSeat&&attack.TargetSeat==g.Request.TargetSeat)) &&
            g.ActionId == rescue.ActionId && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && rescue.PhysicalCards.Count == 0 &&
            facts.ActorLineage(use.Id,rescue.ActionId,g.Source.OwnerSeat,rescue.ActorSeat,rescue.ProviderSeat,rescue.Type) && rescue.ProviderSeat == g.Source.OwnerSeat && rescue.RequesterSeat is null &&
            rescue.EffectiveKind == g.Request.OutputKind && rescue.EffectiveSuit == Suit.None && rescue.EffectiveRank == 0 && rescue.EffectiveIsRed is null &&
            rescue.ConversionChain.SequenceEqual([g.Source]) && facts.Program(g.Source.SkillId).GameplayHash == g.GameplayHash &&
            facts.History<MouXiaoQiaoJueyanResponseGrantedEvent>().Count(e => e.Grant == (g with { ActionId = 0 })) == 1 &&
            facts.History<MouXiaoQiaoJueyanResponseIssuedEvent>().Count(e => e == new MouXiaoQiaoJueyanResponseIssuedEvent(use.Id, g.ProgramFrameId, rescue.ActionId, rescue.ActorSeat, rescue.EffectiveKind)) == 1) return true;
        if (use.MouXiaoQiaoOlOrigin is not { } o || use.Action is not { Type: CardActionType.Use } a || use.CardId != 0 ||
            use.PhysicalCardIds is not { Count: 0 } || a.PhysicalCards.Count != 0 || a.EffectiveSuit != Suit.None || a.EffectiveRank != 0 ||
            a.EffectiveIsRed is not null || a.EffectiveKind != o.Kind || use.CardKind != o.Kind || a.ActionId != o.ActionId ||
            a.ProviderSeat != o.ActorSeat || a.RequesterSeat is not null || !a.ConversionChain.SequenceEqual([o.Source]) || !facts.ActorLineage(use.Id,a.ActionId,o.ActorSeat,a.ActorSeat,a.ProviderSeat,a.Type) ||
            frames.Find(o.ProgramFrameId) is not ProgramSkillFrame { MouXiaoQiaoOl: { Paid: true } r } f || !Valid(f) ||
            r.UseId != use.Id || r.ActionId != o.ActionId || r.Source != o.Source || f.GameplayHash != o.GameplayHash || r.OutputKind != o.Kind ||
            use.SourceSeat != a.ActorSeat) return false;
        return facts.History<MouXiaoQiaoJueyanNativeIssuedEvent>().Count(e => e == new MouXiaoQiaoJueyanNativeIssuedEvent(f.Id, use.Id, a.ActionId, o.ActorSeat, o.Kind)) == 1;
    }
    [NativeUseValidation(34000)]
    internal void ValidateNativeUse(CardUseFrame use)
    {
        if (use.MouXiaoQiaoOlOrigin is not null && (!IsIssuedUse(use) || use.Step != ResolutionFrameStep.Completed ||
            use.PendingRecoveryAttempts is { Count: > 0 } || use.RecoveryPaidContinuation is not null))
            throw new InvalidOperationException("Jueyan cannot retire its genuine native use before all children.");
    }
    [NativeUseReturn(34000)]
    internal void NativeUseReturned(CardUseFrame use)
    {
        if (use.MouXiaoQiaoOlOrigin is not { } o) return;
        if (frames.Top is not ProgramSkillFrame { MouXiaoQiaoOl: { } r } f || f.Id != o.ProgramFrameId ||
            !Valid(f) || r.NativeReturned || r.UseId != use.Id || r.ActionId != o.ActionId || frames.Find(use.Id) is not null)
            throw new InvalidOperationException("Jueyan can return only once to the actual retained-Hand mother.");
        operations.SignNativeBatches(f,use);
        frames.ReplaceTop(f with { MouXiaoQiaoOl = r with { NativeReturned = true, Stage = MouXiaoQiaoOlStage.NativeChildren,After=facts.MovementSequence },PendingMovementContinuation=new(f.OwnerSeat,0,null) });
        events.AdvanceFact(new MouXiaoQiaoJueyanNativeReturnedEvent(f.Id, use.Id, o.ActionId));
    }
}
