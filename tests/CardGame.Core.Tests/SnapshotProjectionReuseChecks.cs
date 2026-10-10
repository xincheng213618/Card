using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SnapshotProjectionReuseChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Ordinary = typeof(GameEngine).GetMethod("ToSnapshot", Flags)!;
    private static readonly MethodInfo Judgment = typeof(GameEngine).GetMethod("ToJudgmentSnapshot", Flags)!;

    public static void OrdinaryCardReusePreservesAppearancePrivacyAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage());
        var host = Create(interactive: true);
        var game = Create(interactive: false);
        var baseCard = new Card(1_000_000, CardKind.Slash, Suit.Spade, 7);
        var baseline = Project(host, baseCard);
        Require(ReferenceEquals(baseline, Project(host, baseCard)),
            "An unchanged positive physical identity must reuse its ordinary projection.");

        // Host-only representations exercise the projection boundary without
        // pretending that altered material or negative ranks are replay commands.
        Card[] variants =
        [
            baseCard with { Id = baseCard.Id + 1 },
            baseCard with { Kind = CardKind.Dodge },
            baseCard with { Suit = Suit.Heart },
            baseCard with { Rank = 8 },
            baseCard with { PrintedName = "独立牌面名" }
        ];
        foreach (var variant in variants)
        {
            Require(Project(host, baseCard) == baseline, "Restoring the printed material must retain all six original fields.");
            var changed = Project(host, variant);
            Require(changed == Expected(variant) && !ReferenceEquals(changed, baseline) &&
                    ReferenceEquals(changed, Project(host, variant)) && baseline == Expected(baseCard),
                "Each changed projection field must produce the exact new immutable value without rewriting an older view.");
            Require(Project(host, baseCard) == baseline,
                "A same-Id appearance must not contaminate a later projection of the original material.");
        }

        var appearance = baseCard with { Kind = CardKind.FireSlash, Suit = Suit.Diamond, Rank = 12 };
        var shownAppearance = Project(host, appearance);
        Require(shownAppearance == Expected(appearance) && Project(host, baseCard) == baseline &&
                shownAppearance == Expected(appearance),
            "A combined use appearance and its printed material must retain separate immutable values.");
        foreach (var id in new[] { 0, -1 })
        {
            var virtualCard = baseCard with { Id = id };
            var first = Project(host, virtualCard);
            var second = Project(host, virtualCard);
            Require(first == Expected(virtualCard) && second == first && !ReferenceEquals(first, second),
                "Zero and negative virtual identities must retain fresh ordinary projections.");
        }

        VerifyRankFormatting();
        VerifyGeneratedAndJudgment();

        var other = Project(game, baseCard);
        Require(other == baseline && !ReferenceEquals(other, Project(host, baseCard)) &&
                ReferenceEquals(other, Project(game, baseCard)),
            "Even engines sharing one registry must own separate projection caches.");

        var before = game.CreateSnapshot(0);
        var held = before.Players[0].Hand.First();
        Require(ReferenceEquals(held, game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == held.Id)),
            "Repeated real player views must reuse unchanged physical-card values.");
        var beforeJson = SnapshotJson.Serialize(before);
        var rejected = false;
        CardSnapshot? publishedCard = null;
        game.StateChanged += snapshot =>
        {
            var hand = snapshot.Players[0].Hand;
            publishedCard = hand.First();
            try { ((IList<CardSnapshot>)hand)[0] = publishedCard with { DisplayName = "observer replacement" }; }
            catch (NotSupportedException) { rejected = true; }
        };
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted && rejected && publishedCard is not null && game.ObserverFailures.Count == 0 &&
                SnapshotJson.Serialize(before) == beforeJson &&
                game.CreateSnapshot(0).Players[0].Hand.Single(card => card.Id == publishedCard.Id) == publishedCard,
            "Observer collection replacement must fail while both old and current shared card values remain intact.");
        var privateView = game.CreateSnapshot(1);
        Require(privateView.Players[0].Hand.Count == 0 && privateView.Players[1].Hand.Count > 0,
            "Sharing a card value must not bypass the viewer's hand visibility checks.");
        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(Enumerable.Range(0, game.PlayerCount).All(seat =>
                    SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
                game.CardMovements.SequenceEqual(restored.CardMovements) &&
                JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack) &&
                game.Events.Select(EventJson).SequenceEqual(restored.Events.Select(EventJson)),
            "The accepted Start command must cold-replay every viewer, movement, typed frame and committed event unchanged.");

        VerifyOptionalLedgerViews();

        GameEngine Create(bool interactive) => GameEngine.CreateStandard(new GameOptions
        {
            Seed = 337, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:standard-5",
            UseInteractiveSetup = interactive, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false
        }, registry);

        void VerifyRankFormatting()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                var firstCulture = (CultureInfo)originalCulture.Clone();
                firstCulture.NumberFormat.NegativeSign = "first-minus";
                CultureInfo.CurrentCulture = firstCulture;
                var formattingCard = baseCard with { Id = baseCard.Id + 2, Rank = -2 };
                var first = Project(host, formattingCard);
                var secondCulture = (CultureInfo)originalCulture.Clone();
                secondCulture.NumberFormat.NegativeSign = "second-minus";
                CultureInfo.CurrentCulture = secondCulture;
                var second = Project(host, formattingCard);
                Require(first.RankText == "first-minus2" && second.RankText == formattingCard.RankText &&
                        second.RankText == "second-minus2" && !ReferenceEquals(first, second) &&
                        first.RankText == "first-minus2" && ReferenceEquals(second, Project(host, formattingCard)),
                    "RankText must be reread with the original culture-sensitive expression even when every other field matches.");
            }
            finally { CultureInfo.CurrentCulture = originalCulture; }
        }

        void VerifyOptionalLedgerViews()
        {
            // Host-assembled ledgers compare the projection contract. Accepted
            // command replay and observer mutation are checked separately above.
            var projection = Create(interactive: true);
            var shields = Field<List<BeneficiarySuitShield>>(projection, "_beneficiarySuitShields");
            var alignments = Field<List<DeferredHandAlignment>>(projection, "_deferredHandAlignments");
            var bans = Field<List<IssuedPlayPhaseUseProhibition>>(projection, "_issuedPlayPhaseUseProhibitions");
            var deposits = Field<List<DeferredPublicPileDeposit>>(projection, "_deferredPublicPileDeposits");
            var skillId = registry.Skills.Keys.First();
            var source = new CardConversionSource(skillId, "fixture", 0, "fixture-instance");
            var hasBan = typeof(GameEngine).GetMethod("HasIssuedPlayPhaseUseBan", Flags)!
                .CreateDelegate<Func<int, bool>>(projection);
            Set("_turnNumber", 7); Set("_currentSeat", 0); Set("_cardUseDebitPhaseInstanceId", 4);
            Set("_phase", TurnPhase.Play);
            Check();
            shields.Add(new(2, 1, Suit.Heart, source));
            alignments.Add(new(2, source, 1, 7, DeferredHandAlignmentDueKind.SourceCurrentTurnEnd, "fixture"));
            bans.Add(new(source, 1, 7, 4, 2));
            deposits.Add(new(2, 1, 0, skillId, source.SkillInstanceId, Array.Empty<int>(), 7));
            Check();
            shields.Add(new(9, 0, Suit.Spade, source)); shields.Add(new(3, 0, Suit.Club, source));
            alignments.Add(new(9, source, 0, 7, DeferredHandAlignmentDueKind.SourceCurrentTurnEnd, "first"));
            alignments.Add(new(3, source, 0, 7, DeferredHandAlignmentDueKind.TargetNextActualTurnEnd, "second"));
            bans.Add(new(source, 0, 6, 3, 9)); bans.Add(new(source, 0, 7, 4, 3));
            deposits.Add(new(9, 0, 1, skillId, source.SkillInstanceId, Array.Empty<int>(), 7));
            var retained = Check();
            var retainedJson = SnapshotJson.Serialize(retained);
            Reject(retained.Players[0].BeneficiarySuitShields!);
            Reject(retained.Players[0].DeferredHandAlignments!);
            Reject(retained.Players[0].IssuedPlayPhaseUseProhibitions!);
            Set("_currentSeat", 1); Check();
            Set("_currentSeat", 0); Set("_cardUseDebitPhaseInstanceId", 5); Check();
            Set("_cardUseDebitPhaseInstanceId", 4); Set("_phase", TurnPhase.Finished); Check();
            Set("_phase", TurnPhase.Play); Check();
            shields.RemoveAt(1); alignments.RemoveAt(1); bans.RemoveAt(2); deposits.RemoveAt(1); Check();
            shields.Clear(); alignments.Clear(); bans.Clear(); deposits.Clear(); Check();
            Require(SnapshotJson.Serialize(retained) == retainedJson,
                "Later ledger or phase changes at the same revision must not mutate an earlier player view.");

            GameSnapshot Check()
            {
                GameSnapshot? result = null;
                for (var viewer = 0; viewer < projection.PlayerCount; viewer++)
                {
                    var snapshot = projection.CreateSnapshot(viewer);
                    result ??= snapshot;
                    foreach (var player in snapshot.Players)
                    {
                        var expected = JsonSerializer.Serialize(new
                        {
                            BeneficiarySuitShields = shields.Any(s => s.BeneficiarySeat == player.Seat)
                                ? shields.Where(s => s.BeneficiarySeat == player.Seat).ToArray() : null,
                            DeferredHandAlignments = alignments.Where(d => d.TargetSeat == player.Seat).ToArray() is { Length: > 0 } due ? due : null,
                            IssuedPlayPhaseUseProhibitions = bans.Where(p => p.ActorSeat == player.Seat && hasBan(player.Seat)).ToArray() is { Length: > 0 } issued ? issued : null,
                            PublicDeferredPileName = deposits.FirstOrDefault(d => d.OwnerSeat == player.Seat) is { } deposit
                                ? registry.GetSkill(deposit.SkillId).ProgramPresentation?.AuthorityName : null,
                            AuthorityName = player.Skills?.Select(s => s.ContentId is { } id
                                ? registry.Skills.GetValueOrDefault(id)?.ProgramPresentation?.AuthorityName : null).FirstOrDefault(n => n is not null)
                        });
                        var actual = JsonSerializer.Serialize(new
                        {
                            player.BeneficiarySuitShields, player.DeferredHandAlignments,
                            player.IssuedPlayPhaseUseProhibitions, player.PublicDeferredPileName, player.AuthorityName
                        });
                        Require(actual == expected, "Optional ledger views must preserve the original null, ordering and phase qualification expressions for every viewer.");
                    }
                }
                return result!;
            }
            void Set(string name, object value) => typeof(GameEngine).GetField(name, Flags)!.SetValue(projection, value);
            void Reject<T>(IReadOnlyList<T> values)
            {
                var rejected = false;
                try { ((IList<T>)values).Add(values[0]); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected, "A populated ledger projection must reject collection mutation.");
            }
        }

        void VerifyGeneratedAndJudgment()
        {
            var zones = Field<CardZoneStore>(host, "_cardZones");
            var generated = new Card(1_000_010, CardKind.GeneralWeapon, Suit.None, 0)
                { PrintedName = "动态武将武器", IsGeneralWeapon = true, PrintedAttackRange = 3 };
            zones.AddGeneratedCard(generated);
            zones.Move(generated.Id, CardLocation.OutsideGame, CardLocation.Equipment(0));
            var shown = host.CreateSnapshot(0).Players[0].Equipment.Single(card => card.Id == generated.Id);
            Require(shown == Expected(generated) && shown.RankText == "" && shown.DisplayName == generated.PrintedName &&
                    ReferenceEquals(shown, Project(host, generated)),
                "A generated physical card must join ordinary reuse with its actual printed name and rank zero.");

            var kinds = Field<Dictionary<int, CardKind>>(host, "_judgmentEffectiveCardKinds");
            var declaredJudgment = generated with { Id = generated.Id + 1, Kind = CardKind.Slash, Suit = Suit.Club, Rank = 5 };
            var ordinary = Project(host, declaredJudgment);
            kinds[declaredJudgment.Id] = CardKind.Indulgence;
            var delayed = ProjectJudgment(host, declaredJudgment);
            Require(delayed.Kind == CardKind.Indulgence && delayed.DisplayName == CardCatalog.Get(CardKind.Indulgence).DisplayName &&
                    delayed.Suit == declaredJudgment.Suit && delayed.RankText == declaredJudgment.RankText &&
                    ReferenceEquals(ordinary, Project(host, declaredJudgment)),
                "Judgment's live effective kind and catalog name must remain separate from the ordinary printed projection.");
            kinds[declaredJudgment.Id] = CardKind.SupplyShortage;
            Require(ProjectJudgment(host, declaredJudgment).Kind == CardKind.SupplyShortage && delayed.Kind == CardKind.Indulgence,
                "A later judgment rename must not rewrite an earlier immutable judgment value.");
            kinds.Remove(declaredJudgment.Id);
            var printedJudgment = ProjectJudgment(host, declaredJudgment);
            Require(printedJudgment.Kind == declaredJudgment.Kind &&
                    printedJudgment.DisplayName == CardCatalog.Get(declaredJudgment.Kind).DisplayName &&
                    printedJudgment.DisplayName != ordinary.DisplayName,
                "Even without a kind override judgment must retain its original catalog-name semantics.");
        }
    }

    private static CardSnapshot Project(GameEngine game, Card card) => (CardSnapshot)Ordinary.Invoke(game, [card])!;
    private static CardSnapshot ProjectJudgment(GameEngine game, Card card) => (CardSnapshot)Judgment.Invoke(game, [card])!;
    private static CardSnapshot Expected(Card card) => new(card.Id, card.Kind, card.Suit, card.Rank, card.DisplayName, card.RankText);
    private static T Field<T>(GameEngine game, string name) => (T)typeof(GameEngine).GetField(name, Flags)!.GetValue(game)!;
    private static string EventJson(EventEnvelope item) => JsonSerializer.Serialize(new
    {
        item.Id, item.ParentId, item.Sequence, item.Revision, item.CorrelationId,
        PayloadType = item.Payload.GetType().FullName,
        Payload = JsonSerializer.Serialize(item.Payload, item.Payload.GetType())
    });
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
