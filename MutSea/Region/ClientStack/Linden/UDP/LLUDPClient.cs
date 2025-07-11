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
using System.Threading;
using log4net;
using MutSea.Framework;
using OpenMetaverse;

using TokenBucket = MutSea.Region.ClientStack.LindenUDP.TokenBucket;

namespace MutSea.Region.ClientStack.LindenUDP
{
    #region Delegates

    /// <summary>
    /// Fired when updated networking stats are produced for this client
    /// </summary>
    /// <param name="inPackets">Number of incoming packets received since this
    /// event was last fired</param>
    /// <param name="outPackets">Number of outgoing packets sent since this
    /// event was last fired</param>
    /// <param name="unAckedBytes">Current total number of bytes in packets we
    /// are waiting on ACKs for</param>
    public delegate void PacketStats(int inPackets, int outPackets, int unAckedBytes);
    /// <summary>
    /// Fired when the queue for one or more packet categories is empty. This
    /// event can be hooked to put more data on the empty queues
    /// </summary>
    /// <param name="category">Categories of the packet queues that are empty</param>
    public delegate void QueueEmpty(ThrottleOutPacketTypeFlags categories);

    #endregion Delegates

    /// <summary>
    /// Tracks state for a client UDP connection and provides client-specific methods
    /// </summary>
    public sealed class LLUDPClient
    {
        private static readonly ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The number of packet categories to throttle on. If a throttle category is added
        /// or removed, this number must also change</summary>
        const int THROTTLE_CATEGORY_COUNT = 8;

        /// <summary>
        /// Controls whether information is logged about each outbound packet immediately before it is sent.  For debug purposes.
        /// </summary>
        /// <remarks>Any level above 0 will turn on logging.</remarks>
        public int DebugDataOutLevel { get; set; }

        /// <summary>
        /// Controls whether information is logged about each outbound packet immediately before it is sent.  For debug purposes.
        /// </summary>
        /// <remarks>Any level above 0 will turn on logging.</remarks>
        public int ThrottleDebugLevel { get; set; }

        /// <summary>Fired when updated networking stats are produced for this client</summary>
        public event PacketStats OnPacketStats;
        /// <summary>Fired when the queue for a packet category is empty. This event can be
        /// hooked to put more data on the empty queue</summary>
        public event QueueEmpty OnQueueEmpty;

        public event Func<ThrottleOutPacketTypeFlags, bool> HasUpdates;

        /// <summary>AgentID for this client</summary>
        public readonly UUID AgentID;
        /// <summary>The remote address of the connected client</summary>
        public readonly IPEndPoint RemoteEndPoint;
        /// <summary>Circuit code that this client is connected on</summary>
        public readonly uint CircuitCode;
        /// <summary>Sequence numbers of packets we've received (for duplicate checking)</summary>
        public IncomingPacketHistoryCollection PacketArchive = new(1024);

        /// <summary>Packets we have sent that need to be ACKed by the client</summary>
        public UnackedPacketCollection NeedAcks = new();

        /// <summary>ACKs that are queued up, waiting to be sent to the client</summary>
        public DoubleLocklessQueue<uint> PendingAcks = new();

        public int AckStalls;

        /// <summary>Current packet sequence number</summary>
        public int CurrentSequence;
        /// <summary>Current ping sequence number</summary>
        public byte CurrentPingSequence;
        /// <summary>True when this connection is alive, otherwise false</summary>
        public bool IsConnected = true;
        /// <summary>True when this connection is paused, otherwise false</summary>
        public bool IsPaused;
        /// <summary>Environment.TickCount when the last packet was received for this client</summary>
        public int TickLastPacketReceived;

