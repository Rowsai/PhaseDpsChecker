using System;
using System.Collections.Concurrent;
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
	private readonly Func<string> getConfiguredWebSocketUrl;
	private readonly ICallGateSubscriber<Version> getVersion;
	private readonly ICallGateSubscriber<Version> getIpcVersion;
	private readonly ICallGateSubscriber<string, bool> createLegacySubscriber;
	private readonly ICallGateSubscriber<string, bool> unsubscribe;
	private readonly ICallGateProvider<JObject, bool> receiver;
	private readonly ICallGateSubscriber<bool> getServerRunning;
	private readonly ICallGateSubscriber<Uri?> getServerUri;
	private readonly IinactWebSocketClient webSocket;
	private readonly IinactEncounterLifecycle encounterLifecycle = new();
	private readonly ConcurrentQueue<IinactCombatSnapshot> encounterStarts = new();
	private readonly ConcurrentQueue<IinactCombatSnapshot> encounterEnds = new();
	private DateTime nextRetryAt;
	private IinactCombatSnapshot? latest;
	private long sequence;
	private bool receiverRegistered;
	private bool subscriberCreated;
	private string webSocketResolutionError = string.Empty;
	private string lastSource = string.Empty;
	private DateTime lastSourceReceivedAt;

	public bool IsConnected
	{
		get
		{
			lock (syncRoot)
			{
				return latest != null || webSocket.IsConnected;
			}
		}
	}

	public Version? Version { get; private set; }
	public string Status { get; private set; } = "IINACTへの接続待ち";
	public string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IINACT");
	public string WebSocketEndpoint => webSocket.Endpoint?.ToString() ?? "自動検出待ち";

	public IinactBridge(IDalamudPluginInterface pluginInterface, IPluginLog log, Func<string> getConfiguredWebSocketUrl)
	{
		this.log = log;
		this.getConfiguredWebSocketUrl = getConfiguredWebSocketUrl;
		getVersion = pluginInterface.GetIpcSubscriber<Version>("IINACT.Version");
		getIpcVersion = pluginInterface.GetIpcSubscriber<Version>("IINACT.IpcVersion");
		createLegacySubscriber = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.CreateLegacySubscriber");
		unsubscribe = pluginInterface.GetIpcSubscriber<string, bool>("IINACT.Unsubscribe");
		receiver = pluginInterface.GetIpcProvider<JObject, bool>(SubscriberName);
		getServerRunning = pluginInterface.GetIpcSubscriber<bool>("IINACT.Server.Listening");
		getServerUri = pluginInterface.GetIpcSubscriber<Uri?>("IINACT.Server.Uri");
		webSocket = new IinactWebSocketClient(message => Receive(message, "WebSocket"));
	}

	public void Update(DateTime now)
	{
		if (now < nextRetryAt)
		{
			return;
		}
		nextRetryAt = now + RetryInterval;
		TryConnect();
		RefreshStatus();
	}

	public bool TryGetLatest(out IinactCombatSnapshot? snapshot)
	{
		lock (syncRoot)
		{
			snapshot = latest;
			return snapshot != null;
		}
	}

	public bool HasFreshActiveCombatData(DateTime now)
	{
		lock (syncRoot)
		{
			return latest is { IsActive: true } snapshot && now - snapshot.ReceivedAt <= TimeSpan.FromSeconds(15);
		}
	}

	public bool TryTakeEncounterStart(out IinactCombatSnapshot? snapshot) =>
		TryTakeLatest(encounterStarts, out snapshot);

	public bool TryTakeEncounterEnd(out IinactCombatSnapshot? snapshot) =>
		TryTakeLatest(encounterEnds, out snapshot);

	public void Dispose()
	{
		webSocket.Dispose();
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
			TryEnsureIpcSubscriber();
			TryEnsureWebSocket();
		}
		catch (Exception ex)
		{
			subscriberCreated = false;
			Status = "IINACTが見つかりません。IINACTを導入・有効化してください";
			log.Verbose(ex, "IINACTへの接続待ちです。");
		}
	}

	private void TryEnsureIpcSubscriber()
	{
		try
		{
			if (!receiverRegistered)
			{
				receiver.RegisterFunc(message => Receive(message, "IPC"));
				receiverRegistered = true;
			}
			if (!subscriberCreated)
			{
				subscriberCreated = createLegacySubscriber.InvokeFunc(SubscriberName);
				if (!subscriberCreated)
				{
					unsubscribe.InvokeFunc(SubscriberName);
					subscriberCreated = createLegacySubscriber.InvokeFunc(SubscriberName);
				}
			}
		}
		catch (Exception ex)
		{
			subscriberCreated = false;
			log.Verbose(ex, "IINACT IPCのCombatData購読に失敗したため、WebSocketを使用します。");
		}
	}

	private void TryEnsureWebSocket()
	{
		string configured = getConfiguredWebSocketUrl();
		Uri? discovered = null;
		bool serverRunning = false;
		try
		{
			serverRunning = getServerRunning.InvokeFunc();
			if (serverRunning)
			{
				discovered = getServerUri.InvokeFunc();
			}
		}
		catch (Exception ex)
		{
			log.Verbose(ex, "IINACT WebSocket接続先の自動検出に失敗しました。");
		}

		if (string.IsNullOrWhiteSpace(configured) && !serverRunning)
		{
			webSocketResolutionError = "IINACTのWebSocketサーバーが停止しています";
			return;
		}
		if (!IinactWebSocketEndpoint.TryResolve(configured, discovered, out Uri? endpoint, out string error) || endpoint == null)
		{
			webSocketResolutionError = error;
			return;
		}
		webSocketResolutionError = string.Empty;
		webSocket.EnsureConnected(endpoint);
	}

	private bool Receive(JObject message, string source)
	{
		try
		{
			if (!IinactCombatDataParser.TryExtract(message, out JObject combatData))
			{
				return true;
			}
			DateTime receivedAt = DateTime.UtcNow;
			IinactCombatSnapshot snapshot = IinactCombatDataParser.Parse(combatData, receivedAt, System.Threading.Interlocked.Increment(ref sequence));
			IinactEncounterTransition transition;
			bool acceptedSource;
			lock (syncRoot)
			{
				transition = encounterLifecycle.Observe(source, snapshot);
				acceptedSource = string.IsNullOrWhiteSpace(lastSource)
					|| string.Equals(lastSource, source, StringComparison.Ordinal)
					|| receivedAt - lastSourceReceivedAt > TimeSpan.FromSeconds(15);
				if (acceptedSource)
				{
					latest = snapshot;
					lastSource = source;
					lastSourceReceivedAt = receivedAt;
				}
			}
			if (transition == IinactEncounterTransition.Started)
			{
				encounterStarts.Enqueue(snapshot);
			}
			else if (transition == IinactEncounterTransition.Ended)
			{
				encounterEnds.Enqueue(snapshot);
			}
			if (!acceptedSource)
			{
				return true;
			}
			Status = $"IINACT {Version} / {source} / {(snapshot.IsActive ? "計測中" : "END")} / 最終集計 {snapshot.ReceivedAt.ToLocalTime():HH:mm:ss} / {snapshot.Combatants.Count}人";
		}
		catch (Exception ex)
		{
			log.Warning(ex, "IINACT CombatDataの読み取りに失敗しました。");
		}
		return true;
	}

	private static bool TryTakeLatest(ConcurrentQueue<IinactCombatSnapshot> queue, out IinactCombatSnapshot? snapshot)
	{
		snapshot = null;
		while (queue.TryDequeue(out IinactCombatSnapshot? candidate))
		{
			snapshot = candidate;
		}
		return snapshot != null;
	}

	private void RefreshStatus()
	{
		IinactCombatSnapshot? snapshot;
		string source;
		lock (syncRoot)
		{
			snapshot = latest;
			source = lastSource;
		}
		if (snapshot != null)
		{
			Status = $"IINACT {Version} / {source} / {(snapshot.IsActive ? "計測中" : "END")} / 最終集計 {snapshot.ReceivedAt.ToLocalTime():HH:mm:ss} / {snapshot.Combatants.Count}人";
			return;
		}
		if (webSocket.IsConnected)
		{
			Status = $"IINACT {Version} / WebSocket接続済み / CombatData待ち";
			return;
		}
		if (subscriberCreated)
		{
			Status = $"IINACT {Version} / IPC購読済み / CombatData待ち";
			return;
		}
		string socketError = !string.IsNullOrWhiteSpace(webSocket.LastError) ? webSocket.LastError : webSocketResolutionError;
		Status = string.IsNullOrWhiteSpace(socketError)
			? $"IINACT {Version} / CombatData接続待ち"
			: $"IINACT {Version} / WebSocket接続待ち: {socketError}";
	}
}
