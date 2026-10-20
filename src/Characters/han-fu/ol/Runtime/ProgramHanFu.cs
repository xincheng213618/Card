using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum HanFuStage { GiftChildren, ReturnChoice, ReturnChildren, Benefit, BoardChoice, BoardChildren, DamageIssued, DrawChildren, DiscardChoice, DiscardChildren }
public sealed record HanFuMaterial(int CardId, CardKind PrintedKind, CardKind Name, CardLocation From, bool IsGeneralWeapon = false);
public sealed record HanFuMovement(int Index, HanFuStage Stage, string Reason, long Before, long After, int ActualCount);
public sealed record HanFuStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int ActualTurn, int PhaseInstance, int TargetSeat, long ParentId, long? DamageFrameId, int DamageAmount) : IGameEvent;
public sealed record HanFuMaterialPaidEvent(long FrameId, HanFuStage Stage, int Index, HanFuMaterial Material, CardLocation To, long? MovementSequence) : IGameEvent;
public sealed record HanFuMovementIssuedEvent(long FrameId, HanFuMovement Movement) : IGameEvent;
public sealed record HanFuCompletedEvent(long FrameId) : IGameEvent;
public sealed record HanFuReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex, int ActualTurn,
    int PhaseInstance, int TargetSeat, long ParentId, long? DamageFrameId, int DamageAmount, HanFuStage Stage,
    IReadOnlyList<HanFuMaterial>? Gifts = null, HanFuMaterial? Returned = null, HanFuMaterial? Board = null,
    IReadOnlyList<HanFuMovement>? Movements = null, int? HistoryCutoff = null, IReadOnlyList<int>? DamagedSeats = null,
    int? RequiredDiscard = null, IReadOnlyList<HanFuMaterial>? DiscardCandidates = null, IReadOnlyList<int>? SelectedDiscard = null)
{
    private IReadOnlyList<HanFuMaterial>? _gifts = Gifts is null ? null : Array.AsReadOnly(Gifts.ToArray());
    public IReadOnlyList<HanFuMaterial>? Gifts { get => _gifts; init => _gifts = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<HanFuMovement>? _movements = Movements is null ? null : Array.AsReadOnly(Movements.ToArray());
    public IReadOnlyList<HanFuMovement>? Movements { get => _movements; init => _movements = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int>? _damagedSeats = DamagedSeats is null ? null : Array.AsReadOnly(DamagedSeats.ToArray());
    public IReadOnlyList<int>? DamagedSeats { get => _damagedSeats; init => _damagedSeats = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<HanFuMaterial>? _discardCandidates = DiscardCandidates is null ? null : Array.AsReadOnly(DiscardCandidates.ToArray());
    public IReadOnlyList<HanFuMaterial>? DiscardCandidates { get => _discardCandidates; init => _discardCandidates = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int>? _selectedDiscard = SelectedDiscard is null ? null : Array.AsReadOnly(SelectedDiscard.ToArray());
    public IReadOnlyList<int>? SelectedDiscard { get => _selectedDiscard; init => _selectedDiscard = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public HanFuReceipt? HanFu { get; init; } }
internal interface IHanFuHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op); }
internal abstract class HanFuDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new HanFuHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_,_) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op == SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard ? [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)] : [new ConsumeSelectedCards(2), new ReadSelectedTarget()];
}
internal sealed class HanFuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int targetSeat, ISkillProgramEffectHost host) => host.GetCapability<IHanFuHost>().Resolve(f,op);
}
internal static class HanFuComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveShuziExchange or SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard;
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => Handles(e.Op))) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveShuziExchange, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 2 || a.MaxCards != 2 || !a.SourceZones.Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.Order()) || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": Shuzi requires two actual owned HE cards, any one living recipient and one activation per actual Play phase.");
    }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [{ Op: SkillProgramEffectOp.ResolveKuangshouDamageDrawDiscard, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Window != SkillProgramTriggerWindow.AfterDamageApplied || t.Subject != SkillProgramTriggerSubject.Owner || t.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || t.Optional ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.MarkerCost is not null || t.UsageLimit is not null)
            throw new InvalidOperationException(path + ": Kuangshou requires the actual mandatory whole-damage Draw3 then distinct actual-turn damaged-person discard.");
    }
}
