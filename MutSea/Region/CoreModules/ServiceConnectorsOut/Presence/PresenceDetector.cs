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
using System.Reflection;
using log4net;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Services.Interfaces;

namespace MutSea.Region.CoreModules.ServiceConnectorsOut.Presence
{
    public class PresenceDetector
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IPresenceService m_PresenceService;
        private Scene m_aScene;

        public PresenceDetector(IPresenceService presenceservice)
        {
            m_PresenceService = presenceservice;
        }

        public void AddRegion(Scene scene)
        {
            scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;

            m_PresenceService.LogoutRegionAgents(scene.RegionInfo.RegionID);

            if (m_aScene == null)
                m_aScene = scene;
        }

        public void RemoveRegion(Scene scene)
        {
            scene.EventManager.OnMakeRootAgent -= OnMakeRootAgent;

            m_PresenceService.LogoutRegionAgents(scene.RegionInfo.RegionID);
        }

        public void OnMakeRootAgent(ScenePresence sp)
        {
            if (sp.IsNPC)
                return;

            sp.ControllingClient.OnConnectionClosed += OnConnectionClose;

            if (sp.m_gotCrossUpdate)
            {
                Util.FireAndForget(delegate
                {
                    DoOnMakeRootAgent(sp);
                }, null, "PresenceDetector_MakeRoot");
            }
            else
                DoOnMakeRootAgent(sp);
        }

        public void DoOnMakeRootAgent(ScenePresence sp)
        {
//            m_log.DebugFormat("[PRESENCE DETECTOR]: Detected root presence {0} in {1}", sp.UUID, sp.Scene.RegionInfo.RegionName);
            if (sp.PresenceType != PresenceType.Npc)
                m_PresenceService.ReportAgent(sp.ControllingClient.SessionId, sp.Scene.RegionInfo.RegionID);
        }

        public void OnConnectionClose(IClientAPI client)
        {
            if (client != null && client.SceneAgent != null && !client.SceneAgent.IsChildAgent)
            {
//                m_log.DebugFormat("[PRESENCE DETECTOR]: Detected client logout {0} in {1}", client.AgentId, client.Scene.RegionInfo.RegionName);
                m_PresenceService.LogoutAgent(client.SessionId);
            }
        }
    }
}