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
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.IO;
using OpenMetaverse.StructuredData;
using OpenMetaverse;
using log4net;

namespace MutSea.Framework.Servers.HttpServer
{
    /// <summary>
    /// Json rpc request manager.
    /// </summary>
    public class JsonRpcRequestManager
    {
        static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public JsonRpcRequestManager()
        {
        }

        /// <summary>
        /// Sends json-rpc request with a serializable type.
        /// </summary>
        /// <returns>
        /// OSD Map.
        /// </returns>
        /// <param name='parameters'>
        /// Serializable type .
        /// </param>
        /// <param name='method'>
        /// Json-rpc method to call.
        /// </param>
        /// <param name='uri'>
        /// URI of json-rpc service.
        /// </param>
        /// <param name='jsonId'>
        /// Id for our call.
        /// </param>
        public bool JsonRpcRequest(ref object parameters, string method, string uri, string jsonId)
        {
            if (jsonId is null)
                throw new ArgumentNullException("jsonId");
            if (uri is null)
                throw new ArgumentNullException("uri");
            if (method is null)
                throw new ArgumentNullException("method");
            if (parameters is null)
                throw new ArgumentNullException("parameters");

            if(string.IsNullOrWhiteSpace(uri))
                return false;

            OSDMap request = new()
            {
                { "jsonrpc", OSD.FromString("2.0") },
                { "id", OSD.FromString(jsonId) },
                { "method", OSD.FromString(method) },
                { "params", OSD.SerializeMembers(parameters) }
            };

            OSDMap outerResponse;
            try
            {
                outerResponse = WebUtil.PostToService(uri, request, 10000, true);
            }
            catch (Exception e)
            {
                m_log.Debug(string.Format("JsonRpc request '{0}' to {1} failed", method, uri), e);
                return false;
            }

            OSD osdtmp;
            if (!outerResponse.TryGetValue("_Result", out osdtmp) || (osdtmp is not OSDMap response))
            {
                m_log.DebugFormat("JsonRpc request '{0}' to {1} returned an invalid response: {2}",
                    method, uri, OSDParser.SerializeJsonString(outerResponse));
                return false;
            }

            if (response.TryGetValue("error", out osdtmp))
            {
                m_log.DebugFormat("JsonRpc request '{0}' to {1} returned an error: {2}",
                    method, uri, OSDParser.SerializeJsonString(osdtmp));
                return false;
            }

            if (!response.TryGetValue("result", out osdtmp) || (osdtmp is not OSDMap resultmap))
            {
                m_log.DebugFormat("JsonRpc request '{0}' to {1} returned an invalid response: {2}",
                    method, uri, OSDParser.SerializeJsonString(response));
                return false;
            }

            OSD.DeserializeMembers(ref parameters, resultmap);
            return true;
        }

        /// <summary>
        /// Sends json-rpc request with OSD parameter.
        /// </summary>
        /// <returns>
        /// The rpc request.
        /// </returns>
        /// <param name='data'>
        /// data - incoming as parameters, outgoing as result/error
        /// </param>
        /// <param name='method'>
        /// Json-rpc method to call.
        /// </param>
        /// <param name='uri'>
        /// URI of json-rpc service.
        /// </param>
        /// <param name='jsonId'>
        /// If set to <c>true</c> json identifier.
        /// </param>
        public bool JsonRpcRequest(ref OSD data, string method, string uri, string jsonId)
        {
            if (string.IsNullOrEmpty(jsonId))
                jsonId = UUID.Random().ToString();

            OSDMap request = new()
            {
                { "jsonrpc", OSD.FromString("2.0") },
                { "id", OSD.FromString(jsonId) },
                { "method", OSD.FromString(method) },
                { "params", data }
            };

            OSDMap outerresponse;
            try
            {
                outerresponse = WebUtil.PostToService(uri, request, 10000, true);
            }
            catch (Exception e)
            {
                m_log.Debug(string.Format("JsonRpc request '{0}' to {1} failed", method, uri), e);
                return false;
            }

            OSD osdtmp;
            if (!outerresponse.TryGetValue("_Result", out osdtmp) || (osdtmp is not OSDMap response))
            {
                m_log.DebugFormat("JsonRpc request '{0}' to {1} returned an invalid response: {2}",
                    method, uri, OSDParser.SerializeJsonString(outerresponse));
                return false;
            }

            if (response.TryGetValue("error", out osdtmp))
            {
                data = osdtmp;
                m_log.DebugFormat("JsonRpc request '{0}' to {1} returned an error: {2}",
                    method, uri, OSDParser.SerializeJsonString(osdtmp));
                return false;
            }

            data = response;
            return true;
        }
    }
}
