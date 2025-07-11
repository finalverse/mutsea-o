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
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using Mono.Addins;
using MutSea.Framework;
using MutSea.Region.CoreModules.World.WorldMap;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;


namespace MutSea.Region.CoreModules.Hypergrid
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "HGWorldMapModule")]
    public class HGWorldMapModule : WorldMapModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // Remember the map area that each client has been exposed to in this region
        private Dictionary<UUID, List<MapBlockData>> m_SeenMapBlocks = new Dictionary<UUID, List<MapBlockData>>();

        private string m_MapImageServerURL = string.Empty;

        private IUserManagement m_UserManagement;

        #region INonSharedRegionModule Members

        public override void Initialise(IConfigSource source)
        {
            string[] configSections = new string[] { "Map", "Startup" };
            if (Util.GetConfigVarFromSections<string>(
                source, "WorldMapModule", configSections, "WorldMap") == "HGWorldMap")
            {
                m_Enabled = true;

                m_MapImageServerURL = Util.GetConfigVarFromSections<string>(source, "MapTileURL", new string[] {"LoginService", "HGWorldMap", "SimulatorFeatures"});

                if (!string.IsNullOrEmpty(m_MapImageServerURL))
                {
                    m_MapImageServerURL = m_MapImageServerURL.Trim();
                    if (!m_MapImageServerURL.EndsWith("/"))
                        m_MapImageServerURL = m_MapImageServerURL + "/";
                }

                expireBlackListTime = (int)Util.GetConfigVarFromSections<int>(source, "BlacklistTimeout", configSections, 10 * 60);
                expireBlackListTime *= 1000;
                m_exportPrintScale =
                    Util.GetConfigVarFromSections<bool>(source, "ExportMapAddScale", configSections, m_exportPrintScale);
                m_exportPrintRegionName =
                    Util.GetConfigVarFromSections<bool>(source, "ExportMapAddRegionName", configSections, m_exportPrintRegionName);
                m_localV1MapAssets =
                    Util.GetConfigVarFromSections<bool>(source, "LocalV1MapAssets", configSections, m_localV1MapAssets);
            }
        }

        public override void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            base.AddRegion(scene);

            scene.EventManager.OnClientClosed += EventManager_OnClientClosed;
        }

        public override void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            base.RegionLoaded(scene);
            ISimulatorFeaturesModule featuresModule = m_scene.RequestModuleInterface<ISimulatorFeaturesModule>();

            if (featuresModule != null)
                featuresModule.OnSimulatorFeaturesRequest += OnSimulatorFeaturesRequest;

            m_UserManagement = m_scene.RequestModuleInterface<IUserManagement>();

        }

        public override void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            base.RemoveRegion(scene);

            scene.EventManager.OnClientClosed -= EventManager_OnClientClosed;
        }

        public override string Name
        {
            get { return "HGWorldMap"; }
        }

        #endregion

        void EventManager_OnClientClosed(UUID clientID, Scene scene)
        {
            ScenePresence sp = scene.GetScenePresence(clientID);
            if (sp != null)
            {
                if (m_SeenMapBlocks.ContainsKey(clientID))
                {
                    List<MapBlockData> mapBlocks = m_SeenMapBlocks[clientID];
                    foreach (MapBlockData b in mapBlocks)
                    {
                        b.Name = string.Empty;
                        // Set 'simulator is offline'. We need this because the viewer ignores SimAccess.Unknown (255)
                        b.Access = (byte)SimAccess.Down;
                    }

                    m_log.DebugFormat("[HG MAP]: Resetting {0} blocks", mapBlocks.Count);
                    sp.ControllingClient.SendMapBlock(mapBlocks, 0);
                    m_SeenMapBlocks.Remove(clientID);
                }
            }
        }

        protected override List<MapBlockData> GetAndSendBlocksInternal(IClientAPI remoteClient, int minX, int minY, int maxX, int maxY, uint flag)
        {
            List<MapBlockData>  mapBlocks = base.GetAndSendBlocksInternal(remoteClient, minX, minY, maxX, maxY, flag);
            if(mapBlocks.Count > 0)
            {
                lock (m_SeenMapBlocks)
                {
                    if (!m_SeenMapBlocks.ContainsKey(remoteClient.AgentId))
                    {
                        m_SeenMapBlocks.Add(remoteClient.AgentId, mapBlocks);
                    }
                    else
                    {
                        List<MapBlockData> seen = m_SeenMapBlocks[remoteClient.AgentId];
                        List<MapBlockData> newBlocks = new List<MapBlockData>();
                        foreach (MapBlockData b in mapBlocks)
                            if (seen.Find(delegate(MapBlockData bdata) { return bdata.X == b.X && bdata.Y == b.Y; }) == null)
                                newBlocks.Add(b);
                        seen.AddRange(newBlocks);
                    }
                }
            }
            return mapBlocks;
        }

        private void OnSimulatorFeaturesRequest(UUID agentID, ref OSDMap features)
        {
            if (m_UserManagement != null && !string.IsNullOrEmpty(m_MapImageServerURL) && !m_UserManagement.IsLocalGridUser(agentID))
            {
                if (!features.TryGetValue("MutSeaExtras", out OSD extras))
                {
                    extras = new OSDMap();
                    features["MutSeaExtras"] = extras;
                }

                ((OSDMap)extras)["map-server-url"] = m_MapImageServerURL;

            }
        }
    }
}
