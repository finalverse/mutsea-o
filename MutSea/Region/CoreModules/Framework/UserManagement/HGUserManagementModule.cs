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
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using MutSea.Framework;
using MutSea.Framework.Console;
using MutSea.Region.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Services.Interfaces;
using MutSea.Services.Connectors.Hypergrid;

using OpenMetaverse;
using OpenMetaverse.Packets;
using log4net;
using Nini.Config;
using Mono.Addins;

namespace MutSea.Region.CoreModules.Framework.UserManagement
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "HGUserManagementModule")]
    public class HGUserManagementModule : UserManagementModule, ISharedRegionModule, IUserManagement
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region ISharedRegionModule

        public override void Initialise(IConfigSource config)
        {
            string umanmod = config.Configs["Modules"].GetString("UserManagementModule", null);
            if (umanmod == Name)
            {
                m_Enabled = true;
                base.Init(config);
                m_log.DebugFormat("[USER MANAGEMENT MODULE]: {0} is enabled", Name);
            }
        }

        public override string Name
        {
            get { return "HGUserManagementModule"; }
        }

        #endregion ISharedRegionModule

        protected override void AddAdditionalUsers(string query, List<UserData> users, HashSet<UUID> found)
        {
            if (query.Contains("@"))  // First.Last@foo.com, maybe?
            {
                string[] words = query.Split(new char[] { '@' });
                if (words.Length != 2)
                {
                    m_log.DebugFormat("[USER MANAGEMENT MODULE]: Malformed address {0}", query);
                    return;
                }

                words[0] = words[0].Trim(); // it has at least 1
                words[1] = words[1].Trim().ToLower();
                string match1 = "@" + words[1];
                if (String.IsNullOrWhiteSpace(words[0])) // query was @foo.com?
                {
                    foreach (UserData d in m_userCacheByID.Values)
                    {
                        if(found.Contains(d.Id))
                            continue;
                        if (d.LastName.ToLower().StartsWith(match1))
                            users.Add(d);
                    }

                    // We're done
                    return;
                }

                string match0 = words[0].ToLower();
                // words.Length == 2 and words[0] != string.empty
                // first.last@foo.com ?
                foreach (UserData d in m_userCacheByID.Values)
                {
                    if (found.Contains(d.Id))
                        continue;
                    if (d.LastName.ToLower().Equals(match1) &&
                        d.FirstName.ToLower().Equals(match0))
                    {
                        users.Add(d);
                        // It's cached. We're done
                        return;
                    }
                }

                // This is it! Let's ask the other world
                if (words[0].Contains("."))
                {
                    string[] names = words[0].Split(Util.SplitDotArray);
                    if (names.Length >= 2)
                    {
                        string uriStr = "http://" + words[1];
                        // Let's check that the last name is a valid address
                        try
                        {
                            new Uri(uriStr);
                        }
                        catch (UriFormatException)
                        {
                            m_log.DebugFormat("[USER MANAGEMENT MODULE]: Malformed address {0}", uriStr);
                            return;
                        }

                        UUID userID = UUID.Zero;
                        uriStr = uriStr.ToLower();
                        if(!WebUtil.GlobalExpiringBadURLs.ContainsKey(uriStr))
                        {
                            UserAgentServiceConnector uasConn = new UserAgentServiceConnector(uriStr);
                            try
                            {
                                userID = uasConn.GetUUID(names[0], names[1]);
                            }
                            catch (Exception e)
                            {
                                m_log.Debug("[USER MANAGEMENT MODULE]: GetUUID call failed ", e);
                            }
                        }

                        if (!userID.Equals(UUID.Zero))
                        {
                            UserData ud = new UserData();
                            ud.Id = userID;
                            ud.FirstName = words[0];
                            ud.LastName = "@" + words[1];
                            users.Add(ud);
                            AddUser(userID, names[0], names[1], uriStr);
                            m_log.DebugFormat("[USER MANAGEMENT MODULE]: User {0}@{1} found", words[0], words[1]);
                        }
                        else
                            m_log.DebugFormat("[USER MANAGEMENT MODULE]: User {0}@{1} not found", words[0], words[1]);
                    }
                }
            }
        }
    }
}
