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

	private readonly PartyOverlayWindow partyOverlayWindow;

	private readonly CombatTracker combatTracker;
	private HistoryFileSizeLevel lastHistoryFileSizeLevel;

	[PluginService]
	internal static IDalamudPluginInterface PluginInterface { get; private set; }

	[PluginService]
	internal static ICommandManager CommandManager { get; private set; }

	[PluginService]
	internal static IFramework Framework { get; private set; }

	[PluginService]
	internal static IDataManager DataManager { get; private set; }

	[PluginService]
	internal static ITextureProvider TextureProvider { get; private set; }

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
	internal static IChatGui ChatGui { get; private set; }

	[PluginService]
	internal static IGameInteropProvider GameInteropProvider { get; private set; }

	[PluginService]
	internal static IPluginLog Log { get; private set; }

	public Configuration Configuration { get; }

	public Plugin()
	{
		Configuration = (PluginInterface.GetPluginConfig() as Configuration) ?? new Configuration();
		if (Configuration.Version < 7)
		{
			if (Configuration.MaxEncounterHistory <= 0)
			{
				Configuration.MaxEncounterHistory = 20;
			}
			Configuration.IsEnabled = true;
			Configuration.Version = 7;
			Configuration.Save();
		}
		if (Configuration.Version < 8)
		{
			Configuration.EnableAllSummaryColumns();
			Configuration.Version = 8;
			Configuration.Save();
		}
		if (Configuration.Version < 9)
		{
			Configuration.FflogsAnalyzeBase = false;
			Configuration.Version = 9;
			Configuration.Save();
		}
		if (Configuration.Version < 10)
		{
			Configuration.ShowTotalDamageColumn = true;
			Configuration.Version = 10;
			Configuration.Save();
		}
		combatTracker = new CombatTracker(Configuration, Framework, DataManager, ObjectTable, PartyList, DutyState, Condition, ClientState, ChatGui, GameInteropProvider, Log);
		partyOverlayWindow = new PartyOverlayWindow(Configuration, combatTracker);
		mainWindow = new MainWindow(Configuration, combatTracker);
		windowSystem.AddWindow(mainWindow);
		windowSystem.AddWindow(partyOverlayWindow);
		CommandManager.AddHandler("/phasedps", new CommandInfo(OnCommand)
		{
			HelpMessage = "Phase DPS Checker を開閉します。",
			ShowInHelp = true
		});
		PluginInterface.UiBuilder.Draw += DrawUi;
		PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
		PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
	}

	public void Dispose()
	{
		PluginInterface.UiBuilder.Draw -= DrawUi;
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

	private void DrawUi()
	{
		HistoryFileSizeLevel currentHistoryFileSizeLevel = combatTracker.HistoryFileSizeStatus.Level;
		if (currentHistoryFileSizeLevel == HistoryFileSizeLevel.Danger && lastHistoryFileSizeLevel != HistoryFileSizeLevel.Danger)
		{
			mainWindow.IsOpen = true;
		}
		lastHistoryFileSizeLevel = currentHistoryFileSizeLevel;
		partyOverlayWindow.IsOpen = Configuration.IsEnabled && Configuration.ShowPartyOverlay;
		windowSystem.Draw();
	}
}
