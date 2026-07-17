using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace PhaseDpsChecker.Combat;

public sealed class ActionEffectCapture : IDisposable
{
	private readonly ConcurrentQueue<RawActionEvent> queue = new ConcurrentQueue<RawActionEvent>();

	private readonly ConcurrentQueue<RawPeriodicEvent> periodicQueue = new ConcurrentQueue<RawPeriodicEvent>();

	private readonly IPluginLog log;

	private readonly Hook<ActionEffectHandler.Delegates.Receive> receiveHook;

	private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket> actorControlHook;
	private bool enabled;

	public unsafe ActionEffectCapture(IGameInteropProvider interopProvider, IPluginLog log, bool enabled)
	{
		this.log = log;
		receiveHook = interopProvider.HookFromAddress<ActionEffectHandler.Delegates.Receive>(ActionEffectHandler.MemberFunctionPointers.Receive, OnReceiveActionEffect);
		Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? createdActorControlHook = null;
		try
		{
			createdActorControlHook = interopProvider.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>((nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, OnActorControl);
			actorControlHook = createdActorControlHook;
			SetEnabled(enabled);
		}
		catch
		{
			if (this.enabled)
			{
				receiveHook.Disable();
			}
			createdActorControlHook?.Dispose();
			receiveHook.Dispose();
			throw;
		}
	}

	public void SetEnabled(bool shouldEnable)
	{
		if (enabled == shouldEnable)
		{
			return;
		}
		if (shouldEnable)
		{
			receiveHook.Enable();
			try
			{
				actorControlHook.Enable();
			}
			catch
			{
				receiveHook.Disable();
				throw;
			}
			enabled = true;
			return;
		}
		actorControlHook.Disable();
		receiveHook.Disable();
		enabled = false;
	}

	public bool TryDequeue(out RawActionEvent? actionEvent)
	{
		return queue.TryDequeue(out actionEvent);
	}

	public bool TryDequeuePeriodic(out RawPeriodicEvent? periodicEvent)
	{
		return periodicQueue.TryDequeue(out periodicEvent);
	}

	public void Dispose()
	{
		if (enabled)
		{
			actorControlHook.Disable();
			receiveHook.Disable();
			enabled = false;
		}
		actorControlHook.Dispose();
		receiveHook.Dispose();
	}

	private unsafe void OnReceiveActionEffect(uint casterEntityId, Character* caster, Vector3* targetPosition, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targetEntityIds)
	{
		receiveHook.Original(casterEntityId, caster, targetPosition, header, effects, targetEntityIds);
		try
		{
			if (header == null || effects == null || targetEntityIds == null)
			{
				return;
			}
			List<EffectSample> list = new List<EffectSample>();
			List<StatusApplication> list2 = new List<StatusApplication>();
			byte b = Math.Min(header->NumTargets, (byte)32);
			for (int i = 0; i < b; i++)
			{
				uint objectId = targetEntityIds[i].ObjectId;
				for (int j = 0; j < 8; j++)
				{
					ref ActionEffectHandler.Effect reference = ref effects[i].Effects[j];
					byte type = reference.Type;
					if (type != 0)
					{
						if ((uint)(type - 14) <= 1u)
						{
							uint targetEntityId = ((type == 15) ? casterEntityId : objectId);
							list2.Add(new StatusApplication(targetEntityId, reference.Value));
						}
						uint num = DecodeAmount(reference);
						bool flag = ((type == 3 || (uint)(type - 5) <= 1u) ? true : false);
						uint num2 = (flag ? num : 0u);
						uint num3 = ((type == 4) ? num : 0u);
						if (flag || num3 != 0)
						{
							List<EffectSample> list3 = list;
							uint targetEntityId2 = objectId;
							uint damage = num2;
							uint healing = num3;
							flag = ((type == 4) ? ((reference.Param1 & 0x20) != 0) : ((reference.Param0 & 0x20) != 0));
							bool flag2 = ((type == 3 || (uint)(type - 5) <= 1u) ? true : false);
							list3.Add(new EffectSample(targetEntityId2, damage, healing, flag, flag2 && (reference.Param0 & 0x40) != 0, flag2));
						}
					}
				}
			}
			uint actionId = ((header->ActionId != 0) ? header->ActionId : header->SpellId);
			queue.Enqueue(new RawActionEvent(DateTime.UtcNow, casterEntityId, actionId, header->ActionType, list, list2));
		}
		catch (Exception exception)
		{
			log.Error(exception, "ActionEffect の読み取りに失敗しました。");
		}
	}

	private void OnActorControl(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded)
	{
		actorControlHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
		bool flag = category - 1540 <= 1;
		if (flag && arg2 != 0)
		{
			periodicQueue.Enqueue(new RawPeriodicEvent(DateTime.UtcNow, entityId, arg1, arg2, arg3, category == 1540));
		}
	}

	private static uint DecodeAmount(ActionEffectHandler.Effect effect)
	{
		uint num = effect.Value;
		if ((effect.Param4 & 0x40) != 0)
		{
			num += (uint)(effect.Param3 << 16);
		}
		return num;
	}
}
