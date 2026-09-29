namespace CardGame.Core;

/// <summary>
/// 左慈（化身 / 新生）。化身牌堆是游戏外武将牌的数据承载：每名左慈持有一列
/// 武将牌 id，其中一张处于"亮出"状态并已声明其一个技能。所有随机取将都走引擎
/// 确定性 _random，牌堆与亮出状态只随命令结算变化，因此随命令日志检查点完整
/// 重放；不需要独立序列化路径。
/// </summary>
public sealed partial class GameEngine
{
    private const string HuaShenSkillId = "classic:huashen";
    private const string XinShengSkillId = "classic:xinsheng";
    private const string HuaShenGrantSourceId = "huashen";
    private const int HuaShenInitialAvatarCount = 2;

    private enum HuaShenChoiceStage
    {
        RevealAvatar,
        DeclareSkill
    }

    private sealed record HuaShenAvatarPile(
        IReadOnlyList<string> GeneralIds,
        int RevealedIndex,
        string? DeclaredSkillId);

    private sealed record HuaShenPendingChoice(
        int OwnerSeat,
        HuaShenChoiceStage Stage,
        long? ProgramFrameId,
        string? ChosenGeneralId);

    private readonly Dictionary<int, HuaShenAvatarPile> _huaShenAvatarPiles = [];
    private HuaShenPendingChoice? _pendingHuaShenChoice;

    /// <summary>Trusted-host diagnostic projection of one character's avatar pile.</summary>
    public IReadOnlyList<string> GetHuaShenAvatarGeneralIds(int seat)
    {
        ValidatePlayerSeat(seat, nameof(seat));
        return _huaShenAvatarPiles.TryGetValue(seat, out var pile)
            ? pile.GeneralIds
            : Array.Empty<string>();
    }

    public string? GetHuaShenRevealedAvatarGeneralId(int seat) =>
        _huaShenAvatarPiles.TryGetValue(seat, out var pile) && pile.RevealedIndex >= 0
            ? pile.GeneralIds[pile.RevealedIndex]
            : null;

    public string? GetHuaShenDeclaredSkillId(int seat) =>
        _huaShenAvatarPiles.TryGetValue(seat, out var pile) ? pile.DeclaredSkillId : null;

    /// <summary>
    /// The effective-faction choke point routes through the revealed avatar while a
    /// declaration is active; the god-faction selection still wins because it is
    /// chosen explicitly.
    /// </summary>
    private string? GetHuaShenEffectiveFactionId(CharacterState player)
    {
        if (!_huaShenAvatarPiles.TryGetValue(player.Seat, out var pile) || pile.RevealedIndex < 0)
            return null;
        return _contentRegistry!.Generals[pile.GeneralIds[pile.RevealedIndex]].FactionId;
    }

    private static bool HasHuaShenSkill(CharacterState player) =>
        player.SkillGrants.Grants.Any(grant => grant.IsEnabled && grant.SkillId == HuaShenSkillId);

    private bool NeedsHuaShenSetup(CharacterState player) =>
        player.IsAlive && HasHuaShenSkill(player) && !_huaShenAvatarPiles.ContainsKey(player.Seat);

    /// <summary>
    /// Setup-time hook, mirroring the god-faction step: one 左慈 per call draws the
    /// initial two avatar cards and either resolves a declaration immediately (AI)
    /// or parks on a private human decision.
    /// </summary>
    private bool TryBeginHuaShenSetup()
    {
        var owner = _players.FirstOrDefault(NeedsHuaShenSetup);
        if (owner is null) return false;

        var drawn = DrawRandomHuaShenAvatars(owner.Seat, HuaShenInitialAvatarCount);
        _huaShenAvatarPiles[owner.Seat] = new HuaShenAvatarPile(drawn, -1, null);
        AddLog("SkillEffect",
            $"{owner.Name} 随机获得了 {drawn.Count} 张武将牌作为【化身】牌。", owner.Seat);
        BeginHuaShenDeclaration(owner.Seat, programFrameId: null);
        return true;
    }

