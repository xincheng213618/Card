namespace CardGame.Core;

internal static class ProgramRecordedSuitSlash
{
    internal static readonly Suit[] Suits = [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond];
    internal static string StateId(string prefix, Suit suit) => prefix + "-" + suit.ToString().ToLowerInvariant();
    internal static int Bit(Suit? suit)
    {
        var index = suit is { } value ? Array.IndexOf(Suits, value) : -1;
        return index >= 0 ? 1 << index : 0;
    }
}

public sealed record ProgramRecordedSuitSlashReceipt(long ActionId, long CardUseFrameId, long ProducerFrameId,
    int ActorSeat, int TargetSeat, string SkillId, string SkillInstanceId, string GameplayHash,
    string TriggerId, int EffectIndex, string StatePrefix, int SuitMask, int DamageBonus);
public sealed record ProgramRecordedSuitSlashIssuedEvent(ProgramRecordedSuitSlashReceipt Receipt) : IGameEvent;

public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramRecordedSuitSlashReceipt>? RecordedSuitSlashReceipts { get; init; }
}

internal interface IRecordedSuitSlashProgramHost
{
    void RevealRecordedSuitSlash(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class RevealRecordedSuitSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealRecordedSuitSlash;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealRecordedSuitSlashHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ProhibitCurrentResponse,
        static (effect, context) => context.RecordedSuitSlashValue(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "resultBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"), resultBind: r.RequiredIdentifier("resultBind"),
            visibility: SkillProgramCardSetVisibility.Public);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor(),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]),
         new CreateCardSet(effect.ResultBind!, 3, true)];
}

internal sealed class RevealRecordedSuitSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealRecordedSuitSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IRecordedSuitSlashProgramHost)host).RevealRecordedSuitSlash(frame, effect);
        return SkillProgramStepOutcome.Continue;
    }
}
