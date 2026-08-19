using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PhaseDpsChecker.Combat;

internal sealed class IinactWebSocketClient : IDisposable
{
	private const int MaximumMessageBytes = 4 * 1024 * 1024;
	private readonly object syncRoot = new();
	private readonly Action<JObject> receive;
	private CancellationTokenSource? cancellation;
	private ClientWebSocket? socket;
	private Task? worker;
	private Uri? endpoint;
	private string lastError = string.Empty;
	private long generation;
	private int connected;

	public bool IsConnected => Volatile.Read(ref connected) != 0;

	public Uri? Endpoint
	{
		get
		{
			lock (syncRoot)
			{
				return endpoint;
			}
		}
	}

	public string LastError
	{
		get
		{
			lock (syncRoot)
			{
				return lastError;
			}
		}
	}

	public IinactWebSocketClient(Action<JObject> receive)
	{
		this.receive = receive;
	}

	public void EnsureConnected(Uri target)
	{
		CancellationTokenSource? previousCancellation;
		ClientWebSocket? previousSocket;
		lock (syncRoot)
		{
			if (endpoint == target && worker is { IsCompleted: false })
			{
				return;
			}
			previousCancellation = cancellation;
			previousSocket = socket;
			cancellation = new CancellationTokenSource();
			socket = new ClientWebSocket();
			socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
			endpoint = target;
			lastError = string.Empty;
			long currentGeneration = ++generation;
			worker = RunAsync(currentGeneration, target, socket, cancellation.Token);
		}
		previousCancellation?.Cancel();
		previousSocket?.Abort();
		previousSocket?.Dispose();
		previousCancellation?.Dispose();
	}

	public void Dispose()
	{
		CancellationTokenSource? currentCancellation;
		ClientWebSocket? currentSocket;
		lock (syncRoot)
		{
			generation++;
			currentCancellation = cancellation;
			currentSocket = socket;
			cancellation = null;
			socket = null;
			worker = null;
			endpoint = null;
			Volatile.Write(ref connected, 0);
		}
		currentCancellation?.Cancel();
		currentSocket?.Abort();
		currentSocket?.Dispose();
		currentCancellation?.Dispose();
	}

	private async Task RunAsync(long currentGeneration, Uri target, ClientWebSocket currentSocket, CancellationToken cancellationToken)
	{
		Exception? failure = null;
		try
		{
			using (CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				connectCancellation.CancelAfter(TimeSpan.FromSeconds(5));
				await currentSocket.ConnectAsync(target, connectCancellation.Token).ConfigureAwait(false);
			}
			if (IsCurrent(currentGeneration))
			{
				Volatile.Write(ref connected, 1);
			}
			byte[] subscribe = Encoding.UTF8.GetBytes("{\"call\":\"subscribe\",\"events\":[\"CombatData\"]}");
			await currentSocket.SendAsync(subscribe, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
			await ReceiveLoopAsync(currentSocket, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			failure = ex;
		}
		finally
		{
			if (IsCurrent(currentGeneration))
			{
				Volatile.Write(ref connected, 0);
				lock (syncRoot)
				{
					lastError = failure?.Message ?? string.Empty;
				}
			}
			currentSocket.Dispose();
		}
	}

	private async Task ReceiveLoopAsync(ClientWebSocket currentSocket, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[16 * 1024];
		while (currentSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			using MemoryStream message = new();
			WebSocketReceiveResult result;
			do
			{
				result = await currentSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}
				message.Write(buffer, 0, result.Count);
				if (message.Length > MaximumMessageBytes)
				{
					throw new InvalidDataException("IINACT WebSocketメッセージが上限を超えました");
				}
			}
			while (!result.EndOfMessage);

			if (result.MessageType != WebSocketMessageType.Text)
			{
				continue;
			}
			string json = Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
			receive(JObject.Parse(json));
		}
	}

	private bool IsCurrent(long currentGeneration)
	{
		lock (syncRoot)
		{
			return generation == currentGeneration;
		}
	}
}
