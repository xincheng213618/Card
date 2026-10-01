namespace CardGame.Core;

public sealed record ProgramDiscardBudgetParticipant(int Seat, int Hp);
public sealed record ProgramDiscardBudgetDraft(int Budget, IReadOnlyList<ProgramDiscardBudgetParticipant> Participants,
    string? Mode = null, IReadOnlyList<int>? Selected = null, bool Committed = false, int Cursor = 0);
public sealed record ProgramDiscardBudgetCommittedEvent(long FrameId, int OwnerSeat, int Budget, string Mode,
    IReadOnlyList<ProgramDiscardBudgetParticipant> Participants) : IGameEvent;
public sealed record ProgramCompletedFactionGiftDraft(long ActionId, int ProviderSeat, int PhaseInstanceId,
    IReadOnlyList<int> CardIds, string Stage = "offer");
public sealed record CompletedFactionCostGiftedEvent(long FrameId, long ActionId, int ProviderSeat, int RecipientSeat,
    string SkillId, string SkillInstanceId, int TurnNumber, int PhaseInstanceId, IReadOnlyList<int> CardIds) : IGameEvent;

internal interface IDiscardBudgetAndFactionGiftHost
{
    SkillProgramStepOutcome ResolveDiscardBudgetParticipants(ProgramSkillFrame frame);
    SkillProgramStepOutcome PreventOwnPlayOutsideTargetRangeDamage(ProgramSkillFrame frame);
    SkillProgramStepOutcome DiscardOutsideRangeAfterInsufficientUses(ProgramSkillFrame frame);
    SkillProgramStepOutcome OfferCompletedFactionCostGift(ProgramSkillFrame frame, string faction);
}
internal abstract class DiscardBudgetAndFactionGiftDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_,_)=>{});
    protected SkillProgramEffect ParseOwner(ProgramOperationNodeReader r, bool faction = false)
    {
        if (faction) r.AllowOnly("op","target","providerFactionId","condition");
        else r.AllowOnly("op","target","condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            providerFactionId: faction ? r.RequiredIdentifier("providerFactionId") : null);
        RequireAlways(effect,r.Path);return effect;
    }
}
internal sealed class ResolveDiscardBudgetParticipantsDescriptor : DiscardBudgetAndFactionGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDiscardBudgetParticipants;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveDiscardBudgetParticipantsHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwner(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseEnded)];
}
internal sealed class PreventOwnPlayOutsideTargetRangeDamageDescriptor : DiscardBudgetAndFactionGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PreventOwnPlayOutsideTargetRangeDamage;
    public override ISkillProgramEffectHandler Handler { get; } = new PreventOwnPlayOutsideTargetRangeDamageHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwner(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.BeforeDamageApplied)];
}
internal sealed class DiscardOutsideRangeAfterInsufficientUsesDescriptor : DiscardBudgetAndFactionGiftDescriptor, IActualPlayPhaseUseLedgerOperation
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOutsideRangeAfterInsufficientUses;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardOutsideRangeAfterInsufficientUsesHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwner(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class OfferCompletedFactionCostGiftDescriptor : DiscardBudgetAndFactionGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferCompletedFactionCostGift;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferCompletedFactionCostGiftHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)=>ParseOwner(r,true);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted),new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Observer,[CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash])];
}
public sealed class ResolveDiscardBudgetParticipantsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveDiscardBudgetParticipants;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IDiscardBudgetAndFactionGiftHost)h).ResolveDiscardBudgetParticipants(f);
}
public sealed class PreventOwnPlayOutsideTargetRangeDamageHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.PreventOwnPlayOutsideTargetRangeDamage;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IDiscardBudgetAndFactionGiftHost)h).PreventOwnPlayOutsideTargetRangeDamage(f);
}
public sealed class DiscardOutsideRangeAfterInsufficientUsesHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.DiscardOutsideRangeAfterInsufficientUses;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IDiscardBudgetAndFactionGiftHost)h).DiscardOutsideRangeAfterInsufficientUses(f);
}
public sealed class OfferCompletedFactionCostGiftHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op=>SkillProgramEffectOp.OfferCompletedFactionCostGift;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e,ProgramSkillFrame f,int seat,ISkillProgramEffectHost h)=>((IDiscardBudgetAndFactionGiftHost)h).OfferCompletedFactionCostGift(f,e.ProviderFactionId!);
}
