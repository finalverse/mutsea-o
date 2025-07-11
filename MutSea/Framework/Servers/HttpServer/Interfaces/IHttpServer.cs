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

using Nwc.XmlRpc;

namespace MutSea.Framework.Servers.HttpServer
{
    /// <summary>
    /// Interface to OpenSimulator's built in HTTP server.  Use this to register handlers (http, llsd, xmlrpc, etc.)
    /// for given URLs.
    /// </summary>
    public interface IHttpServer
    {
        uint SSLPort { get; }
        string SSLCommonName { get; }

        uint Port { get; }
        bool UseSSL { get; }

//        // Note that the agent string is provided simply to differentiate
//        // the handlers - it is NOT required to be an actual agent header
//        // value.
//        bool AddAgentHandler(string agent, IHttpAgentHandler handler);

        /// <summary>
        /// </remarks>
        /// <param name="methodName"></param>
        /// <param name="handler"></param>
        /// <returns>
        /// true if the handler was successfully registered, false if a handler with the same name already existed.
        /// </returns>
        bool AddHTTPHandler(string methodName, GenericHTTPMethod handler);

        bool AddPollServiceHTTPHandler(string uripath, PollServiceEventArgs args);
        bool AddPollServiceHTTPHandler(PollServiceEventArgs args);
        bool AddPollServiceHTTPHandlerVarPath(PollServiceEventArgs args);

        void RemovePollServiceHTTPHandler(string url, string path);
        void RemovePollServiceHTTPHandler(string path);

        /// <summary>
        /// Adds a LLSD handler, yay.
        /// </summary>
        /// <param name="path">/resource/ path</param>
        /// <param name="handler">handle the LLSD response</param>
        /// <returns></returns>
        bool AddLLSDHandler(string path, LLSDMethod handler);

        /// <summary>
        /// Add a stream handler to the http server.  If the handler already exists, then nothing happens.
        /// </summary>
        /// <param name="handler"></param>
        void AddStreamHandler(IRequestHandler handler);
        void AddSimpleStreamHandler(ISimpleStreamHandler handler, bool varPath = false);

        bool AddXmlRPCHandler(string method, XmlRpcMethod handler);
        bool AddXmlRPCHandler(string method, XmlRpcMethod handler, bool keepAlive);

        bool AddJsonRPCHandler(string method, JsonRPCMethod handler);

        /// <summary>
        /// Websocket HTTP server handlers.
        /// </summary>
        /// <param name="servicepath"></param>
        /// <param name="handler"></param>
        void AddWebSocketHandler(string servicepath, BaseHttpServer.WebSocketRequestDelegate handler);


        void RemoveWebSocketHandler(string servicepath);

        /// <summary>
        /// Gets the XML RPC handler for given method name
        /// </summary>
        /// <param name="method">Name of the method</param>
        /// <returns>Returns null if not found</returns>
        XmlRpcMethod GetXmlRPCHandler(string method);

        bool SetDefaultLLSDHandler(DefaultLLSDMethod handler);

//        /// <summary>
//        /// Remove the agent if it is registered.
//        /// </summary>
//        /// <param name="agent"></param>
//        /// <param name="handler"></param>
//        /// <returns></returns>
//        bool RemoveAgentHandler(string agent, IHttpAgentHandler handler);

        /// <summary>
        /// Remove an HTTP handler
        /// </summary>
        /// <param name="httpMethod"></param>
        /// <param name="path"></param>
        void RemoveHTTPHandler(string httpMethod, string path);


        bool RemoveLLSDHandler(string path, LLSDMethod handler);

        void RemoveStreamHandler(string httpMethod, string path);
        void RemoveSimpleStreamHandler(string path);

        void RemoveXmlRPCHandler(string method);

        void RemoveJsonRPCHandler(string method);

        string GetHTTP404();
        void AddIndexPHPMethodHandler(string key, SimpleStreamMethod sh);
        void RemoveIndexPHPMethodHandler(string key);
        SimpleStreamMethod TryGetIndexPHPMethodHandler(string key);
    }
}
