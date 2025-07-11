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
using System.IO;
using System.Text;
using System.Reflection;
using MutSea.Framework;
using MutSea.Services.Base;
using MutSea.Services.Interfaces;
using Nini.Config;
using log4net;
using OpenMetaverse;

namespace MutSea.Server.Handlers.BakedTextures
{
    public class XBakes : ServiceBase, IBakedTextureService
    {
        private static readonly ILog m_log = LogManager.GetLogger( MethodBase.GetCurrentMethod().DeclaringType);

        protected string m_FSBase;

        public XBakes(IConfigSource config) : base(config)
        {
            MainConsole.Instance.Commands.AddCommand("fs", false,
                    "delete bakes", "delete bakes <ID>",
                    "Delete agent's baked textures from server",
                    HandleDeleteBakes);

            IConfig assetConfig = config.Configs["BakedTextureService"];
            if (assetConfig == null)
            {
                throw new Exception("No BakedTextureService configuration");
            }

            m_FSBase = assetConfig.GetString("BaseDirectory", string.Empty);
            if (m_FSBase.Length == 0)
            {
                m_log.ErrorFormat("[BAKES]: BaseDirectory not specified");
                throw new Exception("Configuration error");
            }

            m_log.Info("[BAKES]: XBakes service enabled");
        }

        public byte[] Get(string id)
        {
            string file = HashToFile(id);
            string diskFile = Path.Combine(m_FSBase, file);

            try
            {
                byte[] content = File.ReadAllBytes(diskFile);
                return content;
            }
            catch
            {
            }
            return Array.Empty<byte>();
        }

        public void Store(string id, byte[] data, int dataLength)
        {
            string file = HashToFile(id);
            string diskFile = Path.Combine(m_FSBase, file);

            Directory.CreateDirectory(Path.GetDirectoryName(diskFile));

            File.Delete(diskFile);
            using (FileStream fs = File.Create(diskFile))
                fs.Write(data, 0, dataLength);
        }

        private void HandleDeleteBakes(string module, string[] args)
        {
            if (args.Length < 3)
            {
                MainConsole.Instance.Output("Syntax: delete bakes <ID>");
                return;
            }

            string file = HashToFile(args[2]);
            string diskFile = Path.Combine(m_FSBase, file);

            if (File.Exists(diskFile))
            {
                File.Delete(diskFile);
                MainConsole.Instance.Output("Bakes deleted");
                return;
            }
            MainConsole.Instance.Output("Bakes not found");
        }

        public string HashToPath(string hash)
        {
            return Path.Combine(hash.Substring(0, 2),
                   Path.Combine(hash.Substring(2, 2),
                   Path.Combine(hash.Substring(4, 2),
                   hash.Substring(6, 4))));
        }

        public string HashToFile(string hash)
        {
            return Path.Combine(HashToPath(hash), hash);
        }
    }
}
