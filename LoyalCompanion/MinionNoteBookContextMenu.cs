using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Collections.Generic;

namespace LoyalCompanion
{
    // Adds a submenu to the base-game Minion Guide (MinionNoteBook) right-click menu that
    // lets you toggle the right-clicked minion in any existing list. Lists already containing
    // the minion are marked; clicking a list adds or removes the minion from it.
    public class MinionNoteBookContextMenu : IDisposable
    {
        private const string AddonName = "MinionNoteBook";

        // The game font has no checkmark glyph, so use plain-ASCII checkbox markers.
        private const string InListMark = "[x] ";
        private const string NotInListMark = "[ ] ";

        // Dalamud's default boxed-letter plugin prefix colour (the same red used for the "D").
        private const ushort PluginPrefixColor = 539;

        private readonly Configuration configuration;

        public MinionNoteBookContextMenu(Configuration configuration)
        {
            this.configuration = configuration;
            Service.ContextMenu.OnMenuOpened += OnMenuOpened;
        }

        public void Dispose()
        {
            Service.ContextMenu.OnMenuOpened -= OnMenuOpened;
        }

        private void OnMenuOpened(IMenuOpenedArgs args)
        {
            try
            {
                if (args.MenuType != ContextMenuType.Default || args.AddonName != AddonName)
                    return;

                var minionId = GetSelectedMinionId();
                if (minionId == 0)
                    return;

                // Setting our own prefix ("L") suppresses Dalamud's forced boxed-"D"; the "C" is
                // the whole name, so the entry reads as just an "LC" tag with no trailing text.
                args.AddMenuItem(new MenuItem
                {
                    PrefixChar = 'L',
                    PrefixColor = PluginPrefixColor,
                    Name = new SeStringBuilder()
                        .AddUiForeground(SeIconChar.BoxedLetterC.ToIconString(), PluginPrefixColor)
                        .Build(),
                    IsSubmenu = true,
                    OnClicked = clicked => OpenListSubmenu(clicked, minionId),
                });
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Error building minion context menu");
            }
        }

        private void OpenListSubmenu(IMenuItemClickedArgs args, uint minionId)
        {
            var items = new List<MenuItem>();

            if (configuration.MinionLists.Count == 0)
            {
                items.Add(new MenuItem
                {
                    Name = "No lists - create one from the gearset window",
                    IsEnabled = false,
                });
            }
            else
            {
                foreach (var list in configuration.MinionLists)
                {
                    var captured = list;
                    var inList = captured.Minions.Contains(minionId);
                    var mark = inList ? InListMark : NotInListMark;
                    items.Add(new MenuItem
                    {
                        Name = $"{mark}{captured.Name} ({captured.Minions.Count})",
                        OnClicked = _ => ToggleMinion(captured, minionId),
                    });
                }
            }

            args.OpenSubmenu("Loyal Companion lists", items);
        }

        private void ToggleMinion(MinionList list, uint minionId)
        {
            // Remove returns false when the minion wasn't present, so add it instead.
            if (!list.Minions.Remove(minionId))
                list.Minions.Add(minionId);
            configuration.Save();
        }

        // The minion the guide is currently showing (right-clicking a cell selects it first).
        private static unsafe uint GetSelectedMinionId()
        {
            var agentModule = AgentModule.Instance();
            if (agentModule == null)
                return 0;

            var agent = (AgentMinionNoteBook*)agentModule->GetAgentByInternalId(AgentId.MinionNotebook);
            if (agent == null)
                return 0;

            var selection = agent->CurrentSelection;
            if (selection == null)
                return 0;

            var minionId = selection->Id;
            if (minionId == 0)
                return 0;

            // Guard against the selection Id being something other than a Companion row.
            var uiState = UIState.Instance();
            if (uiState == null || !uiState->IsCompanionUnlocked(minionId))
                return 0;

            return minionId;
        }
    }
}
