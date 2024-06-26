#region License (GPL v2)
/*
    Bag Teleport
    Copyright (c) RFC1920 <desolationoutpostpve@gmail.com>

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; version 2
    of the License only.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/
#endregion License (GPL v2)
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Bag Teleport", "RFC1920", "1.0.3")]
    [Description("Allow teleport to sleeping bags")]
    internal class BagTP : RustPlugin
    {
        [PluginReference]
        private readonly Plugin GridAPI;

        private ConfigData configData;
        private bool newsave;
        private Dictionary<ulong, List<BagTeleport>> bagTeleport = new Dictionary<ulong, List<BagTeleport>>();
        private const string permBagTP_Use = "bagtp.use";

        class BagTeleport
        {
            public ulong id;
            public string name;
            public string type;
            public string grid;
            public Vector3 location;
            public int usageCount;
        }

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Message(Lang(key, player.Id, args));
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You don't have permission to use this command",
                ["notyours"] = "Not your bag",
                ["nobag"] = "No bag found",
                ["bagset"] = "Bag {0} set",
                ["nosuchbag"] = "No such bag",
                ["bagremoved"] = "Bag {0} removed",
                ["bagdestroyed"] = "Bag {0} at {1}({2}) was destroyed",
                ["bagtooclose"] = "Too close to bag",
                ["bagteleport"] = "Teleporting to bag {0} in {1} second(s)",
                ["bags"] = "Bags:\n{0}"
            }, this);
        }
        #endregion Message

        private void OnServerInitialized()
        {
            LoadConfigVariables();
            LoadData();
            AddCovalenceCommand("bag", "CmdTeleport");
            AddCovalenceCommand("setbag", "CmdSetBag");
            AddCovalenceCommand("rembag", "CmdRemoveBag");

            permission.RegisterPermission(permBagTP_Use, this);

            if (newsave)
            {
                newsave = false;
                bagTeleport = new Dictionary<ulong, List<BagTeleport>>();
                SaveData();
            }
        }

        private void OnNewSave() => newsave = true;

        private void DoLog(string message)
        {
            if (configData.Options.debug) Puts(message);
        }

        private Vector3 FindBag(BasePlayer player, out string name, out ulong bagid)
        {
            name = "";
            bagid = 0;
            //List<SleepingBag> bags = new List<SleepingBag>();
            //Vis.Entities(player.transform.position, 10, bags);
            BaseEntity ent = RaycastAll<BaseEntity>(player.eyes.HeadRay()) as BaseEntity;
            SleepingBag bag = ent.GetComponent<SleepingBag>();
            if (bag?.OwnerID != player.userID)
            {
                Message(player.IPlayer, "notyours");
                return Vector3.zero;
            }

            DoLog($"Found bag at {bag.transform.position}");
            bagid = bag.net.ID.Value;
            name = bag.name;
            return bag.transform.position;
        }

        // e.g. rembag water
        [Command("rembag")]
        private void CmdRemoveBag(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
            string debug = string.Join(",", args); DoLog($"CmdRemoveBag: {debug}");
            string bagname = "";
            ulong bagid = 0;
            if (args.Length == 0)
            {
                FindBag(iplayer.Object as BasePlayer, out bagname, out bagid);
            }
            else if (args.Length == 1)
            {
                bagname = args[0];
            }
            if (args.Length == 1) CmdTeleport(iplayer, "remove", new string[] { "remove", bagname });
        }

        // e.g. setbag mining
        [Command("setbag")]
        private void CmdSetBag(IPlayer iplayer, string command, string[] args)
        {
            if (iplayer.Id == "server_console") return;
            string debug = string.Join(",", args); DoLog($"CmdSetBag: {debug}");
            string bagname = "";
            ulong bagid = 0;
            if (args.Length == 0)
            {
                FindBag(iplayer.Object as BasePlayer, out bagname, out bagid);
            }
            else if (args.Length == 1)
            {
                bagname = args[0];
            }
            if (args.Length == 1) CmdTeleport(iplayer, "set", new string[] { "set", bagname });
        }

        // e.g. bag one
        [Command("bag")]
        private void CmdTeleport(IPlayer iplayer, string command, string[] args)
        {
            if (!iplayer.HasPermission(permBagTP_Use)) { Message(iplayer, "notauthorized"); return; }
            if (iplayer.Id == "server_console") return;

            string debug = string.Join(",", args); DoLog($"CmdTeleport: {debug}");

            BasePlayer player = iplayer.Object as BasePlayer;
            if (!bagTeleport.ContainsKey(player.userID))
            {
                bagTeleport.Add(player.userID, new List<BagTeleport>());
            }

            if (args.Length == 2 && command == "set")// || (args.Length == 2 && args[0] == "set"))
            {
                // Set bag
                string bagname = "";
                ulong bagid = 0;
                // Find a local bag within 5m
                Vector3 loc = FindBag(player, out bagname, out bagid);
                if (loc == Vector3.zero)
                {
                    Message(iplayer, "nobag");
                    return;
                }
                if (args.Length == 2)
                {
                    bagname = args[1];
                }
                BagTeleport bag = new BagTeleport()
                {
                    name = bagname,
                    id = bagid,
                    location = loc,
                    grid = PositionToGrid(loc),
                    type = "bag"
                };
                bagTeleport[player.userID].Add(bag);
                SaveData();
                Message(iplayer, "bagset", bagname);
            }
            else if (args.Length == 2 && command == "remove")// || (args.Length == 2 && args[0] == "remove"))
            {
                // Remove a bag
                string bagname = args[1];
                if (args.Length == 2)
                {
                    bagname = args[1];
                }
                BagTeleport bag = bagTeleport[player.userID].Find(x => x.name == bagname);
                if (bag == null)
                {
                    Message(iplayer, "nosuchbag");
                    return;
                }
                bagTeleport[player.userID].Remove(bag);
                SaveData();
                Message(iplayer, "bagremoved", bagname);
            }
            else if (args.Length == 1 && args[0] == "list")
            {
                string output = "";
                foreach (BagTeleport bag in bagTeleport[player.userID])
                {
                    output += $"  {bag.name} {bag.location}({bag.grid})\n";
                }
                Message(iplayer, "bags", output);
            }
            else if (args.Length == 1)
            {
                // Teleport to a named bag
                BagTeleport bag = bagTeleport[player.userID].Find(x => x.name == args[0]);
                if (bag == null)
                {
                    // echo
                    return;
                }
                SleepingBag realbag = BaseNetworkable.serverEntities.Find(new NetworkableId(bag.id)) as SleepingBag;
                if (realbag == null)
                {
                    Message(iplayer, "bagdestroyed", bag.name, bag.location, bag.grid);
                    bagTeleport[player.userID].Remove(bag);
                    SaveData();
                    return;
                }

                // Get and update bag position, which might be on a boat.
                // 5 seconds will be a long time if the boat is moving :)
                float delay = configData.Options.countdownTimer;
                if (Vector3.Distance(bag.location, realbag.transform.position) > 0.2f)
                {
                    delay = 0;
                }
                if (Vector3.Distance(realbag.transform.position, player.transform.position) < 5)
                {
                    Message(iplayer, "bagtooclose");
                    return;
                }
                Message(iplayer, "bagteleport", bag.name, delay.ToString());

                bag.usageCount++;
                bag.location = realbag.transform.position;
                bag.grid = PositionToGrid(realbag.transform.position);
                SaveData();
                timer.Once(delay, () => Teleport(player, realbag.transform.position, "bag"));
            }
            else if (args.Length == 0)
            {
                // Find local bag, use that name
            }
        }

        private void OnBagUnclaimed(BasePlayer player, SleepingBag bag)
        {
            // Remove from list
        }

        private void OnPickupBag(SleepingBag bag, BasePlayer player)
        {
            // Remove from list
        }

        public static string RemoveSpecialCharacters(string str)
        {
            return Regex.Replace(str, "[^a-zA-Z0-9_.]+", "", RegexOptions.Compiled);
        }

        private object RaycastAll<T>(Ray ray) where T : BaseEntity
        {
            RaycastHit[] hits = Physics.RaycastAll(ray);
            GamePhysics.Sort(hits);
            const float distance = 6f;
            object target = false;
            foreach (RaycastHit hit in hits)
            {
                BaseEntity ent = hit.GetEntity();
                if (ent is T && hit.distance < distance)
                {
                    target = ent;
                    break;
                }
            }

            return target;
        }

        public void Teleport(BasePlayer player, Vector3 position, string type="")
        {
            if (player.net?.connection != null) player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);

            player.SetParent(null, true, true);
            player.EnsureDismounted();
            player.Teleport(position);
            player.UpdateNetworkGroup();
            player.StartSleeping();
            player.SendNetworkUpdateImmediate(false);

            if (player.net?.connection != null) player.ClientRPC(RpcTarget.Player("StartLoading", player));
        }

        public string PositionToGrid(Vector3 position)
        {
            if (GridAPI != null)
            {
                string[] g = (string[]) GridAPI.CallHook("GetGrid", position);
                return string.Concat(g);
            }
            else
            {
                // From GrTeleport for display only
                Vector2 r = new Vector2((World.Size / 2) + position.x, (World.Size / 2) + position.z);
                float x = Mathf.Floor(r.x / 146.3f) % 26;
                float z = Mathf.Floor(World.Size / 146.3f) - Mathf.Floor(r.y / 146.3f);

                return $"{(char)('A' + x)}{z - 1}";
            }
        }

        #region Data
        private void LoadData()
        {
            bagTeleport = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, List<BagTeleport>>>(Name + "/bagTeleport");
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name + "/bagTeleport", bagTeleport);
        }
        #endregion Data

        #region config
        private class ConfigData
        {
            public Options Options;
            public VersionNumber Version;
        }

        public class Options
        {
            [JsonProperty(PropertyName = "Countdown delay for static teleport")]
            public float countdownTimer;

            public bool debug;
        }

        private void LoadConfigVariables()
        {
            configData = Config.ReadObject<ConfigData>();

            if (configData.Version < new VersionNumber(1, 0, 2) && configData.Options.countdownTimer == 0)
            {
                configData.Options.countdownTimer = 5;
            }

            configData.Version = Version;
            SaveConfig(configData);
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            ConfigData config = new ConfigData
            {
                Options = new Options()
                {
                    countdownTimer = 5,
                    debug = false
                },
                Version = Version
            };

            SaveConfig(config);
        }

        private void SaveConfig(ConfigData config)
        {
            Config.WriteObject(config, true);
        }
        #endregion
    }
}