        /// <summary>Smoothed round-trip time. A smoothed average of the round-trip time for sending a
        /// reliable packet to the client and receiving an ACK</summary>
        public float SRTT;
        /// <summary>Round-trip time variance. Measures the consistency of round-trip times</summary>
        public float RTTVAR;
        /// <summary>Retransmission timeout. Packets that have not been acknowledged in this number of
        /// milliseconds or longer will be resent</summary>
        /// <remarks>Calculated from <seealso cref="SRTT"/> and <seealso cref="RTTVAR"/> using the
        /// guidelines in RFC 2988</remarks>
        public int m_RTO;
        /// <summary>Number of bytes received since the last acknowledgement was sent out. This is used
        /// to loosely follow the TCP delayed ACK algorithm in RFC 1122 (4.2.3.2)</summary>
        public int BytesSinceLastACK;
        /// <summary>Number of packets received from this client</summary>
        public int PacketsReceived;
        /// <summary>Number of packets sent to this client</summary>
        public int PacketsSent;
        /// <summary>Number of packets resent to this client</summary>
        public int PacketsResent;
        /// <summary>Total byte count of unacked packets sent to this client</summary>
        public int UnackedBytes;

        private int m_packetsUnAckReported;
        /// <summary>Total number of received packets that we have reported to the OnPacketStats event(s)</summary>
        private int m_packetsReceivedReported;
        /// <summary>Total number of sent packets that we have reported to the OnPacketStats event(s)</summary>
        private int m_packetsSentReported;
        /// <summary>Holds the Environment.TickCount value of when the next OnQueueEmpty can be fired</summary>
        private double m_nextOnQueueEmpty = 0;

        /// <summary>Throttle bucket for this agent's connection</summary>
        private readonly AdaptiveTokenBucket m_throttleClient;
        public AdaptiveTokenBucket FlowThrottle
        {
            get { return m_throttleClient; }
        }

        /// <summary>Throttle buckets for each packet category</summary>
        private readonly TokenBucket[] m_throttleCategories;
        /// <summary>Outgoing queues for throttled packets</summary>
        private readonly DoubleLocklessQueue<OutgoingPacket>[] m_packetOutboxes = new DoubleLocklessQueue<OutgoingPacket>[THROTTLE_CATEGORY_COUNT];
        /// <summary>A container that can hold one packet for each outbox, used to store
        /// dequeued packets that are being held for throttling</summary>
        private readonly OutgoingPacket[] m_nextPackets = new OutgoingPacket[THROTTLE_CATEGORY_COUNT];
        /// <summary>A reference to the LLUDPServer that is managing this client</summary>
        private readonly LLUDPServer m_udpServer;

        private readonly int m_defaultRTO = 1000; // 1sec is the recommendation in the RFC
        private readonly int m_maxRTO = 3000;
        private readonly int m_minRTO = 250;

        private readonly float m_burstTime;
        private readonly float m_maxRate;

        public double m_lastStartpingTimeMS;
        public int m_pingMS;

        public int PingTimeMS
        {
            get
            {
                if (m_pingMS < 10)
                    return 10;
                if(m_pingMS > 2000)
                    return 2000;
                return m_pingMS;
            }
        }

