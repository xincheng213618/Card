using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MobileShiChenQunUseRecordedEvent(int Sequence, int ActualTurn, int TurnOwner,
    int ActorSeat, long NativeFrameId, long? ActionId, int? PreviousActorSeat) : IGameEvent;
public sealed record MobileShiChenQunFaenDebtIssuedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner, int AfterUseSequence, string Effect) : IGameEvent;
public sealed record MobileShiChenQunFaenDebtClaimedEvent(long IssuingFrameId, long ClaimingFrameId,
    int UseSequence, int ActorSeat) : IGameEvent;
public sealed record MobileShiChenQunFaenEffectCompletedEvent(long FrameId, CardConversionSource Source,
    int UseSequence, int ActorSeat, string Effect, int ActualCount, bool WasDebt) : IGameEvent;

public sealed record MobileShiChenQunFaenReceipt(CardConversionSource Source, string GameplayHash,
    long WindowId, long NativeParentId, long ActionId, int UseSequence, int ActorSeat, int ActualTurn,
    int TurnOwner, long BeforeMovement, MobileShiChenQunFaenDebtIssuedEvent? Debt = null,
    string? Effect = null, bool Completed = false);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiChenQunFaenReceipt? MobileShiChenQunFaen { get; init; }
}

internal interface IMobileShiChenQunProgramHost
{
    SkillProgramStepOutcome Claim(ProgramSkillFrame frame);
    void Issue(ProgramSkillFrame frame);
}

internal sealed class ClaimMobileShiChenQunFaenDebtDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMobileShiChenQunFaenDebt;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiChenQunHandler(true);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeLogicalDiscard);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted),
         new RequireContext(ProgramContextCapability.CardAction), new CreateChoiceResult("faen-effect", ["draw", "discard"])];
}

internal sealed class IssueMobileShiChenQunFaenDebtDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.IssueMobileShiChenQunFaenDebt;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiChenQunHandler(false);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted), new RequireContext(ProgramContextCapability.CardAction)];
}

internal sealed class MobileShiChenQunHandler(bool claim) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => claim ? SkillProgramEffectOp.ClaimMobileShiChenQunFaenDebt : SkillProgramEffectOp.IssueMobileShiChenQunFaenDebt;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        if (claim) return host.GetCapability<IMobileShiChenQunProgramHost>().Claim(frame);
        host.GetCapability<IMobileShiChenQunProgramHost>().Issue(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal interface IMobileShiChenQunSelectionQueries : IProgramSelectionQuery { IReadOnlyList<int> MaximumLossSeats { get; } }

[ProgramTargetResolver(SkillProgramTargetKind.MobileShiChenQunMaximumLoss)]
internal sealed class MobileShiChenQunMaximumLossResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) =>
        queries.Get<IMobileShiChenQunSelectionQueries>().MaximumLossSeats;
}

internal sealed class MobileShiChenQunCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => "mobile:shi-faen";
    public bool AllowsSingleActionInstance => true;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        var debt = trigger.Id == "faen-next-use";
        if ((!debt && trigger.Id != "faen-use") || trigger.Window != SkillProgramTriggerWindow.CardUseCommitted ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || !trigger.IncludeResponseUses || !trigger.SingleActionInstance ||
            trigger.Priority != (debt ? 2 : 1) ||
            trigger.Optional == debt || (debt ? trigger.UsageScope is not null || trigger.UsageLimit is not null :
                trigger.UsageScope != SkillUsageScope.Turn || trigger.UsageLimit != 2) ||
            trigger.Effects is not [
                { Op: SkillProgramEffectOp.ClaimMobileShiChenQunFaenDebt, Condition.Kind: SkillProgramConditionKind.Always },
                { Op: SkillProgramEffectOp.Draw, TargetReference.Kind: ProgramParticipantRef.EventSource, Amount: 1,
                    Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "faen-effect", OptionId: "draw" } },
                { Op: SkillProgramEffectOp.SelectOwnedCards, TargetReference.Kind: ProgramParticipantRef.EventSource, Amount: 1, ResultBind: "faen-discard",
                    Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "faen-effect", OptionId: "discard" } },
                { Op: SkillProgramEffectOp.MoveBoundCards, SourceBind: "faen-discard", Destination: SkillProgramCardDestination.DiscardPile, AwaitMovementTriggers: true,
                    Condition: { Kind: SkillProgramConditionKind.ChoiceIs, SourceBind: "faen-effect", OptionId: "discard" } },
                { Op: SkillProgramEffectOp.IssueMobileShiChenQunFaenDebt, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException("Shi Chen Qun Faen requires a real committed-use observer, two optional turn invocations and exact native actor draw/discard followed by its opposite next-use debt.");
    }
}
