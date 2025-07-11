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
using System.Net;
using System.Text;
using log4net;
using Nini.Config;
using Mono.Addins;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using Caps = MutSea.Framework.Capabilities.Caps;

namespace MutSea.Region.ClientStack.Linden
{
    /// <summary>
    /// MeshUploadFlag capability. This is required for uploading Mesh.
    /// </summary>
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "MeshUploadFlagModule")]
    public class MeshUploadFlagModule : INonSharedRegionModule
    {
//        private static readonly ILog m_log =
//            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Is this module enabled?
        /// </summary>
        public bool Enabled { get; private set; }

        private Scene m_scene;

        #region ISharedRegionModule Members

        public MeshUploadFlagModule()
        {
            Enabled = true;
        }

        public void Initialise(IConfigSource source)
        {
            IConfig config = source.Configs["Mesh"];
            if (config == null)
            {
                return;
            }
            else
            {
                Enabled = config.GetBoolean("AllowMeshUpload", Enabled);
            }
        }

        public void AddRegion(Scene s)
        {
            if (!Enabled)
                return;

            m_scene = s;
            m_scene.EventManager.OnRegisterCaps += RegisterCaps;
        }

        public void RemoveRegion(Scene s)
        {
            if (!Enabled)
                return;

            m_scene.EventManager.OnRegisterCaps -= RegisterCaps;
        }

        public void RegionLoaded(Scene s)
        {
        }

        public void PostInitialise()
        {
        }

        public void Close() { }

        public string Name { get { return "MeshUploadFlagModule"; } }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        public void RegisterCaps(UUID agentID, Caps caps)
        {
            caps.RegisterSimpleHandler("MeshUploadFlag",
                    new SimpleStreamHandler("/" + UUID.Random(), MeshUploadFlag));
                    /* to use when/if we do check on dbs
                    new SimpleStreamHandler("/" + UUID.Random(), delegate (IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
                    {
                        MeshUploadFlag(httpRequest, httpResponse, agentID);
                    }));
                    */
        }

        //private void MeshUploadFlag(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse, UUID agentID)
        private void MeshUploadFlag(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
//            m_log.DebugFormat("[MESH UPLOAD FLAG MODULE]: MeshUploadFlag request");
            if(httpRequest.HttpMethod != "GET")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            OSDMap data = new OSDMap();
            data["mesh_upload_status"] = "valid";
            httpResponse.RawBuffer = Encoding.UTF8.GetBytes(OSDParser.SerializeLLSDXmlString(data));
            httpResponse.StatusCode = (int)HttpStatusCode.OK;
        }
    }
}