    /// <summary>
    /// 新生结算：随机将一张游戏外的武将牌置入化身牌堆。牌堆内容保持私密，事件
    /// 只发布拥有者与新牌堆规模。
    /// </summary>
    private void DeclareProgramHuaShenXinSheng(long frameId, int ownerSeat)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.OwnerSeat != ownerSeat || !_players[ownerSeat].IsAlive ||
            !HasRuntimeSkill(_players[ownerSeat], XinShengSkillId))
            throw new InvalidOperationException(
                "The Xinsheng settlement requires the active living owner.");

        if (!_huaShenAvatarPiles.TryGetValue(ownerSeat, out var pile))
            pile = new HuaShenAvatarPile([], -1, null);
        var candidates = EnumerateHuaShenCandidateGeneralIds(ownerSeat)
            .Where(generalId => !pile.GeneralIds.Contains(generalId, StringComparer.Ordinal))
            .ToArray();
        if (candidates.Length == 0)
        {
            AddLog("SkillEffect", $"{_players[ownerSeat].Name} 的化身牌堆外没有可用武将牌。", ownerSeat);
            return;
        }

        var generalId = candidates[_random.Next(candidates.Length)];
        _huaShenAvatarPiles[ownerSeat] = pile with
        {
            GeneralIds = Array.AsReadOnly(pile.GeneralIds.Append(generalId).ToArray())
        };
        QueueGameEvent(new HuaShenAvatarGainedEvent(
            frame.Id, frame.SkillId, GetProgramBindingId(frame), ownerSeat,
            _huaShenAvatarPiles[ownerSeat].GeneralIds.Count));
        AddLog("SkillEffect",
            $"{_players[ownerSeat].Name} 随机将一张游戏外武将牌置入了化身牌堆" +
            $"（现有 {_huaShenAvatarPiles[ownerSeat].GeneralIds.Count} 张）。", ownerSeat);
    }

    /// <summary>
    /// 化身换牌结算入口：回合开始/结束触发被确认后，重新亮出一张化身牌并声明技能。
    /// AI 立即结算；人类停在被待决的化身选择上（程序帧停留在栈顶）。
    /// </summary>
    private SkillProgramStepOutcome ChangeProgramHuaShenAvatar(
        ProgramSkillFrame frame,
        int ownerSeat,
        IReadOnlyList<SkillTag> declaredSkillTags)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.OwnerSeat != frame.OwnerSeat || active.SkillId != frame.SkillId ||
            active.TriggerId != frame.TriggerId)
            throw new InvalidOperationException("Avatar changes require the active program binding.");
        if (!_players[ownerSeat].IsAlive || !HasRuntimeSkill(_players[ownerSeat], HuaShenSkillId))
            throw new InvalidOperationException("Avatar changes require the living 化身 owner.");

        var pile = _huaShenAvatarPiles.GetValueOrDefault(ownerSeat) ??
            new HuaShenAvatarPile([], -1, null);
        var alternatives = pile.GeneralIds
            .Select((generalId, index) => (generalId, index))
            .Where(entry => entry.index != pile.RevealedIndex)
            .OrderBy(entry => entry.generalId, StringComparer.Ordinal)
            .ToArray();
        if (alternatives.Length == 0)
        {
            AddLog("SkillEffect", $"{_players[ownerSeat].Name} 没有可更改的化身牌。", ownerSeat);
            return SkillProgramStepOutcome.Continue;
        }

        if (!_players[ownerSeat].IsHuman)
        {
            var generalId = alternatives[0].generalId;
            var skillId = SelectAiHuaShenSkill(ownerSeat, generalId, declaredSkillTags);
            ApplyHuaShenDeclaration(ownerSeat, generalId, skillId, frame.Id);
            return SkillProgramStepOutcome.Continue;
        }

        _pendingHuaShenChoice = new HuaShenPendingChoice(ownerSeat, HuaShenChoiceStage.RevealAvatar,
            frame.Id, null);
        RequestHumanHuaShenChoice(alternatives.Select(entry => entry.generalId).ToArray(),
            declaredSkillTags);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void BeginHuaShenDeclaration(int ownerSeat, long? programFrameId)
    {
        var owner = _players[ownerSeat];
        var pile = _huaShenAvatarPiles[ownerSeat];
        var candidates = pile.GeneralIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!_players[ownerSeat].IsHuman)
        {
            var generalId = candidates[0];
            ApplyHuaShenDeclaration(ownerSeat, generalId,
                SelectAiHuaShenSkill(ownerSeat, generalId, GetHuaShenDeclaredSkillExclusions()),
                programFrameId);
            PublishState();
            return;
        }

        _pendingHuaShenChoice =
            new HuaShenPendingChoice(ownerSeat, HuaShenChoiceStage.RevealAvatar, programFrameId, null);
        RequestHumanHuaShenChoice(candidates, GetHuaShenDeclaredSkillExclusions());
    }

    private void RequestHumanHuaShenChoice(
        IReadOnlyList<string> candidateGeneralIds,
        IReadOnlyList<SkillTag> declaredSkillTags)
    {
        var pending = _pendingHuaShenChoice ??
            throw new InvalidOperationException("A hua-shen choice requires pending state.");
        var owner = _players[pending.OwnerSeat];
        PromptChoice[] choices;
        string prompt;
        if (pending.Stage == HuaShenChoiceStage.RevealAvatar)
        {
            choices = candidateGeneralIds.OrderBy(id => id, StringComparer.Ordinal)
                .Select(generalId => new PromptChoice(
                    new ChoiceId($"huashen.reveal.{pending.OwnerSeat}.{generalId}"),
                    $"亮出【{_contentRegistry!.Generals[generalId].Name}】的化身牌。", [], [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "huashen-reveal",
                        ["general-id"] = generalId
                    }))
                .ToArray();
            prompt = pending.ProgramFrameId is null
                ? "【化身】请选择一张化身牌亮出，并声明其上一个技能。"
                : "【化身】请选择要更改亮出的化身牌。";
        }
        else
        {
            var general = _contentRegistry!.Generals[pending.ChosenGeneralId!];
            choices = FilterHuaShenDeclarableSkills(general, declaredSkillTags)
                .Select(skillId => new PromptChoice(
                    new ChoiceId($"huashen.declare.{pending.OwnerSeat}.{skillId}"),
                    $"声明【{general.Name}】的【{_contentRegistry.Skills[skillId].Name}】。",
                    [], [],
                    new Dictionary<string, string>
                    {
                        ["action"] = "huashen-declare",
                        ["general-id"] = general.Id,
                        ["skill-id"] = skillId
                    }))
                .ToArray();
            prompt = $"【化身】请声明化身牌【{general.Name}】上的一个技能。";
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.HuaShen, pending.OwnerSeat, prompt, [], [])
        {
            PromptId = CreatePromptId(),
            Choices = Array.AsReadOnly(choices),
            IsPrivate = true
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitHuaShenAnswer(int actorSeat, PromptId promptId, ChoiceId choiceId)
    {
        var error = ValidateHumanPrompt(actorSeat, DecisionKind.HuaShen, promptId,
            CommandErrorCode.IllegalAction);
        if (error is not null) return Reject(error.Code, error.Message);
        var selected = _pendingDecision!.Choices.SingleOrDefault(choice => choice.Id == choiceId);
        if (selected is null)
            return Reject(CommandErrorCode.InvalidChoice, "The hua-shen choice is unavailable.");
        return Accept(() =>
        {
            ResolveHuaShenChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveHuaShenChoice(PromptChoice selected)
    {
        var pending = _pendingHuaShenChoice ??
            throw new InvalidOperationException("The hua-shen choice lost its pending state.");
        var action = selected.Parameters.GetValueOrDefault("action");
        if (pending.Stage == HuaShenChoiceStage.RevealAvatar)
        {
            if (action != "huashen-reveal" ||
                !selected.Parameters.TryGetValue("general-id", out var generalId) ||
                !_huaShenAvatarPiles.TryGetValue(pending.OwnerSeat, out var pile) ||
                GetHuaShenAvatarIndex(pile, generalId) < 0 ||
                GetHuaShenAvatarIndex(pile, generalId) == pile.RevealedIndex)
                throw new InvalidOperationException("The revealed avatar choice is malformed.");
            ClearPendingDecision();
            _pendingHuaShenChoice = pending with { Stage = HuaShenChoiceStage.DeclareSkill, ChosenGeneralId = generalId };
            RequestHumanHuaShenChoice([], GetHuaShenDeclaredSkillExclusions());
            return;
        }

        if (action != "huashen-declare" ||
            !selected.Parameters.TryGetValue("general-id", out var declaredGeneralId) ||
            !selected.Parameters.TryGetValue("skill-id", out var skillId) ||
            declaredGeneralId != pending.ChosenGeneralId)
            throw new InvalidOperationException("The declared skill choice is malformed.");
        ClearPendingDecision();
        _pendingHuaShenChoice = null;
        ApplyHuaShenDeclaration(pending.OwnerSeat, declaredGeneralId, skillId, pending.ProgramFrameId);
        if (pending.ProgramFrameId is { } frameId)
            ContinueProgramSkill(frameId);
    }

    private void ApplyHuaShenDeclaration(
        int ownerSeat,
        string generalId,
        string skillId,
        long? programFrameId)
    {
        var owner = _players[ownerSeat];
        var general = _contentRegistry!.Generals[generalId];
        _ = _contentRegistry.GetSkill(skillId);
        if (!_huaShenAvatarPiles.TryGetValue(ownerSeat, out var pile) ||
            GetHuaShenAvatarIndex(pile, generalId) < 0)
            throw new InvalidOperationException(
                $"The avatar declaration requires an owned avatar card '{generalId}'.");

        var grantId = $"{HuaShenGrantSourceId}:{ownerSeat}";
        owner.SkillGrants.RemoveGrant(grantId);
        owner.SkillGrants.Grant(new SkillGrant(grantId, skillId, grantId, HuaShenGrantSourceId));
        owner.GenderOverride = general.Gender;
        _huaShenAvatarPiles[ownerSeat] = pile with
        {
            RevealedIndex = GetHuaShenAvatarIndex(pile, generalId),
            DeclaredSkillId = skillId
        };
        QueueGameEvent(new HuaShenAvatarRevealedEvent(
            programFrameId, HuaShenSkillId, ownerSeat, generalId, general.Name, skillId,
            _contentRegistry.Skills[skillId].Name));
        AddLog("SkillEffect",
            $"{owner.Name} 亮出化身牌【{general.Name}】，声明了技能【{_contentRegistry.Skills[skillId].Name}】。",
            ownerSeat);
    }

    private string SelectAiHuaShenSkill(
        int ownerSeat,
        string generalId,
        IReadOnlyList<SkillTag> declaredSkillTags)
    {
        var declarable = FilterHuaShenDeclarableSkills(
            _contentRegistry!.Generals[generalId], declaredSkillTags).ToArray();
        if (declarable.Length == 0)
            throw new InvalidOperationException(
                $"The avatar card '{generalId}' exposes no declarable skill.");
        return declarable.OrderBy(id => id, StringComparer.Ordinal).First();
    }

    private IReadOnlyList<string> FilterHuaShenDeclarableSkills(
        ContentGeneralDefinition general,
        IReadOnlyList<SkillTag> declaredSkillTags)
    {
        var exclusions = declaredSkillTags.Aggregate(SkillTag.None, (current, tag) => current | tag);
        return general.SkillIds
            .Where(skillId => _contentRegistry!.Skills.TryGetValue(skillId, out var definition) &&
                definition.Program is not null &&
                (definition.Tags & exclusions) == 0)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetHuaShenAvatarIndex(HuaShenAvatarPile pile, string generalId)
    {
        for (var index = 0; index < pile.GeneralIds.Count; index++)
        {
            if (string.Equals(pile.GeneralIds[index], generalId, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }

    /// <summary>
    /// The declared-skill exclusion lives in the content file (化身的 change-avatar
    /// effect) so a later rules revision can retune it without touching the engine.
    /// </summary>
    private IReadOnlyList<SkillTag> GetHuaShenDeclaredSkillExclusions() =>
        _contentRegistry!.Skills.GetValueOrDefault(HuaShenSkillId)?.Program?.Triggers
            .SelectMany(trigger => trigger.Effects)
            .FirstOrDefault(effect => effect.Op == SkillProgramEffectOp.HuaShenChangeAvatar) is
        { DeclaredSkillTags.Count: > 0 } changeAvatar
            ? changeAvatar.DeclaredSkillTags
            : HuaShenChangeAvatarProgramOperationDescriptor.DefaultDeclaredSkillTags;

    private IReadOnlyList<string> DrawRandomHuaShenAvatars(int ownerSeat, int count)
    {
        var drawn = new List<string>();
        for (var index = 0; index < count; index++)
        {
            var candidates = EnumerateHuaShenCandidateGeneralIds(ownerSeat)
                .Where(generalId => !drawn.Contains(generalId, StringComparer.Ordinal))
                .ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException(
                    "The avatar pool ran out before the initial two avatar cards were drawn.");
            drawn.Add(candidates[_random.Next(candidates.Length)]);
        }
        return Array.AsReadOnly(drawn.ToArray());
    }

    /// <summary>
    /// 游戏外武将牌堆：当前身份局武将池去掉已在场武将与没有可声明技能的武将。
    /// 池随在场武将固定，随机消费只发生在命令结算里，回放一致。
    /// </summary>
    private IEnumerable<string> EnumerateHuaShenCandidateGeneralIds(int ownerSeat)
    {
        var exclusions = _players
            .Where(player => player.GeneralSelected)
            .Select(player => player.General.Id)
            .ToHashSet(StringComparer.Ordinal);
        var poolIds = _modeDefinition.GeneralPoolIds ??
            (IReadOnlyList<string>)_contentRegistry!.Generals.Keys.Order(StringComparer.Ordinal).ToArray();
        var declaredSkillTags = GetHuaShenDeclaredSkillExclusions();
        return poolIds
            .Order(StringComparer.Ordinal)
            .Where(generalId => _contentRegistry!.Generals.ContainsKey(generalId) &&
                !exclusions.Contains(generalId))
            .Where(generalId => FilterHuaShenDeclarableSkills(
                _contentRegistry.Generals[generalId], declaredSkillTags).Count > 0);
    }
}
