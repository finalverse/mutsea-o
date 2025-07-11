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
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;

using Nini.Config;
using log4net;

using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Server.Handlers.Base;
using OpenMetaverse;

namespace MutSea.Server.Handlers.MapImage
{
    public class MapGetServiceConnector : ServiceConnector
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private IMapImageService m_MapService;

        private string m_ConfigName = "MapImageService";

        public MapGetServiceConnector(IConfigSource config, IHttpServer server, string configName) :
            base(config, server, configName)
        {
            IConfig serverConfig = config.Configs[m_ConfigName];
            if (serverConfig == null)
                throw new Exception(String.Format("No section {0} in config file", m_ConfigName));

            string gridService = serverConfig.GetString("LocalServiceModule", string.Empty);

            if (string.IsNullOrWhiteSpace(gridService))
                throw new Exception("No LocalServiceModule in config file");

            object[] args = new object[] { config };
            m_MapService = ServerUtils.LoadPlugin<IMapImageService>(gridService, args);

            server.AddStreamHandler(new MapServerGetHandler(m_MapService));
        }
    }

    class MapServerGetHandler : BaseStreamHandler
    {
        public static readonly object ev = new object();

        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IMapImageService m_MapService;

        public MapServerGetHandler(IMapImageService service) :
                base("GET", "/map")
        {
            m_MapService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream request, IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if(!Monitor.TryEnter(ev, 5000))
            {
                httpResponse.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                httpResponse.AddHeader("Retry-After", "10");
                return Array.Empty<byte>();
            }

            byte[] result = Array.Empty<byte>();
            string format = string.Empty;

            //UUID scopeID = new UUID("07f8d88e-cd5e-4239-a0ed-843f75d09992");
            UUID scopeID = UUID.Zero;

            // This will be map/tilefile.ext, but on multitenancy it will be
            // map/scope/teilefile.ext
            path = path.Trim('/');
            string[] bits = path.Split(new char[] {'/'});
            if (bits.Length > 2)
            {
                try
                {
                    scopeID = new UUID(bits[1]);
                }
                catch
                {
                    return new byte[9];
                }
                path = bits[2];
                path = path.Trim('/');
            }

            if(path.Length == 0)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                httpResponse.ContentType = "text/plain";
                return Array.Empty<byte>();
            }

            result = m_MapService.GetMapTile(path, scopeID, out format);
            if (result.Length > 0)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
                if (format.Equals(".png"))
                    httpResponse.ContentType = "image/png";
                else if (format.Equals(".jpg") || format.Equals(".jpeg"))
                    httpResponse.ContentType = "image/jpeg";
            }
            else
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                httpResponse.ContentType = "text/plain";
            }

            Monitor.Exit(ev);

            return result;
        }
    }
}
