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

using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nini.Config;
using MutSea.Framework;
using MutSea.Framework.ServiceAuth;
using MutSea.Services.Interfaces;
using GridRegion = MutSea.Services.Interfaces.GridRegion;
using IAvatarService = MutSea.Services.Interfaces.IAvatarService;
using MutSea.Server.Base;
using OpenMetaverse;

namespace MutSea.Services.Connectors
{
    public class AgentPreferencesServicesConnector : BaseServiceConnector, IAgentPreferencesService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string m_ServerURI = String.Empty;

        public AgentPreferencesServicesConnector()
        {
        }

        public AgentPreferencesServicesConnector(string serverURI)
        {
            m_ServerURI = serverURI.TrimEnd('/');
        }

        public AgentPreferencesServicesConnector(IConfigSource source)
            : base(source, "AgentPreferencesService")
        {
            Initialise(source);
        }

        public void Initialise(IConfigSource source)
        {
            IConfig gridConfig = source.Configs["AgentPreferencesService"];
            if (gridConfig == null)
            {
                m_log.Error("[AGENT PREFERENCES CONNECTOR]: AgentPreferencesService missing from MutSea.ini");
                throw new Exception("Agent Preferences connector init error");
            }

            string serviceURI = gridConfig.GetString("AgentPreferencesServerURI", string.Empty);

            if (string.IsNullOrEmpty(serviceURI))
            {
                m_log.Error("[AGENT PREFERENCES CONNECTOR]: No Server URI named in section AgentPreferences");
                throw new Exception("Agent Preferences connector init error");
            }
            m_ServerURI = serviceURI;

            base.Initialise(source, "AgentPreferencesService");
        }

        #region IAgentPreferencesService

        public AgentPrefs GetAgentPreferences(UUID principalID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            string reply = string.Empty;
            string uri = String.Concat(m_ServerURI, "/agentprefs");

            sendData["METHOD"] = "getagentprefs";
            sendData["UserID"] = principalID;
            string reqString = ServerUtils.BuildQueryString(sendData);
            // m_log.DebugFormat("[AGENT PREFS CONNECTOR]: queryString = {0}", reqString);

            try
            {
                reply = SynchronousRestFormsRequester.MakeRequest("POST", uri, reqString, m_Auth);
                if (string.IsNullOrEmpty(reply))
                {
                    m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetAgentPreferences received null or empty reply");
                    return null;
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: Exception when contacting agent preferences server at {0}: {1}", uri, e.Message);
            }

            Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);
            if (replyData != null)
            {
                if (replyData.ContainsKey("result") && 
                    (replyData["result"].ToString() == "null" || replyData["result"].ToString() == "Failure"))
                {
                    m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetAgentPreferences received Failure response");
                    return null;
                }
            }
            else
            {
                m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetAgentPreferences received null response");
                return null;
            }
            AgentPrefs prefs = new AgentPrefs(replyData);
            return prefs;
        }

        public bool StoreAgentPreferences(AgentPrefs data)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();

            sendData["METHOD"] = "setagentprefs";

            sendData["PrincipalID"] = data.PrincipalID.ToString();
            sendData["AccessPrefs"] = data.AccessPrefs;
            sendData["HoverHeight"] = data.HoverHeight.ToString();
            sendData["Language"] = data.Language;
            sendData["LanguageIsPublic"] = data.LanguageIsPublic.ToString();
            sendData["PermEveryone"] = data.PermEveryone.ToString();
            sendData["PermGroup"] = data.PermGroup.ToString();
            sendData["PermNextOwner"] = data.PermNextOwner.ToString();

            string uri = string.Concat(m_ServerURI, "/agentprefs");
            string reqString = ServerUtils.BuildQueryString(sendData);
            // m_log.DebugFormat("[AGENT PREFS CONNECTOR]: queryString = {0}", reqString);

            try
            {
                string reply = SynchronousRestFormsRequester.MakeRequest("POST", uri, reqString, m_Auth);
                if (reply != string.Empty)
                {
                    int indx = reply.IndexOf("success", StringComparison.InvariantCultureIgnoreCase);
                    if (indx > 0)
                        return true;
                    return false;
                }
                else
                    m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: StoreAgentPreferences received empty reply");
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: Exception when contacting agent preferences server at {0}: {1}", uri, e.Message);
            }

            return false;
        }

        public string GetLang(UUID principalID)
        {
            Dictionary<string, object> sendData = new Dictionary<string, object>();
            string reply = string.Empty;

            sendData["METHOD"] = "getagentlang";
            sendData["UserID"] = principalID.ToString();

            string uri = string.Concat(m_ServerURI, "/agentprefs");
            string reqString = ServerUtils.BuildQueryString(sendData);

            try
            {
                reply = SynchronousRestFormsRequester.MakeRequest("POST", uri, reqString, m_Auth);
                if (string.IsNullOrEmpty(reply))
                {
                    m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetLang received null or empty reply");
                    return "en-us"; // I guess? Gotta return somethin'!
                }
            }
            catch (Exception e)
            {
                m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: Exception when contacting agent preferences server at {0}: {1}", uri, e.Message);
            }

            Dictionary<string, object> replyData = ServerUtils.ParseXmlResponse(reply);
            if (replyData != null)
            {
                if (replyData.ContainsKey("result") &&
                    (replyData["result"].ToString() == "null" || replyData["result"].ToString() == "Failure"))
                {
                    m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetLang received Failure response");
                    return "en-us";
                }
                if (replyData.ContainsKey("Language"))
                    return replyData["Language"].ToString();
            }
            else
            {
                m_log.DebugFormat("[AGENT PREFERENCES CONNECTOR]: GetLang received null response");

            }
            return "en-us";
        }

        #endregion IAgentPreferencesService
    }
}
