using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PhaseDpsChecker.Combat;

public sealed record RaidBuffContribution(
	double ExternalDamageReceived,
	IReadOnlyDictionary<uint, double> DamageGrantedByProvider,
	bool HasExternalCriticalBuff,
	bool HasExternalDirectHitBuff)
{
	public static readonly RaidBuffContribution Empty = new(0.0, new Dictionary<uint, double>(), false, false);
}

internal enum RaidBuffKind
{
	Damage,
	Critical,
	DirectHit,
}

internal sealed record RaidBuffComponent(uint ProviderEntityId, RaidBuffKind Kind, double Rate);

internal sealed class RaidBuffContributionCalculator
{
	private readonly IDataManager dataManager;
	private readonly IObjectTable objectTable;
	private readonly PartyRoster roster;

	public RaidBuffContributionCalculator(IDataManager dataManager, IObjectTable objectTable, PartyRoster roster)
	{
		this.dataManager = dataManager;
		this.objectTable = objectTable;
		this.roster = roster;
	}

	public RaidBuffContribution Calculate(
		uint sourceEntityId,
		uint attackerEntityId,
		uint targetEntityId,
		EffectSample effect,
		PlayerPhaseStatistics player,
		IReadOnlyDictionary<uint, string> partyMembers)
	{
		if (effect.Damage == 0)
		{
			return RaidBuffContribution.Empty;
		}

		List<RaidBuffComponent> buffs = Capture(sourceEntityId, attackerEntityId, targetEntityId, partyMembers);
		if (buffs.Count == 0)
		{
			return RaidBuffContribution.Empty;
		}

		List<RaidBuffComponent> damageBuffs = buffs.Where(buff => buff.Kind == RaidBuffKind.Damage).ToList();
		List<RaidBuffComponent> criticalBuffs = buffs.Where(buff => buff.Kind == RaidBuffKind.Critical).ToList();
		List<RaidBuffComponent> directHitBuffs = buffs.Where(buff => buff.Kind == RaidBuffKind.DirectHit).ToList();
		double damageMultiplier = damageBuffs.Aggregate(1.0, (value, buff) => value * (1.0 + buff.Rate));
		double criticalBonus = buffs.Where(buff => buff.Kind == RaidBuffKind.Critical).Sum(buff => buff.Rate);
		double directHitBonus = buffs.Where(buff => buff.Kind == RaidBuffKind.DirectHit).Sum(buff => buff.Rate);
		double criticalChance = Math.Clamp(player.EstimatedUnbuffedCriticalChance + criticalBonus, 0.0, 1.0);
		double directHitChance = Math.Clamp(player.EstimatedUnbuffedDirectHitChance + directHitBonus, 0.0, 1.0);
		double externalCriticalProbability = effect.Critical && criticalChance > 0.0 ? Math.Clamp(criticalBonus / criticalChance, 0.0, 1.0) : 0.0;
		double externalDirectHitProbability = effect.DirectHit && directHitChance > 0.0 ? Math.Clamp(directHitBonus / directHitChance, 0.0, 1.0) : 0.0;
		const double criticalMultiplier = 1.40;
		const double directHitMultiplier = 1.25;

		Dictionary<uint, double> granted = new();
		double observedDamage = effect.Damage;
		double afterDamageBuffRemoval = observedDamage / damageMultiplier;
		double damageBuffReceived = observedDamage - afterDamageBuffRemoval;
		double criticalBuffReceived = afterDamageBuffRemoval * externalCriticalProbability * (1.0 - 1.0 / criticalMultiplier);
		double afterCriticalRemoval = afterDamageBuffRemoval - criticalBuffReceived;
		double directHitBuffReceived = afterCriticalRemoval * externalDirectHitProbability * (1.0 - 1.0 / directHitMultiplier);
		double received = damageBuffReceived + criticalBuffReceived + directHitBuffReceived;

		// FFLogs型rDPSでは、各付与者がそのバフを付与しなかった場合との差（限界寄与）を
		// 付与者へ加算する。複数バフの増分を対数比で分割しない。
		foreach (RaidBuffComponent buff in damageBuffs)
		{
			AddGranted(granted, buff.ProviderEntityId, observedDamage * buff.Rate / (1.0 + buff.Rate));
		}
		foreach (RaidBuffComponent buff in criticalBuffs)
		{
			double probability = effect.Critical && criticalChance > 0.0 ? Math.Clamp(buff.Rate / criticalChance, 0.0, 1.0) : 0.0;
			AddGranted(granted, buff.ProviderEntityId, afterDamageBuffRemoval * probability * (1.0 - 1.0 / criticalMultiplier));
		}
		foreach (RaidBuffComponent buff in directHitBuffs)
		{
			double probability = effect.DirectHit && directHitChance > 0.0 ? Math.Clamp(buff.Rate / directHitChance, 0.0, 1.0) : 0.0;
			AddGranted(granted, buff.ProviderEntityId, afterCriticalRemoval * probability * (1.0 - 1.0 / directHitMultiplier));
		}

		return new RaidBuffContribution(received, granted, criticalBonus > 0.0, directHitBonus > 0.0);
	}

