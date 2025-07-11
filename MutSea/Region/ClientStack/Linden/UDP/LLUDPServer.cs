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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using OpenMetaverse.Packets;
using MutSea.Framework;
using MutSea.Framework.Monitoring;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.Framework.Interfaces;
using OpenMetaverse;
using Mono.Addins;


namespace MutSea.Region.ClientStack.LindenUDP
{
    /// <summary>
    /// A shim around LLUDPServer that implements the IClientNetworkServer interface
    /// </summary>
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "LLUDPServerShim")]
    public class LLUDPServerShim : INonSharedRegionModule
    {
        protected IConfigSource m_Config;
        protected LLUDPServer m_udpServer;

        #region INonSharedRegionModule
        public virtual string Name
        {
            get { return "LLUDPServerShim"; }
        }

        public virtual Type ReplaceableInterface
        {
            get { return null; }
        }

        public virtual void Initialise(IConfigSource source)
        {
            m_Config = source;
        }

        public virtual void Close()
        {
        }

        public virtual void AddRegion(Scene scene)
        {
            uint port = (uint)scene.RegionInfo.InternalEndPoint.Port;

            IPAddress listenIP = scene.RegionInfo.InternalEndPoint.Address;
            m_udpServer = new LLUDPServer(listenIP, port, scene.RegionInfo.ProxyOffset, m_Config, scene.AuthenticateHandler);
            scene.RegionInfo.InternalEndPoint.Port = m_udpServer.Port;
            AddScene(scene);
        }

        public virtual void RemoveRegion(Scene scene)
        {
            Stop();
        }

        public virtual void RegionLoaded(Scene scene)
        {
            Start();
        }
        #endregion

        public virtual void AddScene(IScene scene)
        {
            m_udpServer.AddScene(scene);

            StatsManager.RegisterStat(
                new Stat(
                    "ClientLogoutsDueToNoReceives",
                    "Number of times a client has been logged out because no packets were received before the timeout.",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.None,
                    stat => stat.Value = m_udpServer.ClientLogoutsDueToNoReceives,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "IncomingUDPReceivesCount",
                    "Number of UDP receives performed",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.UdpReceives,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "IncomingPacketsProcessedCount",
                    "Number of inbound LL protocol packets processed",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.IncomingPacketsProcessed,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "IncomingPacketsMalformedCount",
                    "Number of inbound UDP packets that could not be recognized as LL protocol packets.",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.IncomingMalformedPacketCount,
                    StatVerbosity.Info));

            StatsManager.RegisterStat(
                new Stat(
                    "IncomingPacketsOrphanedCount",
                    "Number of inbound packets that were not initial connections packets and could not be associated with a viewer.",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.IncomingOrphanedPacketCount,
                    StatVerbosity.Info));

            StatsManager.RegisterStat(
                new Stat(
                    "IncomingPacketsResentCount",
                    "Number of inbound packets that clients indicate are resends.",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.IncomingPacketsResentCount,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "OutgoingUDPSendsCount",
                    "Number of UDP sends performed",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.UdpSends,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "OutgoingPacketsResentCount",
                    "Number of packets resent because a client did not acknowledge receipt",
                    "",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = m_udpServer.PacketsResentCount,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "AverageUDPProcessTime",
                    "Average number of milliseconds taken to process each incoming UDP packet in a sample.",
                    "This is for initial receive processing which is separate from the later client LL packet processing stage.",
                    "ms",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.None,
                    stat => stat.Value = m_udpServer.AverageReceiveTicksForLastSamplePeriod,
                    //stat => stat.Value = Math.Round(m_udpServer.AverageReceiveTicksForLastSamplePeriod, 7),
                    StatVerbosity.Debug));
        }

        public virtual void Start()
        {
            m_udpServer.Start();
        }

        public virtual void Stop()
        {
            m_udpServer.Stop();
        }

    }

    /// <summary>
    /// The LLUDP server for a region. This handles incoming and outgoing
    /// packets for all UDP connections to the region
    /// </summary>
    public class LLUDPServer : MutSeaUDPBase
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Maximum transmission unit, or UDP packet size, for the LLUDP protocol</summary>
        public const int MTU = 1400;
        public const int MAXPAYLOAD = 1200;

        /// <summary>Number of forced client logouts due to no receipt of packets before timeout.</summary>
        public int ClientLogoutsDueToNoReceives { get; protected set; }

        /// <summary>
        /// Default packet debug level given to new clients
        /// </summary>
        public int DefaultClientPacketDebugLevel { get; set; }

        /// <summary>
        /// If set then all inbound agent updates are discarded.  For debugging purposes.
        /// discard agent update.
        /// </summary>
        public bool DiscardInboundAgentUpdates { get; set; }

        /// <summary>The measured resolution of Environment.TickCount</summary>
        public readonly float TickCountResolution;

        /// <summary>Number of texture packets to put on the queue each time the
        /// OnQueueEmpty event is triggered for textures</summary>
        public readonly int TextureSendLimit;

        protected BlockingCollection<IncomingPacket> packetInbox = new();

        /// <summary>Bandwidth throttle for this UDP server</summary>
        public TokenBucket Throttle { get; protected set; }

        /// <summary>Per client throttle rates enforced by this server</summary>
        /// <remarks>
        /// If the total rate is non-zero, then this is the maximum total throttle setting that any client can ever have.
        /// The other rates (resend, asset, etc.) are the defaults for a new client and can be changed (and usually
        /// do get changed immediately).  They do not need to sum to the total.
        /// </remarks>
        public ThrottleRates ThrottleRates { get; protected set; }

        /// <summary>Manages authentication for agent circuits</summary>
        protected AgentCircuitManager m_circuitManager;

        /// <summary>Reference to the scene this UDP server is attached to</summary>
        public Scene Scene { get; protected set; }

        /// <summary>The size of the receive buffer for the UDP socket. This value
        /// is passed up to the operating system and used in the system networking
        /// stack. Use zero to leave this value as the default</summary>
        protected int m_recvBufferSize;

        /// <summary>Tracks whether or not a packet was sent each round so we know
        /// whether or not to sleep</summary>
        protected bool m_packetSent;

        /// <summary>Environment.TickCount of the last time that packet stats were reported to the scene</summary>
        protected int m_elapsedMSSinceLastStatReport = 0;

        /// <summary>Environment.TickCount of the last time the outgoing packet handler executed</summary>
        protected double m_tickLastOutgoingPacketHandler;

        /// <summary>Keeps track of the number of elapsed milliseconds since the last time the outgoing packet handler looped</summary>
        protected double m_elapsedMSOutgoingPacketHandler;

        /// <summary>Keeps track of the number of 100 millisecond periods elapsed in the outgoing packet handler executed</summary>
        protected int m_elapsed100MSOutgoingPacketHandler;

        /// <summary>Keeps track of the number of 500 millisecond periods elapsed in the outgoing packet handler executed</summary>
        protected int m_elapsed500MSOutgoingPacketHandler;

        /// <summary>Flag to signal when clients should check for resends</summary>
        protected bool m_resendUnacked;

        /// <summary>Flag to signal when clients should send ACKs</summary>
        protected bool m_sendAcks;

        /// <summary>Flag to signal when clients should send pings</summary>
        protected bool m_sendPing;

        protected readonly ExpiringCacheOS<IPEndPoint, Queue<UDPPacketBuffer>> m_pendingCache = new(10000);

        protected int m_defaultRTO = 0;
        protected int m_maxRTO = 0;
        protected int m_ackTimeout = 0;
        protected int m_pausedAckTimeout = 0;
        protected bool m_disableFacelights = false;

        /// <summary>
        /// Record how many packets have been resent
        /// </summary>
        internal int PacketsResentCount { get; set; }

        /// <summary>
        /// Record how many packets have been sent
        /// </summary>
        internal int PacketsSentCount { get; set; }

        /// <summary>
        /// Record how many incoming packets are indicated as resends by clients.
        /// </summary>
        internal int IncomingPacketsResentCount { get; set; }

        /// <summary>
        /// Record how many inbound packets could not be recognized as LLUDP packets.
        /// </summary>
        public int IncomingMalformedPacketCount { get; protected set; }

        /// <summary>
        /// Record how many inbound packets could not be associated with a simulator circuit.
        /// </summary>
        public int IncomingOrphanedPacketCount { get; protected set; }

        public bool SupportViewerObjectsCache = true;
        /// <summary>
        /// Run queue empty processing within a single persistent thread.
        /// </summary>
        /// <remarks>
        /// This is the alternative to having every
        /// connection schedule its own job in the threadpool which causes performance problems when there are many
        /// connections.
        /// </remarks>
        public JobEngine OqrEngine { get; protected set; }

        public LLUDPServer(
            IPAddress listenIP, uint port, int proxyPortOffsetParm,
            IConfigSource configSource, AgentCircuitManager circuitManager)
            : base(listenIP, (int)port)
        {
            #region Environment.TickCount Measurement

            // Measure the resolution of Environment.TickCount
            TickCountResolution = 0f;
            for (int i = 0; i < 10; i++)
            {
                int start = Environment.TickCount;
                int now = start;
                while (now == start)
                    now = Environment.TickCount;
                TickCountResolution += (float)(now - start);
            }
            m_log.Info($"[LLUDPSERVER]: Average Environment.TickCount resolution: {TickCountResolution * 0.1f}ms");

            TickCountResolution = 0f;
            for (int i = 0; i < 100; i++)
            {
                double start = Util.GetTimeStampMS();
                double now = start;
                while (now == start)
                    now = Util.GetTimeStampMS();
                TickCountResolution += (float)((now - start));
            }

            TickCountResolution = MathF.Round(TickCountResolution * 0.01f,6,MidpointRounding.AwayFromZero);
            m_log.Info($"[LLUDPSERVER]: Average Util.GetTimeStampMS resolution: {TickCountResolution}ms");

            #endregion Environment.TickCount Measurement

            m_circuitManager = circuitManager;
            int sceneThrottleBps = 0;
            // bool usePools = false;

            IConfig config = configSource.Configs["ClientStack.LindenUDP"];
            if (config != null)
            {
                m_recvBufferSize = config.GetInt("client_socket_rcvbuf_size", 0);
                sceneThrottleBps = config.GetInt("scene_throttle_max_bps", 6250000);

                TextureSendLimit = config.GetInt("TextureSendLimit", 20);

                m_defaultRTO = config.GetInt("DefaultRTO", 0);
                m_maxRTO = config.GetInt("MaxRTO", 0);
                m_disableFacelights = config.GetBoolean("DisableFacelights", false);
                m_ackTimeout = 1000 * config.GetInt("AckTimeout", 60);
                m_pausedAckTimeout = 1000 * config.GetInt("PausedAckTimeout", 300);
                SupportViewerObjectsCache = config.GetBoolean("SupportViewerObjectsCache", SupportViewerObjectsCache);
            }
            else
            {
                TextureSendLimit = 20;
                m_ackTimeout = 1000 * 60; // 1 minute
                m_pausedAckTimeout = 1000 * 300; // 5 minutes
            }

            // FIXME: This actually only needs to be done once since the PacketPool is shared across all servers.
            // However, there is no harm in temporarily doing it multiple times.
            IConfig packetConfig = configSource.Configs["PacketPool"];
            if (packetConfig != null)
            {
                //PacketPool.Instance.RecyclePackets = packetConfig.GetBoolean("RecyclePackets", true);
                //PacketPool.Instance.RecycleDataBlocks = packetConfig.GetBoolean("RecycleDataBlocks", true);
                //usePools = packetConfig.GetBoolean("RecycleBaseUDPPackets", usePools);
            }

            #region BinaryStats
            config = configSource.Configs["Statistics.Binary"];
            m_shouldCollectStats = false;
            if (config != null)
            {
                m_shouldCollectStats = config.GetBoolean("Enabled", false);
                binStatsMaxFilesize = TimeSpan.FromSeconds(config.GetInt("packet_headers_period_seconds", 300));
                binStatsDir = config.GetString("stats_dir", ".");
                m_aggregatedBWStats = config.GetBoolean("aggregatedBWStats", false);
            }
            #endregion BinaryStats

            Throttle = new TokenBucket(null, sceneThrottleBps, sceneThrottleBps * 10e-3f);
            ThrottleRates = new ThrottleRates(configSource);

            //if (usePools)
            //    EnablePools();
        }

        public void Start()
        {
            StartInbound();
            StartOutbound();
            //IpahEngine.Start();
            OqrEngine.Start();

            m_elapsedMSSinceLastStatReport = Environment.TickCount;
        }

        public void StartInbound()
        {
            m_log.Info("[LLUDPSERVER]: Starting inbound packet processing for the LLUDP server");

            base.StartInbound(m_recvBufferSize);

            // This thread will process the packets received that are placed on the packetInbox
            WorkManager.StartThread(
                IncomingPacketHandler,
                $"Incoming Packets ({Scene.Name})",
                ThreadPriority.Normal,
                true,
                true,
                null,
                Watchdog.DEFAULT_WATCHDOG_TIMEOUT_MS);
        }

        public override void StartOutbound()
        {
            m_log.Info("[LLUDPSERVER]: Starting outbound packet processing for the LLUDP server");

            base.StartOutbound();

            WorkManager.StartThread(
                OutgoingPacketHandler,
                $"Outgoing Packets ({Scene.Name})",
                ThreadPriority.Normal,
                true,
                true,
                null,
                Watchdog.DEFAULT_WATCHDOG_TIMEOUT_MS);
        }

        public void Stop()
        {
            m_log.Info($"[LLUDPSERVER]: Shutting down the LLUDP server for {Scene.Name}");
            base.StopOutbound();
            base.StopInbound();
            //IpahEngine.Stop();
            OqrEngine.Stop();
        }

         /// <summary>
        /// If the outgoing UDP thread times out, then return client that was being processed to help with debugging.
        /// </summary>
        /// <returns></returns>

        public void AddScene(IScene scene)
        {
            if (Scene is not null)
            {
                m_log.Error("[LLUDPSERVER]: AddScene() called on an LLUDPServer that already has a scene");
                return;
            }

            if (scene is not MutSea.Region.Framework.Scenes.Scene)
            {
                m_log.Error($"[LLUDPSERVER]: AddScene() called with an unrecognized scene type {scene.GetType()}");
                return;
            }

            Scene = (Scene)scene;

            OqrEngine = new JobEngine($"Outgoing Queue Refill Engine ({Scene.Name})", "OutQueueRefillEng", 4500);

            StatsManager.RegisterStat(
                new Stat(
                    "InboxPacketsCount",
                    "Number of LL protocol packets waiting for the second stage of processing after initial receive.",
                    "Number of LL protocol packets waiting for the second stage of processing after initial receive.",
                    "",
                    "clientstack",
                    scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => {try{stat.Value = packetInbox.Count;}catch{}},
                    StatVerbosity.Debug));

            // XXX: These stats are also pool stats but we register them separately since they are currently not
            // turned on and off by EnablePools()/DisablePools()
            StatsManager.RegisterStat(
                new PercentageStat(
                    "PacketsReused",
                    "Packets reused",
                    "Number of packets reused out of all requests to the packet pool",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    stat =>
                        { PercentageStat pstat = (PercentageStat)stat;
                          pstat.Consequent = PacketPool.Instance.PacketsRequested;
                          pstat.Antecedent = PacketPool.Instance.PacketsReused; },
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new PercentageStat(
                    "PacketDataBlocksReused",
                    "Packet data blocks reused",
                    "Number of data blocks reused out of all requests to the packet pool",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    stat =>
                        { PercentageStat pstat = (PercentageStat)stat;
                          pstat.Consequent = PacketPool.Instance.BlocksRequested;
                          pstat.Antecedent = PacketPool.Instance.BlocksReused; },
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "PacketsPoolCount",
                    "Objects within the packet pool",
                    "The number of objects currently stored within the packet pool",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    stat => stat.Value = PacketPool.Instance.PacketsPooled,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "PacketDataBlocksPoolCount",
                    "Objects within the packet data block pool",
                    "The number of objects currently stored within the packet data block pool",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    stat => stat.Value = PacketPool.Instance.BlocksPooled,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "OutgoingPacketsQueuedCount",
                    "Packets queued for outgoing send",
                    "Number of queued outgoing packets across all connections",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.AverageChangeOverTime,
                    stat => stat.Value = GetTotalQueuedOutgoingPackets(),
                    StatVerbosity.Info));
            /*
            StatsManager.RegisterStat(
                new Stat(
                    "IncomingPacketAsyncRequestsWaiting",
                    "Number of incoming packets waiting for async processing in engine.",
                    "",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.None,
                    stat => stat.Value = IpahEngine.JobsWaiting,
                    StatVerbosity.Debug));
            */
            StatsManager.RegisterStat(
                new Stat(
                    "OQRERequestsWaiting",
                    "Number of outgong queue refill requests waiting for processing.",
                    "",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    MeasuresOfInterest.None,
                    stat => stat.Value = OqrEngine.JobsWaiting,
                    StatVerbosity.Debug));

            StatsManager.RegisterStat(
                new Stat(
                    "UDPBuffersPoolCount",
                    "Buffers in the UDP buffers pool",
                    "The number of buffers currently stored within the UDP buffers pool",
                    "",
                    "clientstack",
                    Scene.Name,
                    StatType.Pull,
                    stat => stat.Value = m_udpBuffersPoolPtr + 1,
                    StatVerbosity.Debug));

            LLUDPServerCommands commands = new(MainConsole.Instance, this);
            commands.Register();
        }

        public int GetTotalQueuedOutgoingPackets()
        {
            int total = 0;

            foreach (ScenePresence sp in Scene.GetScenePresences())
            {
                // XXX: Need a better way to determine which IClientAPIs have UDPClients (NPCs do not, for instance).
                if (sp.ControllingClient is LLClientView view)
                {
                    LLUDPClient udpClient = view.UDPClient;
                    total += udpClient.GetTotalPacketsQueuedCount();
                }
            }

            return total;
        }

        /// <summary>
        /// Start the process of sending a packet to the client.
        /// </summary>
        /// <param name="udpClient"></param>
        /// <param name="packet"></param>
        /// <param name="category"></param>
        /// <param name="allowSplitting"></param>
        /// <param name="method">
        /// The method to call if the packet is not acked by the client.  If null, then a standard
        /// resend of the packet is done.
        /// </param>
        public virtual void SendPacket(
            LLUDPClient udpClient, Packet packet, ThrottleOutPacketType category, bool allowSplitting, UnackedPacketMethod method)
        {
            // CoarseLocationUpdate packets cannot be split in an automated way
            if (allowSplitting && packet.HasVariableBlocks && packet.Type != PacketType.CoarseLocationUpdate &&
                    packet.Length + 20 > MTU)
            {
                byte[][] datas = packet.ToBytesMultiple();
                int packetCount = datas.Length;

                if (packetCount < 1)
                    m_log.Error($"[LLUDPSERVER]: Failed to split {packet.Type} with estimated length {packet.Length}");

                for (int i = 0; i < packetCount; i++)
                    SendPacketData(udpClient, datas[i], packet.Type, category, method);
            }
            else
            {
                byte[] data = packet.ToBytes();
                SendPacketData(udpClient, data, packet.Type, category, method);
            }
            PacketPool.Instance.ReturnPacket(packet);
        }

        public static int ZeroEncode(byte[] src, int srclen, byte[] dest)
        {
            Buffer.BlockCopy(src, 0, dest, 0, 6);
 
            int zerolen = 6;
            byte zerocount = 0;

            for (int i = zerolen; i < srclen; i++)
            {
                if (src[i] == 0x00)
                {
                    zerocount++;
                    if (zerocount == 0)
                    {
                        dest[zerolen++] = 0x00;
                        dest[zerolen++] = 0xff;
                        zerocount++;
                    }
                }
                else
                {
                    if (zerocount != 0)
                    {
                        dest[zerolen++] = 0x00;
                        dest[zerolen++] = zerocount;
                        zerocount = 0;
                    }

                    dest[zerolen++] = src[i];
                }
            }

            if (zerocount != 0)
            {
                dest[zerolen++] = 0x00;
                dest[zerolen++] = zerocount;
            }

            return zerolen;
        }
        /// <summary>
        /// Start the process of sending a packet to the client.
        /// </summary>
        /// <param name="udpClient"></param>
        /// <param name="data"></param>
        /// <param name="type"></param>
        /// <param name="category"></param>
        /// <param name="method">
        /// The method to call if the packet is not acked by the client.  If null, then a standard
        /// resend of the packet is done.
        /// </param>
        /// <returns>true if the data was sent immediately, false if it was queued for sending</returns>
        public bool SendPacketData(
            LLUDPClient udpClient, byte[] data, PacketType type, ThrottleOutPacketType category, UnackedPacketMethod method)
        {
            int dataLength = data.Length;
            bool doZerocode = (data[0] & Helpers.MSG_ZEROCODED) != 0;
            bool doCopy = true;

            UDPPacketBuffer buffer = GetNewUDPBuffer(udpClient.RemoteEndPoint);

            // Zerocode if needed
            if (doZerocode)
            {
                try
                {
                    int testlen = ZeroEncode(data, dataLength, buffer.Data);
                    if(testlen <= dataLength)
                    {
                        dataLength = testlen;
                        doCopy = false;
                    }
                    else
                        data[0] = (byte)(data[0] & ~Helpers.MSG_ZEROCODED);
                }
                catch (IndexOutOfRangeException)
                {
                    // The packet grew larger than the bufferSize while zerocoding.
                    // Remove the MSG_ZEROCODED flag and send the unencoded data
                    // instead
                    m_log.Debug($"[LLUDPSERVER]: Packet exceeded buffer size ({buffer.Data.Length}) during zerocoding for {type}. DataLength={dataLength}. Removing MSG_ZEROCODED flag");
                    data[0] = (byte)(data[0] & ~Helpers.MSG_ZEROCODED);
                }
            }

            // If the packet data wasn't already copied during zerocoding, copy it now
            if (doCopy)
            {
                //if (dataLength <= buffer.Data.Length)
                if (dataLength <= LLUDPServer.MTU)
                {
                    Buffer.BlockCopy(data, 0, buffer.Data, 0, dataLength);
                }
                else
                {
                    m_log.Error($"[LLUDPSERVER]: Packet exceeded MTU ({LLUDPServer.MTU}) Type={type}, DataLength={dataLength}");
                    // buffer = new UDPPacketBuffer(udpClient.RemoteEndPoint, dataLength);
                    buffer = GetNewUDPBuffer(udpClient.RemoteEndPoint);
                    Buffer.BlockCopy(data, 0, buffer.Data, 0, dataLength);
                }
            }
            data = null;
            buffer.DataLength = dataLength;

            #region Queue or Send

            OutgoingPacket outgoingPacket = new(udpClient, buffer, category, null);

            // If we were not provided a method for handling unacked, use the UDPServer default method
            if ((outgoingPacket.Buffer.Data[0] & Helpers.MSG_RELIABLE) != 0)
                outgoingPacket.UnackedMethod = method ?? delegate(OutgoingPacket oPacket) { ResendUnacked(oPacket); };

            // If a Linden Lab 1.23.5 client receives an update packet after a kill packet for an object, it will
            // continue to display the deleted object until relog.  Therefore, we need to always queue a kill object
            // packet so that it isn't sent before a queued update packet.
            if (!outgoingPacket.Client.EnqueueOutgoing(outgoingPacket))
            {
                SendPacketFinal(outgoingPacket);
                return true;
            }

            return false;

            #endregion Queue or Send
        }

        public unsafe UDPPacketBuffer ZeroEncode(UDPPacketBuffer input)
        {
            UDPPacketBuffer zb = GetNewUDPBuffer(null);
            int srclen = input.DataLength;
            byte[] src = input.Data;
            byte[] dest = zb.Data;

            int zerolen = 6;
            byte zerocount = 0;

            for (int i = zerolen; i < srclen; i++)
            {
                if (src[i] == 0x00)
                {
                    zerocount++;
                    if (zerocount == 0)
                    {
                        dest[zerolen++] = 0x00;
                        dest[zerolen++] = 0xff;
                        zerocount++;
                    }
                }
                else
                {
                    if (zerocount != 0)
                    {
                        dest[zerolen++] = 0x00;
                        dest[zerolen++] = zerocount;
                        zerocount = 0;
                    }

                    dest[zerolen++] = src[i];
                }
            }

            if (zerocount != 0)
            {
                dest[zerolen++] = 0x00;
                dest[zerolen++] = zerocount;
            }

            if(zerolen >= srclen)
            {
                FreeUDPBuffer(zb);

                src[0] &= unchecked((byte)~Helpers.MSG_ZEROCODED);
                return input;
            }

            Buffer.BlockCopy(src, 0, dest, 0, 6);

            zb.RemoteEndPoint = input.RemoteEndPoint;
            zb.DataLength = zerolen;

            FreeUDPBuffer(input);

            return zb;
        }

        public void SendUDPPacket(
            LLUDPClient udpClient, UDPPacketBuffer buffer, ThrottleOutPacketType category, UnackedPacketMethod method, bool zerocode)
        {
            if (zerocode)
                buffer = ZeroEncode(buffer);

            OutgoingPacket outgoingPacket = new(udpClient, buffer, category, null);

            // If we were not provided a method for handling unacked, use the UDPServer default method
            if ((outgoingPacket.Buffer.Data[0] & Helpers.MSG_RELIABLE) != 0)
                outgoingPacket.UnackedMethod = method ?? delegate (OutgoingPacket oPacket) { ResendUnacked(oPacket); };

            if (!outgoingPacket.Client.EnqueueOutgoing(outgoingPacket))
                SendPacketFinal(outgoingPacket);
        }

        public void SendUDPPacket(LLUDPClient udpClient, UDPPacketBuffer buffer, ThrottleOutPacketType category)
        {
            OutgoingPacket outgoingPacket = new(udpClient, buffer, category, null);

            // If we were not provided a method for handling unacked, use the UDPServer default method
            if ((outgoingPacket.Buffer.Data[0] & Helpers.MSG_RELIABLE) != 0)
                outgoingPacket.UnackedMethod = delegate (OutgoingPacket oPacket) { ResendUnacked(oPacket); };

            if (!outgoingPacket.Client.EnqueueOutgoing(outgoingPacket))
                SendPacketFinal(outgoingPacket);
        }

        static private readonly byte[] PacketAckHeader = new byte[] {
                0,
                0, 0, 0, 0, // sequence number
                0, // extra
                0xff, 0xff, 0xff, 0xfb // ID 65531 (low frequency bigendian)
                };

        public void SendAcks(LLUDPClient udpClient)
        {
            if(udpClient.PendingAcks.Count == 0)
                return;

            UDPPacketBuffer buf = GetNewUDPBuffer(udpClient.RemoteEndPoint);
            Buffer.BlockCopy(PacketAckHeader, 0, buf.Data, 0, 10);
            byte[] data = buf.Data;
            // count at position 10
            int pos = 11;

            int count = 0;
            while (udpClient.PendingAcks.Dequeue(out uint ack))
            {
                Utils.UIntToBytes(ack, data, pos); pos += 4;
                ++count;

                if (count == 255)
                {
                    data[10] = 255;
                    buf.DataLength = pos;
                    SendUDPPacket(udpClient, buf, ThrottleOutPacketType.Unknown);

                    buf = GetNewUDPBuffer(udpClient.RemoteEndPoint);
                    Buffer.BlockCopy(PacketAckHeader, 0, buf.Data, 0, 10);
                    data = buf.Data;
                    pos = 11;
                    count = 0;
                }
            }
            if (count > 0)
            {
                data[10] = (byte)count;
                buf.DataLength = pos;
                SendUDPPacket(udpClient, buf, ThrottleOutPacketType.Unknown);
            }
        }

        static private readonly byte[] StartPingCheckHeader = new byte[] {
                0,
                0, 0, 0, 0, // sequence number
                0, // extra
                1 // ID 1 (high frequency)
                };

        public void SendPing(LLUDPClient udpClient)
        {
            UDPPacketBuffer buf = GetNewUDPBuffer(udpClient.RemoteEndPoint);
            Buffer.BlockCopy(StartPingCheckHeader, 0, buf.Data, 0, 7);
            byte[] data = buf.Data;

            data[7] = udpClient.CurrentPingSequence++;
            // older seq number of our un ack packets, so viewers could clean deduplication lists TODO
            Utils.UIntToBytes(udpClient.NeedAcks.Oldest(), data, 8);

            buf.DataLength = 12;
            SendUDPPacket(udpClient, buf, ThrottleOutPacketType.Unknown);

            udpClient.m_lastStartpingTimeMS = Util.GetTimeStampMS();
        }

        static private readonly byte[] CompletePingCheckHeader = new byte[] {
                0,
                0, 0, 0, 0, // sequence number
                0, // extra
                2 // ID 1 (high frequency)
                };

        public void CompletePing(LLUDPClient udpClient, byte pingID)
        {
            UDPPacketBuffer buf = GetNewUDPBuffer(udpClient.RemoteEndPoint);
            Buffer.BlockCopy(CompletePingCheckHeader, 0, buf.Data, 0, 7);
            byte[] data = buf.Data;

            data[7] = pingID;

            buf.DataLength = 8;
            SendUDPPacket(udpClient, buf, ThrottleOutPacketType.Unknown);
        }

        public void HandleUnacked(LLClientView client)
        {
            LLUDPClient udpClient = client.UDPClient;

            if (!client.IsActive || !udpClient.IsConnected)
                return;

            // Disconnect an agent if no packets are received for some time
            int timeoutTicks = m_ackTimeout;

            // Allow more slack if the client is "paused" eg file upload dialogue is open
            // Some sort of limit is needed in case the client crashes, loses its network connection
            // or some other disaster prevents it from sendung the AgentResume
            if (udpClient.IsPaused)
                timeoutTicks = m_pausedAckTimeout;

            if (client.IsActive &&
                (Environment.TickCount & Int32.MaxValue) - udpClient.TickLastPacketReceived > timeoutTicks)
            {
                // We must set IsActive synchronously so that we can stop the packet loop reinvoking this method, even
                // though it's set later on by LLClientView.Close()
                client.IsActive = false;

                // Fire this out on a different thread so that we don't hold up outgoing packet processing for
                // everybody else if this is being called due to an ack timeout.
                Util.FireAndForget(
                    o => DeactivateClientDueToTimeout(client, timeoutTicks), null, "LLUDPServer.DeactivateClientDueToTimeout");

                return;
            }

            // Get a list of all of the packets that have been sitting unacked longer than udpClient.RTO
            List<OutgoingPacket> expiredPackets = udpClient.NeedAcks.GetExpiredPackets(udpClient.m_RTO);

            if (expiredPackets != null)
            {
                for (int i = 0; i < expiredPackets.Count; ++i)
                    expiredPackets[i].UnackedMethod(expiredPackets[i]);
            }
        }

        public void ResendUnacked(OutgoingPacket outgoingPacket)
        {
            //m_log.DebugFormat("[LLUDPSERVER]: Resending packet #{0} (attempt {1}), {2}ms have passed",
            //    outgoingPacket.SequenceNumber, outgoingPacket.ResendCount, Environment.TickCount - outgoingPacket.TickCount);

            // Set the resent flag
            outgoingPacket.Buffer.Data[0] = (byte)(outgoingPacket.Buffer.Data[0] | Helpers.MSG_RESENT);
            outgoingPacket.Category = ThrottleOutPacketType.Resend;

            // Bump up the resend count on this packet
            Interlocked.Increment(ref outgoingPacket.ResendCount);
            // Requeue or resend the packet
            if (!outgoingPacket.Client.EnqueueOutgoing(outgoingPacket))
                SendPacketFinal(outgoingPacket);
        }

        public void Flush(LLUDPClient udpClient)
        {
            // FIXME: Implement?
        }

        /// <summary>
        /// Actually send a packet to a client.
        /// </summary>
        /// <param name="outgoingPacket"></param>
        internal void SendPacketFinal(OutgoingPacket outgoingPacket)
        {
            UDPPacketBuffer buffer = outgoingPacket.Buffer;
            if(buffer == null) // canceled packet
                return;
            LLUDPClient udpClient = outgoingPacket.Client;
            if (!udpClient.IsConnected)
            {
                FreeUDPBuffer(buffer);
                return;
            }

            byte flags = buffer.Data[0];
            bool isResend = (flags & Helpers.MSG_RESENT) != 0;
            bool isReliable = (flags & Helpers.MSG_RELIABLE) != 0;
            //bool isZerocoded = (flags & Helpers.MSG_ZEROCODED) != 0;

            int dataLength = buffer.DataLength;

            // only append acks on plain reliable messages
            if (flags == Helpers.MSG_RELIABLE && outgoingPacket.UnackedMethod == null)
            {
                // Keep appending ACKs until there is no room left in the buffer or there are
                // no more ACKs to append
                int ackCount = 0;
                while (dataLength + 5 < MTU && ackCount < 256 && udpClient.PendingAcks.Dequeue(out uint ack))
                {
                    Utils.UIntToBytesBig(ack, buffer.Data, dataLength);
                    dataLength += 4;
                    ++ackCount;
                }

                if (ackCount > 0)
                {
                    // Set the last byte of the packet equal to the number of appended ACKs
                    buffer.Data[dataLength++] = (byte)ackCount;
                    // Set the appended ACKs flag on this packet
                    buffer.Data[0] = (byte)(buffer.Data[0] | Helpers.MSG_APPENDED_ACKS);
                }
                buffer.DataLength = dataLength;
            }

            if (!isResend)
            {
                // Not a resend, assign a new sequence number
                uint sequenceNumber = (uint)Interlocked.Increment(ref udpClient.CurrentSequence);
                Utils.UIntToBytesBig(sequenceNumber, buffer.Data, 1);
                outgoingPacket.SequenceNumber = sequenceNumber;
            }
            else
            {
                Interlocked.Increment(ref udpClient.PacketsResent);
                PacketsResentCount++;
            }

            // Stats tracking
            Interlocked.Increment(ref udpClient.PacketsSent);
            PacketsSentCount++;

            SyncSend(buffer);

            // Keep track of when this packet was sent out (right now)
            Interlocked.Exchange(ref outgoingPacket.TickCount, Environment.TickCount & Int32.MaxValue);

            if (outgoingPacket.UnackedMethod == null)
                FreeUDPBuffer(buffer);
            else if(!isResend)
            {
                // Add this packet to the list of ACK responses we are waiting on from the server
                udpClient.NeedAcks.Add(outgoingPacket);
            }

            if (udpClient.DebugDataOutLevel > 0)
                m_log.Debug(
                    $"[LLUDPSERVER]: Sending packet #{outgoingPacket.SequenceNumber} (rel: {isReliable}, res: {isResend}) to {udpClient.AgentID} from {Scene.Name}");
        }

        protected void RecordMalformedInboundPacket(IPEndPoint endPoint)
        {
            //if (m_malformedCount < 100)
            //   m_log.DebugFormat("[LLUDPSERVER]: Dropped malformed packet: " + e.Message);

            IncomingMalformedPacketCount++;

            if ((IncomingMalformedPacketCount % 10000) == 0)
                m_log.Warn(
                    $"[LLUDPSERVER]: Received {IncomingMalformedPacketCount} malformed packets so far, probable network attack.  Last was from {endPoint}");
        }

        public override void PacketReceived(UDPPacketBuffer buffer)
        {
            // Debugging/Profiling
            //try { Thread.CurrentThread.Name = "PacketReceived (" + m_scene.RegionInfo.RegionName + ")"; }
            //catch (Exception) { }
            //m_log.DebugFormat(
            //    "[LLUDPSERVER]: Packet received from {0} in {1}", buffer.RemoteEndPoint, m_scene.RegionInfo.RegionName);

            Packet packet = null;
            int bufferLen = buffer.DataLength;
            IPEndPoint endPoint = (IPEndPoint)buffer.RemoteEndPoint;

            #region Decoding

            if (bufferLen < 7)
            {
                //m_log.WarnFormat(
                //    "[LLUDPSERVER]: Dropping undersized packet with {0} bytes received from {1} in {2}",
                //    buffer.DataLength, buffer.RemoteEndPoint, m_scene.RegionInfo.RegionName);
                RecordMalformedInboundPacket(endPoint);
                FreeUDPBuffer(buffer);
                return; // Drop undersized packet
            }

            int bufferDataptr = buffer.Data[5] + 6;
            int headerLen = 7;
            if (buffer.Data[bufferDataptr] == 0xFF)
            {
                if (buffer.Data[bufferDataptr + 1] == 0xFF)
                    headerLen = 10;
                else
                    headerLen = 8;
            }

            headerLen += buffer.Data[5];
            if (bufferLen < headerLen)
            {
                //m_log.WarnFormat(
                //    "[LLUDPSERVER]: Dropping packet with malformed header received from {0} in {1}",
                //    buffer.RemoteEndPoint, m_scene.RegionInfo.RegionName);
                RecordMalformedInboundPacket(endPoint);
                FreeUDPBuffer(buffer);
                return; // Malformed header
            }

            try
            {
                // get a buffer for zero decode using the udp buffers pool
                UDPPacketBuffer zerodecodebufferholder = null;
                byte[] zerodecodebuffer = null;
                // only if needed
                if (((buffer.Data[0] & Helpers.MSG_ZEROCODED) != 0))
                {
                    zerodecodebufferholder = GetNewUDPBuffer(null);
                    zerodecodebuffer = zerodecodebufferholder.Data;
                }

                packet = Packet.BuildPacket(buffer.Data, ref bufferLen, zerodecodebuffer);
                if(zerodecodebufferholder != null)
                    FreeUDPBuffer(zerodecodebufferholder);
            }
            catch (Exception e)
            {
                if (IncomingMalformedPacketCount < 100)
                    m_log.Debug("[LLUDPSERVER]: Dropped malformed packet: " + e.ToString());
            }

            // Fail-safe check
            if (packet == null)
            {
                if (IncomingMalformedPacketCount < 100)
                {
                    m_log.WarnFormat("[LLUDPSERVER]: Malformed data, cannot parse {0} byte packet from {1}, data {2}:",
                        buffer.DataLength, buffer.RemoteEndPoint, Utils.BytesToHexString(buffer.Data, buffer.DataLength, null));
                }

                RecordMalformedInboundPacket(endPoint);
                FreeUDPBuffer(buffer);
                return;
            }

            #endregion Decoding


            #region Packet to Client Mapping

            // If still creating a new client just enqueue until it is done
            lock (m_pendingCache)
            {
                if (m_pendingCache.TryGetValue(endPoint, out Queue<UDPPacketBuffer> queue))
                {
                    if (packet.Type == PacketType.UseCircuitCode) // ignore viewer resends
                    {
                        FreeUDPBuffer(buffer);
                        SendAckImmediate(endPoint, packet.Header.Sequence); // i hear you shutup
                        return;
                    }

                    //m_log.DebugFormat("[LLUDPSERVER]: Enqueued a {0} packet into the pending queue", packet.Type);
                    queue.Enqueue(buffer);
                    return;
                }
            }

            // usecircuitcode handling
            if (!Scene.TryGetClient(endPoint, out IClientAPI client))
            {
                // UseCircuitCode handling
                if (packet.Type == PacketType.UseCircuitCode)
                {
                    lock (m_pendingCache)
                    {
                        if (m_pendingCache.Contains(endPoint))
                        {
                            // this should not happen
                            FreeUDPBuffer(buffer);
                            SendAckImmediate(endPoint, packet.Header.Sequence); // i hear you shutup
                            return;
                        }
                        m_pendingCache.AddOrUpdate(endPoint, new Queue<UDPPacketBuffer>(), 60);
                    }

                    Util.FireAndForget(HandleUseCircuitCode, new object[] { endPoint, packet });
                    FreeUDPBuffer(buffer);
                    SendAckImmediate(endPoint, packet.Header.Sequence);
                    return;
                }
            }

            FreeUDPBuffer(buffer);

            // Determine which agent this packet came from
            if (client == null || client is not LLClientView lclient)
            {
                //m_log.Debug("[LLUDPSERVER]: Received a " + packet.Type + " packet from an unrecognized source: " + address + " in " + m_scene.RegionInfo.RegionName);

                IncomingOrphanedPacketCount++;

                if ((IncomingOrphanedPacketCount % 10000) == 0)
                    m_log.Warn(
                        $"[LLUDPSERVER]: Received {IncomingOrphanedPacketCount} orphaned packets so far.  Last was from {endPoint}");

                return;
            }

            LLUDPClient udpClient = lclient.UDPClient;

            if (!udpClient.IsConnected)
            {
                m_log.Debug($"[LLUDPSERVER]: Received a {packet.Type} packet for a unConnected client in {Scene.Name}");
                return;
            }

            #endregion Packet to Client Mapping

            // Stats tracking
            Interlocked.Increment(ref udpClient.PacketsReceived);

            int now = Environment.TickCount & Int32.MaxValue;
            udpClient.TickLastPacketReceived = now;

            if(packet.NeedValidateIDs)
            {
                if(!packet.ValidIDs(lclient.m_sessionId, lclient.m_agentId))
                    return;
            }

            #region ACK Receiving

            // Handle appended ACKs
            if (packet.Header.AppendedAcks && packet.Header.AckList != null)
            {
                // m_log.DebugFormat(
                //      "[LLUDPSERVER]: Handling {0} appended acks from {1} in {2}",
                //      packet.Header.AckList.Length, client.Name, m_scene.Name);

                for (int i = 0; i < packet.Header.AckList.Length; i++)
                    udpClient.NeedAcks.Acknowledge(packet.Header.AckList[i], now, packet.Header.Resent);
            }

            // Handle PacketAck packets
            if (packet.Type == PacketType.PacketAck)
            {
                PacketAckPacket ackPacket = (PacketAckPacket)packet;

                // m_log.DebugFormat(
                //      "[LLUDPSERVER]: Handling {0} packet acks for {1} in {2}",
                //      ackPacket.Packets.Length, client.Name, m_scene.Name);

                for (int i = 0; i < ackPacket.Packets.Length; i++)
                    udpClient.NeedAcks.Acknowledge(ackPacket.Packets[i].ID, now, packet.Header.Resent);

                // We don't need to do anything else with PacketAck packets
                return;
            }

            #endregion ACK Receiving

            #region ACK Sending

            if (packet.Header.Reliable)
            {
                //m_log.DebugFormat(
                //    "[LLUDPSERVER]: Adding ack request for {0} {1} from {2} in {3}",
                //    packet.Type, packet.Header.Sequence, client.Name, m_scene.Name);

                udpClient.PendingAcks.Enqueue(packet.Header.Sequence);

                // This is a somewhat odd sequence of steps to pull the client.BytesSinceLastACK value out,
                // add the current received bytes to it, test if 2*MTU bytes have been sent, if so remove
                // 2*MTU bytes from the value and send ACKs, and finally add the local value back to
                // client.BytesSinceLastACK. Lockless thread safety
                int bytesSinceLastACK = Interlocked.Exchange(ref udpClient.BytesSinceLastACK, 0);
                bytesSinceLastACK += buffer.DataLength;
                if (bytesSinceLastACK > LLUDPServer.MTU * 2)
                {
                    bytesSinceLastACK -= LLUDPServer.MTU * 2;
                    SendAcks(udpClient);
                }
                Interlocked.Add(ref udpClient.BytesSinceLastACK, bytesSinceLastACK);
            }

            #endregion ACK Sending

            #region Incoming Packet Accounting

            // We're not going to worry about interlock yet since its not currently critical that this total count
            // is 100% correct
            if (packet.Header.Resent)
                IncomingPacketsResentCount++;

            // Check the archive of received reliable packet IDs to see whether we already received this packet
            if (packet.Header.Reliable && !udpClient.PacketArchive.TryEnqueue(packet.Header.Sequence))
            {
                if (packet.Header.Resent)
                    m_log.Debug(
                        $"[LLUDPSERVER]: Received a resend of already processed packet #{packet.Header.Sequence}, type {packet.Type} from {client.Name}");
                 else
                    m_log.Warn(
                        $"[LLUDPSERVER]: Received a duplicate (not marked as resend) of packet #{packet.Header.Sequence}, type {packet.Type} from {client.Name}");

                // Avoid firing a callback twice for the same packet
                return;
            }

            #endregion Incoming Packet Accounting

            #region BinaryStats
            LogPacketHeader(true, udpClient.CircuitCode, 0, packet.Type, (ushort)packet.Length);
            #endregion BinaryStats

            #region Ping Check Handling

            if (packet.Type == PacketType.StartPingCheck)
            {
                //m_log.DebugFormat("[LLUDPSERVER]: Handling ping from {0} in {1}", client.Name, m_scene.Name);

                // We don't need to do anything else with ping checks
                StartPingCheckPacket startPing = (StartPingCheckPacket)packet;
                CompletePing(udpClient, startPing.PingID.PingID);

                if ((Environment.TickCount - m_elapsedMSSinceLastStatReport) >= 3000)
                {
                    udpClient.SendPacketStats();
                    m_elapsedMSSinceLastStatReport = Environment.TickCount;
                }
                return;
            }
            else if (packet.Type == PacketType.CompletePingCheck)
            {
                double t = Util.GetTimeStampMS() - udpClient.m_lastStartpingTimeMS;
                double c = 0.8 * udpClient.m_pingMS;
                c += 0.2 * t;
                int p = (int)c;
                udpClient.m_pingMS = p;
                udpClient.UpdateRoundTrip(p);
                return;
            }

            #endregion Ping Check Handling

            packetInbox.Add(new IncomingPacket((LLClientView)client, packet));
        }

        #region BinaryStats

        public class PacketLogger
        {
            public DateTime StartTime;
            public string Path = null;
            public System.IO.BinaryWriter Log = null;
        }

        public static PacketLogger PacketLog;

        protected static bool m_shouldCollectStats = false;
        // Number of seconds to log for
        static TimeSpan binStatsMaxFilesize = TimeSpan.FromSeconds(300);
        static readonly object binStatsLogLock = new();
        static string binStatsDir = "";

        //for Aggregated In/Out BW logging
        static bool m_aggregatedBWStats = false;
        static long m_aggregatedBytesIn = 0;
        static long m_aggregatedByestOut = 0;
        static readonly object aggBWStatsLock = new();

        public static long AggregatedLLUDPBytesIn
        {
            get { return m_aggregatedBytesIn; }
        }
        public static long AggregatedLLUDPBytesOut
        {
            get {return m_aggregatedByestOut;}
        }

        public static void LogPacketHeader(bool incoming, uint circuit, byte flags, PacketType packetType, ushort size)
        {
            if (m_aggregatedBWStats)
            {
                lock (aggBWStatsLock)
                {
                    if (incoming)
                        m_aggregatedBytesIn += size;
                    else
                        m_aggregatedByestOut += size;
                }
            }

            if (!m_shouldCollectStats) return;

            // Binary logging format is TTTTTTTTCCCCFPPPSS, T=Time, C=Circuit, F=Flags, P=PacketType, S=size

            // Put the incoming bit into the least significant bit of the flags byte
            if (incoming)
                flags |= 0x01;
            else
                flags &= 0xFE;

            // Put the flags byte into the most significant bits of the type integer
            uint type = (uint)packetType;
            type |= (uint)flags << 24;

            // m_log.Debug("1 LogPacketHeader(): Outside lock");
            lock (binStatsLogLock)
            {
                DateTime now = DateTime.Now;

                // m_log.Debug("2 LogPacketHeader(): Inside lock. now is " + now.Ticks);
                try
                {
                    if (PacketLog == null || (now > PacketLog.StartTime + binStatsMaxFilesize))
                    {
                        if (PacketLog != null && PacketLog.Log != null)
                        {
                            PacketLog.Log.Close();
                        }

                        // First log file or time has expired, start writing to a new log file
                        PacketLog = new PacketLogger
                        {
                            StartTime = now,
                            Path = (binStatsDir.Length > 0 ? binStatsDir + System.IO.Path.DirectorySeparatorChar.ToString() : "")
                                + String.Format("packets-{0}.log", now.ToString("yyyyMMddHHmmss"))
                        };
                        PacketLog.Log = new BinaryWriter(File.Open(PacketLog.Path, FileMode.Append, FileAccess.Write));
                    }

                    // Serialize the data
                    byte[] output = new byte[18];
                    Buffer.BlockCopy(BitConverter.GetBytes(now.Ticks), 0, output, 0, 8);
                    Buffer.BlockCopy(BitConverter.GetBytes(circuit), 0, output, 8, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(type), 0, output, 12, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(size), 0, output, 16, 2);

                    // Write the serialized data to disk
                    if (PacketLog != null && PacketLog.Log != null)
                        PacketLog.Log.Write(output);
                }
                catch (Exception ex)
                {
                    m_log.Error($"Packet statistics gathering failed: {ex.Message}");
                    if (PacketLog.Log != null)
                    {
                        PacketLog.Log.Close();
                    }
                    PacketLog = null;
                }
            }
        }

        #endregion BinaryStats

        protected void HandleUseCircuitCode(object o)
        {
            IPEndPoint endPoint = null;
            IClientAPI client = null;

            try
            {
                object[] array = (object[])o;
                endPoint = (IPEndPoint)array[0];
                UseCircuitCodePacket uccp = (UseCircuitCodePacket)array[1];

                m_log.DebugFormat(
                    $"[LLUDPSERVER]: Handling UseCircuitCode request for circuit {uccp.CircuitCode.Code} to {Scene.Name} from IP {endPoint}");

                if (IsClientAuthorized(uccp, out AuthenticateResponse sessionInfo))
                {
                    AgentCircuitData aCircuit = m_circuitManager.GetAgentCircuitData(uccp.CircuitCode.Code);
                    if(!string.IsNullOrEmpty(aCircuit.IPAddress))
                    {
                        try
                        {
                            IPAddress aIP = IPAddress.Parse(aCircuit.IPAddress);
                            if(!endPoint.Address.Equals(aIP))
                                m_log.Debug($"[LLUDPSERVER]: HandleUseCircuitCode IP mismatch {endPoint.Address} != {aCircuit.IPAddress}");
                        }
                        catch
                        {
                            m_log.Debug($"[LLUDPSERVER]: HandleUseCircuitCode could not compare IP {endPoint.Address} {aCircuit.IPAddress}");
                        }
                    }

                    // Begin the process of adding the client to the simulator
                    client = AddClient(
                            uccp.CircuitCode.Code,
                            uccp.CircuitCode.ID,
                            uccp.CircuitCode.SessionID,
                            endPoint,
                            sessionInfo);

                    if (client == null)
                    {
                        lock (m_pendingCache)
                            m_pendingCache.Remove(endPoint);
                        return;
                    }

                    // This will be true if the client is new, e.g. not
                    // an existing child agent, and there is no circuit data
                    if (aCircuit == null)
                    {
                        Scene.CloseAgent(client.AgentId, true);
                        lock (m_pendingCache)
                            m_pendingCache.Remove(endPoint);
                        return;
                    }

                    m_log.Debug("[LLUDPSERVER]: Client created");

                    Queue<UDPPacketBuffer> queue = null;
                    lock (m_pendingCache)
                    {
                        if (m_pendingCache.TryGetValue(endPoint, out queue))
                            m_pendingCache.Remove(endPoint);
                        else
                            m_log.Debug("[LLUDPSERVER]: HandleUseCircuitCode with no pending queue present");
                    }

                    // Reinject queued packets
                    if(queue != null)
                    {
                        m_log.Debug($"[LLUDPSERVER]: processing UseCircuitCode pending queue, {queue.Count} entries");
                        while (queue.Count > 0)
                        {
                            UDPPacketBuffer buf = queue.Dequeue();
                            PacketReceived(buf);
                        }
                        queue = null;
                    }

                    if(aCircuit.teleportFlags <= 0)
                        client.SendRegionHandshake();
                }
                else
                {
                    // Don't create clients for unauthorized requesters.
                    m_log.WarnFormat(
                        "[LLUDPSERVER]: Ignoring connection request for {0} to {1} with unknown circuit code {2} from IP {3}",
                        uccp.CircuitCode.ID, Scene.RegionInfo.RegionName, uccp.CircuitCode.Code, endPoint);

                    lock (m_pendingCache)
                        m_pendingCache.Remove(endPoint);
                }

                //m_log.DebugFormat(
                //    "[LLUDPSERVER]: Handling UseCircuitCode request from {0} took {1}ms",
                //     buffer.RemoteEndPoint, (DateTime.Now - startTime).Milliseconds);

            }
            catch (Exception e)
            {
                m_log.ErrorFormat(
                    "[LLUDPSERVER]: UseCircuitCode handling from endpoint {0}, client {1} {2} failed.  Exception {3}{4}",
                    endPoint != null ? endPoint.ToString() : "n/a",
                    client != null ? client.Name : "unknown",
                    client != null ? client.AgentId.ToString() : "unknown",
                    e.Message,
                    e.StackTrace);
            }
        }

        /// <summary>
        /// Send an ack immediately to the given endpoint.
        /// </summary>
        /// <remarks>
        /// FIXME: Might be possible to use SendPacketData() like everything else, but this will require refactoring so
        /// that we can obtain the UDPClient easily at this point.
        /// </remarks>
        /// <param name="remoteEndpoint"></param>
        /// <param name="sequenceNumber"></param>
        protected void SendAckImmediate(IPEndPoint remoteEndpoint, uint sequenceNumber)
        {
            PacketAckPacket ack = new();
            ack.Header.Reliable = false;
            ack.Packets = new PacketAckPacket.PacketsBlock[1];
            ack.Packets[0] = new PacketAckPacket.PacketsBlock
            {
                ID = sequenceNumber
            };

            SendAckImmediate(remoteEndpoint, ack);
        }

        public virtual void SendAckImmediate(IPEndPoint remoteEndpoint, PacketAckPacket ack)
        {
            byte[] packetData = ack.ToBytes();
            int length = packetData.Length;

            UDPPacketBuffer buffer = GetNewUDPBuffer(remoteEndpoint);
            buffer.DataLength = length;

            Buffer.BlockCopy(packetData, 0, buffer.Data, 0, length);

            SyncSend(buffer);
            FreeUDPBuffer(buffer);
        }

        protected bool IsClientAuthorized(UseCircuitCodePacket useCircuitCode, out AuthenticateResponse sessionInfo)
        {
            UseCircuitCodePacket.CircuitCodeBlock ucb = useCircuitCode.CircuitCode;
            sessionInfo = m_circuitManager.AuthenticateSession(ucb.SessionID, ucb.ID, ucb.Code);
            return sessionInfo.Authorised;
        }

        /// <summary>
        /// Add a client.
        /// </summary>
        /// <param name="circuitCode"></param>
        /// <param name="agentID"></param>
        /// <param name="sessionID"></param>
        /// <param name="remoteEndPoint"></param>
        /// <param name="sessionInfo"></param>
        /// <returns>The client if it was added.  Null if the client already existed.</returns>
        protected virtual IClientAPI AddClient(
            uint circuitCode, UUID agentID, UUID sessionID, IPEndPoint remoteEndPoint, AuthenticateResponse sessionInfo)
        {
            IClientAPI client = null;

            // We currently synchronize this code across the whole scene to avoid issues such as
            // http://opensimulator.org/mantis/view.php?id=5365  However, once locking per agent circuit can be done
            // consistently, this lock could probably be removed.
            lock (this)
            {
                if (Scene.TryGetClient(agentID, out client))
                {
                    if (client.SceneAgent is not null &&
                            client.CircuitCode == circuitCode &&
                            client.SessionId == sessionID &&
                            client.RemoteEndPoint == remoteEndPoint &&
                            client.SceneAgent.ControllingClient.SecureSessionId.Equals(sessionInfo.LoginInfo.SecureSession))
                        return client;
                    Scene.CloseAgent(agentID, true);
                }

                LLUDPClient udpClient = new(this, ThrottleRates, Throttle, circuitCode, agentID, remoteEndPoint, m_defaultRTO, m_maxRTO);

                client = new LLClientView(Scene, this, udpClient, sessionInfo, agentID, sessionID, circuitCode);
                client.OnLogout += LogoutHandler;
                client.DebugPacketLevel = DefaultClientPacketDebugLevel;

                ((LLClientView)client).DisableFacelights = m_disableFacelights;

                client.Start();
            }

            return client;
        }

        /// <summary>
        /// Deactivates the client if we don't receive any packets within a certain amount of time (default 60 seconds).
        /// </summary>
        /// <remarks>
        /// If a connection is active then we will always receive packets even if nothing else is happening, due to
        /// regular client pings.
        /// </remarks>
        /// <param name='client'></param>
        /// <param name='timeoutTicks'></param>
        protected void DeactivateClientDueToTimeout(LLClientView client, int timeoutTicks)
        {
            ClientLogoutsDueToNoReceives++;
            if (client.SceneAgent != null)
            {
                m_log.WarnFormat(
                    "[LLUDPSERVER]: No packets received from {0} agent of {1} for {2}ms in {3}.  Disconnecting.",
                    client.SceneAgent.IsChildAgent ? "child" : "root", client.Name, timeoutTicks, Scene.Name);
            }

            if (!Scene.CloseAgent(client.AgentId, true))
                client.Close(true,true);
        }

        protected void IncomingPacketHandler()
        {
            // Set this culture for the thread that incoming packets are received
            // on to en-US to avoid number parsing issues
            Culture.SetCurrentCulture();

            while (m_IsRunningInbound)
            {
                Scene.ThreadAlive(1);
                try
                {
                    if (packetInbox.TryTake(out IncomingPacket incomingPacket, 4500) && IsRunningInbound)
                    {
                        if (incomingPacket.Client.IsActive)
                        {
                            incomingPacket.Client.ProcessInPacket(incomingPacket.Packet);
                            IncomingPacketsProcessed++;
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    // If something is trying to abort the packet processing thread, take that as a hint that it's time to shut down
                    m_log.Info("[LLUDPSERVER]: Caught a thread abort, shutting down the LLUDP server");
                    Stop();
                }
                catch (Exception e)
                {
                    // Don't let a failure in an individual client thread crash the whole sim.
                    m_log.Error($"[LLUDPSERVER]: IncomingPacketHandler threw ", e);
                }

            Watchdog.UpdateThread();
            }

            if (packetInbox.Count > 0)
                m_log.Warn("[LLUDPSERVER]: IncomingPacketHandler is shutting down, dropping " + packetInbox.Count + " packets");
            packetInbox.Dispose();

            Watchdog.RemoveThread();
        }

        protected void OutgoingPacketHandler()
        {
            // Set this culture for the thread that outgoing packets are sent
            // on to en-US to avoid number parsing issues
            Culture.SetCurrentCulture();

            // Typecast the function to an Action<IClientAPI> once here to avoid allocating a new
            // Action generic every round
            Action<IClientAPI> clientPacketHandler = ClientOutgoingPacketHandler;

            while (m_IsRunningOutbound)
            {
                Scene.ThreadAlive(2);

                try
                {
                    m_packetSent = false;

                    #region Update Timers

                    m_resendUnacked = false;
                    m_sendAcks = false;
                    m_sendPing = false;

                    // Update elapsed time
                    double thisTick = Util.GetTimeStampMS();

                    // update some 1ms resolution chained timers
                    m_elapsedMSOutgoingPacketHandler +=  thisTick - m_tickLastOutgoingPacketHandler;
                    m_tickLastOutgoingPacketHandler = thisTick;

                    // Check for pending outgoing resends every 100ms
                    if (m_elapsedMSOutgoingPacketHandler >= 100.0)
                    {
                        m_resendUnacked = true;
                        m_elapsedMSOutgoingPacketHandler = 0.0;
                        ++m_elapsed100MSOutgoingPacketHandler;
                    }

                    // Check for pending outgoing ACKs every 500ms
                    if (m_elapsed100MSOutgoingPacketHandler >= 5)
                    {
                        m_sendAcks = true;
                        m_elapsed100MSOutgoingPacketHandler = 0;
                        ++m_elapsed500MSOutgoingPacketHandler;
                    }

                    // Send pings to clients every 5000ms
                    if (m_elapsed500MSOutgoingPacketHandler >= 10)
                    {
                        m_sendPing = true;
                        m_elapsed500MSOutgoingPacketHandler = 0;
                    }
                    #endregion Update Timers

                    // Handle outgoing packets, resends, acknowledgements, and pings for each
                    // client. m_packetSent will be set to true if a packet is sent
                    Scene.ForEachClient(clientPacketHandler);

                    // If nothing was sent, sleep for the minimum amount of time before a
                    // token bucket could get more tokens

                    if(Scene.GetNumberOfClients() == 0)
                    {
                        Thread.Sleep(100);
                    }
                    else if (!m_packetSent)
                        Thread.Sleep(15); // match the 16ms of windows, dont ask 16 or win may decide to do 32ms.

                    Watchdog.UpdateThread();
                }
                catch (Exception ex)
                {
                    m_log.Error("[LLUDPSERVER]: OutgoingPacketHandler loop threw an exception: " + ex.Message, ex);
                }
            }

            Watchdog.RemoveThread();
        }

        protected void ClientOutgoingPacketHandler(IClientAPI client)
        {
            try
            {
                if (client is LLClientView llClient)
                {
                    LLUDPClient udpClient = llClient.UDPClient;

                    if (udpClient.IsConnected)
                    {
                        if (client.IsActive && m_resendUnacked)
                            HandleUnacked(llClient);

                        if (client.IsActive)
                        {
                            if (m_sendAcks)
                                SendAcks(udpClient);

                            if (m_sendPing)
                                SendPing(udpClient);
                        }

                        // Dequeue any outgoing packets that are within the throttle limits
                        if (udpClient.DequeueOutgoing())
                            m_packetSent = true;
                    }
                }
            }
            catch (Exception ex)
            {
                m_log.Error(
                    string.Format("[LLUDPSERVER]: OutgoingPacketHandler iteration for {0} threw ", client.Name), ex);
            }
        }

        #region Emergency Monitoring
        // Alternative packet handler fuull of instrumentation
        // Handy for hunting bugs
        protected Stopwatch watch1 = new();
        protected Stopwatch watch2 = new();

        protected float avgProcessingTicks = 0;
        protected float avgResendUnackedTicks = 0;
        protected float avgSendAcksTicks = 0;
        protected float avgSendPingTicks = 0;
        protected float avgDequeueTicks = 0;
        protected long nticks = 0;
        protected long nticksUnack = 0;
        protected long nticksAck = 0;
        protected long nticksPing = 0;
        protected int npacksSent = 0;
        protected int npackNotSent = 0;

        /// <summary>
        /// Number of inbound packets processed since startup.
        /// </summary>
        public long IncomingPacketsProcessed { get; protected set; }

        #endregion

        protected void LogoutHandler(IClientAPI client)
        {
            client.SendLogoutPacket();

            if (!client.IsLoggingOut)
            {
                client.IsLoggingOut = true;
                Scene.CloseAgent(client.AgentId, false);
            }
        }
    }
}
