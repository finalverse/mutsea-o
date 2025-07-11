﻿/*
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
using LSL_List = MutSea.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_String = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Float = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;

namespace MutSea.Region.ScriptEngine.Shared.ScriptBase
{
    public partial class ScriptBaseClass
    {
        public ILS_Api m_LS_Functions;

        public void ApiTypeLS(IScriptApi api)
        {
            if (api is ILS_Api p)
                m_LS_Functions = p;
        }

        public LSL_List lsGetWindlightScene(LSL_List rules)
        {
            return m_LS_Functions.lsGetWindlightScene(rules);
        }

        public int lsSetWindlightScene(LSL_List rules)
        {
            return m_LS_Functions.lsSetWindlightScene(rules);
        }

        public int lsSetWindlightSceneTargeted(LSL_List rules, key target)
        {
            return m_LS_Functions.lsSetWindlightSceneTargeted(rules, target);
        }

        public void lsClearWindlightScene()
        {
            m_LS_Functions.lsClearWindlightScene();
        }

        public LSL_List cmGetWindlightScene(LSL_List rules)
        {
            return m_LS_Functions.lsGetWindlightScene(rules);
        }

        public int cmSetWindlightScene(LSL_List rules)
        {
            return m_LS_Functions.lsSetWindlightScene(rules);
        }

        public int cmSetWindlightSceneTargeted(LSL_List rules, key target)
        {
            return m_LS_Functions.lsSetWindlightSceneTargeted(rules, target);
        }

        public void cmClearWindlightScene()
        {
            m_LS_Functions.lsClearWindlightScene();
        }
    }
}
