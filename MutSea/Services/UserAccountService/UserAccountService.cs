/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using MutSea.Data;
using MutSea.Framework;
using MutSea.Services.Interfaces;
using GridRegion = MutSea.Services.Interfaces.GridRegion;
using PermissionMask = MutSea.Framework.PermissionMask;

namespace MutSea.Services.UserAccountService
{
    public class UserAccountService : UserAccountServiceBase, IUserAccountService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static UserAccountService m_RootInstance;

        /// <summary>
        /// Should we create default entries (minimum body parts/clothing, avatar wearable entries) for a new avatar?
        /// </summary>
        private bool m_CreateDefaultAvatarEntries;

        protected IGridService m_GridService;
        protected IAuthenticationService m_AuthenticationService;
        protected IGridUserService m_GridUserService;
        protected IInventoryService m_InventoryService;
        protected IAvatarService m_AvatarService;

        public UserAccountService(IConfigSource config)
            : base(config)
        {
            IConfig userConfig = config.Configs["UserAccountService"];
            if (userConfig == null)
                throw new Exception("No UserAccountService configuration");

            string gridServiceDll = userConfig.GetString("GridService", string.Empty);
            if (gridServiceDll != string.Empty)
                m_GridService = LoadPlugin<IGridService>(gridServiceDll, [config]);

            string authServiceDll = userConfig.GetString("AuthenticationService", string.Empty);
            if (authServiceDll != string.Empty)
                m_AuthenticationService = LoadPlugin<IAuthenticationService>(authServiceDll, [config]);

            string presenceServiceDll = userConfig.GetString("GridUserService", string.Empty);
            if (presenceServiceDll != string.Empty)
                m_GridUserService = LoadPlugin<IGridUserService>(presenceServiceDll, [config]);

            string invServiceDll = userConfig.GetString("InventoryService", string.Empty);
            if (invServiceDll != string.Empty)
                m_InventoryService = LoadPlugin<IInventoryService>(invServiceDll, [config]);

            string avatarServiceDll = userConfig.GetString("AvatarService", string.Empty);
            if (avatarServiceDll != string.Empty)
                m_AvatarService = LoadPlugin<IAvatarService>(avatarServiceDll, [config]);

            m_CreateDefaultAvatarEntries = userConfig.GetBoolean("CreateDefaultAvatarEntries", false);

            if (m_RootInstance == null)
            {
                m_RootInstance = this;

                //  create a system grid god account
                UserAccount ggod = GetUserAccount(UUID.Zero, Constants.servicesGodAgentID);
                if(ggod == null)
                {
                    UserAccountData d = new()
                    {
                        FirstName = "GRID",
                        LastName = "SERVICES",
                        PrincipalID = Constants.servicesGodAgentID,
                        ScopeID = UUID.Zero,
                        Data = new Dictionary<string, string>()
                        { 
                            ["Email"] = string.Empty,
                            ["Created"] = Util.UnixTimeSinceEpoch().ToString(),
                            ["UserLevel"] = "240",
                            ["UserFlags"] = "0",
                            ["ServiceURLs"] = string.Empty
                        }
                    };

                    m_Database.Store(d);
                }

                // In case there are several instances of this class in the same process,
                // the console commands are only registered for the root instance
                if (MainConsole.Instance != null)
                {
               
                    MainConsole.Instance.Commands.AddCommand("Users", false,
                            "create user",
                            "create user [<first> [<last> [<pass> [<email> [<user id> [<model>]]]]]]",
                            "Create a new user", HandleCreateUser);

                    MainConsole.Instance.Commands.AddCommand("Users", false,
                            "reset user password",
                            "reset user password [<first> [<last> [<password>]]]",
                        "Reset a user password", HandleResetUserPassword);

                    MainConsole.Instance.Commands.AddCommand("Users", false,
                        "reset user email",
                        "reset user email [<first> [<last> [<email>]]]",
                        "Reset a user email address", HandleResetUserEmail);

                    MainConsole.Instance.Commands.AddCommand("Users", false,
                            "set user level",
                            "set user level [<first> [<last> [<level>]]]",
                            "Set user level. If >= 200 and 'allow_grid_gods = true' in MutSea.ini, "
                                + "this account will be treated as god-moded. "
                                + "It will also affect the 'login level' command. ",
                            HandleSetUserLevel);

                    MainConsole.Instance.Commands.AddCommand("Users", false,
                            "show account",
                            "show account <first> <last>",
                            "Show account details for the given user", HandleShowAccount);
                }
            }
        }

        #region IUserAccountService

