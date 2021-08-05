using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace RaceSwitcheroo {
    public class Configuration : IPluginConfiguration {
        [NonSerialized]
        private DalamudPluginInterface pluginInterface;

        public int Version { get; set; } = 1;
        
        public Race ChangeOthersTargetRace { get; set; } = Race.ELEZEN;
        public Race ChangeOthersOriginRace { get; set; } = Race.LALAFELL;
        public bool randomTargetRace { get; set; } = true;

        public bool randomOnAllPlayersOfRace { get; set; } = false;

        public bool ShouldChangeOthers { get; set; } = false;
        
        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.pluginInterface = pluginInterface;
        }

        public void Save() {
            this.pluginInterface.SavePluginConfig(this);
        }
    }
}
