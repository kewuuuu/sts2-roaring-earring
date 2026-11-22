using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using Sts2RoaringEarring.Config;

namespace Sts2RoaringEarring.Models;

/// <summary>
/// 狂吼耳环 — Vakuu's Ancient relic. Unlike <c>WhisperingEarring</c> (13 cards, first turn only) it takes
/// over every turn of its owner, and the card budget, order and auto end turn all come from the config file.
/// </summary>
public sealed class RoaringEarring : RelicModel
{
	public override RelicRarity Rarity => RelicRarity.Ancient;

	/// <summary>
	/// Belt and braces behind <c>VakuuPool1Patch</c>: one copy per player. Same idiom as the vanilla
	/// MassiveScroll (MassiveScroll.cs:19), which also narrows a relic to "not for this run" here.
	/// </summary>
	/// <remarks>
	/// The owner getter calls AssertMutable (RelicModel.cs:214-230), so a canonical instance must be answered
	/// before it is read: the relic library and the pool helpers do ask canonical models whether they are allowed.
	/// </remarks>
	protected override bool IsAllowedInternal(IRunState runState)
	{
		if (!base.IsMutable)
		{
			return true;
		}
		Player? owner = base.Owner;
		return owner == null || !owner.Relics.Any((RelicModel relic) => relic is RoaringEarring);
	}

	/// <summary>
	/// Display-only: the relic's description is the only reader left (RelicModel.cs:93-111), because
	/// <see cref="ModifyMaxEnergy"/> now reads the run's config directly. A canonical model is built once at
	/// startup, so the energy value can never follow a settings change mid-session; the {AutoPlay} text, in contrast,
	/// is rewritten by <see cref="RefreshIndicator"/> on every switch.
	/// </summary>
	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[2]
	{
		new EnergyVar(RoaringRuntimeConfig.ForThisRun.EnergyPerTurn),
		// Fresh per clone and seeded in the locale this machine runs: the description is rebuilt on every hover
		// (RelicModel.cs:93-111, StringVar.ToString is what SmartFormat substitutes for {AutoPlay}).
		new StringVar(AutoPlayVar, AutoPlayText(true)),
	};

	/// <summary>Name of the display var behind the description's {AutoPlay} placeholder (StringVar.cs:3-16).</summary>
	private const string AutoPlayVar = "AutoPlay";

	// Two values for one placeholder. The description itself is picked per locale when it is injected
	// (RoaringLoc.RelicEntries), so the value has to be picked the same way or one half of the sentence would
	// arrive in the other language.
	internal const string AutoPlayOnChinese = "自动出牌：开";

	internal const string AutoPlayOffChinese = "自动出牌：关";

	internal const string AutoPlayOnEnglish = "auto-play on";

	internal const string AutoPlayOffEnglish = "auto-play off";

	/// <summary>Default of <see cref="AutoPlayEnabled"/>; an old save has no such property and reads as on.</summary>
	public const bool DefaultAutoPlayEnabled = true;

	private bool _autoPlayEnabled = DefaultAutoPlayEnabled;

	/// <summary>
	/// The owner's own switch, flipped by left-clicking the relic (RelicToggleClickPatch) and carried through a
	/// save: [SavedProperty] registers it in the model's property table (ModelIdSerializationCache.cs:157-181) and
	/// restores it on load (RelicModel.cs:588-608). A save written before 0.3.0-beta has no value for it, so
	/// SavedProperties.Fill leaves the field at its initializer (SavedProperties.cs:273-283): old saves read as on.
	/// Every peer writes it, but only from the queued action (ToggleAutoPlayAction): the write lands at the same
	/// action-queue position everywhere, so no checksum can catch two peers on different values.
	/// </summary>
	[SavedProperty]
	public bool AutoPlayEnabled
	{
		get
		{
			return _autoPlayEnabled;
		}
		set
		{
			AssertMutable();
			_autoPlayEnabled = value;
			RefreshIndicator();
		}
	}

	private PlayerCombatState? _snapshotState;

	private int _snapshotTurn;

	private bool _snapshotEnabled;

	/// <summary>
	/// On when the config disables clicking (show_toggle = false keeps the relic working as if it were on),
	/// otherwise whatever the owner last chose.
	/// </summary>
	private bool EffectiveAutoPlayEnabled => !RoaringRuntimeConfig.ForThisRun.ShowToggle || _autoPlayEnabled;

