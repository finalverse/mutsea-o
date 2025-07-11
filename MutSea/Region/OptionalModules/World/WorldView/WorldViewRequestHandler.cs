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
using System.IO;
using System.Reflection;
using System.Xml;

using MutSea.Framework;
using MutSea.Server.Base;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Interfaces;

using OpenMetaverse;
using log4net;

namespace MutSea.Region.OptionalModules.World.WorldView
{
    public class WorldViewRequestHandler : BaseStreamHandler
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected WorldViewModule m_WorldViewModule;
        protected Object m_RequestLock = new Object();

        public WorldViewRequestHandler(WorldViewModule fmodule, string rid)
                : base("GET", "/worldview/" + rid)
        {
            m_WorldViewModule = fmodule;
        }

        protected override byte[] ProcessRequest(string path, Stream requestData,
                IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            httpResponse.ContentType = "image/jpeg";

//            StreamReader sr = new StreamReader(requestData);
//            string body = sr.ReadToEnd();
//            sr.Close();
//            body = body.Trim();

            try
            {
                lock (m_RequestLock)
                {
                    Dictionary<string, object> request =
                            new Dictionary<string, object>();
                    foreach (string name in httpRequest.QueryString)
                        request[name] = httpRequest.QueryString[name];

                    return SendWorldView(request);
                }
            }
            catch (Exception e)
            {
                m_log.Debug("[WORLDVIEW]: Exception: " + e.ToString());
            }

            return Array.Empty<byte>();
        }

        public Byte[] SendWorldView(Dictionary<string, object> request)
        {
            float posX;
            float posY;
            float posZ;
            float rotX;
            float rotY;
            float rotZ;
            float fov;
            int width;
            int height;
            bool usetex;

            if (!request.ContainsKey("posX"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("posY"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("posZ"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("rotX"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("rotY"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("rotZ"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("fov"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("width"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("height"))
                return Array.Empty<byte>();
            if (!request.ContainsKey("usetex"))
                return Array.Empty<byte>();

            try
            {
                posX = Convert.ToSingle(request["posX"]);
                posY = Convert.ToSingle(request["posY"]);
                posZ = Convert.ToSingle(request["posZ"]);
                rotX = Convert.ToSingle(request["rotX"]);
                rotY = Convert.ToSingle(request["rotY"]);
                rotZ = Convert.ToSingle(request["rotZ"]);
                fov = Convert.ToSingle(request["fov"]);
                width = Convert.ToInt32(request["width"]);
                height = Convert.ToInt32(request["height"]);
                usetex = Convert.ToBoolean(request["usetex"]);
            }
            catch
            {
                return Array.Empty<byte>();
            }

            Vector3 pos = new Vector3(posX, posY, posZ);
            Vector3 rot = new Vector3(rotX, rotY, rotZ);

            return m_WorldViewModule.GenerateWorldView(pos, rot, fov, width,
                    height, usetex);
        }
    }
}

