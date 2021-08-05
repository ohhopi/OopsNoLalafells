#undef DEBUG

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Actors;
using Dalamud.Game.ClientState.Structs;
using Dalamud.Plugin;
using Dalamud.Hooking;
using RaceSwitcheroo.Attributes;

namespace RaceSwitcheroo
{
    public class Plugin : IDalamudPlugin
    {
        private const uint FLAG_INVIS = (1 << 1) | (1 << 11);
        private const uint CHARA_WINDOW_ACTOR_ID = 0xE0000000;
        private const int OFFSET_RENDER_TOGGLE = 0x104;

        private static readonly short[,] RACE_STARTER_GEAR_ID_MAP =
        {
            {84, 85}, // Hyur
            {86, 87}, // Elezen
            {92, 93}, // Lalafell
            {88, 89}, // Miqo
            {90, 91}, // Roe
            {257, 258}, // Au Ra
            {597, -1}, // Hrothgar
            {-1, 581}, // Viera
        };

        private static readonly short[] RACE_STARTER_GEAR_IDS;

        public string Name => "Race Switcheroo";
        private DalamudPluginInterface pluginInterface;
        public Configuration config { get; private set; }
        private bool unsavedConfigChanges = false;

        private PluginUI ui;
        public bool SettingsVisible = false;

        private PluginCommandManager<Plugin> commandManager;

        private delegate IntPtr CharacterIsMounted(IntPtr actor);

        private delegate IntPtr CharacterInitialize(IntPtr actorPtr, IntPtr customizeDataPtr);

        private delegate IntPtr FlagSlotUpdate(IntPtr actorPtr, uint slot, IntPtr equipData);

        private Hook<CharacterIsMounted> charaMountedHook;
        private Hook<CharacterInitialize> charaInitHook;
        private Hook<FlagSlotUpdate> flagSlotUpdateHook;

        private IntPtr lastActor;
        private bool lastWasPlayer;
        private bool lastWasModified;

        private Race lastPlayerRace;
        private byte lastPlayerGender;

        Random random = new Random();

        // This sucks, but here we are
        static Plugin()
        {
            var list = new List<short>();
            foreach (short id in RACE_STARTER_GEAR_ID_MAP)
            {
                if (id != -1)
                {
                    list.Add(id);
                }
            }

            RACE_STARTER_GEAR_IDS = list.ToArray();
        }

        // When loaded by LivePluginLoader, the executing assembly will be wrong.
        // Supplying this property allows LivePluginLoader to supply the correct location, so that
        // you have full compatibility when loaded normally and through LPL.
        public string AssemblyLocation { get => assemblyLocation; set => assemblyLocation = value; }
        private string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;

            this.config = (Configuration) this.pluginInterface.GetPluginConfig() ?? new Configuration();
            this.config.Initialize(pluginInterface);

            this.ui = new PluginUI(this);

            this.pluginInterface.UiBuilder.OnBuildUi += this.ui.Draw;
            this.pluginInterface.UiBuilder.OnOpenConfigUi += OpenSettingsMenu;

            this.commandManager = new PluginCommandManager<Plugin>(this, this.pluginInterface);

            var charaMountedAddr =
                this.pluginInterface.TargetModuleScanner.ScanText("48 83 EC 28 48 8B 01 FF 50 18 83 F8 08 0F 94 C0");
            PluginLog.Log($"Found IsMounted address: {charaMountedAddr.ToInt64():X}");
            this.charaMountedHook ??=
                new Hook<CharacterIsMounted>(charaMountedAddr, new CharacterIsMounted(CharacterIsMountedDetour));
            this.charaMountedHook.Enable();

            var charaInitAddr = this.pluginInterface.TargetModuleScanner.ScanText(
                "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 48 8B F9 48 8B EA 48 81 C1 ?? ?? ?? ?? E8 ?? ?? ?? ??");
            PluginLog.Log($"Found Initialize address: {charaInitAddr.ToInt64():X}");
            this.charaInitHook ??=
                new Hook<CharacterInitialize>(charaInitAddr, new CharacterInitialize(CharacterInitializeDetour));
            this.charaInitHook.Enable();

            var flagSlotUpdateAddr =
                this.pluginInterface.TargetModuleScanner.ScanText(
                    "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B DA 49 8B F0 48 8B F9 83 FA 0A");
            PluginLog.Log($"Found FlagSlotUpdate address: {flagSlotUpdateAddr.ToInt64():X}");
            this.flagSlotUpdateHook ??=
                new Hook<FlagSlotUpdate>(flagSlotUpdateAddr, new FlagSlotUpdate(FlagSlotUpdateDetour));
            this.flagSlotUpdateHook.Enable();

