using System.Text.Json.Serialization;

namespace CardGame.Core;

internal sealed class ReplaceDrawWithLiluGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReplaceDrawWithLiluGift;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new CaoSongHandler(SkillProgramEffectOp.ReplaceDrawWithLiluGift);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}

public enum CaoSongLiluStage { Drawing, Recipient, Cards, GiftChildren, MaximumChildren, RecoveryChildren }
public sealed record CaoSongLiluReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long DrawWindowId, int FrozenHandLimit, CaoSongLiluStage Stage, int? RecipientSeat = null,
    int GiftCount = 0, int? PreviousGiftCount = null, bool Grows = false, int SequenceBefore = 0, int SequenceAfter = 0)
{
    private readonly IReadOnlyList<int> _selectedCardIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> SelectedCardIds
    {
        get => _selectedCardIds;
        init => _selectedCardIds = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record CaoSongLiluGiftCommittedEvent(long ProgramFrameId, CardConversionSource Source,
    int RecipientSeat, int GiftCount, int? PreviousGiftCount, bool Grows,
    int SequenceBefore, int SequenceAfter) : IGameEvent;
