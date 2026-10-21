using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record WushuangZengouReceipt(int InstructionIndex, long ResponseWindowId, long AttackFrameId, long ActionId,
    int DodgerSeat, int DodgeCursor, string Stage = "choosing", int? CostCardId = null, CardLocation? CostFrom = null,
    long Before = 0, long After = 0)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WushuangZengouReceipt? WushuangZengou { get; init; }
}
public sealed record WushuangZengouPaidEvent(long ProgramFrameId, long ResponseWindowId, long AttackFrameId, long ActionId,
    int OwnerSeat, int DodgerSeat, int DodgeCursor, int? CostCardId, CardLocation? CostFrom, long Before, long After) : IGameEvent;
public sealed record WushuangZengouInvalidatedEvent(WushuangZengouPaidEvent Payment, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record WushuangZengouGainedCardEvent(long ProgramFrameId, long ActionId, int OwnerSeat, int CardId, long MovementSequence) : IGameEvent;
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WushuangZengouInvalidatedEvent? WushuangZengouInvalidation { get; init; }
}
internal interface IWushuangZengouHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class WushuangZengouDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangZengou;
    public override ISkillProgramEffectHandler Handler { get; } = new WushuangZengouHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifyCurrentCardEffect, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardResponseCompleted)];
}
internal sealed class WushuangZengouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWushuangZengou;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => host.GetCapability<IWushuangZengouHost>().Resolve(f);
}
