namespace CardGame.Core;
[ProgramRuntimeModule]
internal sealed class MouLiaoshiRuntime(IMouZhangHeFacts facts,IProgramRuntimeEvents events)
{
    private bool Qualified(int owner,int card)=>facts.History<MouZhangHeObtainedEvent>().Where(e=>e.OriginalRecipientSeat==owner)
        .Any(e=>facts.Movements.Any(m=>m.Sequence==e.MovementSequence&&m.CardId==card&&m.To==CardLocation.Hand(owner)&&
            m.Reason is MouQiaobianRuntime.ObtainReason or MouQiaobianRuntime.DrawReason));
    private bool Available(int owner)=>!facts.History<MouZhangHeLiaoshiUsedEvent>().Any(e=>e.OwnerSeat==owner&&e.ActualTurn==facts.Turn);
    private bool Can(int owner,CardKind kind,IReadOnlyList<int> cards)=>kind is CardKind.Slash or CardKind.Dodge&&cards is[var card]&&card>0&&
        facts.Location(card)==CardLocation.Hand(owner)&&Qualified(owner,card)&&Available(owner);
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan,34200)]
    private bool ViewAs(ProgramViewAsLegalityQuery q)=>q.Source.SkillId==MouQiaobianRuntime.Liaoshi&&!Can(q.Owner.Seat,q.Kind,q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.NativeResponseBan,34200)]
    private bool Response(ProgramNativeResponseQuery q)=>q.Source?.SkillId==MouQiaobianRuntime.Liaoshi&&!Can(q.Provider.Seat,q.Kind,q.CardIds);
    [ProgramCardLegality(ProgramCardLegalityStage.SubmittedUseValidation,34200)]
    private void Submit(ProgramSubmittedUseQuery q)
    {
        if(q.ConversionChain.Any(s=>s.SkillId==MouQiaobianRuntime.Liaoshi)&&!Can(q.ActorSeat,q.Kind,q.CardIds))
            throw new InvalidOperationException("Liaoshi requires one card actually received by this beneficiary through Qiaobian and its unused whole-turn budget.");
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,34200)]
    private void Accept(CardActionAcceptedEvent payload)
    {
        var a=payload.Action;
        if(!a.ConversionChain.Any(s=>s.SkillId==MouQiaobianRuntime.Liaoshi&&s.OwnerSeat==a.ProviderSeat)||
            facts.History<MouZhangHeLiaoshiUsedEvent>().Any(e=>e.ActionId==a.ActionId))return;
        if(a.PhysicalCards is not[var material]||material.From!=CardLocation.Hand(a.ProviderSeat)||!Qualified(a.ProviderSeat,material.CardId)||
            a.EffectiveKind is not(CardKind.Slash or CardKind.Dodge)||!Available(a.ProviderSeat))
            throw new InvalidOperationException("An accepted Liaoshi Use/Played must consume its real original beneficiary's one shared turn allowance.");
        events.AdvanceFact(new MouZhangHeLiaoshiUsedEvent(a.ProviderSeat,facts.Turn,a.ActionId,material.CardId,a.EffectiveKind));
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CardEntitySkillLabel,34200)]
    private string? Label(int id)=>id>0&&facts.History<MouZhangHeObtainedEvent>().Select(e=>e.OriginalRecipientSeat).Distinct()
        .Any(owner=>Qualified(owner,id)&&facts.Location(id)==CardLocation.Hand(owner))?"料势":null;
}
