using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;

namespace PhaseDpsChecker.Combat;

public sealed class PartyRoster
{
	private readonly Dictionary<uint, uint> cachedJobIds = new Dictionary<uint, uint>();

	private readonly Configuration configuration;

	private readonly IPartyList partyList;

	private readonly IObjectTable objectTable;

	public PartyRoster(Configuration configuration, IPartyList partyList, IObjectTable objectTable)
	{
		this.configuration = configuration;
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
				cachedJobIds[partyMember.EntityId] = partyMember.ClassJob.RowId;
			}
		}
		IPlayerCharacter localPlayer = objectTable.LocalPlayer;
		if (localPlayer != null && localPlayer.EntityId != 0)
		{
			dictionary.TryAdd(localPlayer.EntityId, localPlayer.Name.TextValue);
			cachedJobIds[localPlayer.EntityId] = localPlayer.ClassJob.RowId;
		}
		if (configuration.ReplayMode)
		{
			AddReplayMembers(dictionary);
		}
		return dictionary;
	}

	private void AddReplayMembers(IDictionary<uint, string> members)
	{
		foreach (IGameObject gameObject in objectTable)
		{
			if (gameObject is not ICharacter character || character.EntityId == 0)
			{
				continue;
			}

			string displayName = character.Name.TextValue;
			if (!ReplayPartyMemberNames.TryResolve(displayName, character.ClassJob.RowId, out uint jobId))
			{
				continue;
			}

			members[character.EntityId] = displayName.Trim();
			cachedJobIds[character.EntityId] = jobId;
		}
	}

	public uint GetJobId(uint entityId)
	{
		if (objectTable.SearchByEntityId(entityId) is ICharacter character && character.ClassJob.RowId != 0)
		{
			uint currentJobId = character.ClassJob.RowId;
			cachedJobIds[entityId] = currentJobId;
			return currentJobId;
		}
		return cachedJobIds.TryGetValue(entityId, out uint cachedJobId) ? cachedJobId : 0u;
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
