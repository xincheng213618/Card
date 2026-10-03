namespace CardGame.Core;

public sealed record ProgramNearestLegalSlashRequest(int ActorSeat, IReadOnlyList<int> NearestSeats,
    string ResultBind, int? TargetSeat = null, bool AwaitingFaction = false);
public sealed record ProgramUnlimitedSlashChoiceEvent(long FrameId, string SkillId, string SkillInstanceId,
    int OwnerSeat, int? TargetSeat, bool Used) : IGameEvent;

internal interface INearestLegalSlashProgramHost
{
    SkillProgramStepOutcome RequestLegalNearestSlashes(SkillProgramEffect effect, ProgramSkillFrame frame);
    SkillProgramStepOutcome OfferUnlimitedVirtualSlash(ProgramSkillFrame frame);
}

internal sealed class RequestLegalSlashByNearestDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RequestLegalSlashByNearest;
    public override ISkillProgramEffectHandler Handler { get; } = new RequestLegalSlashByNearestHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (_, context) => context.PriceLegalNearestSlashParticipants());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "targetKind", "amount", "condition");
        var amount = reader.RequiredInt("amount");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner ||
            reader.RequiredEnum<SkillProgramTargetKind>("targetKind") != SkillProgramTargetKind.OtherLiving || amount != 1)
            throw new InvalidOperationException("Legal nearest Slash requests require all other actors and exactly one HP decline cost.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, amount, reader.Condition(),
            minimumValue: amount, targetKind: SkillProgramTargetKind.OtherLiving);
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new SelectSingleTarget()];
}

internal sealed class OfferUnlimitedVirtualSlashDescriptor : ProgramOperationDescriptorBase, IActualPlayPhaseUseLedgerOperation
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferUnlimitedVirtualSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferUnlimitedVirtualSlashHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        if (reader.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException("An optional unlimited virtual Slash belongs to its current owner.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.Owner, 0, reader.Condition(),
            outputKind: CardKind.Slash, targetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
            useCardActionWindows: true);
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReplaceSingleTarget()];
}

public sealed class RequestLegalSlashByNearestHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RequestLegalSlashByNearest;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((INearestLegalSlashProgramHost)host).RequestLegalNearestSlashes(effect, frame);
}
public sealed class OfferUnlimitedVirtualSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferUnlimitedVirtualSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((INearestLegalSlashProgramHost)host).OfferUnlimitedVirtualSlash(frame);
}

internal sealed partial class ProgramAiEstimateContext
{
    internal void PriceLegalNearestSlashParticipants() =>
        _otherAdjustment += 12d * Math.Max(1, _publicContext.EligibleTargetCount ?? 1);
}