        private readonly ClientInfo m_info = new();

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <param name="server">Reference to the UDP server this client is connected to</param>
        /// <param name="rates">Default throttling rates and maximum throttle limits</param>
        /// <param name="parentThrottle">Parent HTB (hierarchical token bucket)
        /// that the child throttles will be governed by</param>
        /// <param name="circuitCode">Circuit code for this connection</param>
        /// <param name="agentID">AgentID for the connected agent</param>
        /// <param name="remoteEndPoint">Remote endpoint for this connection</param>
        /// <param name="defaultRTO">
        /// Default retransmission timeout for unacked packets.  The RTO will never drop
        /// beyond this number.
        /// </param>
        /// <param name="maxRTO">
        /// The maximum retransmission timeout for unacked packets.  The RTO will never exceed this number.
        /// </param>
        public LLUDPClient(
            LLUDPServer server, ThrottleRates rates, TokenBucket parentThrottle, uint circuitCode, UUID agentID,
            IPEndPoint remoteEndPoint, int defaultRTO, int maxRTO)
        {
            AgentID = agentID;
            RemoteEndPoint = remoteEndPoint;
            CircuitCode = circuitCode;
            m_udpServer = server;
            if (defaultRTO != 0)
                m_defaultRTO = defaultRTO;
            if (maxRTO != 0)
                m_maxRTO = maxRTO;

            m_burstTime = rates.BurstTime;
            m_maxRate = rates.ClientMaxRate;

            // Create a token bucket throttle for this client that has the scene token bucket as a parent
            m_throttleClient = new AdaptiveTokenBucket(parentThrottle, m_maxRate, m_maxRate * m_burstTime, rates.AdaptiveThrottlesEnabled);

            // Create an array of token buckets for this clients different throttle categories
            m_throttleCategories = new TokenBucket[THROTTLE_CATEGORY_COUNT];

            for (int i = 0; i < THROTTLE_CATEGORY_COUNT; i++)
            {
                ThrottleOutPacketType type = (ThrottleOutPacketType)i;

                // Initialize the packet outboxes, where packets sit while they are waiting for tokens
                m_packetOutboxes[i] = new DoubleLocklessQueue<OutgoingPacket>();
                // Initialize the token buckets that control the throttling for each category
                float rate = rates.GetRate(type);
                float burst = rate * m_burstTime;
                m_throttleCategories[i] = new TokenBucket(m_throttleClient, rate , burst);
            }

            m_RTO = m_defaultRTO;

            // Initialize this to a sane value to prevent early disconnects
            TickLastPacketReceived = Environment.TickCount & Int32.MaxValue;
            m_pingMS = 20; // so filter doesnt start at 0;
        }

        /// <summary>
        /// Shuts down this client connection
        /// </summary>
        public void Shutdown()
        {
            IsConnected = false;
            for (int i = 0; i < THROTTLE_CATEGORY_COUNT; i++)
            {
                m_packetOutboxes[i].Clear();
                m_nextPackets[i] = null;
            }

            // pull the throttle out of the scene throttle
            m_throttleClient.Parent.UnregisterRequest(m_throttleClient);
            PendingAcks.Clear();
            NeedAcks.Clear();
         }

