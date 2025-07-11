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
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace MutSea.Framework
{
    public class AvatarAttachment
    {
        public int AttachPoint;
        public UUID ItemID;
        public UUID AssetID;

        public AvatarAttachment(AvatarAttachment attach)
        {
            AttachPoint = attach.AttachPoint;
            ItemID = attach.ItemID;
            AssetID = attach.AssetID;
        }

        public AvatarAttachment(int point, UUID item, UUID asset)
        {
            AttachPoint = point;
            ItemID = item;
            AssetID = asset;
        }

        public AvatarAttachment(OSDMap args)
        {
            Unpack(args);
        }

        public OSDMap Pack()
        {
            OSDMap attachdata = new OSDMap();
            attachdata["point"] = OSD.FromInteger(AttachPoint);
            attachdata["item"] = OSD.FromUUID(ItemID);
            attachdata["asset"] = OSD.FromUUID(AssetID);

            return attachdata;
        }

        public void Unpack(OSDMap args)
        {
            OSD tmpOSD;
            if (args.TryGetValue("point", out tmpOSD))
                AttachPoint = tmpOSD.AsInteger();
            if (args.TryGetValue("item", out tmpOSD))
                ItemID = tmpOSD.AsUUID();
            else
                ItemID = UUID.Zero;

            if (args.TryGetValue("asset", out tmpOSD))
                AssetID = tmpOSD.AsUUID();
            else
                AssetID = UUID.Zero;
        }
    }
}
