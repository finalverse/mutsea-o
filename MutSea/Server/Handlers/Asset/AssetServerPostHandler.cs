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

using Nini.Config;
using log4net;
using System;
using System.Reflection;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using MutSea.Server.Base;
using MutSea.Services.Interfaces;
using MutSea.Framework;
using MutSea.Framework.ServiceAuth;
using MutSea.Framework.Servers.HttpServer;

namespace MutSea.Server.Handlers.Asset
{
    public class AssetServerPostHandler : BaseStreamHandler
    {
        // private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IAssetService m_AssetService;

        public AssetServerPostHandler(IAssetService service) :
                base("POST", "/assets")
        {
            m_AssetService = service;
        }

        public AssetServerPostHandler(IAssetService service, IServiceAuth auth) :
            base("POST", "/assets", auth)
        {
            m_AssetService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream request,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            AssetBase asset;
            XmlSerializer xs = new XmlSerializer(typeof(AssetBase));

            try
            {
                asset = (AssetBase)xs.Deserialize(request);
            }
            catch (Exception)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return null;
            }

            string[] p = SplitParams(path);
            if (p.Length > 0)
            {
                string id = p[0];
                bool result = m_AssetService.UpdateContent(id, asset.Data);

                xs = new XmlSerializer(typeof(bool));
                return ServerUtils.SerializeResult(xs, result);
            }
            else
            {
                string id = m_AssetService.Store(asset);

                xs = new XmlSerializer(typeof(string));
                return ServerUtils.SerializeResult(xs, id);
            }
        }
    }
}
