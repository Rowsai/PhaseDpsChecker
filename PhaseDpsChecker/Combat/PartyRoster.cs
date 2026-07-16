using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;

namespace PhaseDpsChecker.Combat;

public sealed class PartyRoster
{
	private readonly IPartyList partyList;

	private readonly IObjectTable objectTable;

	public PartyRoster(IPartyList partyList, IObjectTable objectTable)
	{
		this.partyList = partyList;
		this.objectTable = objectTable;
	}

	public Dictionary<uint, string> GetCurrentMembers()
	{
		Dictionary<uint, string> dictionary = new Dictionary<uint, string>();
		for (int i = 0; i < partyList.Length; i++)
		{
			IPartyMember partyMember = partyList[i];
			if (partyMember != null && partyMember.EntityId != 0)
			{
				dictionary[partyMember.EntityId] = partyMember.Name.TextValue;
			}
		}
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer != null && localPlayer.EntityId != 0)
		{
			dictionary.TryAdd(localPlayer.EntityId, localPlayer.Name.TextValue);
		}
		return dictionary;
	}

	public uint ResolvePartyOwner(uint sourceEntityId, IReadOnlyDictionary<uint, string> members)
	{
		if (members.ContainsKey(sourceEntityId))
		{
			return sourceEntityId;
		}
		IGameObject gameObject = objectTable.SearchByEntityId(sourceEntityId);
		if (gameObject != null && gameObject.OwnerId != 0 && members.ContainsKey(gameObject.OwnerId))
		{
			return gameObject.OwnerId;
		}
		return 0u;
	}
}
