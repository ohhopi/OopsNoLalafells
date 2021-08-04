using ImGuiNET;
using System;
using System.Numerics;
using Dalamud.Game.Text;

namespace OopsNoLalafells
{
    public class PluginUI
    {
        private static Vector4 WHAT_THE_HELL_ARE_YOU_DOING = new Vector4(1, 0, 0, 1);
        private readonly Plugin plugin;

        public PluginUI(Plugin plugin)
        {
            this.plugin = plugin;
        }

        public void Draw()
        {
            if (!this.plugin.SettingsVisible)
            {
                return;
            }

            bool settingsVisible = this.plugin.SettingsVisible;
            if (ImGui.Begin("Oops, No Lalafells!", ref settingsVisible, ImGuiWindowFlags.AlwaysAutoResize))
            {
                bool shouldChangeOthers = this.plugin.config.ShouldChangeOthers;
                ImGui.Checkbox("Change others", ref shouldChangeOthers);

                    Race othersTargetRace = this.plugin.config.ChangeOthersTargetRace;
                    Race othersOriginRace = this.plugin.config.ChangeOthersOriginRace;
                    if (shouldChangeOthers)
                    {
                        if (ImGui.BeginCombo("Origin Race", othersOriginRace.GetAttribute<Display>().Value))
                        {
                            foreach (Race race in Enum.GetValues(typeof(Race)))
                            {
                                ImGui.PushID((byte)race);
                                if (ImGui.Selectable(race.GetAttribute<Display>().Value, race == othersOriginRace))
                                {
                                    othersOriginRace = race;
                                }

                                if (race == othersOriginRace)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }

                                ImGui.PopID();
                            }

                            ImGui.EndCombo();
                        }

                        if (ImGui.BeginCombo("Target Race", othersTargetRace.GetAttribute<Display>().Value))
                        {
                            foreach (Race race in Enum.GetValues(typeof(Race)))
                            {
                                ImGui.PushID((byte) race);
                                if (ImGui.Selectable(race.GetAttribute<Display>().Value, race == othersTargetRace))
                                {
                                    othersTargetRace = race;
                                }

                                if (race == othersTargetRace)
                                {
                                    ImGui.SetItemDefaultFocus();
                                }

                                ImGui.PopID();
                            }

                            ImGui.EndCombo();
                        }

                    this.plugin.UpdateOtherRace(othersTargetRace);
                    this.plugin.UpdateOtherOriginRace(othersOriginRace);

                    ImGui.TextColored(WHAT_THE_HELL_ARE_YOU_DOING,
                        "Experimental and may crash your game, uncat your boy,\nor cause the Eighth Umbral Calamity. YOU HAVE BEEN WARNED!");

                }
                else
                {
                    this.plugin.UpdateOtherRace(Race.ELEZEN);
                    this.plugin.UpdateOtherOriginRace(Race.LALAFELL);
                }
                
                this.plugin.ToggleOtherRace(shouldChangeOthers);

                ImGui.End();
            }

            this.plugin.SettingsVisible = settingsVisible;
            this.plugin.SaveConfig();
        }
    }
}