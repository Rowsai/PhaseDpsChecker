using System;
using Dalamud.Configuration;
using PhaseDpsChecker.Combat;

namespace PhaseDpsChecker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
	public int Version { get; set; } = 6;

	public uint SelectedEntityId { get; set; }

	public bool ShowPartyOverlay { get; set; }

	public bool ReplayMode { get; set; }

	public PhaseDetectionPreset PhaseDetectionPreset { get; set; } = PhaseDetectionPreset.Normal;

	public int MaxPhaseHistory { get; set; } = 50;

	public int MaxEncounterHistory { get; set; } = 20;

	public string HistoryDirectory { get; set; } = string.Empty;

	public void Save()
	{
		Plugin.PluginInterface.SavePluginConfig(this);
	}
}
