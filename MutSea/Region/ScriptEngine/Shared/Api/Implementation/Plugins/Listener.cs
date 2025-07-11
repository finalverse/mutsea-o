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
using log4net;
using OpenMetaverse;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.CoreModules.Scripting.WorldComm;
using MutSea.Region.ScriptEngine.Interfaces;
using MutSea.Region.ScriptEngine.Shared;
using MutSea.Region.ScriptEngine.Shared.Api;

namespace MutSea.Region.ScriptEngine.Shared.Api.Plugins
{
    public class Listener
    {
        // private static readonly ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public AsyncCommandManager m_CmdManager;

        private IWorldComm m_commsPlugin;

        public int ListenerCount
        {
            get { return m_commsPlugin.ListenerCount; }
        }

        public Listener(AsyncCommandManager CmdManager)
        {
            m_CmdManager = CmdManager;
            m_commsPlugin = m_CmdManager.m_ScriptEngine.World.RequestModuleInterface<IWorldComm>();
        }

        public Object[] GetSerializationData(UUID itemID)
        {
            if (m_commsPlugin != null)
                return m_commsPlugin.GetSerializationData(itemID);
            else
                return new Object[]{};
        }

        public void CreateFromData( UUID itemID, UUID hostID, Object[] data)
        {
            if (m_commsPlugin != null)
                m_commsPlugin.CreateFromData(itemID, hostID, data);
        }
    }
}