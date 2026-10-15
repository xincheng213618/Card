namespace CardGame.Core;

internal interface IMouZhugeProgramHost
{
    SkillProgramStepOutcome ExecuteMouZhuge(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class MouZhugeOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new MouZhugeOperationHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.MouWujingDraw
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        Op == SkillProgramEffectOp.MouWujingDraw ? [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPileReceived),
            new RequireContext(ProgramContextCapability.MovementSource)] : [new RequireActivationEntry()];
}
internal sealed class MouZhitianViewDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouZhitianView; }
internal sealed class MouWujingDrawDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouWujingDraw; }
internal sealed class MouZhijueUseDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouZhijueUse; }
internal sealed class MouZhugeOperationHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<IMouZhugeProgramHost>().ExecuteMouZhuge(frame, effect);
}

public sealed record MouZhijueDraft(string Stage, bool Yang, IReadOnlyList<int> PaidCardIds,
    IReadOnlyList<CardKind> CopiedKinds, long? UseId = null, bool CausedDamage = false);
public sealed record MouZhijueUseReturn(long ProgramFrameId, int InstructionIndex, int OwnerSeat,
    string SkillId, string SkillInstanceId, string GameplayHash, int TurnNumber, bool Yang, CardKind Kind,
    int PhysicalCardId, long ActionId);
public sealed record MouZhitianFirePayment(int CardId, Suit RevealedSuit, long MovementSequence);
public sealed record MouWujingAllowanceGrantedEvent(int OwnerSeat, long BatchId) : IGameEvent;
public sealed record MouZhijuePolarityChangedEvent(int OwnerSeat, bool Yang) : IGameEvent;
public sealed record MouZhijueUndamagedEvent(int OwnerSeat, int VisibleCount) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhijueDraft? MouZhijue { get; init; }
}
public sealed partial record CardUseFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhijueUseReturn? MouZhijueReturn { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public MouZhitianFirePayment? MouZhitianPayment { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeMouZhitianFireFrameId { get; init; }
}