            // Trigger an initial refresh of all players
            RefreshAllPlayers();
        }

        private IntPtr CharacterIsMountedDetour(IntPtr actorPtr)
        {
            if (Marshal.ReadByte(actorPtr + ActorOffsets.ObjectKind) == (byte) ObjectKind.Player)
            {
                lastActor = actorPtr;
                lastWasPlayer = true;
            }
            else
            {
                lastWasPlayer = false;
            }

            return charaMountedHook.Original(actorPtr);
        }

        private IntPtr CharacterInitializeDetour(IntPtr drawObjectBase, IntPtr customizeDataPtr)
        {
            if (lastWasPlayer)
            {
                lastWasModified = false;
                var actor = Marshal.PtrToStructure<Actor>(lastActor);
                if ((uint) actor.ActorId != CHARA_WINDOW_ACTOR_ID
                    && this.pluginInterface.ClientState.LocalPlayer != null
                    && actor.ActorId != this.pluginInterface.ClientState.LocalPlayer.ActorId
                    && actor.Customize[(int)CustomizeIndex.Race] == ((int)this.config.ChangeOthersOriginRace)
                    && this.config.ShouldChangeOthers)
                {
                    if(this.config.randomOnAllPlayersOfRace)
                    {
                        this.ChangeRace(customizeDataPtr, RandomOtherRace());
                    }
                    else
                    {
                        this.ChangeRace(customizeDataPtr, this.config.ChangeOthersTargetRace);
                    }
                   
                }
            }

            return charaInitHook.Original(drawObjectBase, customizeDataPtr);
        }

        private void ChangeRace(IntPtr customizeDataPtr, Race targetRace)
        {
            var customData = Marshal.PtrToStructure<CharaCustomizeData>(customizeDataPtr);

            if (customData.Race != targetRace)
            {
                // Modify the race/tribe accordingly
                customData.Race = targetRace;
                customData.Tribe = (byte) ((byte) customData.Race * 2 - customData.Tribe % 2);

                // Special-case Hrothgar/Viera gender to prevent fuckery
                customData.Gender = targetRace switch
                {
                    Race.HROTHGAR => 0, // Force male for Hrothgar
                    Race.VIERA => 1, // Force female for Viera
                    _ => customData.Gender
                };

                // TODO: Re-evaluate these for valid race-specific values? (These are Lalafell values)
                // Constrain face type to 0-3 so we don't decapitate the character
                customData.FaceType %= 4;

                // Constrain body type to 0-1 so we don't crash the game
                customData.ModelType %= 2;

                // Hrothgar have a limited number of lip colors?
                customData.LipColor = targetRace switch
                {
                    Race.HROTHGAR => (byte) (customData.LipColor % 5 + 1),
                    _ => customData.LipColor
                };

                customData.HairStyle = (byte) (customData.HairStyle % RaceMappings.RaceHairs[targetRace] + 1);

                Marshal.StructureToPtr(customData, customizeDataPtr, true);

                // Record the new race/gender for equip model mapping, and mark the equip as dirty
                lastPlayerRace = customData.Race;
                lastPlayerGender = customData.Gender;
                lastWasModified = true;
            }
        }

        private IntPtr FlagSlotUpdateDetour(IntPtr actorPtr, uint slot, IntPtr equipDataPtr)
        {
            if (lastWasPlayer && lastWasModified)
            {
                var equipData = Marshal.PtrToStructure<EquipData>(equipDataPtr);
                // TODO: Handle gender-locked gear for Viera/Hrothgar
                equipData = MapRacialEquipModels(lastPlayerRace, lastPlayerGender, equipData);
                Marshal.StructureToPtr(equipData, equipDataPtr, true);
            }

            return flagSlotUpdateHook.Original(actorPtr, slot, equipDataPtr);
        }

        public bool SaveConfig()
        {
            if (this.unsavedConfigChanges)
            {
                this.config.Save();
                this.unsavedConfigChanges = false;
                this.RefreshAllPlayers();
                return true;
            }

            return false;
        }

        public void ToggleOtherRace(bool changeRace)
        {
            if (this.config.ShouldChangeOthers == changeRace)
            {
                return;
            }

            PluginLog.Log($"Race change is toggled to {changeRace}, refreshing players");
            this.config.ShouldChangeOthers = changeRace;
            unsavedConfigChanges = true;
        }

