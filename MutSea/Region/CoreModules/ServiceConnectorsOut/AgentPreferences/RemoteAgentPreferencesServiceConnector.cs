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

using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using MutSea.Services.Connectors;

using OpenMetaverse;
using log4net;
using Mono.Addins;
using Nini.Config;

namespace MutSea.Region.CoreModules.ServiceConnectorsOut.AgentPreferences
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "RemoteAgentPreferencesServicesConnector")]
    public class RemoteAgentPreferencesServicesConnector : AgentPreferencesServicesConnector,
            ISharedRegionModule, IAgentPreferencesService
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        public string Name
        {
            get { return "RemoteAgentPreferencesServicesConnector"; }
        }

        public new void Initialise(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {
                string name = moduleConfig.GetString("AgentPreferencesServices", "");
                if (name == Name)
                {
                    IConfig userConfig = source.Configs["AgentPreferencesService"];
                    if (userConfig == null)
                    {
                        m_log.Error("[AGENT PREFERENCES CONNECTOR]: AgentPreferencesService missing from MutSea.ini");
                        return;
                    }

                    m_Enabled = true;

                    base.Initialise(source);

                    m_log.Info("[AGENT PREFERENCES CONNECTOR]: Remote agent preferences enabled");
                }
            }
        }

        public void PostInitialise()
        {
            /* no op */
        }

        public void Close()
        {
            /* no op */
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            scene.RegisterModuleInterface<IAgentPreferencesService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
            /* no op */
        }

        public void RegionLoaded(Scene scene)
        {
            /* no op */
        }
    }
}