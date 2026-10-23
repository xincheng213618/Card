using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryGuoZhaoHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal abstract class AnniversaryGuoZhaoDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryGuoZhaoHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)],
        SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)],
        _ => [new RequireActivationEntry()]
    };
}
internal sealed class ResolveAnniversaryGuoZhaoPianchongDescriptor : AnniversaryGuoZhaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong; }
internal sealed class DrawAnniversaryGuoZhaoLostColorDescriptor : AnniversaryGuoZhaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor; }
internal sealed class ResolveAnniversaryGuoZhaoZunweiDescriptor : AnniversaryGuoZhaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei; }
internal sealed class AnniversaryGuoZhaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryGuoZhaoHost>().Begin(f, op);
}
public enum AnniversaryGuoZhaoStage { GainChildren, ColorChoice, OptionChoice, Drawing, DrawChildren, RecoveryChildren, EquipmentNext, NativeEquipment, EquipmentChildren, Completed }
public sealed record AnniversaryGuoZhaoLoss(int OwnerSeat, bool IsRed, int OwnerStartTurn, long LastLeaseFrameId, int Sequence);
public sealed partial record CardMovementRecord
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryGuoZhaoLoss? AnniversaryGuoZhaoLoss { get; init; } }
public sealed record AnniversaryGuoZhaoGain(int CardId, bool IsRed, long Sequence);
public sealed record AnniversaryGuoZhaoBenefit(long LeaseId, int Count, bool DrawRed);
public sealed record AnniversaryGuoZhaoEquipment(int Ordinal, int CardId, CardKind Kind, long UseId, long ActionId, uint RandomBefore, uint RandomAfter)
{
    private IReadOnlyList<int> _candidates = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryGuoZhaoReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, AnniversaryGuoZhaoStage Stage)
{
    public int Target { get; init; } = -1;
    public string Option { get; init; } = "";
    public bool Paid { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int Amount { get; init; }
    public int Cursor { get; init; }
    public bool? DrawRed { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public AnniversaryGuoZhaoEquipment? Equipment { get; init; }
    private IReadOnlyList<AnniversaryGuoZhaoGain> _gains = Array.AsReadOnly(Array.Empty<AnniversaryGuoZhaoGain>());
    public IReadOnlyList<AnniversaryGuoZhaoGain> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<AnniversaryGuoZhaoBenefit> _benefits = Array.AsReadOnly(Array.Empty<AnniversaryGuoZhaoBenefit>());
    public IReadOnlyList<AnniversaryGuoZhaoBenefit> Benefits { get => _benefits; init => _benefits = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryGuoZhaoReceipt? AnniversaryGuoZhao { get; init; } }
public sealed record AnniversaryGuoZhaoStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn) : IGameEvent;
public sealed record AnniversaryGuoZhaoLeaseEvent(long FrameId, CardConversionSource Source, string Hash, int Turn, int OwnerStartTurn, bool LoseRed) : IGameEvent;
public sealed record AnniversaryGuoZhaoOptionPaidEvent(long FrameId, CardConversionSource Source, string Hash, int Target, string Option, int Amount) : IGameEvent;
public sealed record AnniversaryGuoZhaoBenefitIssuedEvent(long FrameId, int Ordinal, long LeaseId, int Count, bool DrawRed) : IGameEvent;
public sealed record AnniversaryGuoZhaoDrawReturnedEvent(long FrameId, int Ordinal, int Requested, int Actual, long Before, long After) : IGameEvent;
public sealed record AnniversaryGuoZhaoEquipmentIssuedEvent(long FrameId, int Ordinal, int CardId, CardKind Kind, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryGuoZhaoEquipmentReturnedEvent(long FrameId, int Ordinal, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryGuoZhaoCompletedEvent(long FrameId) : IGameEvent;
public sealed record AnniversaryGuoZhaoRecoveryIssuedEvent(long FrameId, int Amount, int OwnerHp, int TargetHp) : IGameEvent;
internal static class AnniversaryGuoZhaoComposition
{
    internal const string Pianchong = "anniversary:pianchong", Zunwei = "anniversary:zunwei";
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong or SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor or SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei;
    [ProgramCatalogValidation(44300)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => Handles(e.Op))) return;
        bool Plain(SkillProgramEffect e, SkillProgramEffectOp op) => e.Op == op && e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always && e.TargetReference is null && e.ActorReference is null;
        var valid = p.Id switch
        {
            Pianchong => p.Activations.Count == 0 && p.Triggers is [
                { Id: "replace-draw-colors", Window: SkillProgramTriggerWindow.DrawPhaseStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: true, DrawPhaseMode: SkillProgramDrawPhaseMode.Replacement, Effects: [var replace] },
                { Id: "lost-color-benefit", Window: SkillProgramTriggerWindow.CardsMoved, Subject: SkillProgramTriggerSubject.Owner, Optional: false, IsSkillInvocation: false, MovementOccurrence: SkillProgramMovementOccurrence.PerOwnerBatch, SourceZones: [CardZoneKind.Hand, CardZoneKind.Equipment], Effects: [var loss] }
            ] && Plain(replace, SkillProgramEffectOp.ResolveAnniversaryGuoZhaoPianchong) && Plain(loss, SkillProgramEffectOp.DrawAnniversaryGuoZhaoLostColor),
            Zunwei => p.Triggers.Count == 0 && p.Activations is [{ Id: "three-permanent-options", MinCards: 0, MaxCards: 0, MinTargets: 0, MaxTargets: 0, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: 1, Effects: [var choose] }] && Plain(choose, SkillProgramEffectOp.ResolveAnniversaryGuoZhaoZunwei),
            _ => false
        };
        if (!valid || p.Triggers.Any(t => t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageLimit is not null) ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": anniversary Guo Zhao requires exact color replacement/loss lease and three permanently removed native benefits.");
    }
}