        public UserAccount GetUserAccount(UUID scopeID, string firstName,
                string lastName)
        {
        //m_log.DebugFormat(
        //    "[USER ACCOUNT SERVICE]: Retrieving account by username for {0} {1}, scope {2}",
        //        firstName, lastName, scopeID);

            UserAccountData[] d;

            if (scopeID.IsNotZero())
            {
                d = m_Database.Get(
                        ["ScopeID", "FirstName", "LastName"],
                        [scopeID.ToString(), firstName, lastName]);
                if (d.Length < 1)
                {
                    d = m_Database.Get(
                            ["ScopeID", "FirstName", "LastName"],
                            [UUID.Zero.ToString(), firstName, lastName]);
                }
            }
            else
            {
                d = m_Database.Get(
                        ["FirstName", "LastName"],
                        [firstName, lastName]);
            }

            if (d.Length < 1)
                return null;

            return MakeUserAccount(d[0]);
        }

        private UserAccount MakeUserAccount(UserAccountData d)
        {
            UserAccount u = new()
            {
                FirstName = d.FirstName,
                LastName = d.LastName,
                PrincipalID = d.PrincipalID,
                ScopeID = d.ScopeID
            };
            if (d.Data.TryGetValue("Email", out string value) && value != null)
                u.Email = value;
            else
                u.Email = string.Empty;
            u.Created = Convert.ToInt32(d.Data["Created"].ToString());
            if (d.Data.TryGetValue("UserTitle", out string valueut) && valueut != null)
                u.UserTitle = valueut;
            else
                u.UserTitle = string.Empty;
            if (d.Data.TryGetValue("UserLevel", out string valueul) && valueul != null)
                Int32.TryParse(valueul, out u.UserLevel);
            if (d.Data.TryGetValue("UserFlags", out string valueuf) && valueuf != null)
                Int32.TryParse(valueuf, out u.UserFlags);
            if (d.Data.TryGetValue("UserCountry", out string valueuc) && valueuc != null)
                u.UserCountry = valueuc;
            else
                u.UserCountry = string.Empty;

            u.ServiceURLs = new Dictionary<string, object>();
            if (d.Data.TryGetValue("ServiceURLs", out string ServiceURLsvalue) && !string.IsNullOrEmpty(ServiceURLsvalue))
            {
                string[] URLs = ServiceURLsvalue.Split(' ');
                foreach (string url in URLs)
                {
                    string[] parts = url.Split('=');

                    if (parts.Length != 2)
                        continue;

                    u.ServiceURLs[System.Web.HttpUtility.UrlDecode(parts[0])] =
                        System.Web.HttpUtility.UrlDecode(parts[1]);
                }
            }
            return u;
        }

        public UserAccount GetUserAccount(UUID scopeID, string email)
        {
            UserAccountData[] d;

            if (scopeID.IsNotZero())
            {
                d = m_Database.Get(["ScopeID", "Email"], [scopeID.ToString(), email]);
                if (d.Length < 1)
                {
                    d = m_Database.Get(["ScopeID", "Email"], [UUID.ZeroString, email]);
                }
            }
            else
            {
                d = m_Database.Get(["Email"], [email]);
            }

            if (d.Length < 1)
                return null;

            return MakeUserAccount(d[0]);
        }

        public UserAccount GetUserAccount(UUID scopeID, UUID principalID)
        {
            UserAccountData[] d;

            if (scopeID.IsNotZero())
            {
                d = m_Database.Get(["ScopeID", "PrincipalID"], [ scopeID.ToString(), principalID.ToString()]);
                if (d.Length < 1)
                {
                    d = m_Database.Get(["ScopeID", "PrincipalID"], [UUID.Zero.ToString(), principalID.ToString()]);
                }
            }
            else
            {
                d = m_Database.Get(["PrincipalID"], [principalID.ToString()]);
            }

            if (d.Length < 1)
            {
                return null;
            }

            return MakeUserAccount(d[0]);
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, List<string> IDs)
        {
            UserAccountData[] ret = m_Database.GetUsersWhere(scopeID, "PrincipalID in ('" + string.Join("', '", IDs) + "')");
            if(ret == null || ret.Length == 0)
                return [];

            List<UserAccount> lret = new(ret.Length);
            for(int i = 0; i < ret.Length; i++)
                lret.Add(MakeUserAccount(ret[i]));
            return lret;
        }

        public void InvalidateCache(UUID userID)
        {
        }

        public bool StoreUserAccount(UserAccount data)
        {
            //            m_log.DebugFormat(
            //                "[USER ACCOUNT SERVICE]: Storing user account for {0} {1} {2}, scope {3}",
            //                data.FirstName, data.LastName, data.PrincipalID, data.ScopeID);

            UserAccountData d = new()
            {
                FirstName = data.FirstName,
                LastName = data.LastName,
                PrincipalID = data.PrincipalID,
                ScopeID = data.ScopeID,
                Data = new Dictionary<string, string>
                {
                    ["Email"] = data.Email,
                    ["Created"] = data.Created.ToString(),
                    ["UserLevel"] = data.UserLevel.ToString(),
                    ["UserFlags"] = data.UserFlags.ToString()
                }
            };
            if (!string.IsNullOrEmpty(data.UserTitle))
                d.Data["UserTitle"] = data.UserTitle;
            if (!string.IsNullOrEmpty(data.UserCountry))
                d.Data["UserCountry"] = data.UserCountry;

            if(data.ServiceURLs.Count > 0)
            { 
                StringBuilder sb = new();
                int i = 1;
                foreach (KeyValuePair<string, object> kvp in data.ServiceURLs)
                {
                    sb.Append(System.Web.HttpUtility.UrlEncode(kvp.Key));
                    sb.Append('=');
                    sb.Append(System.Web.HttpUtility.UrlEncode(kvp.Value.ToString()));
                    if(i++ < data.ServiceURLs.Count)
                        sb.Append(' ');
                }
                d.Data["ServiceURLs"] = sb.ToString();
            }
            else
                d.Data["ServiceURLs"] = string.Empty;

            return m_Database.Store(d);
        }

