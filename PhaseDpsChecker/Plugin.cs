using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PhaseDpsChecker.Combat;
using PhaseDpsChecker.Windows;

namespace PhaseDpsChecker;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
	private const string CommandName = "/phasedps";

	private readonly WindowSystem windowSystem = new WindowSystem("PhaseDpsChecker");

	private readonly MainWindow mainWindow;

	private readonly CombatTracker combatTracker;

	[PluginService]
	internal static IDalamudPluginInterface PluginInterface { get; private set; }

	[PluginService]
	internal static ICommandManager CommandManager { get; private set; }

	[PluginService]
	internal static IFramework Framework { get; private set; }

	[PluginService]
	internal static IDataManager DataManager { get; private set; }

	[PluginService]
	internal static IObjectTable ObjectTable { get; private set; }

	[PluginService]
	internal static IPartyList PartyList { get; private set; }

	[PluginService]
	internal static IDutyState DutyState { get; private set; }

	[PluginService]
	internal static ICondition Condition { get; private set; }

	[PluginService]
	internal static IClientState ClientState { get; private set; }

	[PluginService]
	internal static IGameInteropProvider GameInteropProvider { get; private set; }

	[PluginService]
	internal static IPluginLog Log { get; private set; }

	public Configuration Configuration { get; }

	public Plugin()
	{
		Configuration = (PluginInterface.GetPluginConfig() as Configuration) ?? new Configuration();
		if (Configuration.Version < 2)
		{
			Configuration.Version = 2;
			if (Configuration.MaxEncounterHistory <= 0)
			{
				Configuration.MaxEncounterHistory = 20;
			}
			Configuration.Save();
		}
		combatTracker = new CombatTracker(Configuration, Framework, DataManager, ObjectTable, PartyList, DutyState, Condition, ClientState, GameInteropProvider, Log);
		mainWindow = new MainWindow(Configuration, combatTracker);
		windowSystem.AddWindow(mainWindow);
		CommandManager.AddHandler("/phasedps", new CommandInfo(OnCommand)
		{
			HelpMessage = "Phase DPS Checker を開閉します。",
			ShowInHelp = true
		});
		PluginInterface.UiBuilder.Draw += windowSystem.Draw;
		PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
		PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
	}

	public void Dispose()
	{
		PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
		PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
		PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
		CommandManager.RemoveHandler("/phasedps");
		windowSystem.RemoveAllWindows();
		combatTracker.Dispose();
		mainWindow.Dispose();
	}

	private void OnCommand(string _, string __)
	{
		ToggleMainUi();
	}

	private void ToggleMainUi()
	{
		mainWindow.Toggle();
	}
}
