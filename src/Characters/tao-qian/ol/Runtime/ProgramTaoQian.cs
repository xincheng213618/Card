using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum TaoQianStage { ActorChoice, SlashTargets, SlashIssuing, NativeSlash, NativeChildren, DiscardChoice, DiscardChildren, RecipientChoice, Drawing, DrawChildren }
public sealed record TaoQianReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long WindowId, int ActualTurn, int TurnOwner, TaoQianStage Stage)
{
    public bool Paid { get; init; }
    public int ActorSeat { get; init; } = -1;
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public int Cursor { get; init; }
    public int DrawAmount { get; init; }
    public int DamageRecipientCount { get; init; }
    public int DiscardRequired { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public ActualDiscardRecoveryPhaseKey? Phase { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<CardActionCost> _materials = Array.AsReadOnly(Array.Empty<CardActionCost>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardActionCost> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ZongluanSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, long UseId, long ActionId, int ActualTurn, int TurnOwner);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TaoQianReceipt? TaoQian { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZongluanSlashOrigin? ZongluanSlash { get; init; } }
public sealed record TaoQianStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, long WindowId, int ActualTurn, int TurnOwner) : IGameEvent;
public sealed record TaoQianCompletedEvent(long FrameId, SkillProgramEffectOp Op, bool Paid) : IGameEvent;
public sealed record ZongluanCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActorSeat, int ActualTurn) : IGameEvent;
public sealed record ZongluanSlashIssuedEvent(ZongluanSlashOrigin Origin, int TargetCount) : IGameEvent;
public sealed record ZongluanInitialTargetEvent(long FrameId, long UseId, int Index, int TargetSeat) : IGameEvent;
public sealed record ZongluanDirectDamageEvent(long FrameId, long UseId, long DamageFrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
public sealed record ZongluanSlashReturnedEvent(long FrameId, long UseId, long ActionId, int DamageRecipientCount) : IGameEvent;
public sealed record ZongluanDiscardIssuedEvent(long FrameId, int DamageRecipientCount, int RequiredCount, long Before) : IGameEvent;
public sealed record ZongluanDiscardEntityEvent(long FrameId, int Index, long BatchId, int CardId, CardKind Kind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WenrenCommittedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int RecipientCount) : IGameEvent;
public sealed record WenrenRecipientFrozenEvent(long FrameId, int Index, int RecipientSeat) : IGameEvent;
public sealed record WenrenRecipientEvaluatedEvent(long FrameId, int Index, int RecipientSeat, int OwnerHand, int RecipientHand, int Amount) : IGameEvent;
public sealed record WenrenDrawReturnedEvent(long FrameId, int Index, int RecipientSeat, int RequestedCount, int ActualCount, int ReplacementCount, long Before, long After, long? AttemptId) : IGameEvent;

internal interface ITaoQianHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class TaoQianDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new TaoQianHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard ? [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)] : [];
}
internal sealed class ResolveZongluanDescriptor() : TaoQianDescriptor(SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard);
internal sealed class ResolveWenrenDescriptor() : TaoQianDescriptor(SkillProgramEffectOp.ResolveWenrenSequentialDraw);
internal sealed class TaoQianHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<ITaoQianHost>().Resolve(f, op);
}
internal static class TaoQianComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        ZhaohuoComposition.ValidateProgram(path, p);
        if (p.Id == "ol:zhaohuo") return;
        var effects = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).ToArray();
        if (!effects.Any(e => e.Op is SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard or SkillProgramEffectOp.ResolveWenrenSequentialDraw)) return;
        if (p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 || effects.Any(e => e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always)) throw new InvalidOperationException(path + ": Tao Qian requires his exact native operations.");
        if (p.Id == "ol:zongluan" && p.Activations.Count == 0 && p.Triggers is [{ Id: "preparation-slash", Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Subject: SkillProgramTriggerSubject.Owner, Optional: true, Effects: [{ Op: SkillProgramEffectOp.ResolveZongluanPreparationSlashAndDiscard }] } t] && Plain(t)) return;
        if (p.Id == "ol:wenren" && p.Triggers.Count == 0 && p.Activations is [{ Id: "distribute-draw", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, TargetKind: SkillProgramTargetKind.AnyLiving, UsesPerTurn: null, Effects: [{ Op: SkillProgramEffectOp.ResolveWenrenSequentialDraw }] } a] && a.Condition.Kind == SkillProgramConditionKind.Always && a.MarkerCost is null && a.UsesPerGame is null && a.UsesPerPhase is null) return;
        throw new InvalidOperationException(path + ": Tao Qian's complete native Preparation or actual Play draft contract changed.");
    }
    private static bool Plain(SkillProgramTrigger t) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageScope is null && t.UsageLimit is null && t.MarkerCost is null && t.OwnerRelation is null && !t.IncludeResponseUses && !t.SingleActionInstance && t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementOccurrence is null && t.DamageOccurrence is null;
}