	private List<RaidBuffComponent> Capture(uint sourceEntityId, uint attackerEntityId, uint targetEntityId, IReadOnlyDictionary<uint, string> partyMembers)
	{
		List<RaidBuffComponent> result = new();
		HashSet<(uint StatusId, uint ProviderId, RaidBuffKind Kind)> seen = new();
		AddActorStatuses(sourceEntityId, attackerEntityId, partyMembers, result, seen);
		if (sourceEntityId != attackerEntityId)
		{
			AddActorStatuses(attackerEntityId, attackerEntityId, partyMembers, result, seen);
		}
		AddActorStatuses(targetEntityId, attackerEntityId, partyMembers, result, seen);
		return result;
	}

	private void AddActorStatuses(
		uint entityId,
		uint attackerEntityId,
		IReadOnlyDictionary<uint, string> partyMembers,
		List<RaidBuffComponent> output,
		HashSet<(uint StatusId, uint ProviderId, RaidBuffKind Kind)> seen)
	{
		if (objectTable.SearchByEntityId(entityId) is not IBattleChara actor)
		{
			return;
		}
		foreach (var status in actor.StatusList)
		{
			uint providerId = roster.ResolvePartyOwner(status.SourceId, partyMembers);
			if (providerId == 0 || providerId == attackerEntityId || !TryResolve(status.StatusId, out List<(RaidBuffKind Kind, double Rate)>? components))
			{
				continue;
			}
			foreach ((RaidBuffKind kind, double rate) in components)
			{
				if (seen.Add((status.StatusId, providerId, kind)))
				{
					output.Add(new RaidBuffComponent(providerId, kind, rate));
				}
			}
		}
	}

	private bool TryResolve(uint statusId, out List<(RaidBuffKind Kind, double Rate)> components)
	{
		List<(RaidBuffKind Kind, double Rate)> resolved = new();
		components = resolved;
		if (!dataManager.GetExcelSheet<Status>().TryGetRow(statusId, out Status status))
		{
			return false;
		}
		string name = status.Name.ToString();
		if (Matches(name, "Divination", "ディヴィネーション")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Technical Finish", "テクニカルフィニッシュ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Standard Finish", "スタンダードフィニッシュ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Brotherhood", "桃園結義")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Embolden", "エンボルデン")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Arcane Circle", "アルケインサークル")) Add(RaidBuffKind.Damage, 0.03);
		else if (Matches(name, "Searing Light", "シアリングライト")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Radiant Finale", "光神のフィナーレ")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Starry Muse", "星空のモチーフ", "星空のミューズ")) Add(RaidBuffKind.Damage, 0.05);
		else if (Matches(name, "Kunai's Bane", "Dokumori", "くないの毒", "毒盛の術")) Add(RaidBuffKind.Damage, 0.10);
		else if (Matches(name, "The Balance", "The Spear", "アーゼマの均衡", "ハルオーネの槍")) Add(RaidBuffKind.Damage, 0.06);
		else if (Matches(name, "Battle Litany", "バトルリタニー")) Add(RaidBuffKind.Critical, 0.10);
		else if (Matches(name, "Chain Stratagem", "連環計")) Add(RaidBuffKind.Critical, 0.10);
		else if (Matches(name, "Battle Voice", "バトルボイス")) Add(RaidBuffKind.DirectHit, 0.20);
		else if (Matches(name, "Devilment", "攻めのタンゴ"))
		{
			Add(RaidBuffKind.Critical, 0.20);
			Add(RaidBuffKind.DirectHit, 0.20);
		}
		return resolved.Count != 0;

		void Add(RaidBuffKind kind, double rate) => resolved.Add((kind, rate));
	}

	private static bool Matches(string actual, params string[] names) => names.Any(name => string.Equals(actual, name, StringComparison.OrdinalIgnoreCase));

	private static void AddGranted(Dictionary<uint, double> values, uint providerId, double amount)
	{
		values[providerId] = values.TryGetValue(providerId, out double current) ? current + amount : amount;
	}
}
