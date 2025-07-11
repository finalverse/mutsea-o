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
using System.Data;
using System.Reflection;
using System.Threading;
using log4net;
using OpenMetaverse;
using MutSea.Framework;
using MySql.Data.MySqlClient;

namespace MutSea.Data.MySQL
{
    /// <summary>
    /// A MySQL Interface for the Grid Server
    /// </summary>
    public class MySQLPresenceData : MySQLGenericTableHandler<PresenceData>,
            IPresenceData
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public MySQLPresenceData(string connectionString, string realm) :
                base(connectionString, realm, "Presence")
        {
        }

        public PresenceData Get(UUID sessionID)
        {
            PresenceData[] ret = Get("SessionID",
                    sessionID.ToString());

            if (ret.Length == 0)
                return null;

            return ret[0];
        }

        public void LogoutRegionAgents(UUID regionID)
        {
            using (MySqlCommand cmd = new MySqlCommand())
            {
                cmd.CommandText = String.Format("delete from {0} where `RegionID`=?RegionID", m_Realm);

                cmd.Parameters.AddWithValue("?RegionID", regionID.ToString());

                ExecuteNonQuery(cmd);
            }
        }

        public bool ReportAgent(UUID sessionID, UUID regionID)
        {
            PresenceData[] pd = Get("SessionID", sessionID.ToString());
            if (pd.Length == 0)
                return false;

            if (regionID.IsZero())
                return false;

            using (MySqlCommand cmd = new MySqlCommand())
            {
                cmd.CommandText = String.Format("update {0} set RegionID=?RegionID, LastSeen=NOW() where `SessionID`=?SessionID", m_Realm);

                cmd.Parameters.AddWithValue("?SessionID", sessionID.ToString());
                cmd.Parameters.AddWithValue("?RegionID", regionID.ToString());

                if (ExecuteNonQuery(cmd) == 0)
                    return false;
            }

            return true;
        }

        public bool VerifyAgent(UUID agentId, UUID secureSessionID)
        {
            PresenceData[] ret = Get("SecureSessionID",
                    secureSessionID.ToString());

            if (ret.Length == 0)
                return false;

            if(ret[0].UserID != agentId.ToString())
                return false;

            return true;
        }
    }
}