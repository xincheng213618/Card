using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum ShiGyStage { Benefit, Record, Discard, Movement, Drawing, DrawChildren, RecoveryChildren, LostSkillChildren, UseChoice, IssuingUse, NativeUse, NativeTail, Finished }
public sealed record ShiGyPaidCard(int ParticipantIndex, int OwnerSeat, int CardId, CardLocation From, CardLocation To, Suit Suit, int Sequence);
public sealed record ShiGyNativeUse(long FrameId, CardConversionSource Source, string Hash, int Turn, int Actor, int CardId, LegalAction Action, long UseId, long ActionId);
public sealed record ShiGyReceipt(CardConversionSource Source, string Hash, int Turn, long WindowId, ShiGyStage Stage)
{
    public bool Damaged { get; init; }
    public long OriginalUse { get; init; }
    public long DyingId { get; init; }
    public int Victim { get; init; } = -1;
    public int Killer { get; init; } = -1;
    public int Index { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public bool Returned { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiGyNativeUse? Native { get; init; }
    private readonly IReadOnlyList<int> _seats = [];
    private readonly IReadOnlyList<long> _batches = [];
    private readonly IReadOnlyList<ShiGyPaidCard> _paid = [];
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<ShiGyPaidCard> Paid { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiGyReceipt? ShiGuoYuan { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShiGyNativeUse? ShiGuoYuanUse { get; init; } }
public sealed record ShiGyStartedEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, long WindowId, string TriggerId) : IGameEvent;
public sealed record ShiGyRecordCommittedEvent(long FrameId, CardConversionSource Source, int Count) : IGameEvent;
public sealed record ShiGyHandRecordedEvent(long FrameId, int Target, int HandCount) : IGameEvent;
public sealed record ShiGyComparedEvent(long FrameId, long RecordFrameId, int Target, int ActualHand, int RecordedHand, bool DrawTwo, bool RaiseLimit) : IGameEvent;
public sealed record ShiGyHandLimitRaisedEvent(long FrameId, int Target) : IGameEvent;
public sealed record ShiGyDiscardPaidEvent(long FrameId, ShiGyPaidCard Card) : IGameEvent;
public sealed record ShiGyBenefitObtainedEvent(long FrameId, CardKind RequestedKind, int? CardId) : IGameEvent;
public sealed record ShiGyRescueIssuedEvent(long FrameId, int Victim, int RequestedHp) : IGameEvent;
public sealed record ShiGyChensheLostEvent(long FrameId, int Owner) : IGameEvent;
public sealed record ShiGyNativeIssuedEvent(long FrameId, long UseId, long ActionId, int CardId) : IGameEvent;
public sealed record ShiGyNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record ShiGyNativeBatchEvent(long FrameId, long UseId, long BatchId) : IGameEvent;

internal interface IShiGuoYuanHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class ShiGuoYuanDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new ShiGuoYuanHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiGuoYuanQingdao => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)],
        SkillProgramEffectOp.ResolveMobileShiGuoYuanXiugeng => [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnBeforeStart, SkillProgramTriggerWindow.DrawPhaseStarting])],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)]
    };
}
internal sealed class ResolveMobileShiGuoYuanQingdaoDescriptor() : ShiGuoYuanDescriptor(SkillProgramEffectOp.ResolveMobileShiGuoYuanQingdao);
internal sealed class ResolveMobileShiGuoYuanXiugengDescriptor() : ShiGuoYuanDescriptor(SkillProgramEffectOp.ResolveMobileShiGuoYuanXiugeng);
internal sealed class ResolveMobileShiGuoYuanChensheDescriptor() : ShiGuoYuanDescriptor(SkillProgramEffectOp.ResolveMobileShiGuoYuanChenshe);
internal sealed class ShiGuoYuanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IShiGuoYuanHost>().Begin(f, op);
}
internal static class ShiGuoYuanComposition
{
    [ProgramCatalogValidation(48100)] internal static void Validate(string path, SkillProgram p)
    {
        var ops = new[] { SkillProgramEffectOp.ResolveMobileShiGuoYuanQingdao, SkillProgramEffectOp.ResolveMobileShiGuoYuanXiugeng, SkillProgramEffectOp.ResolveMobileShiGuoYuanChenshe };
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => ops.Contains(e.Op))) return;
        static bool Plain(SkillProgramTrigger t, SkillProgramEffectOp op) => t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.Effects is [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always } e] && e.Op == op;
        var valid = p.Id switch
        {
            "mobile:shi-qingdao" => p.Triggers is [{ Id: "damage-use-completed", Window: SkillProgramTriggerWindow.CardUseCompleted, OwnerRelation: SkillProgramCardActionOwnerRelation.Target, Optional: true } t] && Plain(t, ops[0]),
            "mobile:shi-xiugeng" => p.Triggers.Count == 2 && p.Triggers.Any(t => t.Id == "record-hands" && t.Window == SkillProgramTriggerWindow.TurnBeforeStart && t.Optional && Plain(t, ops[1])) && p.Triggers.Any(t => t.Id == "compare-record" && t.Window == SkillProgramTriggerWindow.DrawPhaseStarting && t.Subject == SkillProgramTriggerSubject.Owner && t.TurnOwnerScope == SkillProgramTurnOwnerScope.AnyLiving && !t.Optional && Plain(t, ops[1])),
            "mobile:shi-chenshe" => p.Triggers is [{ Id: "other-dying", Window: SkillProgramTriggerWindow.DyingEntering, Optional: true } t] && Plain(t, ops[2]),
            _ => false
        };
        if (!valid || p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.BooleanStates.Count != 0 || p.CardPolicies.Count != 0)
            throw new InvalidOperationException(path + ": Shi Guo Yuan requires completed other damage-use, actual turn records, frozen draw-stage comparison and an original other-dying entry.");
    }
}