        public List<UserAccount> GetUserAccounts(UUID scopeID, string query)
        {
            UserAccountData[] d = m_Database.GetUsers(scopeID, query.Trim());

            if (d == null)
                return [];

            List<UserAccount> ret = [];

            foreach (UserAccountData data in d)
                ret.Add(MakeUserAccount(data));

            return ret;
        }

        public List<UserAccount> GetUserAccountsWhere(UUID scopeID, string where)
        {
            UserAccountData[] d = m_Database.GetUsersWhere(scopeID, where);

            if (d == null)
                return [];

            List<UserAccount> ret = [];

            foreach (UserAccountData data in d)
                ret.Add(MakeUserAccount(data));

            return ret;
        }

        #endregion

        #region Console commands

        /// <summary>
        /// Handle the create user command from the console.
        /// </summary>
        /// <param name="cmdparams">string array with parameters: firstname, lastname, password, locationX, locationY, email, userID, model name </param>
        protected void HandleCreateUser(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string password = "";
            string email;
            string rawPrincipalId;
            string model;

           // List<char> excluded = new List<char>(new char[]{' '});
            List<char> excluded = new List<char>(new char[]{' ', '@', '.', ':' }); //Protect user names from using valid HG identifiers.
            if (cmdparams.Length < 3)
                firstName = MainConsole.Instance.Prompt("First name", "Default", excluded);
            else firstName = cmdparams[2];

            if (cmdparams.Length < 4)
                lastName = MainConsole.Instance.Prompt("Last name", "User", excluded);
            else lastName = cmdparams[3];

            if (cmdparams.Length < 5)
            {
                int retries = 3;
                while(--retries >= 0)
                {
                    password = MainConsole.Instance.Prompt("Password", null, null, false);
                    if(String.IsNullOrWhiteSpace(password))
                        MainConsole.Instance.Output("  You must provide a Password");
                    else
                        break;
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    MainConsole.Instance.Output("create user aborted");
                    return;
                }
            }
            else password = cmdparams[4];

            if (cmdparams.Length < 6)
                email = MainConsole.Instance.Prompt("Email", "");
            else email = cmdparams[5];

            if (cmdparams.Length < 7)
                rawPrincipalId = MainConsole.Instance.Prompt("User ID (enter for random)", "");
            else
                rawPrincipalId = cmdparams[6];

            if (cmdparams.Length < 8)
                model = MainConsole.Instance.Prompt("Model name","");
            else
                model = cmdparams[7];

            UUID principalId = UUID.Zero;
            if(String.IsNullOrWhiteSpace(rawPrincipalId))
                principalId = UUID.Random();
            else if (!UUID.TryParse(rawPrincipalId, out principalId))
                throw new Exception(string.Format("ID {0} is not a valid UUID", rawPrincipalId));

            CreateUser(UUID.Zero, principalId, firstName, lastName, password, email, model);
        }

        protected void HandleShowAccount(string module, string[] cmdparams)
        {
            if (cmdparams.Length != 4)
            {
                MainConsole.Instance.Output("Usage: show account <first-name> <last-name>");
                return;
            }

            string firstName = cmdparams[2];
            string lastName = cmdparams[3];

            UserAccount ua = GetUserAccount(UUID.Zero, firstName, lastName);

            if (ua == null)
            {
                MainConsole.Instance.Output("No user named {0} {1}", firstName, lastName);
                return;
            }

            MainConsole.Instance.Output("Name:    {0}", ua.Name);
            MainConsole.Instance.Output("ID:      {0}", ua.PrincipalID);
            MainConsole.Instance.Output("Title:   {0}", ua.UserTitle);
            MainConsole.Instance.Output("E-mail:  {0}", ua.Email);
            MainConsole.Instance.Output("Created: {0}", Utils.UnixTimeToDateTime(ua.Created));
            MainConsole.Instance.Output("Level:   {0}", ua.UserLevel);
            MainConsole.Instance.Output("Flags:   {0}", ua.UserFlags);
            foreach (KeyValuePair<string, Object> kvp in ua.ServiceURLs)
                MainConsole.Instance.Output("{0}: {1}", kvp.Key, kvp.Value);
        }

