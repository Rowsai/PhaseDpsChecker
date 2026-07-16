using System;
using Dalamud.Configuration;

namespace PhaseDpsChecker;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
	public int Version { get; set; } = 3;

	public uint SelectedEntityId { get; set; }

	public bool ShowPartyOverlay { get; set; }

	public float TargetLossGraceSeconds { get; set; } = 0.75f;

	public int MaxPhaseHistory { get; set; } = 50;

	public int MaxEncounterHistory { get; set; } = 20;

	public void Save()
	{
		Plugin.PluginInterface.SavePluginConfig(this);
	}
}
