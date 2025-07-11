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
using System.Collections;
using System.IO;
using MutSea.Framework.Servers.HttpServer;

namespace MutSea.Framework.Capabilities
{
    public class LLSDStreamhandler<TRequest, TResponse> : BaseStreamHandler
        where TRequest : new()
    {
        private LLSDMethod<TRequest, TResponse> m_method;

        public LLSDStreamhandler(string httpMethod, string path, LLSDMethod<TRequest, TResponse> method)
            : this(httpMethod, path, method, null, null) {}

        public LLSDStreamhandler(
            string httpMethod, string path, LLSDMethod<TRequest, TResponse> method, string name, string description)
            : base(httpMethod, path, name, description)
        {
            m_method = method;
        }

        protected override byte[] ProcessRequest(string path, Stream request,
                                      IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            //Encoding encoding = Util.UTF8;
            //StreamReader streamReader = new StreamReader(request, false);

            //string requestBody = streamReader.ReadToEnd();
            //streamReader.Close();

            // OpenMetaverse.StructuredData.OSDMap hash = (OpenMetaverse.StructuredData.OSDMap)
            //    OpenMetaverse.StructuredData.LLSDParser.DeserializeXml(new XmlTextReader(request));

            Hashtable hash = (Hashtable) LLSD.LLSDDeserialize(request);
            if(hash == null)
                return Array.Empty<byte>();

            TRequest llsdRequest = new TRequest();
            LLSDHelpers.DeserialiseOSDMap(hash, llsdRequest);

            TResponse response = m_method(llsdRequest);

            return Util.UTF8NoBomEncoding.GetBytes(LLSDHelpers.SerialiseLLSDReply(response));
        }
    }
}
