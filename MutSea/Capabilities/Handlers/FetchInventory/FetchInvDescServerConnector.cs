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
using Nini.Config;
using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using MutSea.Framework;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Server.Handlers.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;


namespace MutSea.Capabilities.Handlers
{
    public class FetchInvDescServerConnector : ServiceConnector
    {
        private IInventoryService m_InventoryService;
        private ILibraryService m_LibraryService;
        private string m_ConfigName = "CapsService";

        public FetchInvDescServerConnector(IConfigSource config, IHttpServer server, string configName) :
                base(config, server, configName)
        {
            if (configName != String.Empty)
                m_ConfigName = configName;

            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(String.Format("No section '{0}' in config file", m_ConfigName));

            string invService = serverConfig.GetString("InventoryService", String.Empty);

            if (invService.Length == 0)
                throw new Exception("No InventoryService in config file");

            Object[] args = new Object[] { config };
            m_InventoryService =
                    ServerUtils.LoadPlugin<IInventoryService>(invService, args);

            if (m_InventoryService == null)
                throw new Exception(String.Format("Failed to load InventoryService from {0}; config is {1}", invService, m_ConfigName));

            string libService = serverConfig.GetString("LibraryService", String.Empty);
            m_LibraryService =
                    ServerUtils.LoadPlugin<ILibraryService>(libService, args);

            ExpiringKey<UUID> m_badRequests = new ExpiringKey<UUID>(30000);

            FetchInvDescHandler webFetchHandler = new FetchInvDescHandler(m_InventoryService, m_LibraryService, null);
            ISimpleStreamHandler reqHandler
                = new SimpleStreamHandler("/CAPS/WebFetchInvDesc/", delegate(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
                { 
                    webFetchHandler.FetchInventoryDescendentsRequest(httpRequest, httpResponse, m_badRequests);
                });

            server.AddSimpleStreamHandler(reqHandler);
        }
    }
}