        protected void HandleResetUserPassword(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string newPassword;

            if (cmdparams.Length < 4)
                firstName = MainConsole.Instance.Prompt("First name");
            else firstName = cmdparams[3];

            if (cmdparams.Length < 5)
                lastName = MainConsole.Instance.Prompt("Last name");
            else lastName = cmdparams[4];

            if (cmdparams.Length < 6)
                newPassword = MainConsole.Instance.Prompt("New password", null, null, false);
            else newPassword = cmdparams[5];

            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);
            if (account == null)
            {
                MainConsole.Instance.Output("No such user as {0} {1}", firstName, lastName);
                return;
            }

            bool success = false;
            if (m_AuthenticationService != null)
                success = m_AuthenticationService.SetPassword(account.PrincipalID, newPassword);

            if (!success)
                MainConsole.Instance.Output("Unable to reset password for account {0} {1}.", firstName, lastName);
            else
                MainConsole.Instance.Output("Password reset for user {0} {1}", firstName, lastName);
        }

        protected void HandleResetUserEmail(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string newEmail;

            if (cmdparams.Length < 4)
                firstName = MainConsole.Instance.Prompt("First name");
            else firstName = cmdparams[3];

            if (cmdparams.Length < 5)
                lastName = MainConsole.Instance.Prompt("Last name");
            else lastName = cmdparams[4];

            if (cmdparams.Length < 6)
                newEmail = MainConsole.Instance.Prompt("New Email");
            else newEmail = cmdparams[5];

            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);
            if (account == null)
            {
                MainConsole.Instance.Output("No such user as {0} {1}", firstName, lastName);
                return;
            }

            bool success = false;

            account.Email = newEmail;

            success = StoreUserAccount(account);
            if (!success)
                MainConsole.Instance.Output("Unable to set Email for account {0} {1}.", firstName, lastName);
            else
                MainConsole.Instance.Output("User Email set for user {0} {1} to {2}", firstName, lastName, account.Email);
        }


        protected void HandleSetUserLevel(string module, string[] cmdparams)
        {
            string firstName;
            string lastName;
            string rawLevel;
            int level;

            if (cmdparams.Length < 4)
                firstName = MainConsole.Instance.Prompt("First name");
            else firstName = cmdparams[3];

            if (cmdparams.Length < 5)
                lastName = MainConsole.Instance.Prompt("Last name");
            else lastName = cmdparams[4];

            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);
            if (account == null) {
                MainConsole.Instance.Output("No such user");
                return;
            }

            if (cmdparams.Length < 6)
                rawLevel = MainConsole.Instance.Prompt("User level");
            else rawLevel = cmdparams[5];

            if(int.TryParse(rawLevel, out level) == false) {
                MainConsole.Instance.Output("Invalid user level");
                return;
            }

            account.UserLevel = level;

            bool success = StoreUserAccount(account);
            if (!success)
                MainConsole.Instance.Output("Unable to set user level for account {0} {1}.", firstName, lastName);
            else
                MainConsole.Instance.Output("User level set for user {0} {1} to {2}", firstName, lastName, level);
        }

        #endregion

        /// <summary>
        /// Create a user
        /// </summary>
        /// <param name="scopeID">Allows hosting of multiple grids in a single database.  Normally left as UUID.Zero</param>
        /// <param name="principalID">ID of the user</param>
        /// <param name="firstName"></param>
        /// <param name="lastName"></param>
        /// <param name="password"></param>
        /// <param name="email"></param>
        /// <param name="model"></param>
        public UserAccount CreateUser(UUID scopeID, UUID principalID, string firstName, string lastName, string password, string email, string model = "")
        {
            firstName = firstName.Trim();
            lastName = lastName.Trim();
            UserAccount account = GetUserAccount(UUID.Zero, firstName, lastName);

            if (account is not null)
            {
                m_log.Error($"[USER ACCOUNT SERVICE]: A user with the name {firstName} {lastName} already exists!");
                return null;
            }

            account = new UserAccount(UUID.Zero, principalID, firstName, lastName, email);
            if (account.ServiceURLs == null || account.ServiceURLs.Count == 0)
            {
                account.ServiceURLs = new Dictionary<string, object>
                {
                    ["HomeURI"] = string.Empty,
                    ["InventoryServerURI"] = string.Empty,
                    ["AssetServerURI"] = string.Empty
                };
            }

            if (!StoreUserAccount(account))
            {
                m_log.Error($"[USER ACCOUNT SERVICE]: Account creation failed for account {firstName} {lastName}");
                return null;
            }

            bool success;
            if (m_AuthenticationService != null)
            {
                success = m_AuthenticationService.SetPassword(account.PrincipalID, password);
                if (!success)
                    m_log.Warn($"[USER ACCOUNT SERVICE]: Unable to set password for account {firstName} {lastName}");
            }

            GridRegion home = null;
            if (m_GridService != null)
            {
                List<GridRegion> defaultRegions = m_GridService.GetDefaultRegions(UUID.Zero);
                if (defaultRegions != null && defaultRegions.Count >= 1)
                    home = defaultRegions[0];

                if (home != null)
                    m_GridUserService.SetHome(account.PrincipalID.ToString(), home.RegionID, new Vector3(128, 128, 0), new Vector3(0, 1, 0));
                else
                    m_log.Warn($"[USER ACCOUNT SERVICE]: Unable to set home for account {firstName} {lastName}");
            }
            else
            {
                m_log.Warn(
                    $"[USER ACCOUNT SERVICE]: Unable to retrieve default home region for account {firstName} {lastName}");
            }

            if (m_InventoryService != null)
            {
                success = m_InventoryService.CreateUserInventory(account.PrincipalID);
                if (!success)
                {
                    m_log.Warn(
                        $"[USER ACCOUNT SERVICE]: Unable to create inventory for account {firstName} {lastName}");
                }
                else
                {
                    m_log.Debug(
                        $"[USER ACCOUNT SERVICE]: Created user inventory for {firstName} {lastName}");
                }

                if (m_CreateDefaultAvatarEntries)
                {
                    if (string.IsNullOrEmpty(model))
                        CreateDefaultAppearanceEntries(account.PrincipalID);
                    else
                        EstablishAppearance(account.PrincipalID, model);
                }
            }

            m_log.Info(
                $"[USER ACCOUNT SERVICE]: Account {firstName} {lastName} {account.PrincipalID} created successfully");

            return account;
        }

        protected void CreateDefaultAppearanceEntries(UUID principalID)
        {
            m_log.DebugFormat("[USER ACCOUNT SERVICE]: Creating default appearance items for {0}", principalID);

            InventoryFolderBase bodyPartsFolder = m_InventoryService.GetFolderForType(principalID, FolderType.BodyPart);
            // Get Current Outfit folder
            InventoryFolderBase currentOutfitFolder = m_InventoryService.GetFolderForType(principalID, FolderType.CurrentOutfit);

            InventoryItemBase eyes = new InventoryItemBase(UUID.Random(), principalID);
            eyes.AssetID = AvatarWearable.DEFAULT_EYES_ASSET;
            eyes.Name = "Default Eyes";
            eyes.CreatorId = principalID.ToString();
            eyes.AssetType = (int)AssetType.Bodypart;
            eyes.InvType = (int)InventoryType.Wearable;
            eyes.Folder = bodyPartsFolder.ID;
            eyes.BasePermissions = (uint)PermissionMask.All;
            eyes.CurrentPermissions = (uint)PermissionMask.All;
            eyes.EveryOnePermissions = (uint)PermissionMask.All;
            eyes.GroupPermissions = (uint)PermissionMask.All;
            eyes.NextPermissions = (uint)PermissionMask.All;
            eyes.Flags = (uint)WearableType.Eyes;
            m_InventoryService.AddItem(eyes);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Eyes, eyes.Name, eyes.ID, principalID, currentOutfitFolder.ID);

            InventoryItemBase shape = new InventoryItemBase(UUID.Random(), principalID);
            shape.AssetID = AvatarWearable.DEFAULT_BODY_ASSET;
            shape.Name = "Default Shape";
            shape.CreatorId = principalID.ToString();
            shape.AssetType = (int)AssetType.Bodypart;
            shape.InvType = (int)InventoryType.Wearable;
            shape.Folder = bodyPartsFolder.ID;
            shape.BasePermissions = (uint)PermissionMask.All;
            shape.CurrentPermissions = (uint)PermissionMask.All;
            shape.EveryOnePermissions = (uint)PermissionMask.All;
            shape.GroupPermissions = (uint)PermissionMask.All;
            shape.NextPermissions = (uint)PermissionMask.All;
            shape.Flags = (uint)WearableType.Shape;
            m_InventoryService.AddItem(shape);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Shape, shape.Name, shape.ID, principalID, currentOutfitFolder.ID);

            InventoryItemBase skin = new InventoryItemBase(UUID.Random(), principalID);
            skin.AssetID = AvatarWearable.DEFAULT_SKIN_ASSET;
            skin.Name = "Default Skin";
            skin.CreatorId = principalID.ToString();
            skin.AssetType = (int)AssetType.Bodypart;
            skin.InvType = (int)InventoryType.Wearable;
            skin.Folder = bodyPartsFolder.ID;
            skin.BasePermissions = (uint)PermissionMask.All;
            skin.CurrentPermissions = (uint)PermissionMask.All;
            skin.EveryOnePermissions = (uint)PermissionMask.All;
            skin.GroupPermissions = (uint)PermissionMask.All;
            skin.NextPermissions = (uint)PermissionMask.All;
            skin.Flags = (uint)WearableType.Skin;
            m_InventoryService.AddItem(skin);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Skin, skin.Name, skin.ID, principalID, currentOutfitFolder.ID);

            InventoryItemBase hair = new InventoryItemBase(UUID.Random(), principalID);
            hair.AssetID = AvatarWearable.DEFAULT_HAIR_ASSET;
            hair.Name = "Default Hair";
            hair.CreatorId = principalID.ToString();
            hair.AssetType = (int)AssetType.Bodypart;
            hair.InvType = (int)InventoryType.Wearable;
            hair.Folder = bodyPartsFolder.ID;
            hair.BasePermissions = (uint)PermissionMask.All;
            hair.CurrentPermissions = (uint)PermissionMask.All;
            hair.EveryOnePermissions = (uint)PermissionMask.All;
            hair.GroupPermissions = (uint)PermissionMask.All;
            hair.NextPermissions = (uint)PermissionMask.All;
            hair.Flags = (uint)WearableType.Hair;
            m_InventoryService.AddItem(hair);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Hair, hair.Name, hair.ID, principalID, currentOutfitFolder.ID);

            InventoryFolderBase clothingFolder = m_InventoryService.GetFolderForType(principalID, FolderType.Clothing);

            InventoryItemBase shirt = new InventoryItemBase(UUID.Random(), principalID);
            shirt.AssetID = AvatarWearable.DEFAULT_SHIRT_ASSET;
            shirt.Name = "Default Shirt";
            shirt.CreatorId = principalID.ToString();
            shirt.AssetType = (int)AssetType.Clothing;
            shirt.InvType = (int)InventoryType.Wearable;
            shirt.Folder = clothingFolder.ID;
            shirt.BasePermissions = (uint)PermissionMask.All;
            shirt.CurrentPermissions = (uint)PermissionMask.All;
            shirt.EveryOnePermissions = (uint)PermissionMask.All;
            shirt.GroupPermissions = (uint)PermissionMask.All;
            shirt.NextPermissions = (uint)PermissionMask.All;
            shirt.Flags = (uint)WearableType.Shirt;
            m_InventoryService.AddItem(shirt);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Shirt, shirt.Name, shirt.ID, principalID, currentOutfitFolder.ID);

            InventoryItemBase pants = new InventoryItemBase(UUID.Random(), principalID);
            pants.AssetID = AvatarWearable.DEFAULT_PANTS_ASSET;
            pants.Name = "Default Pants";
            pants.CreatorId = principalID.ToString();
            pants.AssetType = (int)AssetType.Clothing;
            pants.InvType = (int)InventoryType.Wearable;
            pants.Folder = clothingFolder.ID;
            pants.BasePermissions = (uint)PermissionMask.All;
            pants.CurrentPermissions = (uint)PermissionMask.All;
            pants.EveryOnePermissions = (uint)PermissionMask.All;
            pants.GroupPermissions = (uint)PermissionMask.All;
            pants.NextPermissions = (uint)PermissionMask.All;
            pants.Flags = (uint)WearableType.Pants;
            m_InventoryService.AddItem(pants);
            CreateCurrentOutfitLink((int)InventoryType.Wearable, (uint)WearableType.Pants, pants.Name, pants.ID, principalID, currentOutfitFolder.ID);

            if (m_AvatarService != null)
            {
                m_log.DebugFormat("[USER ACCOUNT SERVICE]: Creating default avatar entries for {0}", principalID);

                AvatarWearable[] wearables = new AvatarWearable[6];
                wearables[AvatarWearable.EYES] = new AvatarWearable(eyes.ID, eyes.AssetID);
                wearables[AvatarWearable.BODY] = new AvatarWearable(shape.ID, shape.AssetID);
                wearables[AvatarWearable.SKIN] = new AvatarWearable(skin.ID, skin.AssetID);
                wearables[AvatarWearable.HAIR] = new AvatarWearable(hair.ID, hair.AssetID);
                wearables[AvatarWearable.SHIRT] = new AvatarWearable(shirt.ID, shirt.AssetID);
                wearables[AvatarWearable.PANTS] = new AvatarWearable(pants.ID, pants.AssetID);

                AvatarAppearance ap = new AvatarAppearance();
                // this loop works, but is questionable
                for (int i = 0; i < 6; i++)
                {
                    ap.SetWearable(i, wearables[i]);
                }

                m_AvatarService.SetAppearance(principalID, ap);
            }
        }

        protected void EstablishAppearance(UUID destinationAgent, string model)
        {
            m_log.DebugFormat("[USER ACCOUNT SERVICE]: Establishing new appearance for {0} - {1}",
                              destinationAgent.ToString(), model);

            string[] modelSpecifiers = model.Split();
            if (modelSpecifiers.Length != 2)
            {
                m_log.WarnFormat("[USER ACCOUNT SERVICE]: Invalid model name \'{0}\'. Falling back to Ruth for {1}",
                                 model, destinationAgent);
                CreateDefaultAppearanceEntries(destinationAgent);
                return;
            }

            // Does the source model exist?
            UserAccount modelAccount = GetUserAccount(UUID.Zero, modelSpecifiers[0], modelSpecifiers[1]);
            if (modelAccount == null)
            {
                m_log.WarnFormat("[USER ACCOUNT SERVICE]: Requested model \'{0}\' not found. Falling back to Ruth for {1}",
                                 model, destinationAgent);
                CreateDefaultAppearanceEntries(destinationAgent);
                return;
            }

            // Does the source model have an established appearance herself?
            AvatarAppearance modelAppearance = m_AvatarService.GetAppearance(modelAccount.PrincipalID);
            if (modelAppearance == null)
            {
                m_log.WarnFormat("USER ACCOUNT SERVICE]: Requested model \'{0}\' does not have an established appearance. Falling back to Ruth for {1}",
                                 model, destinationAgent);
                CreateDefaultAppearanceEntries(destinationAgent);
                return;
            }

            try
            {
                CopyWearablesAndAttachments(destinationAgent, modelAccount.PrincipalID, modelAppearance);

                m_AvatarService.SetAppearance(destinationAgent, modelAppearance);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[USER ACCOUNT SERVICE]: Error transferring appearance for {0} : {1}",
                    destinationAgent, e.Message);
            }

            m_log.DebugFormat("[USER ACCOUNT SERVICE]: Finished establishing appearance for {0}",
                destinationAgent.ToString());
        }

        /// <summary>
        /// This method is called by EstablishAppearance to do a copy all inventory items
        /// worn or attached to the Clothing inventory folder of the receiving avatar.
        /// In parallel the avatar wearables and attachments are updated.
        /// </summary>
        private void CopyWearablesAndAttachments(UUID destination, UUID source, AvatarAppearance avatarAppearance)
        {

            AvatarWearable[] wearables = avatarAppearance.Wearables;
            if(wearables.Length == 0)
                throw new Exception("Model does not have wearables");

            // Get Clothing folder of receiver
            InventoryFolderBase destinationFolder = m_InventoryService.GetFolderForType(destination, FolderType.Clothing);

            if (destinationFolder == null)
                throw new Exception("Cannot locate new clothing folder(s)");

            // Get Current Outfit folder
            InventoryFolderBase currentOutfitFolder = m_InventoryService.GetFolderForType(destination, FolderType.CurrentOutfit);

            // wrong destination folder type?  create new
            if (destinationFolder.Type != (short)FolderType.Clothing)
            {
                destinationFolder = new InventoryFolderBase();
                destinationFolder.ID       = UUID.Random();
                destinationFolder.Name     = "Clothing";
                destinationFolder.Owner    = destination;
                destinationFolder.Type     = (short)AssetType.Clothing;
                destinationFolder.ParentID = m_InventoryService.GetRootFolder(destination).ID;
                destinationFolder.Version  = 1;
                m_InventoryService.AddFolder(destinationFolder);     // store base record
                m_log.ErrorFormat("[USER ACCOUNT SERVICE]: Created folder for destination {0} Clothing", source);
            }

            // Wearables
            AvatarWearable basewearable;
            WearableItem wearable;

            AvatarWearable newbasewearable = new AvatarWearable();
            // copy wearables creating new inventory entries
            for (int i = 0; i < wearables.Length; i++)
            {
                basewearable = wearables[i];
                if(basewearable == null || basewearable.Count == 0)
                    continue;

                newbasewearable.Clear();
                for(int j = 0; j < basewearable.Count; j++)
                {
                    wearable = basewearable[j];
                    if (!wearable.ItemID.IsZero())
                    {
                        m_log.DebugFormat("[XXX]: Getting item {0} from avie {1} for {2} {3}",
                            wearable.ItemID, source, i, j);
                        // Get inventory item and copy it
                        InventoryItemBase item = m_InventoryService.GetItem(source, wearable.ItemID);

                        if(item != null && item.AssetType == (int)AssetType.Link)
                        {
                            if(item.AssetID.IsZero())
                                item = null;
                            else
                              item = m_InventoryService.GetItem(source, item.AssetID);
                        }

                        if (item != null)
                        {
                            InventoryItemBase destinationItem = new InventoryItemBase(UUID.Random(), destination);
                            destinationItem.Name = item.Name;
                            destinationItem.Owner = destination;
                            destinationItem.Description = item.Description;
                            destinationItem.InvType = item.InvType;
                            destinationItem.CreatorId = item.CreatorId;
                            destinationItem.CreatorData = item.CreatorData;
                            destinationItem.NextPermissions = item.NextPermissions;
                            destinationItem.CurrentPermissions = item.CurrentPermissions;
                            destinationItem.BasePermissions = item.BasePermissions;
                            destinationItem.EveryOnePermissions = item.EveryOnePermissions;
                            destinationItem.GroupPermissions = item.GroupPermissions;
                            destinationItem.AssetType = item.AssetType;
                            destinationItem.AssetID = item.AssetID;
                            destinationItem.GroupID = item.GroupID;
                            destinationItem.GroupOwned = item.GroupOwned;
                            destinationItem.SalePrice = item.SalePrice;
                            destinationItem.SaleType = item.SaleType;
                            destinationItem.Flags = item.Flags;
                            destinationItem.CreationDate = item.CreationDate;
                            destinationItem.Folder = destinationFolder.ID;
                            ApplyNextOwnerPermissions(destinationItem);

                            m_InventoryService.AddItem(destinationItem);
                            m_log.DebugFormat("[USER ACCOUNT SERVICE]: Added item {0} to folder {1}", destinationItem.ID, destinationFolder.ID);

                            // Wear item
                            newbasewearable.Add(destinationItem.ID,wearable.AssetID);

                            // Add to Current Outfit
                            CreateCurrentOutfitLink((int)InventoryType.Wearable, item.Flags, item.Name, destinationItem.ID, destination, currentOutfitFolder.ID);
                        }
                        else
                        {
                            m_log.WarnFormat("[USER ACCOUNT SERVICE]: Error transferring {0} to folder {1}", wearable.ItemID, destinationFolder.ID);
                        }
                    }
                }
                avatarAppearance.SetWearable(i, newbasewearable);
            }

            // Attachments
            List<AvatarAttachment> attachments = avatarAppearance.GetAttachments();
            avatarAppearance.ClearAttachments();

            foreach (AvatarAttachment attachment in attachments)
            {
                int attachpoint = attachment.AttachPoint;
                UUID itemID = attachment.ItemID;

                if (!itemID.IsZero())
                {
                    // Get inventory item and copy it
                    InventoryItemBase item = m_InventoryService.GetItem(source, itemID);

                    if (item != null)
                    {
                        InventoryItemBase destinationItem = new InventoryItemBase(UUID.Random(), destination);
                        destinationItem.Name = item.Name;
                        destinationItem.Owner = destination;
                        destinationItem.Description = item.Description;
                        destinationItem.InvType = item.InvType;
                        destinationItem.CreatorId = item.CreatorId;
                        destinationItem.CreatorData = item.CreatorData;
                        destinationItem.NextPermissions = item.NextPermissions;
                        destinationItem.CurrentPermissions = item.CurrentPermissions;
                        destinationItem.BasePermissions = item.BasePermissions;
                        destinationItem.EveryOnePermissions = item.EveryOnePermissions;
                        destinationItem.GroupPermissions = item.GroupPermissions;
                        destinationItem.AssetType = item.AssetType;
                        destinationItem.AssetID = item.AssetID;
                        destinationItem.GroupID = item.GroupID;
                        destinationItem.GroupOwned = item.GroupOwned;
                        destinationItem.SalePrice = item.SalePrice;
                        destinationItem.SaleType = item.SaleType;
                        destinationItem.Flags = item.Flags;
                        destinationItem.CreationDate = item.CreationDate;
                        destinationItem.Folder = destinationFolder.ID;
                        ApplyNextOwnerPermissions(destinationItem);

                        m_InventoryService.AddItem(destinationItem);
                        m_log.DebugFormat("[USER ACCOUNT SERVICE]: Added item {0} to folder {1}", destinationItem.ID, destinationFolder.ID);

                        // Attach item
                        avatarAppearance.SetAttachment(attachpoint, destinationItem.ID, destinationItem.AssetID);
                        m_log.DebugFormat("[USER ACCOUNT SERVICE]: Attached {0}", destinationItem.ID);

                        // Add to Current Outfit
                        CreateCurrentOutfitLink(destinationItem.InvType, item.Flags, item.Name, destinationItem.ID, destination, currentOutfitFolder.ID);
                    }
                    else
                    {
                        m_log.WarnFormat("[USER ACCOUNT SERVICE]: Error transferring {0} to folder {1}", itemID, destinationFolder.ID);
                    }
                }
            }
        }

        protected void CreateCurrentOutfitLink(int invType, uint itemType, string name, UUID itemID, UUID userID, UUID currentOutfitFolderUUID)
        {
            UUID LinkInvItem = UUID.Random();
            InventoryItemBase itembase = new InventoryItemBase(LinkInvItem, userID)
            {
                AssetID = itemID,
                AssetType = (int)AssetType.Link,
                CreatorId = userID.ToString(),
                InvType = invType,
                Description = "",
                //Folder = m_InventoryService.GetFolderForType(userID, FolderType.CurrentOutfit).ID,
                Folder = currentOutfitFolderUUID,
                Flags = itemType,
                Name = name,
                BasePermissions = (uint)PermissionMask.Copy,
                CurrentPermissions = (uint)PermissionMask.Copy,
                EveryOnePermissions = (uint)PermissionMask.Copy,
                GroupPermissions = (uint)PermissionMask.Copy,
                NextPermissions = (uint)PermissionMask.Copy
            };

            m_InventoryService.AddItem(itembase);
        }

        /// <summary>
        /// Apply next owner permissions.
        /// </summary>
        private void ApplyNextOwnerPermissions(InventoryItemBase item)
        {
            if (item.InvType == (int)InventoryType.Object)
            {
                uint perms = item.CurrentPermissions;
                item.CurrentPermissions = perms;
            }

            item.CurrentPermissions &= item.NextPermissions;
            item.BasePermissions &= item.NextPermissions;
            item.EveryOnePermissions &= item.NextPermissions;
        }
    }
}
