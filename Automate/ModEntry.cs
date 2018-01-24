using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pathoschild.Stardew.Automate.Framework;
using Pathoschild.Stardew.Automate.Framework.Data;
using Pathoschild.Stardew.Automate.Framework.Models;
using Pathoschild.Stardew.Common;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pathoschild.Stardew.Automate
{
    /// <summary>The mod entry point.</summary>
    internal class ModEntry : Mod
    {
        /*********
        ** Properties
        *********/
        /// <summary>The mod configuration.</summary>
        private ModConfig Config;
        
        /// <summary>Provides metadata that's not available from the game data directly.</summary>
        private Metadata Metadata;

        private Dictionary<int, ConnectorData> ConnectorsByItem;

        /// <summary>The name of the file containing data for the <see cref="Metadata"/> field.</summary>
        private readonly string DatabaseFileName = "data.json";

        /// <summary>Constructs machine instances.</summary>
        private MachineFactory Factory;

        /// <summary>The machines to process.</summary>
        private readonly IDictionary<GameLocation, MachineGroup[]> MachineGroups = new Dictionary<GameLocation, MachineGroup[]>();

        /// <summary>The locations that should be reloaded on the next update tick.</summary>
        private readonly HashSet<GameLocation> ReloadQueue = new HashSet<GameLocation>();

        /// <summary>The number of ticks until the next automation cycle.</summary>
        private int AutomateCountdown;

        /// <summary>The current overlay being displayed, if any.</summary>
        private OverlayMenu CurrentOverlay;


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides methods for interacting with the mod directory, such as read/writing a config file or custom JSON files.</param>
        public override void Entry(IModHelper helper)
        {
            // read config
            this.Config = helper.ReadConfig<ModConfig>();

            // setup the factory
            this.LoadMetadata();
            this.ConnectorsByItem = GetConnectorsUsed();
            this.Factory = new MachineFactory(this.ConnectorsByItem.Values.ToArray());

            // hook events
            SaveEvents.AfterLoad += this.SaveEvents_AfterLoad;
            LocationEvents.CurrentLocationChanged += this.LocationEvents_CurrentLocationChanged;
            LocationEvents.LocationsChanged += this.LocationEvents_LocationsChanged;
            LocationEvents.LocationObjectsChanged += this.LocationEvents_LocationObjectsChanged;
            PlayerEvents.InventoryChanged += this.PlayerEvents_InventoryChanged;
            GameEvents.UpdateTick += this.GameEvents_UpdateTick;

            // handle player interaction
            InputEvents.ButtonPressed += this.InputEvents_ButtonPressed;

            // log info
            if (this.Config.VerboseLogging)
                this.Monitor.Log($"Verbose logging is enabled. This is useful when troubleshooting but can impact performance. It should be disabled if you don't explicitly need it. You can delete {Path.Combine(this.Helper.DirectoryPath, "config.json")} and restart the game to disable it.", LogLevel.Warn);
            this.VerboseLog($"Initialised with automation every {this.Config.AutomationInterval} ticks.");
        }

        /*********
        ** Private methods
        *********/
        /****
        ** Event handlers
        ****/
        /// <summary>The method invoked when the player loads a save.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void SaveEvents_AfterLoad(object sender, EventArgs e)
        {
            // reset automation interval
            this.AutomateCountdown = this.Config.AutomationInterval;
            this.DisableOverlay();
        }

        /// <summary>The method invoked when the player warps to a new location.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void LocationEvents_CurrentLocationChanged(object sender, EventArgsCurrentLocationChanged e)
        {
            this.ResetOverlayIfShown();
        }

        /// <summary>The method invoked when a location is added or removed.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void LocationEvents_LocationsChanged(object sender, EventArgsGameLocationsChanged e)
        {
            this.VerboseLog("Location list changed, reloading all machines.");

            try
            {
                this.MachineGroups.Clear();
                foreach (GameLocation location in CommonHelper.GetLocations())
                    this.ReloadQueue.Add(location);
            }
            catch (Exception ex)
            {
                this.HandleError(ex, "updating locations");
            }
        }

        /// <summary>The method invoked when an object is added or removed to a location.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void LocationEvents_LocationObjectsChanged(object sender, EventArgsLocationObjectsChanged e)
        {
            this.VerboseLog("Object list changed, reloading machines in current location.");
            ReloadCurrentLocation();
        }

        private void PlayerEvents_InventoryChanged(object sender, EventArgsInventoryChanged e)
        {
            //may not pick up all changes. (ex. breaking a path and not picking up the item)
            var changed = e.Added
                .Concat(e.QuantityChanged)
                .Concat(e.Removed);

            if (changed.Any(x => this.ConnectorsByItem.ContainsKey(x.Item.parentSheetIndex)))
            {
                this.VerboseLog("Inventory connector item changed, reloading machines in current location.");
                ReloadCurrentLocation();
            }
        }

        /// <summary>The method invoked when the in-game clock time changes.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void GameEvents_UpdateTick(object sender, EventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            try
            {
                // handle delay
                this.AutomateCountdown--;
                if (this.AutomateCountdown > 0)
                    return;
                this.AutomateCountdown = this.Config.AutomationInterval;

                // reload machines if needed
                if (this.ReloadQueue.Any())
                {
                    foreach (GameLocation location in this.ReloadQueue)
                        this.ReloadMachinesIn(location);
                    this.ReloadQueue.Clear();

                    this.ResetOverlayIfShown();
                }

                // process machines
                foreach (MachineGroup group in this.GetAllMachineGroups())
                    group.Automate();
            }
            catch (Exception ex)
            {
                this.HandleError(ex, "processing machines");
            }
        }

        /// <summary>The method invoked when the player presses a button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void InputEvents_ButtonPressed(object sender, EventArgsInput e)
        {
            try
            {
                // toggle overlay
                if (Context.IsPlayerFree && this.Config.Controls.ToggleOverlay.Contains(e.Button))
                {
                    if (this.CurrentOverlay != null)
                        this.DisableOverlay();
                    else
                        this.EnableOverlay();
                }
            }
            catch (Exception ex)
            {
                this.HandleError(ex, "handling key input");
            }
        }

        /****
        ** Methods
        ****/
        private void ReloadCurrentLocation()
        {
            try
            {
                this.ReloadQueue.Add(Game1.currentLocation);
            }
            catch (Exception ex)
            {
                this.HandleError(ex, "updating the current location");
            }
        }

        /// <summary>Get the machine groups in every location.</summary>
        private IEnumerable<MachineGroup> GetAllMachineGroups()
        {
            foreach (KeyValuePair<GameLocation, MachineGroup[]> group in this.MachineGroups)
            {
                foreach (MachineGroup machineGroup in group.Value)
                    yield return machineGroup;
            }
        }

        /// <summary>Reload the machines in a given location.</summary>
        /// <param name="location">The location whose machines to reload.</param>
        private void ReloadMachinesIn(GameLocation location)
        {
            this.VerboseLog($"Reloading machines in {location.Name}...");

            this.MachineGroups[location] = this.Factory.GetActiveMachinesGroups(location, this.Helper.Reflection).ToArray();
        }

        /// <summary>Log an error and warn the user.</summary>
        /// <param name="ex">The exception to handle.</param>
        /// <param name="verb">The verb describing where the error occurred (e.g. "looking that up").</param>
        private void HandleError(Exception ex, string verb)
        {
            this.Monitor.Log($"Something went wrong {verb}:\n{ex}", LogLevel.Error);
            CommonHelper.ShowErrorMessage($"Huh. Something went wrong {verb}. The error log has the technical details.");
        }

        /// <summary>Log a trace message if verbose logging is enabled.</summary>
        /// <param name="message">The message to log.</param>
        private void VerboseLog(string message)
        {
            if (this.Config.VerboseLogging)
                this.Monitor.Log(message, LogLevel.Trace);
        }

        /// <summary>Disable the overlay, if shown.</summary>
        private void DisableOverlay()
        {
            this.CurrentOverlay?.Dispose();
            this.CurrentOverlay = null;
        }

        /// <summary>Enable the overlay.</summary>
        private void EnableOverlay()
        {
            if (this.CurrentOverlay == null)
                this.CurrentOverlay = new OverlayMenu(this.Factory.GetMachineGroups(Game1.currentLocation, this.Helper.Reflection));
        }

        /// <summary>Reset the overlay if it's being shown.</summary>
        private void ResetOverlayIfShown()
        {
            if (this.CurrentOverlay != null)
            {
                this.DisableOverlay();
                this.EnableOverlay();
            }
        }

        private Dictionary<int, ConnectorData> GetConnectorsUsed()
        {
            var connectorQuery = this.Metadata.Connectors
                .Where(x => this.Config.ConnectorNames.Contains(x.Name, StringComparer.CurrentCultureIgnoreCase));

            var connectors = new Dictionary<int, ConnectorData>();
            foreach (var connector in connectorQuery)
                connectors[connector.ItemId] = connector;

            return connectors;
        }

        /// <summary>Load the file containing metadata that's not available from the game directly.</summary>
        private void LoadMetadata()
        {
            this.Monitor.InterceptErrors("loading metadata", () =>
            {
                this.Metadata = this.Helper.ReadJsonFile<Metadata>(this.DatabaseFileName);
            });
        }
    }
}
