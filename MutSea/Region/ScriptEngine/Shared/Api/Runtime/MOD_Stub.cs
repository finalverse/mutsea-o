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
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.ScriptEngine.Interfaces;
using MutSea.Region.ScriptEngine.Shared.Api.Interfaces;
using integer = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using vector = MutSea.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using rotation = MutSea.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using key = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;

using LSL_Float = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = MutSea.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = MutSea.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = MutSea.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace MutSea.Region.ScriptEngine.Shared.ScriptBase
{
    public partial class ScriptBaseClass
    {
        public IMOD_Api m_MOD_Functions;

        public void ApiTypeMOD(IScriptApi api)
        {
            if (api is IMOD_Api p)
                m_MOD_Functions = p;
        }

        public void modInvokeN(string fname, params object[] parms)
        {
            m_MOD_Functions.modInvokeN(fname, parms);
        }

        public LSL_String modInvokeS(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeS(fname, parms);
        }

        public LSL_Integer modInvokeI(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeI(fname, parms);
        }

        public LSL_Float modInvokeF(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeF(fname, parms);
        }

        public LSL_Key modInvokeK(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeK(fname, parms);
        }

        public LSL_Vector modInvokeV(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeV(fname, parms);
        }

        public LSL_Rotation modInvokeR(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeR(fname, parms);
        }

        public LSL_List modInvokeL(string fname, params object[] parms)
        {
            return m_MOD_Functions.modInvokeL(fname, parms);
        }

        public string modSendCommand(string module, string command, string k)
        {
            return m_MOD_Functions.modSendCommand(module, command, k);
        }
    }
}
