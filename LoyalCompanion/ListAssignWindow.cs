using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using System;
using System.Linq;
using System.Numerics;

namespace LoyalCompanion
{
    public class ListAssignWindow : Window
    {
        private readonly Configuration configuration;
        private readonly MinionSelectWindow minionSelectWindow;
        private int currentGearsetId = -1;
        private string currentGearsetName = string.Empty;
        private Vector2? pendingPosition;
        private bool confirmingDelete;

        public ListAssignWindow(Configuration configuration, MinionSelectWindow minionSelectWindow)
            : base("Assign Minion List###ListAssign", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse)
        {
            this.configuration = configuration;
            this.minionSelectWindow = minionSelectWindow;
        }

        public void SetGearset(int gearsetId, string gearsetName, Vector2? buttonPos)
        {
            this.currentGearsetId = gearsetId;
            this.currentGearsetName = gearsetName;
            this.WindowName = $"Assign List - {gearsetName}###ListAssign";
            this.IsOpen = true;
            this.pendingPosition = buttonPos;
            this.confirmingDelete = false;
        }

        public override void PreDraw()
        {
            if (pendingPosition.HasValue)
            {
                ImGui.SetNextWindowPos(pendingPosition.Value, ImGuiCond.Always);
                pendingPosition = null;
            }
        }

        public override void Draw()
        {
            if (currentGearsetId < 0)
            {
                ImGui.TextUnformatted("No gearset selected.");
                return;
            }

            var assignedList = configuration.GetListForGearset(currentGearsetId);

            // Dropdown to pick a list
            ImGui.TextUnformatted("Minion list:");
            ImGui.SetNextItemWidth(200);
            var previewName = assignedList?.Name ?? "None";
            if (ImGui.BeginCombo("##listCombo", previewName))
            {
                // "None" option to unassign
                if (ImGui.Selectable("None", assignedList == null))
                {
                    configuration.GearsetListAssignments.Remove(currentGearsetId);
                    configuration.Save();
                }

                // All available lists
                foreach (var list in configuration.MinionLists)
                {
                    var isSelected = assignedList != null && assignedList.Id == list.Id;
                    var label = $"{list.Name} ({list.Minions.Count})";
                    if (ImGui.Selectable(label, isSelected))
                    {
                        configuration.GearsetListAssignments[currentGearsetId] = list.Id;
                        configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.Spacing();

            // New / Edit / Delete buttons on one line
            if (ImGui.Button("New"))
            {
                var newList = new MinionList { Name = $"List {configuration.MinionLists.Count + 1}" };
                configuration.MinionLists.Add(newList);
                configuration.GearsetListAssignments[currentGearsetId] = newList.Id;
                configuration.Save();
                minionSelectWindow.SetList(newList, null);
            }

            if (assignedList != null)
            {
                ImGui.SameLine();
                if (ImGui.Button("Edit"))
                {
                    minionSelectWindow.SetList(assignedList, null);
                }

                ImGui.SameLine();
                if (!confirmingDelete)
                {
                    if (ImGui.Button("Delete"))
                    {
                        confirmingDelete = true;
                    }
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                    if (ImGui.Button("Confirm"))
                    {
                        var keysToRemove = configuration.GearsetListAssignments
                            .Where(kv => kv.Value == assignedList.Id)
                            .Select(kv => kv.Key)
                            .ToList();
                        foreach (var key in keysToRemove)
                            configuration.GearsetListAssignments.Remove(key);

                        configuration.MinionLists.Remove(assignedList);
                        configuration.Save();
                        confirmingDelete = false;
                    }
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel##del"))
                    {
                        confirmingDelete = false;
                    }
                }

                // Show how many gearsets use this list
                var usageCount = configuration.GearsetListAssignments.Count(kv => kv.Value == assignedList.Id);
                if (usageCount > 1)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"Shared by {usageCount} gearsets");
                }
            }
        }

        public override void OnClose()
        {
            this.confirmingDelete = false;
        }
    }
}
