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
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using Mono.Addins;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Services.Interfaces;
using MutSea.Services.Connectors.Hypergrid;
using FriendInfo = MutSea.Services.Interfaces.FriendInfo;
using PresenceInfo = MutSea.Services.Interfaces.PresenceInfo;
using GridRegion = MutSea.Services.Interfaces.GridRegion;

namespace MutSea.Region.CoreModules.Avatar.Friends
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "HGFriendsModule")]
    public class HGFriendsModule : FriendsModule, ISharedRegionModule, IFriendsModule, IFriendsSimConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private int m_levelHGFriends = 0;

        IUserManagement m_uMan;
        public IUserManagement UserManagementModule
        {
            get
            {
                m_uMan ??= m_Scenes[0].RequestModuleInterface<IUserManagement>();
                return m_uMan;
            }
        }

        protected HGFriendsServicesConnector m_HGFriendsConnector = new();
        protected HGStatusNotifier m_StatusNotifier;

        #region ISharedRegionModule
        public override string Name
        {
            get { return "HGFriendsModule"; }
        }

        public override void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            base.AddRegion(scene);
            scene.RegisterModuleInterface<IFriendsSimConnector>(this);
        }

        public override void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
            if (m_StatusNotifier == null)
                m_StatusNotifier = new HGStatusNotifier(this);
        }

        protected override void InitModule(IConfigSource config)
        {
            base.InitModule(config);

            // Additionally to the base method
            IConfig friendsConfig = config.Configs["HGFriendsModule"];
            if (friendsConfig != null)
            {
                m_levelHGFriends = friendsConfig.GetInt("LevelHGFriends", 0);

                // TODO: read in all config variables pertaining to
                // HG friendship permissions
            }
        }

        #endregion

        #region IFriendsSimConnector

        /// <summary>
        /// Notify the user that the friend's status changed
        /// </summary>
        /// <param name="userID">user to be notified</param>
        /// <param name="friendID">friend whose status changed</param>
        /// <param name="online">status</param>
        /// <returns></returns>
        public bool StatusNotify(UUID friendID, UUID userID, bool online)
        {
            return LocalStatusNotification(friendID, userID, online);
        }

        #endregion

        protected override void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            if ((InstantMessageDialog)im.dialog == InstantMessageDialog.FriendshipOffered)
            {
                // we got a friendship offer
                UUID principalID = new(im.fromAgentID);
                UUID friendID = new(im.toAgentID);

                // Check if friendID is foreigner and if principalID has the permission
                // to request friendships with foreigners. If not, return immediately.
                if (!UserManagementModule.IsLocalGridUser(friendID))
                {
                    ((Scene)client.Scene).TryGetScenePresence(principalID, out ScenePresence avatar);
                    if (avatar is null)
                        return;

                    if (avatar.GodController.UserLevel < m_levelHGFriends)
                    {
                        client.SendAgentAlertMessage("Unable to send friendship invitation to foreigner. Insufficient permissions.", false);
                        return;
                    }
                }
            }

            base.OnInstantMessage(client, im);
        }

        protected override void OnApproveFriendRequest(IClientAPI client, UUID friendID, List<UUID> callingCardFolders)
        {
            // Update the local cache. Yes, we need to do it right here
            // because the HGFriendsService placed something on the DB
            // from under the sim
            base.OnApproveFriendRequest(client, friendID, callingCardFolders);
        }

        protected override bool CacheFriends(IClientAPI client)
        {
            //m_log.DebugFormat("[HGFRIENDS MODULE]: Entered CacheFriends for {0}", client.Name);

            if (base.CacheFriends(client))
            {
                // we do this only for the root agent
                UserFriendData FriendData = m_Friends[client.AgentId];
                if (FriendData.Refcount == 1)
                {
                    IUserManagement uMan = m_Scenes[0].RequestModuleInterface<IUserManagement>();
                    if(uMan == null)
                        return true;
                    // We need to preload the user management cache with the names
                    // of foreign friends, just like we do with SOPs' creators
                    foreach (FriendInfo finfo in FriendData.Friends)
                    {
                        if (finfo.TheirFlags != -1)
                        {
                            if (Util.ParseFullUniversalUserIdentifier(finfo.Friend, out UUID id, out string url, out string first, out string last))
                            {
                                //m_log.DebugFormat("[HGFRIENDS MODULE]: caching {0}", finfo.Friend);
                                uMan.AddUser(id,first,last, url);
                            }
                        }
                    }

                    //m_log.DebugFormat("[HGFRIENDS MODULE]: Exiting CacheFriends for {0} since detected root agent", client.Name);
                    return true;
                }
            }

            //m_log.DebugFormat("[HGFRIENDS MODULE]: Exiting CacheFriends for {0} since detected not root agent", client.Name);
            return false;
        }

        public override bool SendFriendsOnlineIfNeeded(IClientAPI client)
        {
            //m_log.DebugFormat("[HGFRIENDS MODULE]: Entering SendFriendsOnlineIfNeeded for {0}", client.Name);

            if (base.SendFriendsOnlineIfNeeded(client))
            {
                AgentCircuitData aCircuit = ((Scene)client.Scene).AuthenticateHandler.GetAgentCircuitData(client.AgentId);
                if (aCircuit is not null && (aCircuit.teleportFlags & (uint)Constants.TeleportFlags.ViaHGLogin) != 0)
                {
                    UserAccount account = m_Scenes[0].UserAccountService.GetUserAccount(client.Scene.RegionInfo.ScopeID, client.AgentId);
                    if (account is null) // foreign
                    {
                        FriendInfo[] friends = GetFriendsFromCache(client.AgentId);
                        foreach (FriendInfo f in friends)
                        {
                            int rights = f.TheirFlags;
                            if(rights != -1 )
                                client.SendChangeUserRights(new UUID(f.Friend), client.AgentId, rights);
                        }
                    }
                }
            }

            //m_log.DebugFormat("[HGFRIENDS MODULE]: Exiting SendFriendsOnlineIfNeeded for {0}", client.Name);
            return false;
        }

        protected override void GetOnlineFriends(UUID userID, List<string> friendList, /*collector*/ List<UUID> online)
        {
            //m_log.DebugFormat("[HGFRIENDS MODULE]: Entering GetOnlineFriends for {0}", userID);

            List<string> fList = new();
            foreach (string s in friendList)
            {
                if (s.Length < 36)
                    m_log.WarnFormat(
                        "[HGFRIENDS MODULE]: Ignoring friend {0} ({1} chars) for {2} since identifier too short",
                        s, s.Length, userID);
                else
                    fList.Add(s.Substring(0, 36));
            }

            // FIXME: also query the presence status of friends in other grids (like in HGStatusNotifier.Notify())

            PresenceInfo[] presence = PresenceService.GetAgents(fList.ToArray());
            if (presence.Length == 0)
                return;

            if (!m_OnlineFriendsCache.TryGetValue(userID, out HashSet<UUID> friends))
            {
                friends = new HashSet<UUID>();
                m_OnlineFriendsCache[userID] = friends;
            }

            foreach (PresenceInfo pi in presence)
            {
                if (UUID.TryParse(pi.UserID, out UUID presenceID))
                {
                    online.Add(presenceID);
                    friends.Add(presenceID);
                }
            }

            //m_log.DebugFormat("[HGFRIENDS MODULE]: Exiting GetOnlineFriends for {0}", userID);
        }

        protected override void StatusNotify(List<FriendInfo> friendList, UUID userID, bool online)
        {
            //m_log.DebugFormat("[HGFRIENDS MODULE]: Entering StatusNotify for {0}", userID);

            // First, let's divide the friends on a per-domain basis
            List<FriendInfo> locallst = new(friendList.Count);

            Dictionary<string, List<FriendInfo>> friendsPerDomain = new Dictionary<string, List<FriendInfo>>();
            foreach (FriendInfo friend in friendList)
            {
                if (UUID.TryParse(friend.Friend, out UUID friendID))
                {
                    if (LocalStatusNotification(userID, friendID, online))
                        continue;
                    locallst.Add(friend);
                }
                else
                {
                    // it's a foreign friend
                    if (Util.ParseUniversalUserIdentifier(friend.Friend, out friendID, out string url))
                    {
                        // Let's try our luck in the local sim. Who knows, maybe it's here
                        if (LocalStatusNotification(userID, friendID, online))
                            continue;

                        if (!friendsPerDomain.TryGetValue(url, out List<FriendInfo> lst))
                        {
                            lst = new List<FriendInfo>();
                            friendsPerDomain[url] = lst;
                        }
                        lst.Add(friend);
                    }
                }
            }

            // For the local friends, just call the base method
            // Let's do this first of all
            if (locallst.Count > 0)
                base.StatusNotify(locallst, userID, online);

            if(friendsPerDomain.Count > 0)
                m_StatusNotifier.Notify(userID, friendsPerDomain, online);

            //m_log.DebugFormat("[HGFRIENDS MODULE]: Exiting StatusNotify for {0}", userID);
        }

        protected override bool GetAgentInfo(UUID scopeID, string fid, out UUID agentID, out string first, out string last)
        {
            first = "Unknown"; last = "UserHGGAI";
            if (base.GetAgentInfo(scopeID, fid, out agentID, out first, out last))
                return true;

            // fid is not a UUID...
            if (Util.ParseFullUniversalUserIdentifier(fid, out agentID, out string url, out string f, out string l))
            {
                if (agentID.IsNotZero())
                {
                    m_uMan.AddUser(agentID, f, l, url);

                    string name = m_uMan.GetUserName(agentID);
                    string[] parts = name.Trim().Split();
                    if (parts.Length == 2)
                    {
                        first = parts[0];
                        last = parts[1];
                    }
                    else
                    {
                        first = f;
                        last = l;
                    }
                    return true;
                }
            }
            return false;
        }

        protected override string GetFriendshipRequesterName(UUID agentID)
        {
            return m_uMan.GetUserName(agentID);
        }

        protected override string FriendshipMessage(string friendID)
        {
            if (UUID.TryParse(friendID, out UUID _))
                return base.FriendshipMessage(friendID);

            return "Please confirm this friendship you made while you were on another HG grid";
        }

        protected override FriendInfo GetFriend(FriendInfo[] friends, UUID friendID)
        {
            if(friends.Length > 0)
            {
                string friendIDstr = friendID.ToString();
                foreach (FriendInfo fi in friends)
                {
                    if (fi.Friend.StartsWith(friendIDstr))
                        return fi;
                }
            }
            return null;
        }

        public override FriendInfo[] GetFriendsFromService(IClientAPI client)
        {
            //m_log.DebugFormat("[HGFRIENDS MODULE]: Entering GetFriendsFromService for {0}", client.Name);
            bool agentIsLocal = true;
            if (UserManagementModule is not null)
                agentIsLocal = UserManagementModule.IsLocalGridUser(client.AgentId);

            if (agentIsLocal)
                return base.GetFriendsFromService(client);

            // Foreigner
            AgentCircuitData agentClientCircuit = ((Scene)(client.Scene)).AuthenticateHandler.GetAgentCircuitData(client.CircuitCode);
            if (agentClientCircuit is not null)
            {
                // Note that this is calling a different interface than base; this one calls with a string param!
                FriendInfo[] finfos = FriendsService.GetFriends(client.AgentId.ToString());
                m_log.DebugFormat("[HGFRIENDS MODULE]: Fetched {0} local friends for visitor {1}", finfos.Length, client.AgentId.ToString());
                return finfos;
            }
            else
                return Array.Empty<FriendInfo>();
        }

        protected override bool StoreRights(UUID agentID, UUID friendID, int rights)
        {
            bool agentIsLocal = true;
            bool friendIsLocal = true;
            if (UserManagementModule != null)
            {
                agentIsLocal = UserManagementModule.IsLocalGridUser(agentID);
                friendIsLocal = UserManagementModule.IsLocalGridUser(friendID);
            }

            // Are they both local users?
            if (agentIsLocal && friendIsLocal)
            {
                // local grid users
                return base.StoreRights(agentID, friendID, rights);
            }

            if (agentIsLocal) // agent is local, friend is foreigner
            {
                FriendInfo[] finfos = GetFriendsFromCache(agentID);
                FriendInfo finfo = GetFriend(finfos, friendID);
                if (finfo != null)
                {
                    FriendsService.StoreFriend(agentID.ToString(), finfo.Friend, rights);
                    return true;
                }
            }

            if (friendIsLocal) // agent is foreigner, friend is local
            {
                string agentUUI = GetUUI(friendID, agentID);
                if (agentUUI != string.Empty)
                {
                    FriendsService.StoreFriend(agentUUI, friendID.ToString(), rights);
                    return true;
                }
            }

            return false;
        }

        protected override void StoreBackwards(UUID friendID, UUID agentID)
        {
            bool agentIsLocal = true;
            //bool friendIsLocal = true;

            if (UserManagementModule != null)
            {
                agentIsLocal = UserManagementModule.IsLocalGridUser(agentID);
                //friendIsLocal = UserManagementModule.IsLocalGridUser(friendID);
            }

            // Is the requester a local user?
            if (agentIsLocal)
            {
                // local grid users
                m_log.DebugFormat("[HGFRIENDS MODULE]: Friendship requester is local. Storing backwards.");

                base.StoreBackwards(friendID, agentID);
                return;
            }

            // no provision for this temporary friendship state when user is not local
            //FriendsService.StoreFriend(friendID.ToString(), agentID.ToString(), 0);
        }

        protected override void StoreFriendships(UUID agentID, UUID friendID)
        {
            bool agentIsLocal = true;
            bool friendIsLocal = true;
            if (UserManagementModule != null)
            {
                agentIsLocal = UserManagementModule.IsLocalGridUser(agentID);
                friendIsLocal = UserManagementModule.IsLocalGridUser(friendID);
            }

            // Are they both local users?
            if (agentIsLocal && friendIsLocal)
            {
                // local grid users
                m_log.DebugFormat("[HGFRIENDS MODULE]: Users are both local");
                DeletePreviousHGRelations(agentID, friendID);
                base.StoreFriendships(agentID, friendID);
                return;
            }

            // ok, at least one of them is foreigner, let's get their data
            IClientAPI agentClient = LocateClientObject(agentID);
            IClientAPI friendClient = LocateClientObject(friendID);
            AgentCircuitData agentClientCircuit = null;
            AgentCircuitData friendClientCircuit = null;
            string agentUUI = string.Empty;
            string friendUUI = string.Empty;
            string agentFriendService = string.Empty;
            string friendFriendService = string.Empty;

            if (agentClient is not null)
            {
                agentClientCircuit = ((Scene)(agentClient.Scene)).AuthenticateHandler.GetAgentCircuitData(agentClient.CircuitCode);
                agentUUI = Util.ProduceUserUniversalIdentifier(agentClientCircuit);
                agentFriendService = agentClientCircuit.ServiceURLs["FriendsServerURI"].ToString();
                RecacheFriends(agentClient);
            }
            if (friendClient is not null)
            {
                friendClientCircuit = ((Scene)(friendClient.Scene)).AuthenticateHandler.GetAgentCircuitData(friendClient.CircuitCode);
                friendUUI = Util.ProduceUserUniversalIdentifier(friendClientCircuit);
                friendFriendService = friendClientCircuit.ServiceURLs["FriendsServerURI"].ToString();
                RecacheFriends(friendClient);
            }

            m_log.DebugFormat("[HGFRIENDS MODULE] HG Friendship! thisUUI={0}; friendUUI={1}; foreignThisFriendService={2}; foreignFriendFriendService={3}",
                    agentUUI, friendUUI, agentFriendService, friendFriendService);

            // Generate a random 8-character hex number that will sign this friendship
            string secret = UUID.Random().ToString().Substring(0, 8);

            string theFriendUUID = friendUUI + ";" + secret;
            string agentUUID = agentUUI + ";" + secret;

            if (agentIsLocal) // agent is local, 'friend' is foreigner
            {
                // This may happen when the agent returned home, in which case the friend is not there
                // We need to look for its information in the friends list itself
                FriendInfo[] finfos = null;
                bool confirming = false;
                if (friendUUI.Length == 0)
                {
                    finfos = GetFriendsFromCache(agentID);
                    foreach (FriendInfo finfo in finfos)
                    {
                        if (finfo.TheirFlags == -1)
                        {
                            if (finfo.Friend.StartsWith(friendID.ToString()))
                            {
                                friendUUI = finfo.Friend;
                                theFriendUUID = friendUUI;

                                // If it's confirming the friendship, we already have the full UUI with the secret
                                if (Util.ParseFullUniversalUserIdentifier(theFriendUUID, out UUID utmp, out string url,
                                            out string first, out string last))
                                {
                                    agentUUID = agentUUI + ";" + secret;
                                    m_uMan.AddUser(utmp, first, last, url);
                                }
                                confirming = true;
                                break;
                            }
                        }
                    }
                    if (!confirming)
                    {
                        friendUUI = m_uMan.GetUserUUI(friendID);
                        theFriendUUID = friendUUI + ";" + secret;
                    }

                    friendFriendService = m_uMan.GetUserServerURL(friendID, "FriendsServerURI");

                    //m_log.DebugFormat("[HGFRIENDS MODULE] HG Friendship! thisUUI={0}; friendUUI={1}; foreignThisFriendService={2}; foreignFriendFriendService={3}",
                    //    agentUUI, friendUUI, agentFriendService, friendFriendService);

                }

                // Delete any previous friendship relations
                DeletePreviousRelations(agentID, friendID);

                // store in the local friends service a reference to the foreign friend
                FriendsService.StoreFriend(agentID.ToString(), theFriendUUID, 1);
                // and also the converse
                FriendsService.StoreFriend(theFriendUUID, agentID.ToString(), 1);

                //if (!confirming)
                //{
                    // store in the foreign friends service a reference to the local agent
                    HGFriendsServicesConnector friendsConn = null;
                    if (friendClientCircuit != null) // the friend is here, validate session
                        friendsConn = new HGFriendsServicesConnector(friendFriendService, friendClientCircuit.SessionID, friendClientCircuit.ServiceSessionID);
                    else // the friend is not here, he initiated the request in his home world
                        friendsConn = new HGFriendsServicesConnector(friendFriendService);

                    friendsConn.NewFriendship(friendID, agentUUID);
                //}
            }
            else if (friendIsLocal) // 'friend' is local,  agent is foreigner
            {
                // Delete any previous friendship relations
                DeletePreviousRelations(agentID, friendID);

                // store in the local friends service a reference to the foreign agent
                FriendsService.StoreFriend(friendID.ToString(), agentUUI + ";" + secret, 1);
                // and also the converse
                FriendsService.StoreFriend(agentUUI + ";" + secret, friendID.ToString(), 1);

                if (agentClientCircuit is not null)
                {
                    // store in the foreign friends service a reference to the local agent
                    HGFriendsServicesConnector friendsConn = new HGFriendsServicesConnector(agentFriendService, agentClientCircuit.SessionID, agentClientCircuit.ServiceSessionID);
                    friendsConn.NewFriendship(agentID, friendUUI + ";" + secret);
                }
            }
            else // They're both foreigners!
            {
                HGFriendsServicesConnector friendsConn;
                if (agentClientCircuit is not null)
                {
                    friendsConn = new HGFriendsServicesConnector(agentFriendService, agentClientCircuit.SessionID, agentClientCircuit.ServiceSessionID);
                    friendsConn.NewFriendship(agentID, friendUUI + ";" + secret);
                }
                if (friendClientCircuit is not null)
                {
                    friendsConn = new HGFriendsServicesConnector(friendFriendService, friendClientCircuit.SessionID, friendClientCircuit.ServiceSessionID);
                    friendsConn.NewFriendship(friendID, agentUUI + ";" + secret);
                }
            }
            // my brain hurts now
        }

        private void DeletePreviousRelations(UUID a1, UUID a2)
        {
            // Delete any previous friendship relations
            FriendInfo f;
            FriendInfo[] finfos = GetFriendsFromCache(a1);
            if (finfos is not null)
            {
                f = GetFriend(finfos, a2);
                if (f is not null)
                {
                    FriendsService.Delete(a1, f.Friend);
                    // and also the converse
                    FriendsService.Delete(f.Friend, a1.ToString());
                }
            }

            finfos = GetFriendsFromCache(a2);
            if (finfos is not null)
            {
                f = GetFriend(finfos, a1);
                if (f is not null)
                {
                    FriendsService.Delete(a2, f.Friend);
                    // and also the converse
                    FriendsService.Delete(f.Friend, a2.ToString());
                }
            }
        }

        private void DeletePreviousHGRelations(UUID a1, UUID a2)
        {
            // Delete any previous friendship relations
            FriendInfo[] finfos = GetFriendsFromCache(a1);
            if (finfos is not null)
            {
                string a1str = a1.ToString();
                string a2str = a2.ToString();
                foreach (FriendInfo f in finfos)
                {
                    if (f.TheirFlags == -1)
                    {
                        if (f.Friend.StartsWith(a2str))
                        {
                            FriendsService.Delete(a1, f.Friend);
                            // and also the converse
                            FriendsService.Delete(f.Friend, a1str);
                        }
                    }
                }
            }

            finfos = GetFriendsFromCache(a1);
            if (finfos is not null)
            {
                string a1str2 = a1.ToString();
                string a2str2 = a2.ToString();
                foreach (FriendInfo f in finfos)
                {
                    if (f.TheirFlags == -1)
                    {
                        if (f.Friend.StartsWith(a1str2))
                        {
                            FriendsService.Delete(a2, f.Friend);
                            // and also the converse
                            FriendsService.Delete(f.Friend, a2str2);
                        }
                    }
                }
            }
        }

        protected override bool DeleteFriendship(UUID agentID, UUID exfriendID)
        {
            Boolean agentIsLocal = true;
            Boolean friendIsLocal = true;
            if (UserManagementModule != null)
            {
                agentIsLocal = UserManagementModule.IsLocalGridUser(agentID);
                friendIsLocal = UserManagementModule.IsLocalGridUser(exfriendID);
            }

            // Are they both local users?
            if (agentIsLocal && friendIsLocal)
            {
                // local grid users
                return base.DeleteFriendship(agentID, exfriendID);
            }

            // ok, at least one of them is foreigner, let's get their data
            string agentUUI = string.Empty;
            string friendUUI = string.Empty;

            if (agentIsLocal) // agent is local, 'friend' is foreigner
            {
                // We need to look for its information in the friends list itself
                FriendInfo[] finfos = GetFriendsFromCache(agentID);
                FriendInfo finfo = GetFriend(finfos, exfriendID);
                if (finfo != null)
                {
                    friendUUI = finfo.Friend;

                    // delete in the local friends service the reference to the foreign friend
                    FriendsService.Delete(agentID, friendUUI);
                    // and also the converse
                    FriendsService.Delete(friendUUI, agentID.ToString());

                    // notify the exfriend's service
                    Util.FireAndForget(
                        delegate { Delete(exfriendID, agentID, friendUUI); }, null, "HGFriendsModule.DeleteFriendshipForeignFriend");

                    m_log.DebugFormat("[HGFRIENDS MODULE]: {0} terminated {1}", agentID, friendUUI);
                    return true;
                }
            }
            else if (friendIsLocal) // agent is foreigner, 'friend' is local
            {
                agentUUI = GetUUI(exfriendID, agentID);

                if (agentUUI != string.Empty)
                {
                    // delete in the local friends service the reference to the foreign agent
                    FriendsService.Delete(exfriendID, agentUUI);
                    // and also the converse
                    FriendsService.Delete(agentUUI, exfriendID.ToString());

                    // notify the agent's service?
                    Util.FireAndForget(
                        delegate { Delete(agentID, exfriendID, agentUUI); }, null, "HGFriendsModule.DeleteFriendshipLocalFriend");

                    m_log.DebugFormat("[HGFRIENDS MODULE]: {0} terminated {1}", agentUUI, exfriendID);
                    return true;
                }
            }
            //else They're both foreigners! Can't handle this

            return false;
        }

        private string GetUUI(UUID localUser, UUID foreignUser)
        {
            // Let's see if the user is here by any chance
            FriendInfo[] finfos = GetFriendsFromCache(localUser);
            if (finfos != EMPTY_FRIENDS) // friend is here, cool
            {
                FriendInfo finfo = GetFriend(finfos, foreignUser);
                if (finfo != null)
                {
                    return finfo.Friend;
                }
            }
            else // user is not currently on this sim, need to get from the service
            {
                finfos = FriendsService.GetFriends(localUser);
                foreach (FriendInfo finfo in finfos)
                {
                    if (finfo.Friend.StartsWith(foreignUser.ToString())) // found it!
                    {
                        return finfo.Friend;
                    }
                }
            }
            return string.Empty;
        }

        private void Delete(UUID foreignUser, UUID localUser, string uui)
        {
            if (Util.ParseFullUniversalUserIdentifier(uui, out UUID _, out string _, out string _, out string url, out string secret))
            {
                m_log.DebugFormat("[HGFRIENDS MODULE]: Deleting friendship from {0}", url);
                HGFriendsServicesConnector friendConn = new HGFriendsServicesConnector(url);
                friendConn.DeleteFriendship(foreignUser, localUser, secret);
            }
        }

        protected override bool ForwardFriendshipOffer(UUID agentID, UUID friendID, GridInstantMessage im)
        {
            if (base.ForwardFriendshipOffer(agentID, friendID, im))
                return true;

            // OK, that didn't work, so let's try to find this user somewhere
            if (!m_uMan.IsLocalGridUser(friendID))
            {
                string friendsURL = m_uMan.GetUserServerURL(friendID, "FriendsServerURI");
                if (!string.IsNullOrEmpty(friendsURL))
                {
                    m_log.DebugFormat("[HGFRIENDS MODULE]: Forwading friendship from {0} to {1} @ {2}", agentID, friendID, friendsURL);
                    GridRegion region = new GridRegion();
                    region.ServerURI = friendsURL;

                    string name = im.fromAgentName;
                    if (m_uMan.IsLocalGridUser(agentID))
                    {
                        IClientAPI agentClient = LocateClientObject(agentID);
                        AgentCircuitData agentClientCircuit = ((Scene)(agentClient.Scene)).AuthenticateHandler.GetAgentCircuitData(agentClient.CircuitCode);
                        string agentHomeService = string.Empty;
                        try
                        {
                            agentHomeService = agentClientCircuit.ServiceURLs["HomeURI"].ToString();
                            string lastname = "@" + new Uri(agentHomeService).Authority;
                            string firstname = im.fromAgentName.Replace(" ", ".");
                            name = firstname + lastname;
                        }
                        catch (KeyNotFoundException)
                        {
                            m_log.DebugFormat("[HGFRIENDS MODULE]: Key HomeURI not found for user {0}", agentID);
                            return false;
                        }
                        catch (NullReferenceException)
                        {
                            m_log.DebugFormat("[HGFRIENDS MODULE]: Null HomeUri for local user {0}", agentID);
                            return false;
                        }
                        catch (UriFormatException)
                        {
                            m_log.DebugFormat("[HGFRIENDS MODULE]: Malformed HomeUri {0} for local user {1}", agentHomeService, agentID);
                            return false;
                        }
                    }

                    m_HGFriendsConnector.FriendshipOffered(region, agentID, friendID, im.message, name);

                    return true;
                }
            }

            return false;
        }

        public override bool LocalFriendshipOffered(UUID toID, GridInstantMessage im)
        {
            if (base.LocalFriendshipOffered(toID, im))
            {
                if (im.fromAgentName.Contains("@"))
                {
                    string[] parts = im.fromAgentName.Split(new char[] { '@' });
                    if (parts.Length == 2)
                    {
                        string[] fl = parts[0].Trim().Split(Util.SplitDotArray);
                        if (fl.Length == 2)
                            m_uMan.AddUser(new UUID(im.fromAgentID), fl[0], fl[1], "http://" + parts[1]);
                        else
                            m_uMan.AddUser(new UUID(im.fromAgentID), fl[0], "", "http://" + parts[1]);
                    }
                }
                return true;
            }
            return false;
        }
    }
}