        public void UpdateRandomTargetRace(bool randomRace)
        {
            if (this.config.randomTargetRace == randomRace)
            {
                return;
            }

            if(randomRace)
            {
                this.UpdateOtherRace(RandomOtherRace());
            }

            this.config.randomTargetRace = randomRace;
            unsavedConfigChanges = true;
        }


        public void UpdateRandomOnAllPlayersOfRace(bool randomRace)
        {
            if (this.config.randomOnAllPlayersOfRace == randomRace)
            {
                return;
            }

            this.config.randomOnAllPlayersOfRace = randomRace;
            unsavedConfigChanges = true;
        }

        public Race RandomOtherRace()
        {
            Array values = Enum.GetValues(typeof(Race));
           
            Race RandomRace = (Race)values.GetValue(random.Next(values.Length));
            while (RandomRace == this.config.ChangeOthersOriginRace)
            {
                RandomRace = (Race)values.GetValue(random.Next(values.Length));
            }
            return RandomRace;
        }

        public void UpdateOtherRace(Race race)
        {
            if (this.config.ChangeOthersTargetRace == race)
            {
                return;
            }

            PluginLog.Log($"Target race for other players changed to {race}, refreshing players");
            this.config.ChangeOthersTargetRace = race;
            unsavedConfigChanges = true;
        }

        public void UpdateOtherOriginRace(Race race)
        {
            if (this.config.ChangeOthersOriginRace == race)
            {
                return;
            }

            PluginLog.Log($"Origin race for other players changed to {race}, refreshing players");
            this.config.ChangeOthersOriginRace = race;
            unsavedConfigChanges = true;
        }

        public async void RefreshAllPlayers()
        {
            // Workaround to prevent literally genociding the actor table if we load at the same time as Dalamud + Dalamud is loading while ingame
            await Task.Delay(100); // LMFAOOOOOOOOOOOOOOOOOOO
            var localPlayer = this.pluginInterface.ClientState.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            for (var i = 0; i < this.pluginInterface.ClientState.Actors.Length; i++)
            {
                var actor = this.pluginInterface.ClientState.Actors[i];

                if (actor != null
                    && actor.ObjectKind == ObjectKind.Player)
                {
                    RerenderActor(actor);
                }
            }
        }

        private async void RerenderActor(Dalamud.Game.ClientState.Actors.Types.Actor actor)
        {
            try
            {
                var addrRenderToggle = actor.Address + OFFSET_RENDER_TOGGLE;
                var val = Marshal.ReadInt32(addrRenderToggle);
                
                // Trigger a rerender
                val |= (int) FLAG_INVIS;
                Marshal.WriteInt32(addrRenderToggle, val);
                await Task.Delay(100);
                val &= ~(int) FLAG_INVIS;
                Marshal.WriteInt32(addrRenderToggle, val);
            }
            catch (Exception ex)
            {
                PluginLog.LogError(ex.ToString());
            }
        }

        private EquipData MapRacialEquipModels(Race race, int gender, EquipData eq)
        {
            if (Array.IndexOf(RACE_STARTER_GEAR_IDS, eq.model) > -1)
            {
#if DEBUG
                PluginLog.Log($"Modified {eq.model}, {eq.variant}");
                PluginLog.Log($"Race {race}, index {(byte) (race - 1)}, gender {gender}");
#endif
                eq.model = RACE_STARTER_GEAR_ID_MAP[(byte) race - 1, gender];
                eq.variant = 1;
#if DEBUG
                PluginLog.Log($"New {eq.model}, {eq.variant}");
#endif
            }

            return eq;
        }

        [Command("/rswtich")]
        [HelpMessage("Opens the Race Switcheroo settings menu.")]
        public void OpenSettingsMenuCommand(string command, string args)
        {
            OpenSettingsMenu(command, args);
        }

        private void OpenSettingsMenu(object a, object b)
        {
            this.SettingsVisible = true;
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.commandManager.Dispose();

            this.pluginInterface.UiBuilder.OnOpenConfigUi -= OpenSettingsMenu;
            this.pluginInterface.UiBuilder.OnBuildUi -= this.ui.Draw;
            this.SaveConfig();

            this.charaMountedHook.Disable();
            this.charaInitHook.Disable();
            this.flagSlotUpdateHook.Disable();

            this.charaMountedHook.Dispose();
            this.charaInitHook.Dispose();
            this.flagSlotUpdateHook.Dispose();

            // Refresh all players again
            RefreshAllPlayers();

            this.pluginInterface.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}