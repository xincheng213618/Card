using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ShiHqStage { Target, Card, Drawing, DrawChildren, CleanupChildren, Complete }
public enum ShiHqMarkOrigin { SelectedHand, ExtraDraw, NextMarkReward }
public sealed record ShiHqMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record ShiHqReceipt(CardConversionSource Source, string Hash, int Turn, int TurnOwner,
    long ParentId, SkillProgramEffectOp Operation, ShiHqStage Stage)
{
    public int Target { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    public long? BatchId { get; init; }
    public bool Reward { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private readonly IReadOnlyList<ShiHqMaterial> _materials = [];
    public IReadOnlyList<ShiHqMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiHqReceipt? ShiHeQi { get; init; } }
public sealed record ShiHqStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int TurnOwner, long ParentId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record ShiHqYingjiMarkedEvent(long MarkId, long FrameId, CardConversionSource Source, string Hash, int CardId, int Holder, ShiHqMarkOrigin Origin) : IGameEvent;
public sealed record ShiHqYingjiClearedEvent(long MarkId, long FrameId, int CardId, long MovementSequence) : IGameEvent;
public sealed record ShiHqSelectedEvent(long FrameId, int Target, int CardId, bool RewardConsumed, bool NextReward) : IGameEvent;
public sealed record ShiHqDrawPaidEvent(long FrameId, int Count, bool Replaced, long Before, long After) : IGameEvent;
public sealed record ShiHqCleanupIssuedEvent(long FrameId, int Turn, int TurnOwner, int Count, long Before, long After) : IGameEvent;
public sealed record ShiHqCleanupMaterialEvent(long FrameId, ShiHqMaterial Material, long Sequence) : IGameEvent;
public sealed record ShiHqCompletedEvent(long FrameId, SkillProgramEffectOp Operation) : IGameEvent;
public sealed record ShiHqShanxiUsedEvent(long UseId, long ActionId, CardConversionSource Source, int Turn, long PhaseInstance, int Target, int CardId) : IGameEvent;

internal interface IShiHeQiHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShiHeQiDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShiHeQiHandler(op);
    public override ProgramOperationInteraction Interaction => op == SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark ? ProgramOperationInteraction.Choice : ProgramOperationInteraction.Automatic;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeDrawInvocation | ProgramTrackedFact.NativeActionAppearance);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(op switch {
        SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark => SkillProgramTriggerWindow.CardsMoved,
        SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw => SkillProgramTriggerWindow.DrawPhaseStarting,
        _ => SkillProgramTriggerWindow.AfterTurnEnded })];
}
internal sealed class ResolveMobileShiHeQiQizhouMarkDescriptor() : ShiHeQiDescriptor(SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark);
internal sealed class ResolveMobileShiHeQiQizhouDrawDescriptor() : ShiHeQiDescriptor(SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw);
internal sealed class ResolveMobileShiHeQiYingjiCleanupDescriptor() : ShiHeQiDescriptor(SkillProgramEffectOp.ResolveMobileShiHeQiYingjiCleanup);
internal sealed class ShiHeQiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IShiHeQiHost>().Begin(frame, op);
}
internal static class ShiHeQiComposition
{
    [ProgramCatalogValidation(49000)] internal static void Validate(string path, SkillProgram p)
    {
        if (p.Id == "mobile:shi-shanxi" && (p.ViewAs is not [{ Id: "yingji-duel", InputCount: 1, OutputKind: CardKind.Duel, ForPlay: true, ForResponse: false, SingleCardTrickUse: true, AllowSameKind: true } v] ||
            !v.SourceZones.SequenceEqual([CardZoneKind.Hand]) || p.Activations.Count != 0 || p.Triggers.Count != 0))
            throw new InvalidOperationException(path + ": Shanxi requires its single native own-hand Duel conversion.");
        var ops = new[] { SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark, SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw, SkillProgramEffectOp.ResolveMobileShiHeQiYingjiCleanup };
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => ops.Contains(e.Op))) return;
        if (p.Id != "mobile:shi-qizhou" || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Triggers.Count != 3 ||
            p.BooleanStates is not [{ Id: "next-mark-draw", InitialValue: false, Visibility: SkillProgramStateVisibility.Public, ResetScope: SkillProgramStateResetScope.Game, ReacquirePolicy: SkillProgramStateReacquirePolicy.PreserveUntilGameEnd }] ||
            !p.Triggers.Any(t => t.Id == "field-entry-mark" && t.Window == SkillProgramTriggerWindow.CardsMoved && t.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch && t.Optional && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMobileShiHeQiQizhouMark }]) ||
            !p.Triggers.Any(t => t.Id == "draw-extra-yingji" && t.Window == SkillProgramTriggerWindow.DrawPhaseStarting && !t.Optional && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMobileShiHeQiQizhouDraw }]) ||
            !p.Triggers.Any(t => t.Id == "global-yingji-cleanup" && t.Window == SkillProgramTriggerWindow.AfterTurnEnded && t.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving && !t.Optional && t.Effects is [{ Op: SkillProgramEffectOp.ResolveMobileShiHeQiYingjiCleanup }]))
            throw new InvalidOperationException(path + ": Qizhou requires actual field-entry marking, the consumed-before-renewed next-mark state, separate extra native draw and global actual-turn-end cleanup.");
    }
}
