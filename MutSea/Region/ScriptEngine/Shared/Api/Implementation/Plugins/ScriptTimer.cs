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
using OpenMetaverse;

namespace MutSea.Region.ScriptEngine.Shared.Api.Plugins
{
    public class ScriptTimer
    {
        public class TimerInfo
        {
            public UUID itemID;
            public uint localID;
            public long interval;
            public long next;

            public TimerInfo Clone()
            {
                return (TimerInfo)this.MemberwiseClone();
            }
        }

        public AsyncCommandManager m_CmdManager;

        public int TimersCount
        {
            get
            {
                lock (TimerListLock)
                    return Timers.Count;
            }
        }

        public ScriptTimer(AsyncCommandManager CmdManager)
        {
            m_CmdManager = CmdManager;
        }

        //
        // TIMER
        //
        private static string MakeTimerKey(uint localID, UUID itemID)
        {
            return localID.ToString() + itemID.ToString();
        }

        private readonly Dictionary<string,TimerInfo> Timers = new();
        private readonly object TimerListLock = new();
        private List<TimerInfo> TimersCache = null;

        public void SetTimerEvent(uint _localID, UUID _itemID, double sec)
        {
            if (sec == 0.0) // Disabling timer
            {
                UnSetTimerEvents(_localID, _itemID);
                return;
            }

            string key = MakeTimerKey(_localID, _itemID);
            long intervalTicks = (long)(sec * TimeSpan.TicksPerSecond);

            lock (TimerListLock)
            {
                if (Timers.TryGetValue(key, out TimerInfo ts))
                {
                    ts.interval = intervalTicks;
                    ts.next = DateTime.UtcNow.Ticks + ts.interval;
                }
                else
                {
                    ts = new TimerInfo()
                    {
                        localID = _localID,
                        itemID = _itemID,
                        interval = intervalTicks,
                        next = DateTime.UtcNow.Ticks + intervalTicks
                    };
                    Timers[key] = ts;
                    TimersCache = null;
                }
            }
        }

        public void UnSetTimerEvents(uint m_localID, UUID m_itemID)
        {
            // Remove from timer
            string key = MakeTimerKey(m_localID, m_itemID);
            lock (TimerListLock)
            {
                if (Timers.Remove(key, out TimerInfo ts))
                {
                    m_CmdManager.m_ScriptEngine.CancelScriptEvent(ts.itemID, "timer");
                    TimersCache = null;
                }
            }
        }

        public void CheckTimerEvents()
        {
            List<TimerInfo> tvals;
            lock (TimerListLock)
            {
                if (Timers.Count == 0)
                    return;
                tvals = TimersCache ?? new List<TimerInfo>(Timers.Values);
            }

            long now = DateTime.UtcNow.Ticks;
            foreach (TimerInfo ts in tvals)
            {
                // Time has passed?
                if (ts.next <= now)
                {
                    // Add it to queue
                    m_CmdManager.m_ScriptEngine.PostScriptEvent(ts.itemID,
                            new EventParams("timer", new Object[0],
                            new DetectParams[0]));
                    // set next interval
                    ts.next = now + ts.interval;
                }
            }
        }

        public Object[] GetSerializationData(UUID itemID)
        {
            List<Object> data = new();

            List<TimerInfo> tvals;
            lock (TimerListLock)
            {
                if (Timers.Count == 0)
                    return new object[0];
                tvals = TimersCache ?? new List<TimerInfo>(Timers.Values);
            }

            long now = DateTime.UtcNow.Ticks;
            foreach (TimerInfo ts in tvals)
            {
                if (ts.itemID.Equals(itemID))
                {
                    data.Add(ts.interval);
                    data.Add(ts.next - now);
                }
            }

            return data.ToArray();
        }

        public void CreateFromData(uint localID, UUID itemID, UUID objectID, Object[] data)
        {
            int idx = 0;
            long now = DateTime.UtcNow.Ticks;

            while (idx < data.Length)
            {
                TimerInfo ts = new()
                { 
                    localID = localID,
                    itemID = itemID,
                    interval = (long)data[idx],
                    next = now + (long)data[idx+1]
                };

                idx += 2;
                string tskey = MakeTimerKey(localID, itemID);
                lock (TimerListLock)
                {
                    Timers.Add(tskey, ts);
                    TimersCache = null;
                }
            }
        }

        public List<TimerInfo> GetTimersInfo()
        {
            List<TimerInfo> retList = new();
            List<TimerInfo> tvals;
            lock (TimerListLock)
            {
                if (Timers.Count == 0)
                    return retList;
                tvals = TimersCache ?? new List<TimerInfo>(Timers.Values);
            }

            foreach (TimerInfo i in tvals)
                retList.Add(i.Clone());

            return retList;
        }
    }
}
