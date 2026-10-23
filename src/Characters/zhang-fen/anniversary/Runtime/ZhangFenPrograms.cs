using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryZhangFenHost { SkillProgramStepOutcome Begin(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class ZhangFenDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler => new ZhangFenHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(4));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
}
internal sealed class ZhangFenWangluDescriptor() : ZhangFenDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu);
internal sealed class ZhangFenXianzhuDescriptor() : ZhangFenDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu);
internal sealed class ZhangFenCartDescriptor() : ZhangFenDescriptor(SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack);
internal sealed class ZhangFenChaixieDescriptor() : ZhangFenDescriptor(SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie);
internal sealed class ZhangFenHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryZhangFenHost>().Begin(f, op);
}
internal static class ZhangFenComposition
{
    internal const string Wanglu = "anniversary:wanglu", Xianzhu = "anniversary:xianzhu", Chaixie = "anniversary:chaixie", Cart = "anniversary:dagong-che";
    [ProgramCatalogValidation(43500)] internal static void Validate(string path, SkillProgram p)
    {
        var own = new[] { SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu, SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu, SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack, SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie };
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => own.Contains(e.Op))) return;
        if (p.Activations.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.BooleanStates.Count != 0 || p.CardIdentities.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.Count != (p.Id == Cart ? 2 : 1) || p.Triggers.Any(t => t.OwnerRelation is not null || t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always }] || !Exact(p.Id, t)))
            throw new InvalidOperationException(path + ": Zhang Fen requires exact preparation creation, actual Slash damage, actual cart destruction and native cart Play-start triggers.");
    }
    private static bool Exact(string id, SkillProgramTrigger t) => (id, t.Id, t.Window, t.Effects[0].Op, t.Subject, t.Optional) switch
    {
        (Wanglu, "preparation-cart-or-play", SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, SkillProgramEffectOp.ResolveAnniversaryZhangFenWanglu, SkillProgramTriggerSubject.Owner, false) => true,
        (Xianzhu, "slash-upgrade", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.ResolveAnniversaryZhangFenXianzhu, SkillProgramTriggerSubject.DamageSource, true) => t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage,
        (Chaixie, "destroyed-cart-draw", SkillProgramTriggerWindow.CardsMoved, SkillProgramEffectOp.ResolveAnniversaryZhangFenChaixie, SkillProgramTriggerSubject.Owner, false) => t.SourceZones.SequenceEqual([CardZoneKind.Equipment]) && t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard,
        (Cart, "play-start-slash", SkillProgramTriggerWindow.PlayPhaseStarting, SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack, SkillProgramTriggerSubject.Owner, true) => true,
        (Cart, "cart-slash-discard", SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramEffectOp.ResolveAnniversaryDagongCheAttack, SkillProgramTriggerSubject.DamageSource, false) => t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage,
        _ => false
    };
}
public enum ZhangFenStage { PlaceChildren, ExtraPhase, Upgrade, Targets, NativeUse, NativeReturned, DiscardChoice, DiscardChildren, Drawing, DrawChildren, Done }
public sealed record ZhangFenReceipt(CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, ZhangFenStage Stage)
{
    public int CartId { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int Count { get; init; }
    public int Paid { get; init; }
    public int CardId { get; init; }
    public CardLocation? From { get; init; }
    public CardLocation? To { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long MoveSequence { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool IgnoreRangeArmor { get; init; }
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed record DagongCheSlashOrigin(long FrameId, int Instruction, CardConversionSource Source, string Hash, int CartId, long UseId, long ActionId, int Turn, int InitialDiscardCount, bool IgnoreRangeArmor);
public sealed partial record ProgramSkillFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhangFenReceipt? ZhangFen { get; init; } }
public sealed partial record CardUseFrame { [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DagongCheSlashOrigin? DagongCheSlash { get; init; } }
public sealed record DagongCheCreatedEvent(long FrameId, CardConversionSource Source, string Hash, int CardId, int OwnerSeat) : IGameEvent;
public sealed record DagongCheUpgradedEvent(long FrameId, CardConversionSource Source, string Hash, int CardId, int Ordinal, string Choice) : IGameEvent;
public sealed record DagongCheDestroyedEvent(int CardId, int FormerOwnerSeat, long MovementSequence, long ParentId, CardMoveReason Reason, int Upgrades) : IGameEvent;
public sealed record ZhangFenStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Op, long ParentId, int Turn, int CartId, int Target, int Count) : IGameEvent;
public sealed record DagongCheSlashIssuedEvent(DagongCheSlashOrigin Origin) : IGameEvent;
public sealed record ZhangFenDrawReturnedEvent(long FrameId, int Requested, int Actual, long Before, long After) : IGameEvent;
