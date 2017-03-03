using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{

    [Info("Share", "DyingRust.de", "0.1.0", ResourceId = 0000)]
    [Description("Share cupboards, codelocks and autoturrets")]
    public class Share : RustPlugin
    {
        #region Enum
        private enum WantedEntityType : uint
        {
            AT = 0x0001,
            CL = 0x0002,
            CB = 0x0004,
            ALL = AT + CL + CB
        }
        #endregion

        #region Fields  
        [PluginReference]
        private Plugin Friends, Clans;
        private PluginConfig pluginConfig;
        // Use string interpolation to format a float with 3 decimal points instead of calling string.Format()
        private FieldInfo codelockwhitelist = typeof(CodeLock).GetField("whitelistPlayers", (BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic));
        private string[] WhatOptions;
        #endregion

        #region Configuration
        // Classes for easier handling of config
        private class PluginConfig
        {
            public General General { get; set; }
            public Commands Commands { get; set; }
        }
        private class General
        {
            public bool UsePermission { get; set; }
            public bool PreventPlayersFromUnsharingThemself { get; set; }
            public bool ChangeOwnerIDOnCodeLockDeployed { get; set; }
        }
        private class Commands
        {
            public string ShareCommand { get; set; }
            public string UnshareCommand { get; set; }
            public bool AllowCupboardSharing { get; set; }
            public bool AllowCodelockSharing { get; set; }
            public bool AllowAutoturretSharing { get; set; }
            public float Radius { get; set; }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(pluginConfig);
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            pluginConfig = Config.ReadObject<PluginConfig>();
        }

        protected override void LoadDefaultConfig()
        {
            pluginConfig = new PluginConfig
            {
                General = new General
                {
                    UsePermission = false,
                    PreventPlayersFromUnsharingThemself = false,
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
        }
        #endregion

        #region Localization
        private const string
            NoPermission = "NoPermission",
            NoClan = "NoClan",
            NoFriends = "NoFriends",
            Player404 = "Player404",
            FoundXY = "FoundXY",
            CreatedWL = "CreatedWL",
            DeletedWL = "DeletedWL",
            WrongSyntax = "WrongSyntax",
            PluginDescription = "PluginDescription",
            PluginSyntaxShare = "PluginSyntaxShare",
            PluginSyntaxUnshare = "PluginSyntaxUnshare",
            PluginExample = "PluginExample";
        private string WhatList, Notes;

        private void LoadDefaultMessages()
        {
            // en
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [NoPermission] = "You don't have the permission to use this command!",
                [NoClan] = "You don't belong to any clan!",
                [NoFriends] = "You have no friends added yet!",
                [Player404] = "Player with name '{0}' not found!",
                [FoundXY] = "Found {0} {1}!",
                [CreatedWL] = "Created {0} Whitelist Entries!",
                [DeletedWL] = "Deleted {0} Whitelist Entries!",
                [WrongSyntax] = "Invalid command: '{0}'\nSee <color=orange>/share</color> for help!",
                [PluginDescription] = "Shares items with other players in a {0}m radius around you.",
                [PluginSyntaxShare] = "<color=#FFD479>/{0}  <friends|clan|name>  {1}</color>",
                [PluginSyntaxUnshare] = "<color=#FFD479>/{0}  <friends|clan|name>  {1}</color>",
                [PluginExample] = "Example: <color=#FFD479>/{0} \"Ser Winter\" all</color>"

            }, this);
        }
        private void buildStringsForHelpText()
        {
            List<string> list = new List<string>(),
                notes = new List<string>();
            if (pluginConfig.Commands.AllowAutoturretSharing)
            {
                list.Add("at");
                notes.Add("at=Autoturret");
            }
            if (pluginConfig.Commands.AllowCodelockSharing)
            {
                list.Add("cl");
                notes.Add("cl=Codelock");
            }
            if (pluginConfig.Commands.AllowCupboardSharing)
            {
                list.Add("cb");
                notes.Add("cb=Cupboard");
            }
            if(list.Count > 1)
                list.Add("all");

            WhatOptions = list.ToArray();
            WhatList = "<" + string.Join("|", list.ToArray()) + ">";
            Notes = "Notes: " + string.Join(", ", notes.ToArray());
        }
        #endregion

        #region Hooks
        private void Init()
        {
            if (!pluginConfig.Commands.AllowAutoturretSharing && !pluginConfig.Commands.AllowCodelockSharing && !pluginConfig.Commands.AllowCupboardSharing)
                return;

            LoadDefaultMessages();
            buildStringsForHelpText();

            if (pluginConfig.General.UsePermission)
                permission.RegisterPermission("share.allowed", this);

            // Unsubscribe from Hooks if necessary
            if (!pluginConfig.General.ChangeOwnerIDOnCodeLockDeployed)
                Unsubscribe("OnItemDeployed");

            // Register Commands
            cmd.AddChatCommand("share", this, "cmdShare");
            cmd.AddChatCommand("sh", this, "cmdShare");

            if (string.IsNullOrEmpty(pluginConfig.Commands.ShareCommand))
                Puts("No valid ShareCommand in config.");
            else
                cmd.AddChatCommand(pluginConfig.Commands.ShareCommand, this, "cmdShareShort");

            if (string.IsNullOrEmpty(pluginConfig.Commands.UnshareCommand))
                Puts("No valid UnshareCommand in config.");
            else
            {
                if (string.Equals(pluginConfig.Commands.ShareCommand, pluginConfig.Commands.UnshareCommand))
                    Puts("ShareCommand & UnshareCommand are the same.");
                else
                    cmd.AddChatCommand(pluginConfig.Commands.UnshareCommand, this, "cmdShareShort");
            }

        }

        private void Loaded()
        {
            if (!pluginConfig.Commands.AllowAutoturretSharing && !pluginConfig.Commands.AllowCodelockSharing && !pluginConfig.Commands.AllowCupboardSharing)
            {
                Puts("You don't allow any item to be shareable! So this plugin doesn't really have a use!\n Unloading!");
                rust.RunServerCommand("oxide.unload Share");
                return;
            }

            // Check if optinal dependencies are there
            if (Friends == null)
                Puts("Friends Plugin not found");
            else
                Puts("Friends Plugin found");

            if (Clans == null)
                Puts("Clans Plugin not found");
            else
                Puts("Clans Plugin found");
        }
        // Change OwnerID of entity when codelock is deployed
        private void OnItemDeployed(Deployer deployer, BaseEntity entity)
        {
            if (entity & entity.HasSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock))
            {
                CodeLock cl = entity.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>();
                if (cl)
                    entity.OwnerID = deployer.GetOwnerPlayer().userID;
            }
        }
        #endregion

        #region Commands
        void cmdShare(BasePlayer player, string command, string[] args)
        {
            ShowCommandHelp(player);
            return;
        }

        private void cmdShareShort(BasePlayer player, string command, string[] args)
        {
            // Check Permission
            if (pluginConfig.General.UsePermission && !permission.UserHasPermission(player.UserIDString, "share.allowed"))
            {
                SendReply(player, lang.GetMessage(NoPermission, this, player.UserIDString));
                return;
            }

            // Check Syntax
            if (args == null || args.Length != 2 || (Array.IndexOf(WhatOptions, args[1].ToLower()) == -1))
            {
                SendReply(player, string.Format(lang.GetMessage(WrongSyntax, this, player.UserIDString), "/" + command + " " + string.Join(" ", args)));
                return;
            }

            // Decide with who to share
            List<BasePlayer> playerList;
            switch (args[0].ToLower())
            {
                case "clan":
                    playerList = FindClanMember(player);
                    if (playerList == null || playerList.Count == 0)
                    {
                        SendReply(player, lang.GetMessage(NoClan, this, player.UserIDString));
                        return;
                    }
                    break;
                case "friends":
                    playerList = FindFriends(player);
                    if (playerList == null || playerList.Count == 0)
                    {
                        SendReply(player, lang.GetMessage(NoFriends, this, player.UserIDString));
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
                        SendReply(player, string.Format(lang.GetMessage(Player404, this, player.UserIDString), args[0]));
                        return;
                    }
            }

            // Check on what to auth
            WantedEntityType wantedType = (WantedEntityType)Enum.Parse(typeof(WantedEntityType), args[1].ToUpper());
            List<BaseEntity>[] items;
            items = FindWhat(player, pluginConfig.Commands.Radius, wantedType);


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
            SendReply(player, buildAnswer(player, counter, items[0].Count, items[1].Count, items[2].Count, command, wantedType));
        }

        [HookMethod("SendHelpText")]
        private void ShowCommandHelp(BasePlayer player)
        {
            if (pluginConfig.General.UsePermission && !permission.UserHasPermission(player.UserIDString, "share.allowed"))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("<size=20>Share</size> by DyingRust.de");
            sb.AppendLine("<size=12>" + string.Format(lang.GetMessage(PluginDescription, this, player.UserIDString), pluginConfig.Commands.Radius) + "</size>");
            sb.AppendLine("");
            sb.AppendLine("Syntax Share:      " + string.Format(lang.GetMessage(PluginSyntaxShare, this, player.UserIDString), pluginConfig.Commands.ShareCommand, WhatList));
            sb.AppendLine("Syntax Unshare:  " + string.Format(lang.GetMessage(PluginSyntaxUnshare, this, player.UserIDString), pluginConfig.Commands.UnshareCommand, WhatList));
            sb.AppendLine("<size=12>" + Notes + "</size>");
            sb.AppendLine("");
            sb.AppendLine("<size=12>" + string.Format(lang.GetMessage(PluginExample, this, player.UserIDString), pluginConfig.Commands.ShareCommand) + "</size>");

            SendReply(player, sb.ToString());
        }
        #endregion

        #region Functions

        #region Helper
        private bool IsBitSet(WantedEntityType value, WantedEntityType pos)
        {
            return (value & pos) != 0;
        }

        private string buildAnswer(BasePlayer player, int createdWLEntries, int foundAT, int foundCL, int foundCB, string command, WantedEntityType type)
        {
            var sb = new StringBuilder();
            if (pluginConfig.Commands.AllowAutoturretSharing && IsBitSet(type, WantedEntityType.AT))
                sb.AppendLine(string.Format(lang.GetMessage(FoundXY, this, player.UserIDString), foundAT, "AutoTurrets"));
            if (pluginConfig.Commands.AllowCodelockSharing && IsBitSet(type, WantedEntityType.CL))
                sb.AppendLine(string.Format(lang.GetMessage(FoundXY, this, player.UserIDString), foundCL, "CodeLocks"));
            if (pluginConfig.Commands.AllowCupboardSharing && IsBitSet(type, WantedEntityType.CB))
                sb.AppendLine(string.Format(lang.GetMessage(FoundXY, this, player.UserIDString), foundCB, "Cupboards"));
            if (string.Equals(command, pluginConfig.Commands.ShareCommand))
                sb.AppendLine(string.Format(lang.GetMessage(CreatedWL, this, player.UserIDString), createdWLEntries));
            else if (string.Equals(command, pluginConfig.Commands.UnshareCommand))
                sb.AppendLine(string.Format(lang.GetMessage(DeletedWL, this, player.UserIDString), createdWLEntries));

            return sb.ToString();
        }
        #endregion

        #region FindWhat
        // Finds all entities a player owns on a certain radius & returns them
        private List<BaseEntity>[] FindWhat(BasePlayer player, float radius, WantedEntityType entityMask)
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
                        if (pluginConfig.Commands.AllowAutoturretSharing && IsBitSet(entityMask, WantedEntityType.AT) && entity is AutoTurret)
                            foundItems[0].Add(entity);
                        if (pluginConfig.Commands.AllowCodelockSharing && IsBitSet(entityMask, WantedEntityType.CL) && entity.HasSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock) && entity.GetSlot(BaseEntity.Slot.Lock).GetComponent<CodeLock>())
                            foundItems[1].Add(entity);
                        if (pluginConfig.Commands.AllowCupboardSharing && IsBitSet(entityMask, WantedEntityType.CB) && entity is BuildingPrivlidge)
                            foundItems[2].Add(entity);
                    }
                }

            }
            return foundItems;
        }
        #endregion

        #region FindWho
        private List<BasePlayer> FindFriends(BasePlayer player)
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
        private List<BasePlayer> FindClanMember(BasePlayer player)
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
        private BasePlayer FindPlayer(string playerName)
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
        private BasePlayer FindPlayer(ulong playerID)
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
        #endregion

        #region ManipulateWhitelists
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
        #endregion

        #endregion
    }
}
