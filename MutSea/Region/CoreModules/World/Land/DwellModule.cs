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
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Messages.Linden;
using Mono.Addins;
using MutSea.Framework;
using MutSea.Framework.Capabilities;
using MutSea.Framework.Console;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.CoreModules.Framework.InterfaceCommander;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.PhysicsModules.SharedBase;
using MutSea.Services.Interfaces;
using Caps = MutSea.Framework.Capabilities.Caps;
using GridRegion = MutSea.Services.Interfaces.GridRegion;

namespace MutSea.Region.CoreModules.World.Land
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "DefaultDwellModule")]
    public class DefaultDwellModule : INonSharedRegionModule, IDwellModule
    {
        private Scene m_scene;
        private IConfigSource m_Config;
        private bool m_Enabled = false;

        public Type ReplaceableInterface
        {
            get { return typeof(IDwellModule); }
        }

        public string Name
        {
            get { return "DefaultDwellModule"; }
        }

        public void Initialise(IConfigSource source)
        {
            m_Config = source;

            IConfig DwellConfig = m_Config.Configs ["Dwell"];

            if (DwellConfig == null) {
                m_Enabled = false;
                return;
            }
            m_Enabled = (DwellConfig.GetString ("DwellModule", "DefaultDwellModule") == "DefaultDwellModule");
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_scene = scene;
            m_scene.RegisterModuleInterface<IDwellModule>(this);
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_scene.EventManager.OnNewClient += OnNewClient;
        }

        public void RemoveRegion(Scene scene)
        {
            if (!m_Enabled)
                return;
            m_scene.EventManager.OnNewClient -= OnNewClient;
        }

        public void Close()
        {
        }

        public void OnNewClient(IClientAPI client)
        {
            client.OnParcelDwellRequest += ClientOnParcelDwellRequest;
        }

        private void ClientOnParcelDwellRequest(int localID, IClientAPI client)
        {
            ILandObject parcel = m_scene.LandChannel.GetLandObject(localID);
            if (parcel == null)
                return;

            LandData land = parcel.LandData;
            if(land!= null)
                client.SendParcelDwellReply(localID, land.FakeID, land.Dwell);
        }


        public int GetDwell(UUID parcelID)
        {
            ILandObject parcel = m_scene.LandChannel.GetLandObject(parcelID);
            if (parcel != null && parcel.LandData != null)
               return (int)(parcel.LandData.Dwell);
            return 0;
        }

        public int GetDwell(LandData land)
        {
            if (land != null)
               return (int)(land.Dwell);
            return 0;
        }

    }
}
