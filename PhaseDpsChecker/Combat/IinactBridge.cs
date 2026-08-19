using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json.Linq;

namespace PhaseDpsChecker.Combat;

internal sealed class IinactBridge : IDisposable
{
	private const string SubscriberName = "PhaseDpsChecker.IINACT";
	private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
	private readonly object syncRoot = new();
	private readonly IPluginLog log;
	private readonly ICallGateSubscriber<Version> getVersion;
	private readonly ICallGateSubscriber<Version> getIpcVersion;
	private readonly ICallGateSubscriber<string, bool> createSubscriber;
	private readonly ICallGateSubscriber<string, bool> unsubscribe;
	private readonly ICallGateProvider<JObject, bool> receiver;
	private readonly ICallGateSubscriber<JObject, bool> sender;
	private DateTime nextRetryAt;
	private IinactCombatSnapshot? latest;
	private long sequence;
	private bool receiverRegistered;
	private bool subscriberCreated;

	public bool IsConnected { get; private set; }
	public Version? Version { get; private set; }
	public string Status { get; private set; } = "IINACTへの接続待ち";
	public string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IINACT");

	public IinactBridge(IDalamudPluginInterface pluginInterface, IPluginLog log)
	{
		this.log = log;
		getVersion = pluginInterface.GetIpcSubscriber<Version>("IINACT.Version");
		getIpcVersion = pluginInterface.GetIpcSubscriber<Version>("IINACT.IpcVersion");
		createSubscriber = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.CreateSubscriber");
		unsubscribe = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.Unsubscribe");
		receiver = pluginInterface.GetIpcProvider<JObject, bool>(SubscriberName);
		sender = pluginInterface.GetIpcSubscriber<JObject, bool>($"IINACT.IpcProvider.{SubscriberName}");
	}

	public void Update(DateTime now)
	{
		if (IsConnected || now < nextRetryAt)
		{
			return;
		}
		nextRetryAt = now + RetryInterval;
		TryConnect();
	}

	public bool TryGetLatest(out IinactCombatSnapshot? snapshot)
	{
		lock (syncRoot)
		{
			snapshot = latest;
			return snapshot != null;
		}
	}

	public void Dispose()
	{
		if (subscriberCreated)
		{
			try
			{
				unsubscribe.InvokeFunc(SubscriberName);
			}
			catch (Exception ex)
			{
				log.Debug(ex, "IINACT IPC購読の解除に失敗しました。");
			}
		}
		if (receiverRegistered)
		{
			receiver.UnregisterFunc();
		}
	}

	private void TryConnect()
	{
		try
		{
			Version = getVersion.InvokeFunc();
			Version ipcVersion = getIpcVersion.InvokeFunc();
			if (ipcVersion.Major < 2)
			{
				Status = $"IINACT IPC {ipcVersion} は未対応です（2.x以上が必要）";
				return;
			}

			if (!receiverRegistered)
			{
				receiver.RegisterFunc(Receive);
				receiverRegistered = true;
			}
			if (!subscriberCreated)
			{
				subscriberCreated = createSubscriber.InvokeFunc(SubscriberName);
			}
			if (!subscriberCreated)
			{
				Status = "IINACTは起動していますがIPC購読を作成できませんでした";
				return;
			}

			sender.InvokeAction(JObject.FromObject(new
			{
				call = "subscribe",
				events = new[] { "CombatData" }
			}));
			IsConnected = true;
			Status = $"IINACT {Version} 接続済み / ACT互換集計・FFLogsログ出力";
			log.Information("IINACT {Version} (IPC {IpcVersion}) に接続しました。", Version, ipcVersion);
		}
		catch (Exception ex)
		{
			IsConnected = false;
			Status = "IINACTが見つかりません。IINACTを導入・有効化してください";
			log.Verbose(ex, "IINACT IPCへの接続待ちです。");
		}
	}

	private bool Receive(JObject message)
	{
		try
		{
			if (!string.Equals(message.Value<string>("type"), "CombatData", StringComparison.Ordinal))
			{
				return true;
			}
			IinactCombatSnapshot snapshot = ParseCombatData(message, DateTime.UtcNow, ++sequence);
			lock (syncRoot)
			{
				latest = snapshot;
			}
			Status = $"IINACT {Version} 接続済み / 最終集計 {snapshot.ReceivedAt.ToLocalTime():HH:mm:ss}";
		}
		catch (Exception ex)
		{
			log.Warning(ex, "IINACT CombatDataの読み取りに失敗しました。");
		}
		return true;
	}

	internal static IinactCombatSnapshot ParseCombatData(JObject message, DateTime receivedAt, long sequence)
	{
		JObject encounter = message["Encounter"] as JObject ?? new JObject();
		string encounterId = Value(encounter, "encid", "EncId", "title", "TITLE");
		bool isActive = string.Equals(Value(message, "isActive"), "true", StringComparison.OrdinalIgnoreCase);
		Dictionary<string, IinactCombatantSnapshot> combatants = new(StringComparer.OrdinalIgnoreCase);
		if (message["Combatant"] is JObject combatantObject)
		{
			foreach (JProperty property in combatantObject.Properties())
			{
				if (property.Value is not JObject values)
				{
					continue;
				}
				string name = Value(values, "name", "Name", "NAME");
				if (string.IsNullOrWhiteSpace(name))
				{
					name = property.Name;
				}
				combatants[name] = new IinactCombatantSnapshot(
					name,
					Long(values, "damage", "Damage"),
					Long(values, "healed", "Healed"),
					Long(values, "damagetaken", "DamageTaken"),
					Int(values, "hits", "Hits"),
					Int(values, "crithits", "CritHits"),
					NullableInt(values, "DirectHitCount", "directhitcount", "directhits"),
					NullableInt(values, "CritDirectHitCount", "critdirecthitcount", "critdirecthits"));
			}
		}
		return new IinactCombatSnapshot(sequence, receivedAt, encounterId, isActive, combatants);
	}

	private static string Value(JObject values, params string[] keys)
	{
		foreach (string key in keys)
		{
			JProperty? property = values.Property(key, StringComparison.OrdinalIgnoreCase);
			if (property?.Value.Type is not JTokenType.Null and not JTokenType.Undefined)
			{
				return property.Value.ToString();
			}
		}
		return string.Empty;
	}

	private static long Long(JObject values, params string[] keys) =>
		long.TryParse(Value(values, keys), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out long value) ? value : 0;

	private static int Int(JObject values, params string[] keys) =>
		int.TryParse(Value(values, keys), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int value) ? value : 0;

	private static int? NullableInt(JObject values, params string[] keys)
	{
		string raw = Value(values, keys);
		return string.IsNullOrWhiteSpace(raw)
			? null
			: int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int value) ? value : null;
	}
}
