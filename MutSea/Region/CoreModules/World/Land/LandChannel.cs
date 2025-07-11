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

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;

namespace MutSea.Region.CoreModules.World.Land
{
    public class LandChannel : ILandChannel
    {
        #region Constants

        //Land types set with flags in ParcelOverlay.
        //Only one of these can be used.

        //RequestResults (I think these are right, they seem to work):
        public const int LAND_RESULT_MULTIPLE = 1; // The request they made contained more than a single peice of land
        public const int LAND_RESULT_SINGLE = 0; // The request they made contained only a single piece of land

        //ParcelSelectObjects
        public const int LAND_SELECT_OBJECTS_OWNER = 2;
        public const int LAND_SELECT_OBJECTS_GROUP = 4;
        public const int LAND_SELECT_OBJECTS_OTHER = 8;


        public const byte LAND_TYPE_PUBLIC = 0; //Equals 00000000
        // types 1 to 7 are exclusive
        public const byte LAND_TYPE_OWNED_BY_OTHER = 1; //Equals 00000001
        public const byte LAND_TYPE_OWNED_BY_GROUP = 2; //Equals 00000010
        public const byte LAND_TYPE_OWNED_BY_REQUESTER = 3; //Equals 00000011
        public const byte LAND_TYPE_IS_FOR_SALE = 4; //Equals 00000100
        public const byte LAND_TYPE_IS_BEING_AUCTIONED = 5; //Equals 00000101
        public const byte LAND_TYPE_unused6 = 6;
        public const byte LAND_TYPE_unused7 = 7;
        // next are flags
        public const byte LAND_FLAG_unused8 = 0x08; // this may become excluside in future
        public const byte LAND_FLAG_HIDEAVATARS = 0x10;
        public const byte LAND_FLAG_LOCALSOUND = 0x20;
        public const byte LAND_FLAG_PROPERTY_BORDER_WEST = 0x40; //Equals 01000000
        public const byte LAND_FLAG_PROPERTY_BORDER_SOUTH = 0x80; //Equals 10000000


        //These are other constants. Yay!
        public const int START_LAND_LOCAL_ID = 1;

        #endregion

        private readonly LandManagementModule m_landManagementModule;

        private float m_BanLineSafeHeight = 100.0f;
        public float BanLineSafeHeight
        {
            get
            {
                return m_BanLineSafeHeight;
            }
            private set
            {
                if (value >= 20f && value <= 5000f)
                    m_BanLineSafeHeight = value;
                else
                    m_BanLineSafeHeight = 100.0f;
            }
        }

        public LandChannel(Scene scene, LandManagementModule landManagementMod)
        {
            m_landManagementModule = landManagementMod;
            if(landManagementMod is not null)
                m_BanLineSafeHeight = landManagementMod.BanLineSafeHeight;
        }

        #region ILandChannel Members
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObject(float x_float, float y_float)
        {
            return m_landManagementModule?.GetLandObject(x_float, y_float);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObject(int localID)
        {
            return m_landManagementModule?.GetLandObject(localID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObject(UUID GlobalID)
        {
            return m_landManagementModule?.GetLandObject(GlobalID);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObject(Vector3 position)
        {
            return GetLandObject(position.X, position.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObject(int x, int y)
        {
            return m_landManagementModule?.GetLandObject(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ILandObject GetLandObjectClippedXY(float x, float y)
        {
            return m_landManagementModule?.GetLandObjectClippedXY(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<ILandObject> AllParcels()
        {
            return m_landManagementModule is not null ? m_landManagementModule.AllParcels() : new List<ILandObject>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(bool setupDefaultParcel)
        {
             m_landManagementModule?.Clear(setupDefaultParcel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public List<ILandObject> ParcelsNearPoint(Vector3 position)
        {
            return m_landManagementModule is not null ? m_landManagementModule.ParcelsNearPoint(position) : new List<ILandObject>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsForcefulBansAllowed()
        {
            return m_landManagementModule is not null && m_landManagementModule.AllowedForcefulBans;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateLandObject(int localID, LandData data)
        {
            m_landManagementModule?.UpdateLandObject(localID, data);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SendParcelsOverlay(IClientAPI client)
        {
            m_landManagementModule?.SendParcelOverlay(client);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Join(int start_x, int start_y, int end_x, int end_y, UUID attempting_user_id)
        {
            m_landManagementModule?.Join(start_x, start_y, end_x, end_y, attempting_user_id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Subdivide(int start_x, int start_y, int end_x, int end_y, UUID attempting_user_id)
        {
            m_landManagementModule?.Subdivide(start_x, start_y, end_x, end_y, attempting_user_id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReturnObjectsInParcel(int localID, uint returnType, UUID[] agentIDs, UUID[] taskIDs, IClientAPI remoteClient)
        {
            m_landManagementModule?.ReturnObjectsInParcel(localID, returnType, agentIDs, taskIDs, remoteClient);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void setParcelObjectMaxOverride(overrideParcelMaxPrimCountDelegate overrideDel)
        {
            m_landManagementModule?.setParcelObjectMaxOverride(overrideDel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void setSimulatorObjectMaxOverride(overrideSimulatorMaxPrimCountDelegate overrideDel)
        {
            m_landManagementModule?.setSimulatorObjectMaxOverride(overrideDel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetParcelOtherCleanTime(IClientAPI remoteClient, int localID, int otherCleanTime)
        {
            m_landManagementModule?.SetParcelOtherCleanTime(remoteClient, localID, otherCleanTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void sendClientInitialLandInfo(IClientAPI remoteClient, bool overlay)
        {
            m_landManagementModule?.sendClientInitialLandInfo(remoteClient, overlay);
        }

        public void ClearAllEnvironments()
        {
            List<ILandObject> parcels = AllParcels();
            for(int i=0; i< parcels.Count; ++i)
                parcels[i].StoreEnvironment(null);
        }
        #endregion
    }
}
