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
using OpenMetaverse.StructuredData;
using MutSea.Framework;
using MutSea.Services.Interfaces;
using System.Collections.Generic;

namespace MutSea.Region.Framework.Interfaces
{
    public delegate void ChangeDelegate(UUID regionID);
    public delegate void MessageDelegate(UUID regionID, UUID fromID, string fromName, string message);

    public interface IEstateModule
    {
        event ChangeDelegate OnRegionInfoChange;
        event ChangeDelegate OnEstateInfoChange;
        event MessageDelegate OnEstateMessage;
        event EstateTeleportOneUserHomeRequest OnEstateTeleportOneUserHomeRequest;
        event EstateTeleportAllUsersHomeRequest OnEstateTeleportAllUsersHomeRequest;

        uint GetRegionFlags();
        bool IsManager(UUID avatarID);

        string SetEstateOwner(int estateID, UserAccount account);
        string SetEstateName(int estateID, string newName);
        string SetRegionEstate(RegionInfo regionInfo, int estateID);
        string CreateEstate(string estateName, UUID ownerID);

        /// <summary>
        /// Tell all clients about the current state of the region (terrain textures, water height, etc.).
        /// </summary>
        void sendRegionHandshakeToAll();
        void TriggerEstateInfoChange();

        /// <summary>
        /// Fires the OnRegionInfoChange event.
        /// </summary>
        void TriggerRegionInfoChange();

        void setEstateTerrainBaseTexture(int level, UUID texture);
        void SetEstateTerrainTextures(List<UUID> textureIDs, int types);
        void setEstateTerrainTextureHeights(int corner, float lowValue, float highValue);

        /// <summary>
        /// Returns whether the transfer ID is being used for a terrain transfer.
        /// </summary>
        bool IsTerrainXfer(ulong xferID);
        bool handleEstateChangeInfoCap(string estateName, UUID invoice,
            bool externallyVisible, bool allowDirectTeleport, bool denyAnonymous, bool denyAgeUnverified,
            bool alloVoiceChat, bool overridePublicAccess, bool allowEnvironmentOverride);
        void HandleRegionInfoRequest(IClientAPI remote_client);
        bool SetRegionInfobyCap(OSDMap map);
    }
}
