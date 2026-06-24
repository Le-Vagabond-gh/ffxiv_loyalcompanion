using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;

namespace LoyalCompanion
{
    // Keeps minion lists attached to their gearset when gearsets are reordered or removed
    // in-game. The game swaps gearset contents between ID slots (move set up/down, reassign
    // set number), so the ID-keyed assignment has to be swapped to follow; delete drops it.
    public unsafe class GearsetSyncHook : IDisposable
    {
        private delegate bool MoveGearsetDelegate(AgentGearSet* agent, int gearsetId, bool direction);
        private delegate bool ReassignGearsetDelegate(AgentGearSet* agent, int gearsetId, int newGearsetId);
        private delegate bool DeleteGearsetDelegate(AgentGearSet* agent, int gearsetId);

        private readonly Configuration configuration;
        private readonly Hook<MoveGearsetDelegate>? moveHook;
        private readonly Hook<ReassignGearsetDelegate>? reassignHook;
        private readonly Hook<DeleteGearsetDelegate>? deleteHook;

        // Set while a move runs: the nested ReassignGearsetId call it makes must not swap again.
        private bool inMove;

        public GearsetSyncHook(Configuration configuration)
        {
            this.configuration = configuration;
            try
            {
                moveHook = Service.GameInteropProvider.HookFromAddress<MoveGearsetDelegate>(
                    AgentGearSet.Addresses.MoveGearsetUpOrDown.Value, MoveDetour);
                reassignHook = Service.GameInteropProvider.HookFromAddress<ReassignGearsetDelegate>(
                    AgentGearSet.Addresses.ReassignGearsetId.Value, ReassignDetour);
                deleteHook = Service.GameInteropProvider.HookFromAddress<DeleteGearsetDelegate>(
                    AgentGearSet.Addresses.DeleteGearset.Value, DeleteDetour);

                moveHook.Enable();
                reassignHook.Enable();
                deleteHook.Enable();
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Failed to hook gearset functions");
            }
        }

        private bool MoveDetour(AgentGearSet* agent, int gearsetId, bool direction)
        {
            // Resolve the neighbour before the move - afterwards the contents have swapped.
            var neighborId = -1;
            try { neighborId = ResolveNeighborId(gearsetId, direction); }
            catch (Exception ex) { Service.PluginLog.Error(ex, "Error resolving neighbour gearset"); }

            inMove = true;
            bool result;
            try { result = moveHook!.Original(agent, gearsetId, direction); }
            finally { inMove = false; }

            if (neighborId >= 0 && neighborId != gearsetId)
                TrySwap(gearsetId, neighborId);

            return result;
        }

        private bool ReassignDetour(AgentGearSet* agent, int gearsetId, int newGearsetId)
        {
            var result = reassignHook!.Original(agent, gearsetId, newGearsetId);

            // Skip the nested call made by a move - the move hook handles that swap.
            if (!inMove && newGearsetId != gearsetId)
                TrySwap(gearsetId, newGearsetId);

            return result;
        }

        private bool DeleteDetour(AgentGearSet* agent, int gearsetId)
        {
            var result = deleteHook!.Original(agent, gearsetId);

            try { configuration.RemoveGearsetAssignment(gearsetId); }
            catch (Exception ex) { Service.PluginLog.Error(ex, "Error removing minion list assignment"); }

            return result;
        }

        private void TrySwap(int gearsetIdA, int gearsetIdB)
        {
            try { configuration.SwapGearsetAssignments(gearsetIdA, gearsetIdB); }
            catch (Exception ex) { Service.PluginLog.Error(ex, "Error swapping minion list assignments"); }
        }

        // The gearset that sits directly above (moveUp) or below the moved gearset in the list.
        private static int ResolveNeighborId(int gearsetId, bool moveUp)
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null)
                return -1;

            int enabledIndex = -1;
            for (byte i = 0; i < 100; i++)
            {
                var id = module->ResolveIdFromEnabledIndex(i);
                if (id < 0)
                    break;
                if (id == gearsetId)
                {
                    enabledIndex = i;
                    break;
                }
            }
            if (enabledIndex < 0)
                return -1;

            var neighborIndex = moveUp ? enabledIndex - 1 : enabledIndex + 1;
            if (neighborIndex < 0)
                return -1;

            return module->ResolveIdFromEnabledIndex((byte)neighborIndex);
        }

        public void Dispose()
        {
            moveHook?.Disable();
            moveHook?.Dispose();
            reassignHook?.Disable();
            reassignHook?.Dispose();
            deleteHook?.Disable();
            deleteHook?.Dispose();
        }
    }
}
