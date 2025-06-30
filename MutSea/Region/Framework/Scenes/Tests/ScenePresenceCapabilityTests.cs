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
using System.Reflection;
using System.Text;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;
using Nini.Config;
using NUnit.Framework;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Servers;
using MutSea.Framework.Servers.HttpServer;
using MutSea.Region.ClientStack.Linden;
using MutSea.Region.CoreModules.Framework;
using MutSea.Region.CoreModules.Framework.EntityTransfer;
using MutSea.Region.CoreModules.World.Serialiser;
using MutSea.Region.CoreModules.ServiceConnectorsOut.Simulation;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Interfaces;
using MutSea.Tests.Common;
using GridRegion = MutSea.Services.Interfaces.GridRegion;

namespace MutSea.Region.Framework.Scenes.Tests
{
    [TestFixture]
    public class ScenePresenceCapabilityTests : MutSeaTestCase
    {
        [Test]
        public void TestChildAgentSingleRegionCapabilities()
        {
            TestHelpers.InMethod();
//            TestHelpers.EnableLogging();

            UUID spUuid = TestHelpers.ParseTail(0x1);

            // XXX: This is not great since the use of statics will mean that this has to be manually cleaned up for
            // any subsequent test.
            // XXX: May replace with a mock IHttpServer later.
            BaseHttpServer httpServer = new BaseHttpServer(99999);
            MainServer.AddHttpServer(httpServer);
            MainServer.Instance = httpServer;

            CapabilitiesModule capsMod = new CapabilitiesModule();
            TestScene scene = new SceneHelpers().SetupScene();
            SceneHelpers.SetupSceneModules(scene, capsMod);

            ScenePresence sp = SceneHelpers.AddChildScenePresence(scene, spUuid);
            //Assert.That(capsMod.GetCapsForUser(spUuid), Is.Not.Null);

            // TODO: Need to add tests for other ICapabiltiesModule methods.

//            scene.IncomingCloseAgent(sp.UUID, false);
//            //Assert.That(capsMod.GetCapsForUser(spUuid), Is.Null);
            scene.CloseAgent(sp.UUID, false);
//            Assert.That(capsMod.GetCapsForUser(spUuid), Is.Null);

            // TODO: Need to add tests for other ICapabiltiesModule methods.
        }
    }
}
