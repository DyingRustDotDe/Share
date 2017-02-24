#define DEBUG
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{

    [Info("Share", "DyingRust.de", "0.1.0", ResourceId = 2351)]
    [Description("Share cupboards, codelocks and autoturrets")]
    public class Share : RustPlugin
    {
        #region Fields  
        [PluginReference]
        private Plugin Friends;
        [PluginReference]
        private Plugin Clans;

        enum WantedEntityType : uint
        {
            AT = 0x0001,
            CL = 0x0002,
            CB = 0x0004,
            ALL = AT + CL + CB
        }

        private PluginConfig pluginConfig;

        private FieldInfo codelockwhitelist;
        #endregion

        #region Hooks
        void Loaded()
        {
            // Use string interpolation to format a float with 3 decimal points instead of calling string.Format()
            codelockwhitelist = typeof(CodeLock).GetField("whitelistPlayers", (BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));

            // Check if all dependencies are there
            Friends = plugins.Find("Friends");
            if (Friends == null)
                Logging("Friends Plugin not found");
            else
                Logging("Friends Plugin found");

            Clans = plugins.Find("Clans");
            if (Clans == null)
                Logging("Clans Plugin not found");
            else
                Logging("Clans Plugin found");

            // Load the config file
            LoadFromConfigFile();

            cmd.AddChatCommand("share", this, "cmdShare");
            cmd.AddChatCommand("sh", this, "cmdShare");

            // Unsubscribe from Hooks if necessary
            if (!pluginConfig.General.ChangeOwnerIDOnCodeLockDeployed)
                Unsubscribe("OnItemDeployed");

            // Register Commands
            if (string.IsNullOrEmpty(pluginConfig.Commands.ShareCommand))
                Logging("No valid ShareCommand in config.");
            else
                cmd.AddChatCommand(pluginConfig.Commands.ShareCommand, this, "cmdShareShort");

            if (string.IsNullOrEmpty(pluginConfig.Commands.UnshareCommand))
                Logging("No valid UnshareCommand in config.");
            else
            {
                if (string.Equals(pluginConfig.Commands.ShareCommand, pluginConfig.Commands.UnshareCommand))
                    Logging("ShareCommand & UnshareCommand are the same.");
                else
                    cmd.AddChatCommand(pluginConfig.Commands.UnshareCommand, this, "cmdShareShort");
            }
        }
        // Change OwnerID of entity when codelock is deployed
        void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            if (entity & entity.HasSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock))
            {
                CodeLock cl = entity.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>();
                if (cl)
                    entity.OwnerID = deployer.GetOwnerPlayer().userID;
            }
        }
        #endregion

        #region Configuration
        // Classes for easier handling of config
        class PluginConfig
        {
            public General General { get; set; }
            public Commands Commands { get; set; }
        }
        class General
        {
            public string ChatPrefix { get; set; }
            public bool UsePermission { get; set; }
            public string PermissionName { get; set; }
            public bool PreventPlayersFromUnsharingThemself { get; set; }
            public bool PreventPlayersFromSharingUnowned { get; set; }
            public bool ChangeOwnerIDOnCodeLockDeployed { get; set; }
        }
        class Commands
        {
            public string ShareCommand { get; set; }
            public string UnshareCommand { get; set; }
            public bool AllowCupboardSharing { get; set; }
            public bool AllowCodelockSharing { get; set; }
            public bool AllowAutoturretSharing { get; set; }
            public float Radius { get; set; }
        }

        // Don't ever try to override SaveConfig() & LoadConfig()! Horrible idea!
        private void SaveToConfigFile() => Config.WriteObject(pluginConfig, true);
        private void LoadFromConfigFile() => pluginConfig = Config.ReadObject<PluginConfig>();

        // Creates default configuration file
        protected override void LoadDefaultConfig()
        {
            var defaultConfig = new PluginConfig
            {
                General = new General
                {
                    ChatPrefix = "<color=cyan>[Share]</color>",
                    UsePermission = false,
                    PermissionName = "share",
                    PreventPlayersFromUnsharingThemself = false,
                    PreventPlayersFromSharingUnowned = false,  // TODO
                    ChangeOwnerIDOnCodeLockDeployed = true
                },
                Commands = new Commands
                {
                    ShareCommand = "sh+",
                    UnshareCommand = "sh-",
                    AllowCupboardSharing = true,
                    AllowCodelockSharing = true,
                    AllowAutoturretSharing = true,
                    Radius = 100.0F
                }
            };
            Config.WriteObject(defaultConfig, true); // write into config file
        }
        #endregion

        #region Commands
        // if someone writes /share in the chat give him the help text
        void cmdShare(BasePlayer player, string command, string[] args)
        {
            ShowCommandHelp(player);
            return;
        }

        void cmdShareShort(BasePlayer player, string command, string[] args)
        {
            // Check for right commands+arguments+permission
            //if(player.net.connection.authLevel < 2)
            //{
            //  SendReply(player, "You don´t have the permission to use this command.");
            //  return;
            //}

            if ((args == null || args.Length != 2) && (Array.IndexOf(new[] { "at", "cl", "cb", "all" }, args[1].ToLower()) > -1))
            {
                ShowCommandHelp(player);
                return;
            }

            WantedEntityType wantedType = (WantedEntityType)Enum.Parse(typeof(WantedEntityType), args[1].ToUpper());

            // Decide with who to share
            List<BasePlayer> playerList;
            switch (args[0].ToLower())
            {
                case "clan":
                    playerList = FindClanMember(player);
                    if (playerList == null || playerList.Count == 0)
                    {
                        SendReply(player, "You don't belong to any clan!");
                        return;
                    }
                    break;
                case "friends":
                    playerList = FindFriends(player);
                    if (playerList == null || playerList.Count == 0)
                    {
                        SendReply(player, "You have no friends added yet!");
                        return;
                    }
                    break;
                default:
                    BasePlayer foundPlayer = FindPlayer(args[0]);
                    if (foundPlayer)
                    {
                        playerList = new List<BasePlayer>();
                        playerList.Add(foundPlayer);
                        break;
                    }
                    else
                    {
                        SendReply(player, "Player with name \"" + args[0] + "\" not found!");
                        return;
                    }
            }

            // Check on what to auth
            List<BaseEntity>[] items;
            items = FindItems(player, pluginConfig.Commands.Radius, wantedType);


            // Check whether to add or to remove
            int counter = 0;
            if (string.Equals(command, pluginConfig.Commands.ShareCommand))
            {
                foreach (BasePlayer foundPlayer in playerList)
                {
                    if (foundPlayer == null)
                        continue;

                    foreach (AutoTurret at in items[0])
                        if (AddToWhiteList(at, foundPlayer))
                            counter++;
                    foreach (BaseEntity cl in items[1])
                        if (AddToWhiteList(cl.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>(), foundPlayer))
                            counter++;
                    foreach (BuildingPrivlidge cb in items[2])
                        if (AddToWhiteList(cb, foundPlayer))
                            counter++;
                }
            }
            else if (string.Equals(command, pluginConfig.Commands.UnshareCommand))
            {
                foreach (BasePlayer foundPlayer in playerList)
                {
                    if (foundPlayer == null || (pluginConfig.General.PreventPlayersFromUnsharingThemself && foundPlayer.userID == player.userID))
                        continue;

                    foreach (AutoTurret at in items[0])
                        if (RemoveFromWhiteList(at, foundPlayer))
                            counter++;
                    foreach (BaseEntity cl in items[1])
                        if (RemoveFromWhiteList(cl.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>(), foundPlayer))
                            counter++;
                    foreach (BuildingPrivlidge cb in items[2])
                        if (RemoveFromWhiteList(cb, foundPlayer))
                            counter++;
                }
            }

            // Respond to player what has been done
            SendReply(player, buildAnswer(counter, items[0].Count, items[1].Count, items[2].Count, command, wantedType));
        }

        [HookMethod("SendHelpText")]
        private void ShowCommandHelp(BasePlayer player)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<size=16>Share</size> by DyingRust.de");
            sb.AppendLine("<size=12>Shares items with other players in a " + pluginConfig.Commands.Radius + "m radius around you.</size>");
            sb.AppendLine("<size=1> </size>");

            sb.AppendLine("<color=#FFD479>/" + pluginConfig.Commands.ShareCommand + "  <who>  <what></color>");
            sb.AppendLine("<size=12>Shares the item <what> with every player <who></size>");
            sb.AppendLine("<color=#FFD479>/" + pluginConfig.Commands.UnshareCommand + "  <who>  <what></color>");
            sb.AppendLine("<size=12>Unshares the item <what> with every player <who></size>");
            sb.AppendLine("<size=1> </size>");

            sb.AppendLine("<color=#FFD479><who></color><size=12> can be <color=orange>clan</color>, <color=orange>friends</color> or a player name</size>");
            sb.AppendLine("<color=#FFD479><what></color><size=12> can be <color=orange>at</color>(AutoTurrets), <color=orange>cl</color>(Codelocks), <color=orange>cb</color>(Cupboards) or <color=orange>all</color></size>");
            sb.AppendLine("<size=12>Example: <color=#FFD479>/" + pluginConfig.Commands.ShareCommand + " \"Ser Winter\" all</color></size>");

            SendReply(player, sb.ToString());
        }
        #endregion

        #region Functions
        private string buildAnswer(int createdWLEntries, int foundAT, int foundCL, int foundCB, string command, WantedEntityType type)
        {
            var sb = new StringBuilder();
            if (IsBitSet(type, WantedEntityType.AT))
                sb.AppendLine("Found   " + foundAT + " AutoTurrets!");
            if (IsBitSet(type, WantedEntityType.CL))
                sb.AppendLine("Found   " + foundCL + " CodeLocks!");
            if (IsBitSet(type, WantedEntityType.CB))
                sb.AppendLine("Found   " + foundCB + " Cupboards!");
            if (string.Equals(command, pluginConfig.Commands.ShareCommand))
                sb.AppendLine("Created " + createdWLEntries + " Whitelist Entries!");
            else if (string.Equals(command, pluginConfig.Commands.UnshareCommand))
                sb.AppendLine("Deleted " + createdWLEntries + " Whitelist Entries!");

            return sb.ToString();
        }

        // Finds all entities a player owns on a certain radius & returns them
        private List<BaseEntity>[] FindItems(BasePlayer player, float radius, WantedEntityType entityMask)
        {
            Dictionary<int, int> checkedInstanceIDs = new Dictionary<int, int>();
            List<BaseEntity>[] foundItems = new List<BaseEntity>[3];
            foundItems[0] = new List<BaseEntity>();
            foundItems[1] = new List<BaseEntity>();
            foundItems[2] = new List<BaseEntity>();

            foreach (var collider in Physics.OverlapSphere(player.transform.position, radius))
            {
                BaseEntity entity = collider.gameObject.ToBaseEntity();
                if (entity && !checkedInstanceIDs.ContainsKey(entity.GetInstanceID()))
                {
                    checkedInstanceIDs.Add(entity.GetInstanceID(), 1);
                    if (entity.OwnerID == player.userID)
                    {
                        if (IsBitSet(entityMask, WantedEntityType.AT) && entity is AutoTurret)
                            foundItems[0].Add(entity);
                        if (IsBitSet(entityMask, WantedEntityType.CL) && entity.HasSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>())
                            foundItems[1].Add(entity);
                        if (IsBitSet(entityMask, WantedEntityType.CB) && entity is BuildingPrivlidge)
                            foundItems[2].Add(entity);
                    }
                }

            }
            return foundItems;
        }

        bool IsBitSet(WantedEntityType value, WantedEntityType pos)
        {
            return (value & pos) != 0;
        }

        List<BasePlayer> FindFriends(BasePlayer player)
        {
            if (Friends == null)
                return null;

            List<BasePlayer> friends = new List<BasePlayer>();
            foreach (ulong userID in (ulong[])Friends?.Call("GetFriends", player.userID))
            {
                BasePlayer foundPlayer = FindPlayer(userID);
                if (foundPlayer)
                    friends.Add(foundPlayer);
            }

            return friends;
        }

        List<BasePlayer> FindClanMember(BasePlayer player)
        {
            if (Clans == null)
                return null;

            List<BasePlayer> clanMember = new List<BasePlayer>();
            string clanName = (string)Clans?.Call("GetClanOf", player.userID);

            if (string.IsNullOrEmpty(clanName))
                return null;
            else
            {
                JObject clan = (JObject)Clans?.Call("GetClan", clanName);
                if (clan != null)
                {
                    JArray members = (JArray)clan.GetValue("members");
                    if (members != null)
                    {
                        foreach (string member in members)
                        {
                            if (member == player.UserIDString)
                                continue;
                            BasePlayer foundPlayer = FindPlayer(member);
                            if (foundPlayer)
                                clanMember.Add(foundPlayer);
                        }
                    }
                }
            }

            return clanMember;
        }

        BasePlayer FindPlayer(string playerName)
        {
            BasePlayer foundPlayer = BasePlayer.Find(playerName);
            if (foundPlayer)
                return foundPlayer;

            foundPlayer = BasePlayer.FindSleeping(playerName);
            if (foundPlayer)
                return foundPlayer;

            IPlayer covplayer = covalence.Players.FindPlayer(playerName);
            if (covplayer != null)
                foundPlayer = (BasePlayer)covplayer.Object;

            return foundPlayer;
        }
        BasePlayer FindPlayer(ulong playerID)
        {
            BasePlayer foundPlayer = BasePlayer.FindByID(playerID);
            if (foundPlayer)
                return foundPlayer;

            foundPlayer = BasePlayer.FindSleeping(playerID);
            if (foundPlayer)
                return foundPlayer;

            IPlayer covplayer = covalence.Players.FindPlayerById(playerID.ToString());
            if (covplayer != null)
                foundPlayer = (BasePlayer)covplayer.Object;

            return foundPlayer;
        }

        private bool AddToWhiteList(AutoTurret at, BasePlayer player)
        {
            if (at.IsAuthed(player))
                return false;

            var protobufPlayer = new ProtoBuf.PlayerNameID();
            protobufPlayer.userid = player.userID;
            protobufPlayer.username = player.name;

            at.authorizedPlayers.Add(protobufPlayer);
            at.SendNetworkUpdate();
            at.SetTarget(null);

            return true;
        }
        private bool AddToWhiteList(CodeLock cl, BasePlayer player)
        {
            List<ulong> whitelist = codelockwhitelist.GetValue(cl) as List<ulong>;
            if (whitelist.Contains(player.userID))
                return false;
            whitelist.Add(player.userID);
            codelockwhitelist.SetValue(cl, whitelist);
            cl.SendNetworkUpdate();

            return true;
        }
        private bool AddToWhiteList(BuildingPrivlidge cb, BasePlayer player)
        {
            if (cb.IsAuthed(player))
                return false;

            var protobufPlayer = new ProtoBuf.PlayerNameID();
            protobufPlayer.userid = player.userID;
            protobufPlayer.username = player.name;
            cb.authorizedPlayers.Add(protobufPlayer);
            cb.SendNetworkUpdate();
            if (cb.CheckEntity(player))
                player.SetInsideBuildingPrivilege(cb, true);

            return true;
        }

        private bool RemoveFromWhiteList(AutoTurret at, BasePlayer player)
        {
            if (!at.IsAuthed(player))
                return false;

            int i = 0;
            foreach (var authedPlayer in at.authorizedPlayers)
            {
                if (authedPlayer.userid == player.userID)
                {
                    at.authorizedPlayers.RemoveAt(i);
                    at.SendNetworkUpdate();
                    at.SetTarget(null);
                    return true;
                }
                i++;
            }
            return false;
        }
        private bool RemoveFromWhiteList(CodeLock cl, BasePlayer player)
        {
            List<ulong> whitelist = codelockwhitelist.GetValue(cl) as List<ulong>;
            if (!whitelist.Contains(player.userID))
                return false;
            whitelist.Remove(player.userID);
            codelockwhitelist.SetValue(cl, whitelist);
            cl.SendNetworkUpdate();

            return true;
        }
        private bool RemoveFromWhiteList(BuildingPrivlidge cb, BasePlayer player)
        {
            if (!cb.IsAuthed(player))
                return false;

            int i = 0;
            foreach (var authedPlayer in cb.authorizedPlayers)
            {
                if (authedPlayer.userid == player.userID)
                {
                    cb.authorizedPlayers.RemoveAt(i);
                    cb.SendNetworkUpdate();
                    if (cb.CheckEntity(player))
                        player.SetInsideBuildingPrivilege(cb, false);

                    return true;
                }
                i++;
            }

            return false;
        }

        public void Logging(string msg) { Debug.Log("[Share] " + msg); }
        #endregion
    }
}
