using System;
using Dalamud.Configuration;
using PhaseDpsChecker.Combat;

namespace PhaseDpsChecker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
	public int Version { get; set; } = 10;

	public bool IsEnabled { get; set; } = true;

	public uint SelectedEntityId { get; set; }

	public bool ShowPartyOverlay { get; set; }

	public bool ReplayMode { get; set; }

	public bool FflogsAnalyzeBase { get; set; }

	public PhaseDetectionPreset PhaseDetectionPreset { get; set; } = PhaseDetectionPreset.Normal;

	public bool ShowPhaseColumn { get; set; } = true;
	public bool ShowPlayerColumn { get; set; } = true;
	public bool ShowStartColumn { get; set; } = true;
	public bool ShowEndColumn { get; set; } = true;
	public bool ShowDpsColumn { get; set; } = true;
	public bool ShowRdpsColumn { get; set; } = true;
	public bool ShowTotalDamageColumn { get; set; } = true;
	public bool ShowCriticalColumn { get; set; } = true;
	public bool ShowDirectHitColumn { get; set; } = true;
	public bool ShowCriticalDirectHitColumn { get; set; } = true;
	public bool ShowMaximumDamageColumn { get; set; } = true;
	public bool ShowActiveColumn { get; set; } = true;
	public bool ShowDamageActiveColumn { get; set; } = true;
	public bool ShowHealingActiveColumn { get; set; } = true;

	public int MaxPhaseHistory { get; set; } = 50;

	public int MaxEncounterHistory { get; set; } = 20;

	public string HistoryDirectory { get; set; } = string.Empty;

	public bool IsSummaryColumnVisible(SummaryDisplayColumn column) => column switch
	{
		SummaryDisplayColumn.Phase => ShowPhaseColumn,
		SummaryDisplayColumn.Player => ShowPlayerColumn,
		SummaryDisplayColumn.Start => ShowStartColumn,
		SummaryDisplayColumn.End => ShowEndColumn,
		SummaryDisplayColumn.Dps => ShowDpsColumn,
		SummaryDisplayColumn.Rdps => ShowRdpsColumn,
		SummaryDisplayColumn.TotalDamage => ShowTotalDamageColumn,
		SummaryDisplayColumn.Critical => ShowCriticalColumn,
		SummaryDisplayColumn.DirectHit => ShowDirectHitColumn,
		SummaryDisplayColumn.CriticalDirectHit => ShowCriticalDirectHitColumn,
		SummaryDisplayColumn.MaximumDamage => ShowMaximumDamageColumn,
		SummaryDisplayColumn.Active => ShowActiveColumn,
		SummaryDisplayColumn.DamageActive => ShowDamageActiveColumn,
		SummaryDisplayColumn.HealingActive => ShowHealingActiveColumn,
		_ => true,
	};

	public void SetSummaryColumnVisible(SummaryDisplayColumn column, bool visible)
	{
		switch (column)
		{
			case SummaryDisplayColumn.Phase: ShowPhaseColumn = visible; break;
			case SummaryDisplayColumn.Player: ShowPlayerColumn = visible; break;
			case SummaryDisplayColumn.Start: ShowStartColumn = visible; break;
			case SummaryDisplayColumn.End: ShowEndColumn = visible; break;
			case SummaryDisplayColumn.Dps: ShowDpsColumn = visible; break;
			case SummaryDisplayColumn.Rdps: ShowRdpsColumn = visible; break;
			case SummaryDisplayColumn.TotalDamage: ShowTotalDamageColumn = visible; break;
			case SummaryDisplayColumn.Critical: ShowCriticalColumn = visible; break;
			case SummaryDisplayColumn.DirectHit: ShowDirectHitColumn = visible; break;
			case SummaryDisplayColumn.CriticalDirectHit: ShowCriticalDirectHitColumn = visible; break;
			case SummaryDisplayColumn.MaximumDamage: ShowMaximumDamageColumn = visible; break;
			case SummaryDisplayColumn.Active: ShowActiveColumn = visible; break;
			case SummaryDisplayColumn.DamageActive: ShowDamageActiveColumn = visible; break;
			case SummaryDisplayColumn.HealingActive: ShowHealingActiveColumn = visible; break;
		}
	}

	public void EnableAllSummaryColumns()
	{
		foreach (SummaryDisplayColumn column in SummaryDisplayColumnCatalog.All)
		{
			SetSummaryColumnVisible(column, true);
		}
	}

	public void Save()
	{
		Plugin.PluginInterface.SavePluginConfig(this);
	}
}
