﻿using System;
using System.Collections;

using System.IO;
using System.Net;
using System.Text;
using System.Threading;

using OpenMetaverse;
using OpenMetaverse.StructuredData;

using MutSea.Framework;
using MutSea.Framework.Capabilities;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Interfaces;
using MutSea.Framework.Servers.HttpServer;
using OSDMap = OpenMetaverse.StructuredData.OSDMap;

namespace MutSea.Region.ClientStack.Linden
{
 
    public partial class BunchOfCaps
{
        public void DispatchRegionInfo(IOSHttpRequest request, IOSHttpResponse response, OSDMap map)
        {
            //m_log.Debug("[CAPS]: DispatchRegionInfo Request in region: " + m_regionName + "\n");

            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if(map == map.Count < 3)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            if (!m_Scene.TryGetScenePresence(m_AgentID, out ScenePresence _) || !m_Scene.Permissions.CanIssueEstateCommand(m_AgentID, false))
            {
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                return;
            }

            IEstateModule estateModule = m_Scene.RequestModuleInterface<IEstateModule>();
            if (estateModule == null)
            {
                response.StatusCode = (int)HttpStatusCode.NotImplemented;
                return;
            }

            response.StatusCode = estateModule.SetRegionInfobyCap(map) ? (int)HttpStatusCode.OK : (int)HttpStatusCode.NotImplemented;
        }
    }
}