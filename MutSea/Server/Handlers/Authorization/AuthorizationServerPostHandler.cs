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
using MutSea.Framework.Servers.HttpServer;

namespace MutSea.Server.Handlers.Authorization
{
    public class AuthorizationServerPostHandler : BaseStreamHandler
    {
//        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IAuthorizationService m_AuthorizationService;

        public AuthorizationServerPostHandler(IAuthorizationService service) :
                base("POST", "/authorization")
        {
            m_AuthorizationService = service;
        }

        protected override byte[] ProcessRequest(string path, Stream request,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            XmlSerializer xs = new XmlSerializer(typeof (AuthorizationRequest));
            AuthorizationRequest Authorization = (AuthorizationRequest) xs.Deserialize(request);

            string message = String.Empty;
            bool authorized = m_AuthorizationService.IsAuthorizedForRegion(Authorization.ID, Authorization.FirstName, Authorization.SurName, Authorization.RegionID, out message);

            AuthorizationResponse result = new AuthorizationResponse(authorized, Authorization.ID + " has been authorized");

            xs = new XmlSerializer(typeof(AuthorizationResponse));
            return ServerUtils.SerializeResult(xs, result);

        }
    }
}
