using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
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

	/// <summary>Same shape as the vanilla relic: the energy comes from DynamicVars.Energy, not from the constant.</summary>
	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[1]
	{
		new EnergyVar(RoaringConfigStore.Effective.EnergyPerTurn),
	};

	public override decimal ModifyMaxEnergy(Player player, decimal amount)
	{
		if (player != base.Owner)
		{
			return amount;
		}
		return amount + base.DynamicVars.Energy.BaseValue;
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
		RoaringConfig config = RoaringConfigStore.Effective;
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
			}
		}
		// Auto end turn only for the two natural endings: the cap was reached, or no playable card was left.
		// A finished combat and a turn that already changed must never end the turn again.
		bool endedNaturally = !combatEnded && !turnEnded;
		if (!config.AutoEndTurn || !endedNaturally)
		{
			return;
		}
		PlayerCmd.EndTurn(base.Owner, canBackOut: false);
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
