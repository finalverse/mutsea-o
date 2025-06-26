/*
 * Copyright (c) Contributors, Finalverse Inc.
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
using System.Diagnostics;
using System.Reflection;
using log4net;

namespace MutSea.Framework.Diagnostics
{
    /// <summary>
    /// Helper for tracing function entry and exit.
    /// </summary>
    public sealed class FunctionTracer : IDisposable
    {
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private readonly string m_name;
        private readonly Stopwatch m_timer;

        /// <summary>
        /// Global switch for enabling tracing. Controlled by the MUTSEA_TRACE
        /// environment variable. Any non-empty value enables tracing.
        /// </summary>
        public static readonly bool Enabled;

        static FunctionTracer()
        {
            string env = Environment.GetEnvironmentVariable("MUTSEA_TRACE");
            Enabled = !string.IsNullOrEmpty(env) &&
                      !(env.Equals("0") || env.Equals("false", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Create a tracer instance if tracing is enabled.
        /// </summary>
        /// <param name="name">Name of the function or scope.</param>
        /// <returns>A FunctionTracer or null if tracing is disabled.</returns>
        public static FunctionTracer Trace(string name)
        {
            return Enabled ? new FunctionTracer(name) : null;
        }

        private FunctionTracer(string name)
        {
            m_name = name;
            m_timer = Stopwatch.StartNew();
            m_log.Debug($"[TRACE ENTER] {name}");
        }

        public void Dispose()
        {
            m_timer.Stop();
            m_log.Debug($"[TRACE EXIT] {m_name} after {m_timer.Elapsed.TotalMilliseconds:F0} ms");
            GC.SuppressFinalize(this);
        }
    }
}

