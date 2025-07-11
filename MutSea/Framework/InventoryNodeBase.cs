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

using OpenMetaverse;

namespace MutSea.Framework
{
    /// <summary>
    /// Common base class for inventory nodes of different types (files, folders, etc.)
    /// </summary>
    public class InventoryNodeBase
    {
        /// <summary>
        /// The name of the node (64 characters or less)
        /// </summary>

        public virtual string Name
        {
            get { return UTF8Name == null ? string.Empty : UTF8Name.ToString(); }
            set { UTF8Name = string.IsNullOrEmpty(value) ? null : new osUTF8(value); }
        }
        public osUTF8 UTF8Name;

        /// <summary>
        /// A UUID containing the ID for the inventory node itself
        /// </summary>
        public UUID ID
        {
            get { return m_id; }
            set { m_id = value; }
        }
        private UUID m_id;

        /// <summary>
        /// The agent who's inventory this is contained by
        /// </summary>
        public virtual UUID Owner
        {
            get { return m_owner; }
            set { m_owner = value; }
        }
        private UUID m_owner;
    }
}
