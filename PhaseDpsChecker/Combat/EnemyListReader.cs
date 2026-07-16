using System;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace PhaseDpsChecker.Combat;

internal static unsafe class EnemyListReader
{
	public static bool TryIsEmpty(out bool isEmpty)
	{
		UIState* uiState = UIState.Instance();
		if (uiState == null)
		{
			isEmpty = false;
			return false;
		}

		isEmpty = uiState->Hater.HaterCount <= 0;
		return true;
	}

	public static bool TryContainsAny(out bool containsAny, params string[] enemyNames)
	{
		UIState* uiState = UIState.Instance();
		if (uiState == null)
		{
			containsAny = false;
			return false;
		}

		containsAny = false;
		int count = Math.Clamp(uiState->Hater.HaterCount, 0, uiState->Hater.Haters.Length);
		for (int index = 0; index < count; index++)
		{
			string name = uiState->Hater.Haters[index].NameString;
			foreach (string enemyName in enemyNames)
			{
				if (string.Equals(name, enemyName, StringComparison.Ordinal))
				{
					containsAny = true;
					return true;
				}
			}
		}

		return true;
	}
}
