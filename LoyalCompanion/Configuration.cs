using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LoyalCompanion
{
    [Serializable]
    public class MinionList
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "New List";
        public List<uint> Minions { get; set; } = new();
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        // V0 legacy - kept for migration only
        public Dictionary<int, List<uint>> GearsetMinions { get; set; } = new();

        // V1 - shared named lists
        public List<MinionList> MinionLists { get; set; } = new();

        // Key: gearset ID, Value: MinionList.Id
        public Dictionary<int, string> GearsetListAssignments { get; set; } = new();

        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
            Migrate();
        }

        private void Migrate()
        {
            if (Version == 0 && GearsetMinions.Count > 0)
            {
                foreach (var (gearsetId, minions) in GearsetMinions)
                {
                    if (minions.Count == 0)
                        continue;

                    var list = new MinionList
                    {
                        Name = $"Gearset {gearsetId + 1}",
                        Minions = new List<uint>(minions),
                    };
                    MinionLists.Add(list);
                    GearsetListAssignments[gearsetId] = list.Id;
                }

                GearsetMinions.Clear();
                Version = 1;
                Save();
            }
            else if (Version == 0)
            {
                Version = 1;
            }
        }

        public MinionList? GetListForGearset(int gearsetId)
        {
            if (!GearsetListAssignments.TryGetValue(gearsetId, out var listId))
                return null;
            return MinionLists.FirstOrDefault(l => l.Id == listId);
        }

        public List<uint>? GetMinionsForGearset(int gearsetId)
        {
            var list = GetListForGearset(gearsetId);
            return list?.Minions;
        }

        // Swap the list assignments of two gearsets - used when gearsets are reordered
        // in-game (move set up/down) so the minion list follows its gearset.
        public void SwapGearsetAssignments(int gearsetIdA, int gearsetIdB)
        {
            GearsetListAssignments.TryGetValue(gearsetIdA, out var listA);
            GearsetListAssignments.TryGetValue(gearsetIdB, out var listB);

            if (listB != null)
                GearsetListAssignments[gearsetIdA] = listB;
            else
                GearsetListAssignments.Remove(gearsetIdA);

            if (listA != null)
                GearsetListAssignments[gearsetIdB] = listA;
            else
                GearsetListAssignments.Remove(gearsetIdB);

            Save();
        }

        // Drop a gearset's assignment - used when the gearset is deleted in-game.
        public void RemoveGearsetAssignment(int gearsetId)
        {
            if (GearsetListAssignments.Remove(gearsetId))
                Save();
        }

        public void Save()
        {
            this.PluginInterface!.SavePluginConfig(this);
        }
    }
}
