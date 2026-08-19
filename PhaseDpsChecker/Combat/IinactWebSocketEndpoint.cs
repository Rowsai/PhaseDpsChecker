using System;
using System.Collections.Generic;

namespace PhaseDpsChecker.Combat;

internal static class IinactWebSocketEndpoint
{
	public static bool TryResolve(string configuredValue, Uri? discoveredValue, out Uri? endpoint, out string error)
	{
		endpoint = null;
		error = string.Empty;
		string candidate = configuredValue?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(candidate))
		{
			if (discoveredValue == null)
			{
				error = "IINACT WebSocketの接続先を取得できませんでした";
				return false;
			}
			candidate = discoveredValue.ToString();
		}

		if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? parsed))
		{
			if (!candidate.Contains("://", StringComparison.Ordinal) && Uri.TryCreate($"ws://{candidate}", UriKind.Absolute, out parsed))
			{
				candidate = parsed.ToString();
			}
			else
			{
				error = "WebSocket URLの形式が正しくありません";
				return false;
			}
		}

		if (parsed.Scheme is "http" or "https")
		{
			if (!TryExtractOverlayWebSocket(parsed, out string nested))
			{
				error = "オーバーレイURLにはHOST_PORTまたはOVERLAY_WSが必要です";
				return false;
			}
			if (!Uri.TryCreate(nested, UriKind.Absolute, out parsed))
			{
				error = "オーバーレイURL内のWebSocket URLが正しくありません";
				return false;
			}
		}

		if (parsed.Scheme is not "ws" and not "wss")
		{
			error = "接続先はws://またはwss://で指定してください";
			return false;
		}

		string host = parsed.Host;
		if (host is "0.0.0.0" or "::" or "[::]" or "*")
		{
			host = "127.0.0.1";
		}
		UriBuilder builder = new(parsed)
		{
			Host = host,
			Path = "/ws",
			Query = string.Empty,
			Fragment = string.Empty,
		};
		endpoint = builder.Uri;
		return true;
	}

	private static bool TryExtractOverlayWebSocket(Uri overlayUri, out string value)
	{
		value = string.Empty;
		Dictionary<string, string> query = new(StringComparer.OrdinalIgnoreCase);
		foreach (string pair in overlayUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] parts = pair.Split('=', 2);
			string key = Uri.UnescapeDataString(parts[0]);
			string item = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
			query[key] = item;
		}
		if (query.TryGetValue("OVERLAY_WS", out string? modern) && !string.IsNullOrWhiteSpace(modern))
		{
			value = modern;
			return true;
		}
		if (query.TryGetValue("HOST_PORT", out string? legacy) && !string.IsNullOrWhiteSpace(legacy))
		{
			value = legacy;
			return true;
		}
		return false;
	}
}