	private static bool IsChineseLocale()
	{
		try
		{
			LocManager? manager = LocManager.Instance;
			return manager == null || manager.CultureInfo.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception)
		{
			// Nothing readable this early: RoaringLoc falls back to the Chinese table too.
			return true;
		}
	}

	private static string AutoPlayText(bool enabled)
	{
		if (IsChineseLocale())
		{
			return enabled ? AutoPlayOnChinese : AutoPlayOffChinese;
		}
		return enabled ? AutoPlayOnEnglish : AutoPlayOffEnglish;
	}

	/// <summary>
	/// Puts the current switch where the player can see it: the {AutoPlay} text of the description, and the icon's
	/// status (Active pulses, Disabled greys out — RelicModel.UpdateTexture:505-540, reached through
	/// NRelicInventoryHolder.OnStatusChanged:349-352). Callers include a canonical model's hover and a save load, so
	/// the mutability guard comes first and nothing in here may throw.
	/// </summary>
	internal void RefreshIndicator()
	{
		if (!base.IsMutable)
		{
			return;
		}
		try
		{
			bool enabled = EffectiveAutoPlayEnabled;
			if (DynamicVars.TryGetValue(AutoPlayVar, out DynamicVar? variable) && variable is StringVar text)
			{
				text.StringValue = AutoPlayText(enabled);
			}
			Status = enabled ? RelicStatus.Active : RelicStatus.Disabled;
		}
		catch (Exception)
		{
			// Cosmetic only: an indicator failure must never escape into a hook, a click or a save load.
		}
	}

	/// <summary>
	/// Whether auto-play is in force for the turn currently being fought. The value is snapshotted at the turn
	/// boundary, so a click changes the next turn and never the one already running.
	/// It has to be taken lazily here because a turn's first energy read happens before any model hook of that
	/// turn: CombatManager.SetupPlayerTurn calls PlayerCombatState.ResetEnergy (CombatManager.cs:916-919), which
	/// reads MaxEnergy (PlayerCombatState.cs:182-184), which is Hook.ModifyMaxEnergy (PlayerCombatState.cs:110);
	/// AfterPlayerTurnStart only runs at CombatManager.cs:950 and AfterAutoPrePlayPhaseEntered at CombatManager.cs:891.
	/// PlayerCombatState is rebuilt per combat (Player.cs:807-810), and TurnNumber advances once per player turn
	/// (CombatManager.cs:1999-2002, PlayerCombatState.cs:177-180), so (instance, TurnNumber) names exactly one turn.
	/// The increment happens on the side switch, i.e. before SetupPlayerTurn reads energy again, which is why a
	/// click lands on the next turn and not the one in progress.
	/// </summary>
	private bool AutoPlayActiveThisTurn()
	{
		PlayerCombatState? state = base.Owner?.PlayerCombatState;
		if (state == null)
		{
			// Outside combat (card reward display and friends) there is no turn to snapshot.
			return EffectiveAutoPlayEnabled;
		}
		if (!ReferenceEquals(state, _snapshotState) || state.TurnNumber != _snapshotTurn)
		{
			_snapshotState = state;
			_snapshotTurn = state.TurnNumber;
			_snapshotEnabled = EffectiveAutoPlayEnabled;
		}
		return _snapshotEnabled;
	}

	/// <summary>
	/// Read per call rather than from the DynamicVars snapshot: the snapshot is taken once when the canonical
	/// model is built, which is why a value baked there would never follow the run's own config.
	/// </summary>
	public override decimal ModifyMaxEnergy(Player player, decimal amount)
	{
		if (player != base.Owner)
		{
			return amount;
		}
		// The turn's first energy read happens before any other hook of ours (see the snapshot note above), which also
		// makes this the point where the icon and the description catch up after a load or a mid-turn switch.
		RefreshIndicator();
		if (!AutoPlayActiveThisTurn())
		{
			// Switched off, or clicking is off for the whole run: no energy either.
			return amount;
		}
		return amount + RoaringRuntimeConfig.ForThisRun.EnergyPerTurn;
	}