        /// <summary>
        /// Gets information about this client connection
        /// </summary>
        /// <returns>Information about the client connection</returns>
        public ClientInfo GetClientInfo()
        {
            // TODO: This data structure is wrong in so many ways. Locking and copying the entire lists
            // of pending and needed ACKs for every client every time some method wants information about
            // this connection is a recipe for poor performance

            m_info.resendThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Resend].DripRate;
            m_info.landThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Land].DripRate;
            m_info.windThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Wind].DripRate;
            m_info.cloudThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Cloud].DripRate;
            m_info.taskThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Task].DripRate;
            m_info.assetThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Asset].DripRate;
            m_info.textureThrottle = (int)m_throttleCategories[(int)ThrottleOutPacketType.Texture].DripRate;
            m_info.totalThrottle = (int)m_throttleClient.DripRate;
            return m_info;
        }

        /// <summary>
        /// Modifies the UDP throttles
        /// </summary>
        /// <param name="info">New throttling values</param>
        public void SetClientInfo(ClientInfo info)
        {
            // TODO: Allowing throttles to be manually set from this function seems like a reasonable
            // idea. On the other hand, letting external code manipulate our ACK accounting is not
            // going to happen
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get the total number of pakcets queued for this client.
        /// </summary>
        /// <returns></returns>
        public int GetTotalPacketsQueuedCount()
        {
            int total = 0;

            for (int i = 0; i <= (int)ThrottleOutPacketType.Asset; i++)
                total += m_packetOutboxes[i].Count;

            return total;
        }

        /// <summary>
        /// Get the number of packets queued for the given throttle type.
        /// </summary>
        /// <returns></returns>
        /// <param name="throttleType"></param>
        public int GetPacketsQueuedCount(ThrottleOutPacketType throttleType)
        {
            int icat = (int)throttleType;
            if (icat > 0 && icat < THROTTLE_CATEGORY_COUNT)
                return m_packetOutboxes[icat].Count;
            else
                return 0;
        }

        /// <summary>
        /// Return statistics information about client packet queues.
        /// </summary>
        /// <remarks>
        /// FIXME: This should really be done in a more sensible manner rather than sending back a formatted string.
        /// </remarks>
        /// <returns></returns>
        public string GetStats()
        {
            return string.Format(
                "{0,7} {1,7} {2,7} {3,9} {4,7} {5,7} {6,7} {7,7} {8,7} {9,8} {10,7} {11,7}",
                Util.EnvironmentTickCountSubtract(TickLastPacketReceived),
                PacketsReceived,
                PacketsSent,
                PacketsResent,
                UnackedBytes,
                m_packetOutboxes[(int)ThrottleOutPacketType.Resend].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Land].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Wind].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Cloud].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Task].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Texture].Count,
                m_packetOutboxes[(int)ThrottleOutPacketType.Asset].Count);
        }

        public void SendPacketStats()
        {
            PacketStats callback = OnPacketStats;
            if (callback is not null)
            {
                int newPacketsReceived = PacketsReceived - m_packetsReceivedReported;
                int newPacketsSent = PacketsSent - m_packetsSentReported;
                int newPacketUnAck = UnackedBytes - m_packetsUnAckReported;
                callback(newPacketsReceived, newPacketsSent, UnackedBytes);

                m_packetsReceivedReported += newPacketsReceived;
                m_packetsSentReported += newPacketsSent;
                m_packetsUnAckReported += newPacketUnAck;
            }
        }

        public void SetThrottles(byte[] throttleData)
        {
            SetThrottles(throttleData, 1.0f);
        }

        public void SetThrottles(byte[] throttleData, float factor)
        {
            byte[] adjData;

            if (!BitConverter.IsLittleEndian)
            {
                byte[] newData = new byte[7 * 4];
                Buffer.BlockCopy(throttleData, 0, newData, 0, 7 * 4);

                for (int i = 0; i < 7; i++)
                    Array.Reverse(newData, i * 4, 4);

                adjData = newData;
            }
            else
            {
                adjData = throttleData;
            }

            // 0.125f converts from bits to bytes
            float scale = 0.125f * factor;
            float resend = Utils.BytesToFloat(adjData) * scale;
            float land = Utils.BytesToFloat(adjData, 4) * scale;
            float wind = Utils.BytesToFloat(adjData, 8) * scale;
            float cloud = Utils.BytesToFloat(adjData, 12) * scale;
            float task = Utils.BytesToFloat(adjData, 16) * scale;
            float texture = Utils.BytesToFloat(adjData, 20) * scale;
            float asset = Utils.BytesToFloat(adjData, 24) * scale;

            resend = Math.Clamp(resend, 1000, m_maxRate);
            land = Math.Clamp(land, 1000, m_maxRate);
            wind = Math.Clamp(wind, 50, m_maxRate);
            cloud = Math.Clamp(cloud, 50, m_maxRate);
            task = Math.Clamp(task, 1000, m_maxRate);
            texture = Math.Clamp(texture, 1000, m_maxRate);
            asset = Math.Clamp(asset, 1000, m_maxRate);

            float total = resend + land + wind + cloud + task + texture + asset;
            if(total > m_maxRate)
            {
                scale = m_maxRate / total;
                resend *= scale;
                land *= scale;
                wind *= scale;
                cloud *= scale;
                task *= scale;
                texture *= scale;
                asset *= scale;
                float ntotal = total * scale;
                m_log.Debug($"[LLUDPCLIENT]: limiting {AgentID} bandwith from {total} to {ntotal}");
                total = ntotal;
            }
            
            if (ThrottleDebugLevel > 0)
            {
                m_log.DebugFormat(
                    "[LLUDPCLIENT]: {0} is setting throttles in {1} to Resend={2}, Land={3}, Wind={4}, Cloud={5}, Task={6}, Texture={7}, Asset={8}, TOTAL = {9}",
                    AgentID, m_udpServer.Scene.Name, resend, land, wind, cloud, task, texture, asset, total);
            }

            TokenBucket bucket;
            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Resend];
            bucket.RequestedDripRate = resend;
            bucket.RequestedBurst = resend * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Land];
            bucket.RequestedDripRate = land;
            bucket.RequestedBurst = land * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Wind];
            bucket.RequestedDripRate = wind;
            bucket.RequestedBurst = wind * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Cloud];
            bucket.RequestedDripRate = cloud;
            bucket.RequestedBurst = cloud * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Asset];
            bucket.RequestedDripRate = asset;
            bucket.RequestedBurst = asset * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Task];
            bucket.RequestedDripRate = task;
            bucket.RequestedBurst = task * m_burstTime;

            bucket = m_throttleCategories[(int)ThrottleOutPacketType.Texture];
            bucket.RequestedDripRate = texture;
            bucket.RequestedBurst = texture * m_burstTime;
        }

        public byte[] GetThrottlesPacked(float multiplier)
        {
            byte[] data = new byte[7 * 4];
     
            // multiply by 8 to convert bytes back to bits
            multiplier *= 8;
            float rate;

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Resend].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 0);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Land].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 4);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Wind].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 8);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Cloud].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 12);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Task].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 16);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Texture].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 20);

            rate = (float)m_throttleCategories[(int)ThrottleOutPacketType.Asset].RequestedDripRate * multiplier;
            Utils.FloatToBytes(rate, data, 24);

            return data;
        }

        public int GetCatBytesCanSend(ThrottleOutPacketType cat, int timeMS)
        {
            int icat = (int)cat;
            if (icat > 0 && icat < THROTTLE_CATEGORY_COUNT)
            {
                TokenBucket bucket = m_throttleCategories[icat];
                return bucket.GetCatBytesCanSend(timeMS);
            }
            else
                return 0;
        }

        /// <summary>
        /// Queue an outgoing packet if appropriate.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="forceQueue">Always queue the packet if at all possible.</param>
        /// <returns>
        /// true if the packet has been queued,
        /// false if the packet has not been queued and should be sent immediately.
        /// </returns>
        public bool EnqueueOutgoing(OutgoingPacket packet)
        {
            int category = (int)packet.Category;

            if (category >= 0 && category < m_packetOutboxes.Length)
            {
                DoubleLocklessQueue<OutgoingPacket> queue = m_packetOutboxes[category];
                queue.Enqueue(packet, false);
                return true;
            }
            else
            {
                // We don't have a token bucket for this category, so it will not be queued
                return false;
            }
        }

        /// <summary>
        /// Loops through all of the packet queues for this client and tries to send
        /// an outgoing packet from each, obeying the throttling bucket limits
        /// </summary>
        ///
        /// <remarks>
        /// Packet queues are inspected in ascending numerical order starting from 0.  Therefore, queues with a lower
        /// ThrottleOutPacketType number will see their packet get sent first (e.g. if both Land and Wind queues have
        /// packets, then the packet at the front of the Land queue will be sent before the packet at the front of the
        /// wind queue).
        ///
        /// This function is only called from a synchronous loop in the
        /// UDPServer so we don't need to bother making this thread safe
        /// </remarks>
        ///
        /// <returns>True if any packets were sent, otherwise false</returns>
        public bool DequeueOutgoing()
        {
            //if (m_deliverPackets == false) return false;

            OutgoingPacket packet;
            DoubleLocklessQueue<OutgoingPacket> queue;
            bool packetSent = false;
            ThrottleOutPacketTypeFlags emptyCategories = 0;

            //string queueDebugOutput = String.Empty; // Serious debug business
            // do resends

            packet = m_nextPackets[0];
            if (packet is not null)
            {
                if (packet.Buffer is not null)
                {
                    if (m_throttleCategories[0].RemoveTokens(packet.Buffer.DataLength))
                    {
                        // Send the packet
                        m_udpServer.SendPacketFinal(packet);
                        packetSent = true;
                        m_nextPackets[0] = null;
                    }
                }
                else
                    m_nextPackets[0] = null;
            }
            else
            {
                queue = m_packetOutboxes[0];
                if (queue is not null)
                {
                    if(queue.Dequeue(out packet))
                    {
                        // A packet was pulled off the queue. See if we have
                        // enough tokens in the bucket to send it out
                        if (packet.Buffer is not null)
                        {
                            if (m_throttleCategories[0].RemoveTokens(packet.Buffer.DataLength))
                            {
                                // Send the packet
                                m_udpServer.SendPacketFinal(packet);
                                packetSent = true;
                            }
                            else
                            {
                                // Save the dequeued packet for the next iteration
                                m_nextPackets[0] = packet;
                            }
                        }
                    }
                }
                else
                {
                    m_packetOutboxes[0] = new DoubleLocklessQueue<OutgoingPacket>();
                }
            }

            if(NeedAcks.Count() > 50)
            {
                Interlocked.Increment(ref AckStalls);
                return true;
            }

            for (int i = 1; i < THROTTLE_CATEGORY_COUNT; i++)
            {
                //queueDebugOutput += m_packetOutboxes[i].Count + " ";  // Serious debug business

                packet = m_nextPackets[i];
                if (packet is not null)
                {
                    if(packet.Buffer is null)
                    {
                        if (m_packetOutboxes[i].Count < 5)
                            emptyCategories |= CategoryToFlag(i);
                        m_nextPackets[i] = null;
                        continue;
                    }

                    if (m_throttleCategories[i].RemoveTokens(packet.Buffer.DataLength))
                    {
                        // Send the packet
                        m_udpServer.SendPacketFinal(packet);
                        m_nextPackets[i] = null;
                        packetSent = true;

                        if (m_packetOutboxes[i].Count < 5)
                            emptyCategories |= CategoryToFlag(i);
                    }
                }
                else
                {
                    // No dequeued packet waiting to be sent, try to pull one off
                    // this queue
                    queue = m_packetOutboxes[i];
                    if(queue.Dequeue(out packet))
                    {
                        if (packet.Buffer is null)
                        {
                            // packet canceled elsewhere (by a ack for example)
                            if (queue.Count < 5)
                                emptyCategories |= CategoryToFlag(i);
                            continue;
                        }

                        if (m_throttleCategories[i].RemoveTokens(packet.Buffer.DataLength))
                        {
                            // Send the packet
                            m_udpServer.SendPacketFinal(packet);
                            packetSent = true;
                            if (queue.Count < 5)
                                emptyCategories |= CategoryToFlag(i);
                        }
                        else
                        {
                            // Save the dequeued packet for the next iteration
                            m_nextPackets[i] = packet;
                        }
                    }
                    else
                    {
                        // No packets in this queue. Fire the queue empty callback
                        // if it has not been called recently
                        emptyCategories |= CategoryToFlag(i);
                    }
                }
            }

            if (emptyCategories != 0)
                BeginFireQueueEmpty(emptyCategories);

            //m_log.Info("[LLUDPCLIENT]: Queues: " + queueDebugOutput); // Serious debug business
            return packetSent;
        }

        /// <summary>
        /// Called when we get a ping update
        /// </summary>
        /// <param name="r"> ping time in ms
        /// acknowledgement</param>
        public void UpdateRoundTrip(int p)
        {
            p *= 5;
            if( p> m_maxRTO)
                p = m_maxRTO;
            else if(p < m_minRTO)
                p = m_minRTO;

            m_RTO = p;
        }

        const double MIN_CALLBACK_MS = 20.0;
        public bool QueueEmptyRunning;

        /// <summary>
        /// Does an early check to see if this queue empty callback is already
        /// running, then asynchronously firing the event
        /// </summary>
        /// <param name="categories">Throttle categories to fire the callback for</param>
        private void BeginFireQueueEmpty(ThrottleOutPacketTypeFlags categories)
        {
            if (!QueueEmptyRunning && HasUpdates(categories) && OnQueueEmpty is not null)
            {
                double start = Util.GetTimeStampMS();
                if (start < m_nextOnQueueEmpty)
                    return;

                QueueEmptyRunning = true;
                m_nextOnQueueEmpty = start + MIN_CALLBACK_MS;

                // Asynchronously run the callback
                if (m_udpServer.OqrEngine.IsRunning)
                {
                    LLUDPClient udpcli = this;
                    ThrottleOutPacketTypeFlags cats = categories;
                    void act(LLUDPClient arg1, ThrottleOutPacketTypeFlags arg2)
                    {
                        QueueEmpty callback = udpcli.OnQueueEmpty;
                        if (callback != null)
                        {
                            try
                            {
                                callback(cats);
                            }
                            catch { }
                        }
                        udpcli.QueueEmptyRunning = false;
                        udpcli = null;
                        callback = null;
                    }

                    _ = m_udpServer.OqrEngine.QueueJob(AgentID.ToString(), () => act(udpcli, cats));
                }
                else
                    Util.FireAndForget(FireQueueEmpty, categories, "LLUDPClient.BeginFireQueueEmpty");
            }
        }

        /// <summary>
        /// Fires the OnQueueEmpty callback and sets the minimum time that it
        /// can be called again
        /// </summary>
        /// <param name="o">Throttle categories to fire the callback for,
        /// stored as an object to match the WaitCallback delegate
        /// signature</param>
        public void FireQueueEmpty(object o)
        {
            QueueEmpty callback = OnQueueEmpty;
            if (callback is not null)
            {
                ThrottleOutPacketTypeFlags categories = (ThrottleOutPacketTypeFlags)o;
                try { callback(categories); }
                catch (Exception e) { m_log.Error("[LLUDPCLIENT]: OnQueueEmpty(" + categories + ") threw an exception: " + e.Message, e); }
            }

            QueueEmptyRunning = false;
        }

        internal void ForceThrottleSetting(int throttle, int setting)
        {
            if (throttle > 0 && throttle < THROTTLE_CATEGORY_COUNT)
                m_throttleCategories[throttle].RequestedDripRate = Math.Max(setting, LLUDPServer.MTU);
        }

        internal int GetThrottleSetting(int throttle)
        {
            if (throttle > 0 && throttle < THROTTLE_CATEGORY_COUNT)
                return (int)m_throttleCategories[throttle].RequestedDripRate;
            else
                return 0;
        }

        /// <summary>
        /// Converts a <seealso cref="ThrottleOutPacketType"/> integer to a
        /// flag value
        /// </summary>
        /// <param name="i">Throttle category to convert</param>
        /// <returns>Flag representation of the throttle category</returns>
        private static ThrottleOutPacketTypeFlags CategoryToFlag(int i)
        {
            ThrottleOutPacketType category = (ThrottleOutPacketType)i;

            return category switch
            {
                ThrottleOutPacketType.Land => ThrottleOutPacketTypeFlags.Land,// Terrain data
                ThrottleOutPacketType.Wind => ThrottleOutPacketTypeFlags.Wind,// Wind data
                ThrottleOutPacketType.Cloud => ThrottleOutPacketTypeFlags.Cloud,// Cloud data
                ThrottleOutPacketType.Task => ThrottleOutPacketTypeFlags.Task,// Object updates and everything not on the other categories
                ThrottleOutPacketType.Texture => ThrottleOutPacketTypeFlags.Texture,// Textures data (also impacts http texture and mesh by default)
                ThrottleOutPacketType.Asset => ThrottleOutPacketTypeFlags.Asset,// Non-texture Assets data
                _ => 0,
            };
        }
    }

    public class DoubleLocklessQueue<T> : MutSea.Framework.LocklessQueue<T>
    {
        readonly MutSea.Framework.LocklessQueue<T> highQueue = new();

        public override int Count
        {
            get
            {
                return base.Count + highQueue.Count;
            }
        }

        public override bool Dequeue(out T item)
        {
            if (highQueue.Dequeue(out item))
                return true;

            return base.Dequeue(out item);
        }

        public void Enqueue(T item, bool highPriority)
        {
            if (highPriority)
                highQueue.Enqueue(item);
            else
                Enqueue(item);
        }
    }
}
