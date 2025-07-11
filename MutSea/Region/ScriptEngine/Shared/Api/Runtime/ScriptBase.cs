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
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics; //for [DebuggerNonUserCode]
using MutSea.Region.ScriptEngine.Interfaces;

namespace MutSea.Region.ScriptEngine.Shared.ScriptBase
{
    public partial class ScriptBaseClass : IScript
    {
        private Dictionary<string, MethodInfo> inits = new Dictionary<string, MethodInfo>();
        public ScriptBaseClass()
        {
            MethodInfo[] myArrayMethodInfo = GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (MethodInfo mi in myArrayMethodInfo.AsSpan())
            {
                var name = mi.Name.AsSpan();
                if (name.StartsWith("ApiType"))
                {
                    string type = new string(name[7..]);
                    inits[type] = mi;
                }
            }
        }

        public string[] GetApis()
        {
            string[] apis = new string[inits.Count];
            inits.Keys.CopyTo(apis, 0);
            return apis;
        }

        public void InitApi(string api, IScriptApi data)
        {
            if (inits.TryGetValue(api, out MethodInfo mi))
                mi?.Invoke(this, new object[]{ data });
        }
    }
}