	/// <remarks>
	/// Late because the takeover has to happen after every other auto-pre-play trigger (Imbued and friends).
	/// The vanilla TurnNumber &gt; 1 early return is deliberately absent: this relic plays every turn.
	/// Every peer runs this body; the hook context replicates the actions one layer down, so no local guard
	/// may be added here without desyncing the run.
	/// </remarks>
	public override async Task AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext choiceContext, Player player)
	{
		if (player != base.Owner)
		{
			return;
		}
		if (!AutoPlayActiveThisTurn())
		{
			// The same snapshot the energy read used: a click during this turn does not cut a loop that is already
			// running, and every peer decides it from a value both had before the turn began.
			return;
		}
		ICombatState? combatState = player.Creature.CombatState;
		if (combatState == null)
		{
			return;
		}
		// PlayerCombatState is nullable in this build (Player.cs:115) although vanilla dereferences it unchecked.
		int? startTurn = base.Owner.PlayerCombatState?.TurnNumber;
		if (startTurn == null)
		{
			return;
		}
		// The run's frozen values, not the file's current ones: a settings change mid-run must not move the
		// numbers two peers already agreed on.
		RoaringConfig config = RoaringRuntimeConfig.ForThisRun;
		Flash();
		int cardsPlayed = 0;
		bool combatEnded = false;
		bool turnEnded = false;
		using (RunManager.Instance.CardSelectorStack.PushSelector(base.Owner, new VakuuCardSelector()))
		{
			for (; cardsPlayed < config.MaxCardsPerTurn; cardsPlayed++)
			{
				if (CombatManager.Instance.IsOverOrEnding)
				{
					combatEnded = true;
					break;
				}
				if (CombatManager.Instance.IsPlayerReadyToEndTurn(player))
				{
					turnEnded = true;
					break;
				}
				if (base.Owner.PlayerCombatState?.TurnNumber != startTurn)
				{
					turnEnded = true;
					break;
				}
				CardPile pile = PileType.Hand.GetPile(base.Owner);
				CardModel? card = ChooseCard(pile.Cards, config.Order);
				if (card == null)
				{
					break;
				}
				Creature? target = GetTarget(card, combatState);
				await card.SpendResources();
				await CardCmd.AutoPlay(choiceContext, card, target, AutoPlayType.Default, skipXCapture: true);
				if (config.PlayDelayMs > 0)
				{
					// A raw wait, not CustomScaledWait: this is the player's own pacing knob, so it must still
					// apply when the game's fast mode is Instant. Combat end aborts it (ignoreCombatEnd defaults to false).
					await Cmd.Wait(config.PlayDelayMs / 1000f, default(CancellationToken));
				}
			}
		}
		// Auto end turn only for the two natural endings: the cap was reached, or no playable card was left.
		// A finished combat and a turn that already changed must never end the turn again.
		bool endedNaturally = !combatEnded && !turnEnded;
		if (!config.AutoEndTurn || !endedNaturally)
		{
			return;
		}
		// canBackOut: true keeps the vanilla button usable after we end the turn. CombatManager.SetReadyToEndTurn
		// only records the player in PlayersThatCannotUnEndTurn when canBackOut is false (CombatManager.cs:973-976),
		// and NEndTurnButton.AfterPlayerEndedTurn (NEndTurnButton.cs:462-469) only turns the button into
		// "undo end turn" when canBackOut is true. That press enqueues the networked UndoEndPlayerTurnAction
		// (NEndTurnButton.cs:593), so backing out stays in sync in multiplayer.
		PlayerCmd.EndTurn(base.Owner, canBackOut: true);
	}

	/// <summary>Hand order, reverse hand order, or a random playable card from the combat card-selection RNG.</summary>
	private CardModel? ChooseCard(IReadOnlyList<CardModel> cards, PlayOrder order)
	{
		List<CardModel> playable = new List<CardModel>();
		foreach (CardModel card in cards)
		{
			if (card.CanPlay())
			{
				playable.Add(card);
			}
		}
		if (playable.Count == 0)
		{
			return null;
		}
		return order switch
		{
			PlayOrder.RightToLeft => playable[playable.Count - 1],
			PlayOrder.Random => base.Owner.RunState.Rng.CombatCardSelection.NextItem(playable),
			_ => playable[0],
		};
	}

	/// <summary>Same targeting rules as Vakuu's auto-play: leftmost hittable enemy, random teammate, or self.</summary>
	private Creature? GetTarget(CardModel card, ICombatState combatState)
	{
		Rng combatTargets = base.Owner.RunState.Rng.CombatTargets;
		return card.TargetType switch
		{
			TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
			TargetType.AnyAlly => combatTargets.NextItem(combatState.Allies.Where(
				(Creature c) => c != null && c.IsAlive && c.IsPlayer && c != base.Owner.Creature)),
			TargetType.AnyPlayer => base.Owner.Creature,
			_ => null,
		};
	}
}
