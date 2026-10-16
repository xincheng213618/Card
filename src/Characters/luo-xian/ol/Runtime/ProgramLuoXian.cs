using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record PublicHandEntityRevealedEvent(long RevealFrameId, int OwnerSeat, long HandEntryMovementSequence,
    CardSnapshot Card) : IGameEvent;
public sealed record DailiTurnEndReceipt(CardConversionSource Source, string GameplayHash, long ParentFrameId,
    int ActualTurn, int TurnOwnerSeat, int InstructionIndex, int ShownHandCount, bool WasFaceDown);
public sealed record DailiTurnEndIssuedEvent(long FrameId, DailiTurnEndReceipt Receipt) : IGameEvent;
public sealed record DailiTurnedOverEvent(long FrameId, bool WasFaceDown, bool IsFaceDown) : IGameEvent;
public sealed record DailiCardDrawnEvent(long FrameId, int OwnerSeat, long MovementSequence) : IGameEvent;
public sealed record DailiDrawPaidEvent(long FrameId, long Before, long After, int Count) : IGameEvent;
public sealed record DailiTurnEndCompletedEvent(long FrameId, int ActualDrawCount, int RevealedCount) : IGameEvent;
public enum DailiStage { FlipChildren, DrawChildren }
public sealed record DailiPending(DailiTurnEndReceipt Receipt, DailiStage Stage, long Before = 0, long After = 0, int Count = 0);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DailiPending? Daili { get; init; }
}
internal interface ILuoXianProgramHost
{ SkillProgramStepOutcome ResolveDailiTurnEnd(ProgramSkillFrame frame); }
internal sealed class ResolveDailiTurnEndDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDailiTurnEnd;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveDailiTurnEndHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ResolveDailiTurnEndHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveDailiTurnEnd;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILuoXianProgramHost>().ResolveDailiTurnEnd(frame);
}
internal static class LuoXianContract
{
    internal static void Activation(string path, SkillProgramActivation activation)
    {
        if (activation.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDailiTurnEnd))
            throw new InvalidOperationException(path + ": Daili requires an actual completed turn.");
    }
    internal static void Trigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveDailiTurnEnd)) return;
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.ResolveDailiTurnEnd, Condition.Kind: SkillProgramConditionKind.Always }] ||
            trigger.Window != SkillProgramTriggerWindow.AfterTurnEnded || trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional ||
            trigger.TurnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving) || trigger.UsageLimit is not null ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.MarkerCost is not null)
            throw new InvalidOperationException(path + ": Daili requires its complete optional actual-turn-end flip, draw and public hand reveal.");
    }
}
