using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum KeBiNengPingduanStage { BasicChoice, BasicIssuing, BasicNative, BasicReturned, BasicRewardChildren, RecastChoice, RecastChildren, RecastDrawChildren, RecastRewardChildren, EquipmentChoice, EquipmentChildren, EquipmentRewardChildren }
public sealed record KeBiNengPingduanCard(int CardId, CardLocation From, CardKind EffectiveKind);
public sealed record KeBiNengPingduanUseOrigin(long ProgramFrameId, int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int PhaseInstance, int ActorSeat, long CardUseFrameId, long ActionId, CardKind CardKind);
public sealed record KeBiNengPingduanReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int ActualTurn, int PhaseInstance, int ActorSeat)
{
    public KeBiNengPingduanStage Stage { get; init; } = KeBiNengPingduanStage.BasicChoice;
    public long Before { get; init; }
    public long After { get; init; }
    public int DrawCount { get; init; }
    public int CompletedItems { get; init; }
    public long PaidBatchId { get; init; }
    private IReadOnlyList<LegalAction> _actions = Array.AsReadOnly(Array.Empty<LegalAction>());
    public IReadOnlyList<LegalAction> BasicActions { get => _actions; init => _actions = Array.AsReadOnly(value.Select(FreezeAction).ToArray()); }
    private IReadOnlyList<KeBiNengPingduanCard> _cards = Array.AsReadOnly(Array.Empty<KeBiNengPingduanCard>());
    public IReadOnlyList<KeBiNengPingduanCard> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> WoodenOxGrainIds { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LegalAction? SelectedBasicAction { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeBiNengPingduanUseOrigin? NativeUse { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeBiNengPingduanCard? SelectedCard { get; init; }
    public static LegalAction FreezeAction(LegalAction a) => a with { TargetSeats = Array.AsReadOnly(a.TargetSeats.ToArray()),
        AdditionalConversionSources = a.AdditionalConversionSources is null ? null : Array.AsReadOnly(a.AdditionalConversionSources.ToArray()),
        SelectableCardIds = Array.AsReadOnly(a.SelectableCardIds.ToArray()), SelectableTargetSeats = Array.AsReadOnly(a.SelectableTargetSeats.ToArray()),
        SelectableCardTargets = a.SelectableCardTargets is null ? null : Array.AsReadOnly(a.SelectableCardTargets.ToArray()),
        SelectableTargetSequences = a.SelectableTargetSequences is null ? null : Array.AsReadOnly(a.SelectableTargetSequences.Select(t => (IReadOnlyList<int>)Array.AsReadOnly(t.ToArray())).ToArray()) };
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeBiNengPingduanReceipt? KeBiNengPingduan { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public KeBiNengPingduanUseOrigin? KeBiNengPingduanUse { get; init; } }
public sealed record KeBiNengPingduanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstance, int ActorSeat) : IGameEvent;
public sealed record KeBiNengPingduanNativeIssuedEvent(KeBiNengPingduanUseOrigin Origin) : IGameEvent;
public sealed record KeBiNengPingduanNativeReturnedEvent(KeBiNengPingduanUseOrigin Origin) : IGameEvent;
public sealed record KeBiNengPingduanItemPaidEvent(long FrameId, int Item, int ActorSeat, int? CardId, CardLocation? From, long Before, long After) : IGameEvent;
public sealed record KeBiNengPingduanDrawPaidEvent(long FrameId, int Item, bool RecastDraw, int DrawCount, long Before, long After) : IGameEvent;
public sealed record KeBiNengPingduanCompletedEvent(long FrameId, int CompletedItems, string Reason) : IGameEvent;
public interface IKeBiNengProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
public sealed class ResolvePingduanOrderedBasicRecastEquipmentHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IKeBiNengProgramHost>().Resolve(frame);
}
internal sealed class ResolvePingduanOrderedBasicRecastEquipmentDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolvePingduanOrderedBasicRecastEquipmentHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.AnyLiving)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
}
internal static class KeBiNengComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment)) && !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment))) return;
        if (p.Id != "ol:pingduan" || p.Activations is not [var a] || p.Triggers.Count != 0 || a.Id != "ordered-three-items" ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolvePingduanOrderedBasicRecastEquipment, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerPhase != 1 ||
            a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition.Kind != SkillProgramConditionKind.Always || a.ContinueAfterOwnerDeath ||
            a.MarkerCost is not null || a.CardCountExpression is not null || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || a.EquipmentSlots.Count != 0 || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 ||
            !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) || p.BooleanStates.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.Contributions.Count != 0 ||
            p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Pingduan requires one complete native ordered three-item activation with the original private recipient.");
    }
}
