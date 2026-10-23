using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryLiuYongHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryLiuYongDescriptor(SkillProgramEffectOp operation) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => operation;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new AnniversaryLiuYongHandler(operation);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => operation == SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning ? [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)] : [];
}
internal sealed class ResolveAnniversaryLiuYongZhuningDescriptor() : AnniversaryLiuYongDescriptor(SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning);
internal sealed class ResolveAnniversaryLiuYongFengxiangDescriptor() : AnniversaryLiuYongDescriptor(SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang);
internal sealed class AnniversaryLiuYongHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IAnniversaryLiuYongHost>().Begin(frame, operation);
}
public enum AnniversaryLiuYongStage { Gift, GiftChildren, UseChoice, IssuingUse, NativeUse, NativeReturned, Drawing, DrawChildren, HealthChildren }
public sealed record AnniversaryLiuYongXi(CardConversionSource Source, long GiftFrameId, int CardId, int RecipientSeat);
public sealed partial class CharacterState
{
    private IReadOnlyList<AnniversaryLiuYongXi> _anniversaryLiuYongXiCards = [];
    // Stable private entity marks. Pending selections and costs remain on the program frame.
    public IReadOnlyList<AnniversaryLiuYongXi> AnniversaryLiuYongXiCards { get => _anniversaryLiuYongXiCards; internal set => _anniversaryLiuYongXiCards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryLiuYongPlan(CardKind Kind, LegalActionKind ActionKind, int? TargetCardId = null, CardKind? RequiredKind = null, bool Adjusted = false)
{
    private readonly IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryLiuYongReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, int PhaseInstance, string Mode, AnniversaryLiuYongStage Stage)
{
    public int Recipient { get; init; } = -1;
    public int? Leader { get; init; }
    private readonly IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _from = [];
    public IReadOnlyList<CardLocation> From { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public long Before { get; init; }
    public long After { get; init; }
    public bool GiftPaid { get; init; }
    public AnniversaryLiuYongPlan? Plan { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool CausedDamage { get; init; }
    public bool HadAttack { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryLiuYongReceipt? AnniversaryLiuYong { get; init; } }
public sealed record AnniversaryLiuYongSlashOrigin(long FrameId, CardConversionSource Source, string Hash, long UseId, long ActionId, CardKind Kind, int TargetSeat);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryLiuYongSlashOrigin? AnniversaryLiuYongSlash { get; init; } }
public sealed record AnniversaryLiuYongStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, int PhaseInstance, string Mode) : IGameEvent;
public sealed record AnniversaryLiuYongGiftPaidEvent(long FrameId, CardConversionSource Source, int Recipient, int Count, long Before, long After) : IGameEvent;
public sealed record AnniversaryLiuYongLeaderChangedEvent(long BatchId, CardConversionSource Source, string Hash, int? Previous, int? Current) : IGameEvent;
public sealed record AnniversaryLiuYongNativeIssuedEvent(long FrameId, long UseId, long ActionId, CardKind Kind) : IGameEvent;
public sealed record AnniversaryLiuYongNativeReturnedEvent(long FrameId, long UseId, long ActionId, bool HadAttack, bool CausedDamage) : IGameEvent;
public sealed record AnniversaryLiuYongLimitExpandedEvent(long FrameId, int OwnerSeat, int Turn, int PhaseInstance) : IGameEvent;
public sealed record AnniversaryLiuYongDrawReturnedEvent(long FrameId, long? AttemptId, long Before, long After, int Count) : IGameEvent;

internal static class AnniversaryLiuYongComposition
{
    internal const string Zhuning = "anniversary:zhuning", Fengxiang = "anniversary:fengxiang";
    [ProgramCatalogValidation(43400)] internal static void Validate(string path, SkillProgram program)
    {
        if (!program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning or SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang)) return;
        var valid = program.Id == Zhuning ? program.Triggers.Count == 0 && program.Activations is [{ Id: "gift-and-use", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1, TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerPhase: null, UsesPerTurn: null, UsesPerGame: null,
                Effects: [{ Op: SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }] :
            program.Id == Fengxiang && program.Activations.Count == 0 && program.Triggers.Count == 2 && program.Triggers.All(t => !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.UsageScope is null && t.Condition.Kind == SkillProgramTriggerConditionKind.Always &&
                t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]) &&
                program.Triggers.Any(t => t.Id == "damaged-leader" && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage) &&
                program.Triggers.Any(t => t.Id == "xi-leader-changes" && t.Window == SkillProgramTriggerWindow.CardsMoved && t.SourceZones.SequenceEqual([CardZoneKind.Hand]));
        if (!valid || program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.BooleanStates.Count != 0)
            throw new InvalidOperationException(path + ": Liu Yong requires the complete private Xi gift/native damage-card use and both exact Fengxiang boundaries.");
    }
}
