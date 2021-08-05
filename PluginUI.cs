using ImGuiNET;
using System;
using System.Numerics;
using Dalamud.Game.Text;

namespace RaceSwitcheroo
{
    public class PluginUI
    {
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
            if (ImGui.Begin("Race Switcheroo", ref settingsVisible, ImGuiWindowFlags.AlwaysAutoResize))
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

                    bool randomTargetRace = this.plugin.config.randomTargetRace;
                    ImGui.Checkbox("Random target race", ref randomTargetRace);
                    if (!randomTargetRace)
                    {
                        if (ImGui.BeginCombo("Target Race", othersTargetRace.GetAttribute<Display>().Value))
                        {
                            foreach (Race race in Enum.GetValues(typeof(Race)))
                            {
                                ImGui.PushID((byte)race);
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
                    } else
                    {
                        bool randomOnAllPlayersOfRace = this.plugin.config.randomOnAllPlayersOfRace;
                        ImGui.Checkbox("Random on all of origin race", ref randomOnAllPlayersOfRace);
                        bool RandIsPressed = ImGui.Button("Randomise");
                        if (RandIsPressed)
                        {
                            this.plugin.UpdateOtherRace(this.plugin.RandomOtherRace());
                        }
                        if (!randomOnAllPlayersOfRace)
                        {
                            ImGui.Text("Target race : " + this.plugin.config.ChangeOthersTargetRace.GetAttribute<Display>().Value);
                        }
                        this.plugin.UpdateRandomOnAllPlayersOfRace(randomOnAllPlayersOfRace);
                    }

                    this.plugin.UpdateRandomTargetRace(randomTargetRace);
                    
                    this.plugin.UpdateOtherOriginRace(othersOriginRace);

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