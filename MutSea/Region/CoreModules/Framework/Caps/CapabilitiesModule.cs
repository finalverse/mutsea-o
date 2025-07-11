/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyrightD
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
using System.Text;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using Caps=MutSea.Framework.Capabilities.Caps;

namespace MutSea.Region.CoreModules.Framework
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "CapabilitiesModule")]
    public class CapabilitiesModule : INonSharedRegionModule, ICapabilitiesModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly string m_showCapsCommandFormat = "   {0,-38} {1,-60}\n";

        protected Scene m_scene;

        /// <summary>
        /// Each agent has its own capabilities handler.
        /// </summary>
        protected readonly Dictionary<uint, Caps> m_capsObjects = new();
        protected readonly Dictionary<UUID, string> m_capsPaths = new();
        protected readonly Dictionary<UUID, Dictionary<ulong, string>> m_childrenSeeds = new();

        public void Initialise(IConfigSource source)
        {
        }

        public void AddRegion(Scene scene)
        {
            m_scene = scene;
            m_scene.RegisterModuleInterface<ICapabilitiesModule>(this);

            MainConsole.Instance.Commands.AddCommand(
                "Comms", false, "show caps list",
                "show caps list",
                "Shows list of registered capabilities for users.", HandleShowCapsListCommand);

            MainConsole.Instance.Commands.AddCommand(
                "Comms", false, "show caps stats by user",
                "show caps stats by user [<first-name> <last-name>]",
                "Shows statistics on capabilities use by user.",
                "If a user name is given, then prints a detailed breakdown of caps use ordered by number of requests received.",
                HandleShowCapsStatsByUserCommand);

            MainConsole.Instance.Commands.AddCommand(
                "Comms", false, "show caps stats by cap",
                "show caps stats by cap [<cap-name>]",
                "Shows statistics on capabilities use by capability.",
                "If a capability name is given, then prints a detailed breakdown of use by each user.",
                HandleShowCapsStatsByCapCommand);
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            m_scene.UnregisterModuleInterface<ICapabilitiesModule>(this);
        }

        public void PostInitialise()
        {
        }

        public void Close() {}

        public string Name
        {
            get { return "Capabilities Module"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public void CreateCaps(UUID agentId, uint circuitCode)
        {
            string capsObjectPath = GetCapsPath(agentId);
            Caps caps;
            lock (m_capsObjects)
            {
                if (m_capsObjects.TryGetValue(circuitCode, out Caps oldCaps))
                {
                    if (capsObjectPath == oldCaps.CapsObjectPath)
                    {
                        //m_log.WarnFormat(
                        //    "[CAPS]: Reusing caps for agent {0} in region {1}.  Old caps path {2}, new caps path {3}. ",
                        //    agentId, m_scene.RegionInfo.RegionName, oldCaps.CapsObjectPath, capsObjectPath);
                        return;
                    }
                    else
                    {
                        // not reusing  add extra melanie cleanup
                        // Remove tge handlers. They may conflict with the
                        // new object created below
                        oldCaps.DeregisterHandlers();

                        // Better safe ... should not be needed but also
                        // no big deal
                        m_capsObjects.Remove(circuitCode);
                    }
                }

                //m_log.DebugFormat(
                //    "[CAPS]: Adding capabilities for agent {0} in {1} with path {2}",
                //    agentId, m_scene.RegionInfo.RegionName, capsObjectPath);

                caps = new Caps(MainServer.Instance, m_scene.RegionInfo.ExternalHostName,
                        (MainServer.Instance is null) ? 0: MainServer.Instance.Port,
                        capsObjectPath, agentId, m_scene.RegionInfo.RegionName);

                m_log.Debug($"[CreateCaps]: new caps agent {agentId}, circuit {circuitCode}, path {caps.CapsObjectPath}");

                m_capsObjects[circuitCode] = caps;
            }
            m_scene.EventManager.TriggerOnRegisterCaps(agentId, caps);
        }

        public void RemoveCaps(UUID agentId, uint circuitCode)
        {
            m_log.DebugFormat("[CAPS]: Remove caps for agent {0} in region {1}", agentId, m_scene.RegionInfo.RegionName);
            lock (m_childrenSeeds)
            {
                m_childrenSeeds.Remove(agentId);
            }

            lock (m_capsObjects)
            {
                if (m_capsObjects.TryGetValue(circuitCode, out Caps cp))
                {
                    m_scene.EventManager.TriggerOnDeregisterCaps(agentId, cp);
                    m_capsObjects.Remove(circuitCode);
                    cp.Dispose();
                }
                else
                {
                    foreach (KeyValuePair<uint, Caps> kvp in m_capsObjects)
                    {
                        if (agentId.Equals(kvp.Value.AgentID))
                        {
                            m_scene.EventManager.TriggerOnDeregisterCaps(agentId, kvp.Value);
                            m_capsObjects.Remove(kvp.Key);
                            kvp.Value.Dispose();
                            return;
                        }
                    }
                    m_log.WarnFormat(
                        "[CAPS]: Received request to remove CAPS handler for root agent {0} in {1}, but no such CAPS handler found!",
                        agentId, m_scene.RegionInfo.RegionName);
                }
            }
        }

        public Caps GetCapsForUser(uint circuitCode)
        {
            lock (m_capsObjects)
            {
                if (m_capsObjects.TryGetValue(circuitCode, out Caps cp))
                    return cp;
            }

            return null;
        }

        public void ActivateCaps(uint circuitCode)
        {
            lock (m_capsObjects)
            {
                if (m_capsObjects.TryGetValue(circuitCode, out Caps cp))
                    cp.Activate();
            }
        }

        public void SetAgentCapsSeeds(AgentCircuitData agent)
        {
            lock (m_capsPaths)
                m_capsPaths[agent.AgentID] = agent.CapsPath;

            lock (m_childrenSeeds)
                m_childrenSeeds[agent.AgentID] = (agent.ChildrenCapSeeds ?? new Dictionary<ulong, string>());
        }

        public string GetCapsPath(UUID agentId)
        {
            lock (m_capsPaths)
            {
                if (m_capsPaths.TryGetValue(agentId, out string path))
                    return path;
            }
            return null;
        }

        public Dictionary<ulong, string> GetChildrenSeeds(UUID agentID)
        {
            lock (m_childrenSeeds)
            {
                if (m_childrenSeeds.TryGetValue(agentID, out Dictionary<ulong, string> seeds))
                    return seeds;
            }
            return new Dictionary<ulong, string>();
        }

        public void DropChildSeed(UUID agentID, ulong handle)
        {
            lock (m_childrenSeeds)
            {
                if (m_childrenSeeds.TryGetValue(agentID, out Dictionary<ulong, string> seeds))
                {
                    seeds.Remove(handle);
                }
            }
        }

        public string GetChildSeed(UUID agentID, ulong handle)
        {
            lock (m_childrenSeeds)
            {
                if (m_childrenSeeds.TryGetValue(agentID, out Dictionary<ulong, string> seeds))
                {
                    if (seeds.TryGetValue(handle, out string returnval))
                        return returnval;
                }
            }
            return null;
        }

        public void SetChildrenSeed(UUID agentID, Dictionary<ulong, string> seeds)
        {
            //m_log.DebugFormat(" !!! Setting child seeds in {0} to {1}", m_scene.RegionInfo.RegionName, seeds.Count);

            lock (m_childrenSeeds)
                m_childrenSeeds[agentID] = seeds;
        }

        public void DumpChildrenSeeds(UUID agentID)
        {
            m_log.Info("================ ChildrenSeed "+m_scene.RegionInfo.RegionName+" ================");

            lock (m_childrenSeeds)
            {
                foreach (KeyValuePair<ulong, string> kvp in m_childrenSeeds[agentID])
                {
                    Util.RegionHandleToRegionLoc(kvp.Key, out uint x, out uint y);
                    m_log.Info(" >> "+x+", "+y+": "+kvp.Value);
                }
            }
        }

        private void HandleShowCapsListCommand(string module, string[] cmdParams)
        {
            if (SceneManager.Instance.CurrentScene != null && SceneManager.Instance.CurrentScene != m_scene)
                return;

            StringBuilder capsReport = new();
            capsReport.AppendFormat("Region {0}:\n", m_scene.RegionInfo.RegionName);

            lock (m_capsObjects)
            {
                foreach (KeyValuePair<uint, Caps> kvp in m_capsObjects)
                {
                    Caps caps = kvp.Value;
                    string name = string.Empty;
                    if(m_scene.TryGetScenePresence(caps.AgentID, out ScenePresence sp) && sp!=null)
                        name = sp.Name;
                    capsReport.AppendFormat("** Circuit {0}; {1} {2}:\n", kvp.Key, caps.AgentID,name);
                    capsReport.AppendFormat("**    Base URL {0}\n", caps.CapsHandlers.BaseURL);

                    Dictionary<string, string> capsPaths = caps.CapsHandlers.GetCapsLocalPaths();
                    foreach(KeyValuePair<string, string> kvp2 in capsPaths)
                         capsReport.AppendFormat(m_showCapsCommandFormat, kvp2.Key, kvp2.Value);
 
                    foreach (KeyValuePair<string, PollServiceEventArgs> kvp2 in caps.GetPollHandlers())
                        capsReport.AppendFormat(m_showCapsCommandFormat, kvp2.Key, kvp2.Value.Url);

                    foreach (KeyValuePair<string, string> kvp3 in caps.ExternalCapsHandlers)
                        capsReport.AppendFormat(m_showCapsCommandFormat, kvp3.Key, kvp3.Value);
                }
            }

            MainConsole.Instance.Output(capsReport.ToString());
        }

        private void HandleShowCapsStatsByCapCommand(string module, string[] cmdParams)
        {
            if (SceneManager.Instance.CurrentScene != null && SceneManager.Instance.CurrentScene != m_scene)
                return;

            if (cmdParams.Length != 5 && cmdParams.Length != 6)
            {
                MainConsole.Instance.Output("Usage: show caps stats by cap [<cap-name>]");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Region {0}:\n", m_scene.Name);

            if (cmdParams.Length == 5)
            {
                BuildSummaryStatsByCapReport(sb);
            }
            else if (cmdParams.Length == 6)
            {
                BuildDetailedStatsByCapReport(sb, cmdParams[5]);
            }

            MainConsole.Instance.Output(sb.ToString());
        }

        private void BuildDetailedStatsByCapReport(StringBuilder sb, string capName)
        {
            /*
            sb.AppendFormat("Capability name {0}\n", capName);

            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("User Name", 34);
            cdt.AddColumn("Req Received", 12);
            cdt.AddColumn("Req Handled", 12);
            cdt.Indent = 2;

            Dictionary<string, int> receivedStats = new Dictionary<string, int>();
            Dictionary<string, int> handledStats = new Dictionary<string, int>();

            m_scene.ForEachScenePresence(
                sp =>
                {
                    Caps caps = m_scene.CapsModule.GetCapsForUser(sp.UUID);

                    if (caps == null)
                        return;

                    Dictionary<string, IRequestHandler> capsHandlers = caps.CapsHandlers.GetCapsHandlers();

                    IRequestHandler reqHandler;
                    if (capsHandlers.TryGetValue(capName, out reqHandler))
                    {
                        receivedStats[sp.Name] = reqHandler.RequestsReceived;
                        handledStats[sp.Name] = reqHandler.RequestsHandled;
                    }
                    else
                    {
                        PollServiceEventArgs pollHandler = null;
                        if (caps.TryGetPollHandler(capName, out pollHandler))
                        {
                            receivedStats[sp.Name] = pollHandler.RequestsReceived;
                            handledStats[sp.Name] = pollHandler.RequestsHandled;
                        }
                    }
                }
            );

            foreach (KeyValuePair<string, int> kvp in receivedStats.OrderByDescending(kp => kp.Value))
            {
                cdt.AddRow(kvp.Key, kvp.Value, handledStats[kvp.Key]);
            }

            sb.Append(cdt.ToString());
            */
        }

        private void BuildSummaryStatsByCapReport(StringBuilder sb)
        {
            /*
            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("Name", 34);
            cdt.AddColumn("Req Received", 12);
            cdt.AddColumn("Req Handled", 12);
            cdt.Indent = 2;

            Dictionary<string, int> receivedStats = new Dictionary<string, int>();
            Dictionary<string, int> handledStats = new Dictionary<string, int>();

            m_scene.ForEachScenePresence(
                sp =>
                {
                    Caps caps = m_scene.CapsModule.GetCapsForUser(sp.UUID);

                    if (caps == null)
                        return;

                    foreach (IRequestHandler reqHandler in caps.CapsHandlers.GetCapsHandlers().Values)
                    {
                        string reqName = reqHandler.Name ?? "";

                        if (!receivedStats.ContainsKey(reqName))
                        {
                            receivedStats[reqName] = reqHandler.RequestsReceived;
                            handledStats[reqName] = reqHandler.RequestsHandled;
                        }
                        else
                        {
                            receivedStats[reqName] += reqHandler.RequestsReceived;
                            handledStats[reqName] += reqHandler.RequestsHandled;
                        }
                    }

                    foreach (KeyValuePair<string, PollServiceEventArgs> kvp in caps.GetPollHandlers())
                    {
                        string name = kvp.Key;
                        PollServiceEventArgs pollHandler = kvp.Value;

                        if (!receivedStats.ContainsKey(name))
                        {
                            receivedStats[name] = pollHandler.RequestsReceived;
                            handledStats[name] = pollHandler.RequestsHandled;
                        }
                            else
                        {
                            receivedStats[name] += pollHandler.RequestsReceived;
                            handledStats[name] += pollHandler.RequestsHandled;
                        }
                    }
                }
            );

            foreach (KeyValuePair<string, int> kvp in receivedStats.OrderByDescending(kp => kp.Value))
                cdt.AddRow(kvp.Key, kvp.Value, handledStats[kvp.Key]);

            sb.Append(cdt.ToString());
            */
        }

        private void HandleShowCapsStatsByUserCommand(string module, string[] cmdParams)
        {
            /*
            if (SceneManager.Instance.CurrentScene != null && SceneManager.Instance.CurrentScene != m_scene)
                return;

            if (cmdParams.Length != 5 && cmdParams.Length != 7)
            {
                MainConsole.Instance.Output("Usage: show caps stats by user [<first-name> <last-name>]");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("Region {0}:\n", m_scene.Name);

            if (cmdParams.Length == 5)
            {
                BuildSummaryStatsByUserReport(sb);
            }
            else if (cmdParams.Length == 7)
            {
                string firstName = cmdParams[5];
                string lastName = cmdParams[6];

                ScenePresence sp = m_scene.GetScenePresence(firstName, lastName);

                if (sp == null)
                    return;

                BuildDetailedStatsByUserReport(sb, sp);
            }

            MainConsole.Instance.Output(sb.ToString());
            */
        }

        private void BuildDetailedStatsByUserReport(StringBuilder sb, ScenePresence sp)
        {
            /*
            sb.AppendFormat("Avatar name {0}, type {1}\n", sp.Name, sp.IsChildAgent ? "child" : "root");

            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("Cap Name", 34);
            cdt.AddColumn("Req Received", 12);
            cdt.AddColumn("Req Handled", 12);
            cdt.Indent = 2;

            Caps caps = m_scene.CapsModule.GetCapsForUser(sp.UUID);

            if (caps == null)
                return;

            List<CapTableRow> capRows = new List<CapTableRow>();

            foreach (IRequestHandler reqHandler in caps.CapsHandlers.GetCapsHandlers().Values)
                capRows.Add(new CapTableRow(reqHandler.Name, reqHandler.RequestsReceived, reqHandler.RequestsHandled));

            foreach (KeyValuePair<string, PollServiceEventArgs> kvp in caps.GetPollHandlers())
                capRows.Add(new CapTableRow(kvp.Key, kvp.Value.RequestsReceived, kvp.Value.RequestsHandled));

            foreach (CapTableRow ctr in capRows.OrderByDescending(ctr => ctr.RequestsReceived))
                cdt.AddRow(ctr.Name, ctr.RequestsReceived, ctr.RequestsHandled);

            sb.Append(cdt.ToString());
            */
        }

        private void BuildSummaryStatsByUserReport(StringBuilder sb)
        {
            /*
            ConsoleDisplayTable cdt = new ConsoleDisplayTable();
            cdt.AddColumn("Name", 32);
            cdt.AddColumn("Type", 5);
            cdt.AddColumn("Req Received", 12);
            cdt.AddColumn("Req Handled", 12);
            cdt.Indent = 2;

            m_scene.ForEachScenePresence(
                sp =>
                {
                    Caps caps = m_scene.CapsModule.GetCapsForUser(sp.UUID);

                    if (caps == null)
                        return;

                    Dictionary<string, IRequestHandler> capsHandlers = caps.CapsHandlers.GetCapsHandlers();

                    int totalRequestsReceived = 0;
                    int totalRequestsHandled = 0;

                    foreach (IRequestHandler reqHandler in capsHandlers.Values)
                    {
                        totalRequestsReceived += reqHandler.RequestsReceived;
                        totalRequestsHandled += reqHandler.RequestsHandled;
                    }

                    Dictionary<string, PollServiceEventArgs> capsPollHandlers = caps.GetPollHandlers();

                    foreach (PollServiceEventArgs handler in capsPollHandlers.Values)
                    {
                        totalRequestsReceived += handler.RequestsReceived;
                        totalRequestsHandled += handler.RequestsHandled;
                    }

                    cdt.AddRow(sp.Name, sp.IsChildAgent ? "child" : "root", totalRequestsReceived, totalRequestsHandled);
                }
            );

            sb.Append(cdt.ToString());
            */
        }

        private class CapTableRow
        {
            public string Name { get; set; }
            public int RequestsReceived { get; set; }
            public int RequestsHandled { get; set; }

            public CapTableRow(string name, int requestsReceived, int requestsHandled)
            {
                Name = name;
                RequestsReceived = requestsReceived;
                RequestsHandled = requestsHandled;
            }
        }
    }
}
