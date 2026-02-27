using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;

namespace LoyalCompanion
{
    public class MinionSummoner : IDisposable
    {
        private readonly Configuration configuration;
        private readonly Random random = new();
        private DateTime lastCheck = DateTime.MinValue;
        private DateTime lastSummonAttempt = DateTime.MinValue;
        private int lastGearsetIndex = -1;
        private uint lastSummonedMinionId;

        public MinionSummoner(Configuration configuration)
        {
            this.configuration = configuration;
            Service.Framework.Update += this.OnUpdate;
        }

        public void Dispose()
        {
            Service.Framework.Update -= this.OnUpdate;
        }

        private void OnUpdate(IFramework framework)
        {
            try
            {
                this.Update();
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Error in MinionSummoner update");
            }
        }

        private unsafe void Update()
        {
            var now = DateTime.Now;

            // Throttle: check every 2 seconds, but wait 5 seconds after a summon attempt
            if (now - lastSummonAttempt < TimeSpan.FromSeconds(5))
                return;
            if (now - lastCheck < TimeSpan.FromSeconds(2))
                return;
            lastCheck = now;

            var localPlayer = Service.ClientState.LocalPlayer;
            if (localPlayer == null)
                return;

            // Check blocking conditions
            if (Service.Condition[ConditionFlag.Mounted] ||
                Service.Condition[ConditionFlag.BoundByDuty] ||
                Service.Condition[ConditionFlag.InCombat] ||
                Service.Condition[ConditionFlag.Unconscious] ||
                Service.Condition[ConditionFlag.OccupiedInEvent] ||
                Service.Condition[ConditionFlag.OccupiedInCutSceneEvent] ||
                Service.Condition[ConditionFlag.BetweenAreas] ||
                Service.Condition[ConditionFlag.BetweenAreas51] ||
                Service.Condition[ConditionFlag.Casting] ||
                Service.Condition[ConditionFlag.Jumping] ||
                Service.Condition[ConditionFlag.Jumping61] ||
                Service.Condition[ConditionFlag.RidingPillion])
                return;

            var character = (Character*)localPlayer.Address;
            if (character == null)
                return;

            var gearsetModule = RaptureGearsetModule.Instance();
            if (gearsetModule == null)
                return;
            var currentGearset = gearsetModule->CurrentGearsetIndex;

            // Detect gearset change (first run is not a change)
            var gearsetChanged = lastGearsetIndex != -1 && currentGearset != lastGearsetIndex;
            lastGearsetIndex = currentGearset;

            // Look up configured minions for this gearset
            var minionList = configuration.GetMinionsForGearset(currentGearset);
            if (minionList == null || minionList.Count == 0)
                return;

            var companionOut = character->CompanionData.CompanionObject != null;

            // Minion is out and gearset didn't change — nothing to do
            if (companionOut && !gearsetChanged)
                return;

            var actionManager = ActionManager.Instance();
            if (actionManager == null)
                return;

            if (companionOut && gearsetChanged)
            {
                // Gearset changed with minion out — keep it if it's valid for the new gearset
                if (minionList.Contains(lastSummonedMinionId))
                    return;

                // Current minion isn't in the new list — switch
                var minionId = minionList[random.Next(minionList.Count)];
                if (actionManager->GetActionStatus(ActionType.Companion, minionId) == 0)
                {
                    actionManager->UseAction(ActionType.Companion, minionId);
                    lastSummonedMinionId = minionId;
                    lastSummonAttempt = now;
                    Service.PluginLog.Info($"Switched to minion {minionId} for gearset {currentGearset}");
                }
                return;
            }

            // No minion out — summon one
            var newMinionId = minionList[random.Next(minionList.Count)];
            if (actionManager->GetActionStatus(ActionType.Companion, newMinionId) == 0)
            {
                actionManager->UseAction(ActionType.Companion, newMinionId);
                lastSummonedMinionId = newMinionId;
                lastSummonAttempt = now;
                Service.PluginLog.Info($"Summoned minion {newMinionId} for gearset {currentGearset}");
            }
        }
    }
}
