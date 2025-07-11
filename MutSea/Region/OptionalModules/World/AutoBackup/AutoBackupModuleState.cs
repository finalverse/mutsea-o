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


namespace MutSea.Region.OptionalModules.World.AutoBackup
{
    /// <summary>AutoBackupModuleState: Auto-Backup state for one region (scene).
    /// If you use this class in any way outside of AutoBackupModule, you should treat the class as opaque.
    /// Since it is not part of the framework, you really should not rely upon it outside of the AutoBackupModule implementation.
    /// </summary>
    ///
    public class AutoBackupModuleState
    {
        public AutoBackupModuleState()
        {
            Enabled = false;
            SkipAssets = false;
            NamingType = NamingType.Time;
            Script = null;
        }

        public AutoBackupModuleState(AutoBackupModuleState copyFrom)
        {
            Enabled = copyFrom.Enabled;
            SkipAssets = copyFrom.SkipAssets;
            NamingType = copyFrom.NamingType;
            Script = copyFrom.Script;
        }

        public bool Enabled
        {
            get;
            set;
        }

        public bool SkipAssets
        {
            get;
            set;
        }

        public string Script
        {
            get;
            set;
        }

        public NamingType NamingType
        {
            get;
            set;
        }

        public new string ToString()
        {
            string retval = "";
            retval += "[AUTO BACKUP]: AutoBackup: " + (Enabled ? "ENABLED" : "DISABLED") + "\n";
            retval += "[AUTO BACKUP]: Naming Type: " + NamingType.ToString() + "\n";
            retval += "[AUTO BACKUP]: Script: " + Script + "\n";
            return retval;
        }
    }
}
