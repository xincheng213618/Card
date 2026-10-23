using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IMobileCampClaimHost { SkillProgramStepOutcome Claim(ProgramSkillFrame frame); }
internal sealed class ClaimMobileEndedCampHandDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMobileEndedCampHand;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ClaimMobileEndedCampHandHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(1));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed class ClaimMobileEndedCampHandHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimMobileEndedCampHand;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) => host.GetCapability<IMobileCampClaimHost>().Claim(frame);
}
public sealed record MobileCampClaimReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long EndedFrameId, int EndedSeat, int ActualTurn, long SequenceBefore, long SequenceAfter = 0, long BatchId = 0)
{
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MobileCampClaimedEvent(long ProgramFrameId, CardConversionSource Source, long EndedFrameId,
    int EndedSeat, int ActualTurn, int Count, long SequenceBefore, long SequenceAfter, long BatchId) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileCampClaimReceipt? MobileCampClaim { get; init; }
}
internal static class MobileCampClaimComposition
{
    [ProgramCatalogValidation(39825)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op == SkillProgramEffectOp.ClaimMobileEndedCampHand)) return;
        if (p.Id is not ("mobile-god:jieying" or "happy:jieying") || p.Triggers.Count(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimMobileEndedCampHand)) != 1 ||
            p.Triggers.Single(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimMobileEndedCampHand)) is not
                { Window: SkillProgramTriggerWindow.AfterTurnEnded, Subject: SkillProgramTriggerSubject.Owner, TurnOwnerScope: SkillProgramTurnOwnerScope.OtherLiving, Optional: false, IsSkillInvocation: true,
                    Effects: [{ Op: SkillProgramEffectOp.ClaimMobileEndedCampHand, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] })
            throw new InvalidOperationException(path + ": Mobile camp collection requires the exact other participant's completed-turn trigger and one real invocation.");
    }
}
