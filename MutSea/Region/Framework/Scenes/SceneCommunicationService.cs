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
using System.Net;
using System.Reflection;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using log4net;
using MutSea.Framework;
using MutSea.Framework.Client;
using MutSea.Framework.Capabilities;
using MutSea.Region.Framework.Interfaces;
using MutSea.Services.Interfaces;
using OSD = OpenMetaverse.StructuredData.OSD;
using GridRegion = MutSea.Services.Interfaces.GridRegion;

namespace MutSea.Region.Framework.Scenes
{
    public delegate void RemoveKnownRegionsFromAvatarList(UUID avatarID, List<ulong> regionlst);

    /// <summary>
    /// Class that Region communications runs through
    /// </summary>
    public class SceneCommunicationService //one instance per region
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[SCENE COMM]";

        protected RegionInfo m_regionInfo;
        protected Scene m_scene;

        public void SetScene(Scene s)
        {
            m_scene = s;
            m_regionInfo = s.RegionInfo;
        }

        public void InformNeighborsThatRegionisUp(INeighbourService neighbourService, RegionInfo region)
        {
            //m_log.Info("[INTER]: " + debugRegionName + ": SceneCommunicationService: Sending InterRegion Notification that region is up " + region.RegionName);
            if (neighbourService == null)
            {
                m_log.ErrorFormat("{0} No neighbour service provided for region {1} to inform neigbhours of status", LogHeader, m_scene.Name);
                return;
            }

            List<GridRegion> neighbours
                = m_scene.GridService.GetNeighbours(m_scene.RegionInfo.ScopeID, m_scene.RegionInfo.RegionID);

            List<ulong> onlineNeighbours = new List<ulong>();

            foreach (GridRegion n in neighbours)
            {
                //m_log.DebugFormat(
                //   "{0}: Region flags for {1} as seen by {2} are {3}",
                //    LogHeader, n.RegionName, m_scene.Name, regionFlags != null ? regionFlags.ToString() : "not present");

                // Robust services before 2015-01-14 do not return the regionFlags information.  In this case, we could
                // make a separate RegionFlags call but this would involve a network call for each neighbour.
                if (n.RegionFlags != null)
                {
                    if ((n.RegionFlags & MutSea.Framework.RegionFlags.RegionOnline) != 0)
                        onlineNeighbours.Add(n.RegionHandle);
                }
                else
                {
                    onlineNeighbours.Add(n.RegionHandle);
                }
            }

            if(onlineNeighbours.Count > 0)
            {
                Util.FireAndForget(o =>
                {
                    foreach (ulong regionhandle in onlineNeighbours)
                    {
                        Util.RegionHandleToRegionLoc(regionhandle, out uint rx, out uint ry);
                        GridRegion neighbour = neighbourService.HelloNeighbour(regionhandle, region);
                        if (neighbour != null)
                        {
                            m_log.DebugFormat("{0} Region {1} successfully informed neighbour {2} at {3}-{4} that it is up",
                                LogHeader, m_scene.Name, neighbour.RegionName, rx, ry);

                            m_scene.EventManager.TriggerOnRegionUp(neighbour);
                        }
                        else
                        {
                            m_log.WarnFormat("{0} Region {1} failed to inform neighbour at {2}-{3} that it is up.",
                                LogHeader, m_scene.Name, rx, ry);
                        }
                    }
                });
            }
        }

        /// <summary>
        /// This informs all neighboring regions about the settings of it's child agent.
        /// This contains information, such as, Draw Distance, Camera location, Current Position, Current throttle settings, etc.
        /// </summary>

        public void SendChildAgentDataUpdate(AgentPosition cAgentData, ScenePresence presence)
        {
            //m_log.DebugFormat(
            //   "[SCENE COMMUNICATION SERVICE]: Sending child agent position updates for {0} in {1}",
            //   presence.Name, m_scene.Name);

            // This assumes that we know what our neighbors are.
            try
            {
                List<string> simulatorList = new List<string>();
                foreach (ulong regionHandle in presence.KnownRegionHandles)
                {
                    if (regionHandle != m_regionInfo.RegionHandle)
                    {
                        // we only want to send one update to each simulator; the simulator will
                        // hand it off to the regions where a child agent exists, this does assume
                        // that the region position is cached or performance will degrade
                        GridRegion dest = m_scene.GridService.GetRegionByHandle(UUID.Zero, regionHandle);
                        if (dest == null)
                            continue;

                        if (!simulatorList.Contains(dest.ServerURI))
                        {
                            // we havent seen this simulator before, add it to the list
                            // and send it an update
                            simulatorList.Add(dest.ServerURI);
                            m_scene.SimulationService.UpdateAgent(dest, cAgentData);
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // We're ignoring a collection was modified error because this data gets old and outdated fast.
            }
        }

        /// <summary>
        /// Closes a child agents in a collection of regions. Does so asynchronously
        /// so that the caller doesn't wait.
        /// </summary>
        /// <param name="agentID"></param>
        /// <param name="regionslst"></param>
        public void SendCloseChildAgentConnections(UUID agentID, string auth_code, List<ulong> regionslst)
        {
            if (regionslst.Count == 0)
                return;

            // use a single thread job for all
            Util.FireAndForget(o =>
            {
                foreach (ulong regionHandle in regionslst)
                {
                    // let's do our best, but there's not much we can do if the neighbour doesn't accept.
                    GridRegion destination = m_scene.GridService.GetRegionByHandle(m_regionInfo.ScopeID, regionHandle);
                    if (destination == null)
                    {
                        m_log.DebugFormat(
                            "[SCENE COMMUNICATION SERVICE]: Sending close agent ID {0} FAIL, region with handle {1} not found", agentID, regionHandle);
                        return;
                    }

                    m_log.DebugFormat(
                        "[SCENE COMMUNICATION SERVICE]: Sending close agent ID {0} to {1}", agentID, destination.RegionName);

                    m_scene.SimulationService.CloseAgent(destination, agentID, auth_code);
                }
            }, null, "SCOMM.SendCloseChildAgentConnections");
        }

        public List<GridRegion> RequestNamedRegions(string name, int maxNumber)
        {
            return m_scene.GridService.GetRegionsByName(UUID.Zero, name, maxNumber);
        }
    }
}
