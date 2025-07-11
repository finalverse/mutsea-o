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
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MutSea.Framework
{
    public static class SLUtil
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Asset types used only in MutSea.
        /// To avoid clashing with the code numbers used in Second Life, use only negative numbers here.
        /// </summary>

        #region SL / file extension / content-type conversions

        /// <summary>
        /// Returns the Enum entry corresponding to the given code, regardless of whether it belongs
        /// to the AssetType or MutSeaAssetType enums.
        /// </summary>
        public static object AssetTypeFromCode(sbyte assetType)
        {
            if (Enum.IsDefined(typeof(OpenMetaverse.AssetType), assetType))
                return (OpenMetaverse.AssetType)assetType;
            else
                return OpenMetaverse.AssetType.Unknown;
        }

        private struct TypeMapping
        {
            public readonly sbyte AssetType;
            public readonly sbyte InventoryType;
            public readonly string ContentType;
            public readonly string ContentType2;
            public readonly string Extension;

            private TypeMapping(sbyte assetType, sbyte inventoryType, string contentType, string contentType2, string extension)
            {
                AssetType = assetType;
                InventoryType = inventoryType;
                ContentType = contentType;
                ContentType2 = contentType2;
                Extension = extension;
            }

            public TypeMapping(AssetType assetType, sbyte inventoryType, string contentType, string contentType2, string extension)
            {
                AssetType = (sbyte)assetType;
                InventoryType = inventoryType;
                ContentType = contentType;
                ContentType2 = contentType2;
                Extension = extension;
            }

            public TypeMapping(AssetType assetType, InventoryType inventoryType, string contentType, string contentType2, string extension)
                : this(assetType, (sbyte)inventoryType, contentType, contentType2, extension)
            {
            }

            public TypeMapping(AssetType assetType, InventoryType inventoryType, string contentType, string extension)
                : this(assetType, (sbyte)inventoryType, contentType, null, extension)
            {
            }

            public TypeMapping(AssetType assetType, FolderType inventoryType, string contentType, string extension)
                : this(assetType, (sbyte)inventoryType, contentType, null, extension)
            {
            }
        }

        /// <summary>
        /// Maps between AssetType, InventoryType and Content-Type.
        /// Where more than one possibility exists, the first one takes precedence. E.g.:
        ///   AssetType "AssetType.Texture" -> Content-Type "image-xj2c"
        ///   Content-Type "image/x-j2c" -> InventoryType "InventoryType.Texture"
        /// </summary>
        private static TypeMapping[] MAPPINGS = [
            new TypeMapping(AssetType.Unknown, InventoryType.Unknown, "application/octet-stream", "bin"),
            new TypeMapping(AssetType.Texture, InventoryType.Texture, "image/x-j2c", "image/jp2", "j2c"),
            new TypeMapping(AssetType.Texture, InventoryType.Snapshot, "image/x-j2c", "image/jp2", "j2c"),
            new TypeMapping(AssetType.TextureTGA, InventoryType.Texture, "image/tga", "tga"),
            new TypeMapping(AssetType.ImageTGA, InventoryType.Texture, "image/tga", "tga"),
            new TypeMapping(AssetType.ImageJPEG, InventoryType.Texture, "image/jpeg", "jpg"),
            new TypeMapping(AssetType.Sound, InventoryType.Sound, "audio/ogg", "application/ogg", "ogg"),
            new TypeMapping(AssetType.SoundWAV, InventoryType.Sound, "audio/x-wav", "wav"),
            new TypeMapping(AssetType.CallingCard, InventoryType.CallingCard, "application/vnd.ll.callingcard", "application/x-metaverse-callingcard", "callingcard"),
            new TypeMapping(AssetType.Landmark, InventoryType.Landmark, "application/vnd.ll.landmark", "application/x-metaverse-landmark", "landmark"),
            new TypeMapping(AssetType.Clothing, InventoryType.Wearable, "application/vnd.ll.clothing", "application/x-metaverse-clothing", "clothing"),
            new TypeMapping(AssetType.Object, InventoryType.Object, "application/vnd.ll.primitive", "application/x-metaverse-primitive", "primitive"),
            new TypeMapping(AssetType.Object, InventoryType.Attachment, "application/vnd.ll.primitive", "application/x-metaverse-primitive", "primitive"),
            new TypeMapping(AssetType.Notecard, InventoryType.Notecard, "application/vnd.ll.notecard", "application/x-metaverse-notecard", "notecard"),
            new TypeMapping(AssetType.LSLText, InventoryType.LSL, "application/vnd.ll.lsltext", "application/x-metaverse-lsl", "lsl"),
            new TypeMapping(AssetType.LSLBytecode, InventoryType.LSL, "application/vnd.ll.lslbyte", "application/x-metaverse-lso", "lso"),
            new TypeMapping(AssetType.Bodypart, InventoryType.Wearable, "application/vnd.ll.bodypart", "application/x-metaverse-bodypart", "bodypart"),
            new TypeMapping(AssetType.Animation, InventoryType.Animation, "application/vnd.ll.animation", "application/x-metaverse-animation", "animation"),
            new TypeMapping(AssetType.Gesture, InventoryType.Gesture, "application/vnd.ll.gesture", "application/x-metaverse-gesture", "gesture"),
            new TypeMapping(AssetType.Simstate, InventoryType.Snapshot, "application/x-metaverse-simstate", "simstate"),
            new TypeMapping(AssetType.Link, InventoryType.Unknown, "application/vnd.ll.link", "link"),
            new TypeMapping(AssetType.LinkFolder, InventoryType.Unknown, "application/vnd.ll.linkfolder", "linkfolder"),
            new TypeMapping(AssetType.Mesh, InventoryType.Mesh, "application/vnd.ll.mesh", "llm"),
            new TypeMapping(AssetType.Material, InventoryType.Material, "application/llsd+xml", "glftmat"),

            // The next few items are about inventory folders
            new TypeMapping(AssetType.Folder, FolderType.None, "application/vnd.ll.folder", "folder"),
            new TypeMapping(AssetType.Folder, FolderType.Root, "application/vnd.ll.rootfolder", "rootfolder"),
            new TypeMapping(AssetType.Folder, FolderType.Trash, "application/vnd.ll.trashfolder", "trashfolder"),
            new TypeMapping(AssetType.Folder, FolderType.Snapshot, "application/vnd.ll.snapshotfolder", "snapshotfolder"),
            new TypeMapping(AssetType.Folder, FolderType.LostAndFound, "application/vnd.ll.lostandfoundfolder", "lostandfoundfolder"),
            new TypeMapping(AssetType.Folder, FolderType.Favorites, "application/vnd.ll.favoritefolder", "favoritefolder"),
            new TypeMapping(AssetType.Folder, FolderType.CurrentOutfit, "application/vnd.ll.currentoutfitfolder", "currentoutfitfolder"),
            new TypeMapping(AssetType.Folder, FolderType.Outfit, "application/vnd.ll.outfitfolder", "outfitfolder"),
            new TypeMapping(AssetType.Folder, FolderType.MyOutfits, "application/vnd.ll.myoutfitsfolder", "myoutfitsfolder"),

            // This next mappping is an asset to inventory item mapping.
            // Note: LL stores folders as assets of type Folder = 8, and it has a corresponding InventoryType = 8
            // MutSea doesn't store folders as assets, so this mapping should only be used when parsing things from the viewer to the server
            new TypeMapping(AssetType.Folder, InventoryType.Folder, "application/vnd.ll.folder", "folder"),

            // MutSea specific
            new TypeMapping(AssetType.OSMaterial, InventoryType.Unknown, "application/llsd+xml", "material")
        ];

        private static readonly FrozenDictionary<sbyte, string> asset2Content;
        private static readonly FrozenDictionary<sbyte, string> asset2Extension;
        private static readonly FrozenDictionary<sbyte, string> inventory2Content;
        private static readonly FrozenDictionary<string, sbyte> content2Asset;
        private static readonly FrozenDictionary<string, sbyte> content2Inventory;
        private static readonly FrozenDictionary<string, AssetType> name2Asset = new Dictionary<string, AssetType>()
        {
            {"texture", AssetType.Texture },
            {"sound", AssetType.Sound},
            {"callcard", AssetType.CallingCard},
            {"landmark", AssetType.Landmark},
            {"script", (AssetType)4},
            {"clothing", AssetType.Clothing},
            {"object", AssetType.Object},
            {"notecard", AssetType.Notecard},
            {"category", AssetType.Folder},
            {"lsltext", AssetType.LSLText},
            {"lslbyte", AssetType.LSLBytecode},
            {"txtr_tga", AssetType.TextureTGA},
            {"bodypart", AssetType.Bodypart},
            {"snd_wav", AssetType.SoundWAV},
            {"img_tga", AssetType.ImageTGA},
            {"jpeg", AssetType.ImageJPEG},
            {"animatn", AssetType.Animation},
            {"gesture", AssetType.Gesture},
            {"simstate", AssetType.Simstate},
            {"mesh", AssetType.Mesh},
            {"settings", AssetType.Settings},
            {"material", AssetType.Material}
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<string, FolderType> name2Inventory = new Dictionary<string, FolderType>()
        {
            {"texture", FolderType.Texture},
            {"sound", FolderType.Sound},
            {"callcard", FolderType.CallingCard},
            {"landmark", FolderType.Landmark},
            {"script", (FolderType)4},
            {"clothing", FolderType.Clothing},
            {"object", FolderType.Object},
            {"notecard", FolderType.Notecard},
            {"root", FolderType.Root},
            {"lsltext", FolderType.LSLText},
            {"bodypart", FolderType.BodyPart},
            {"trash", FolderType.Trash},
            {"snapshot", FolderType.Snapshot},
            {"lostandfound", FolderType.LostAndFound},
            {"animatn", FolderType.Animation},
            {"gesture", FolderType.Gesture},
            {"favorites", FolderType.Favorites},
            {"currentoutfit", FolderType.CurrentOutfit},
            {"outfit", FolderType.Outfit},
            {"myoutfits", FolderType.MyOutfits},
            {"mesh", FolderType.Mesh},
            {"settings", FolderType.Settings},
            {"material", FolderType.Material},
            {"suitcase", FolderType.Suitcase}
        }.ToFrozenDictionary();

        static SLUtil()
        {
            Dictionary<sbyte, string> asset2Contentd = [];
            Dictionary<sbyte, string> asset2Extensiond = [];
            Dictionary<sbyte, string> inventory2Contentd = [];
            Dictionary<string, sbyte> content2Assetd = [];
            Dictionary<string, sbyte> content2Inventoryd = [];

            foreach (TypeMapping mapping in MAPPINGS)
            {
                sbyte assetType = mapping.AssetType;
                asset2Contentd.TryAdd(assetType, mapping.ContentType);
                asset2Extensiond.TryAdd(assetType, mapping.Extension);

                inventory2Contentd.TryAdd(mapping.InventoryType, mapping.ContentType);

                content2Assetd.TryAdd(mapping.ContentType, assetType);

                content2Inventoryd.TryAdd(mapping.ContentType, mapping.InventoryType);

                if (mapping.ContentType2 != null)
                {
                    content2Assetd.TryAdd(mapping.ContentType2, assetType);
                    content2Inventoryd.TryAdd(mapping.ContentType2, mapping.InventoryType);
                }
            }
            asset2Content = asset2Contentd.ToFrozenDictionary();
            asset2Extension = asset2Extensiond.ToFrozenDictionary();
            inventory2Content = inventory2Contentd.ToFrozenDictionary();
            content2Asset = content2Assetd.ToFrozenDictionary();
            content2Inventory = content2Inventoryd.ToFrozenDictionary();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AssetType SLAssetName2Type(string name)
        {
             return name2Asset.TryGetValue(name, out AssetType type) ? type : AssetType.Unknown;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FolderType SLInvName2Type(string name)
        {
            return name2Inventory.TryGetValue(name, out FolderType type) ? type : FolderType.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string SLAssetTypeToContentType(int assetType)
        {
            return asset2Content.TryGetValue((sbyte)assetType, out string contentType) ? contentType : asset2Content[(sbyte)AssetType.Unknown];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string SLInvTypeToContentType(int invType)
        {
            return inventory2Content.TryGetValue((sbyte)invType, out string contentType) ? contentType : inventory2Content[(sbyte)InventoryType.Unknown];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ContentTypeToSLAssetType(string contentType)
        {
            return content2Asset.TryGetValue(contentType, out sbyte assetType) ? assetType : (sbyte)AssetType.Unknown;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sbyte ContentTypeToSLInvType(string contentType)
        {
            return content2Inventory.TryGetValue(contentType, out sbyte invType) ? invType : (sbyte)InventoryType.Unknown;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string SLAssetTypeToExtension(int assetType)
        {
            return asset2Extension.TryGetValue((sbyte)assetType, out string extension) ? extension : asset2Extension[(sbyte)AssetType.Unknown];
        }

        #endregion SL / file extension / content-type conversions

        static readonly char[] seps = new char[] { '\t', '\n' };
        static readonly char[] stringseps = new char[] { '|', '\n' };

        static byte[] moronize = new byte[16]
        {
            60, 17, 94, 81, 4, 244, 82, 60, 159, 166, 152, 175, 241, 3, 71, 48
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int getField(string note, int start, string name, bool isString, out string value)
        {
            value = String.Empty;
            int end = -1;
            int limit = note.Length - start;
            if (limit > 64)
                limit = 64;
            int indx = note.IndexOf(name, start, limit);
            if (indx < 0)
                return -1;
            indx += name.Length + 1; // eat \t
            limit = note.Length - indx - 2;
            if (limit > 129)
                limit = 129;
            if (isString)
                end = note.IndexOfAny(stringseps, indx, limit);
            else
                end = note.IndexOfAny(seps, indx, limit);
            if (end < 0)
                return -1;
            value = note.Substring(indx, end - indx);
            return end;
        }

        private static UUID deMoronize(UUID id)
        {
            byte[] data = new byte[16];
            id.ToBytes(data,0);
            for(int i = 0; i < 16; ++i)
                data[i] ^= moronize[i];

            return new UUID(data,0);
        }

        public static InventoryItemBase GetEmbeddedItem(byte[] data, UUID itemID)
        {
            if(data == null || data.Length < 300)
                return null;

            string note = Util.UTF8.GetString(data);
            if (String.IsNullOrWhiteSpace(note))
                return null;

            // waste some time checking rigid versions
            string version = note.Substring(0,21);
            if (!version.Equals("Linden text version 2"))
                return null;

            version = note.Substring(24, 25);
            if (!version.Equals("LLEmbeddedItems version 1"))
                return null;

            int indx = note.IndexOf(itemID.ToString(), 100);
            if (indx < 0)
                return null;

            indx = note.IndexOf("permissions", indx, 100); // skip parentID
            if (indx < 0)
                return null;

            string valuestr;

            indx = getField(note, indx, "base_mask", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint basemask))
                return null;

            indx = getField(note, indx, "owner_mask", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint ownermask))
                return null;

            indx = getField(note, indx, "group_mask", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint groupmask))
                return null;

            indx = getField(note, indx, "everyone_mask", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint everyonemask))
                return null;

            indx = getField(note, indx, "next_owner_mask", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint nextownermask))
                return null;

            indx = getField(note, indx, "creator_id", false, out valuestr);
            if (indx < 0)
                return null;
            if (!UUID.TryParse(valuestr, out UUID creatorID))
                return null;

            indx = getField(note, indx, "owner_id", false, out valuestr);
            if (indx < 0)
                return null;
            if (!UUID.TryParse(valuestr, out UUID ownerID))
                return null;

            int limit = note.Length - indx;
            if (limit > 120)
                limit = 120;
            indx = note.IndexOf('}', indx, limit); // last owner
            if (indx < 0)
                return null;

            int curindx = indx;
            UUID assetID = UUID.Zero;
            indx = getField(note, indx, "asset_id", false, out valuestr);
            if (indx < 0)
            {
                indx = getField(note, curindx, "shadow_id", false, out valuestr);
                if (indx < 0)
                    return null;
                if (!UUID.TryParse(valuestr, out assetID))
                    return null;
                assetID = deMoronize(assetID);
            }
            else
            {
                if (!UUID.TryParse(valuestr, out assetID))
                    return null;
            }

            indx = getField(note, indx, "type", false, out valuestr);
            if (indx < 0)
                return null;

            AssetType assetType = SLAssetName2Type(valuestr);

            indx = getField(note, indx, "inv_type", false, out valuestr);
            if (indx < 0)
                return null;
            FolderType invType = SLInvName2Type(valuestr);

            indx = getField(note, indx, "flags", false, out valuestr);
            if (indx < 0)
                return null;
            if (!uint.TryParse(valuestr, NumberStyles.HexNumber, Culture.NumberFormatInfo, out uint flags))
                return null;

            limit = note.Length - indx;
            if (limit > 120)
                limit = 120;
            indx = note.IndexOf('}', indx, limit); // skip sale
            if (indx < 0)
                return null;

            indx = getField(note, indx, "name", true, out valuestr);
            if (indx < 0)
                return null;

            string name = valuestr;

            indx = getField(note, indx, "desc", true, out valuestr);
            if (indx < 0)
                return null;
            string desc = valuestr;

            InventoryItemBase item = new InventoryItemBase();
            item.AssetID = assetID;
            item.AssetType = (sbyte)assetType;
            item.BasePermissions = basemask;
            item.CreationDate = Util.UnixTimeSinceEpoch();
            item.CreatorData = "";
            item.CreatorId = creatorID.ToString();
            item.CurrentPermissions = ownermask;
            item.Description = desc;
            item.Flags = flags;
            item.Folder = UUID.Zero;
            item.GroupID = UUID.Zero;
            item.GroupOwned = false;
            item.GroupPermissions = groupmask;
            item.InvType = (sbyte)invType;
            item.Name = name;
            item.NextPermissions = nextownermask;
            item.Owner = ownerID;
            item.SalePrice = 0;
            item.SaleType = (byte)SaleType.Not;
            item.ID = UUID.Random();
            return item;
        }

        public static List<UUID> GetEmbeddedAssetIDs(byte[] data)
        {
            if (data == null || data.Length < 79)
                return null;

            string note = Util.UTF8.GetString(data);
            if (String.IsNullOrWhiteSpace(note))
                return null;

            // waste some time checking rigid versions
            string tmpStr = note.Substring(0, 21);
            if (!tmpStr.Equals("Linden text version 2"))
                return null;

            tmpStr = note.Substring(24, 25);
            if (!tmpStr.Equals("LLEmbeddedItems version 1"))
                return null;

            tmpStr = note.Substring(52,5);
            if (!tmpStr.Equals("count"))
                return null;

            int limit = note.Length - 57 - 2;
            if (limit > 8)
                limit = 8;

            int indx = note.IndexOfAny(seps, 57, limit);
            if(indx < 0)
                return null;

            if (!int.TryParse(note.Substring(57, indx - 57), out int count))
                return null;

            List<UUID> ids = new List<UUID>();
            while(count > 0)
            {
                string valuestr;
                UUID assetID = UUID.Zero;
                indx = note.IndexOf('}',indx); // skip to end of permissions
                if (indx < 0)
                    return null;

                int curindx = indx;
                indx = getField(note, indx, "asset_id", false, out valuestr);
                if (indx < 0)
                {
                    indx = getField(note, curindx, "shadow_id", false, out valuestr);
                    if (indx < 0)
                        return null;
                    if (!UUID.TryParse(valuestr, out assetID))
                        return null;
                    assetID = deMoronize(assetID);
                }
                else
                {
                    if (!UUID.TryParse(valuestr, out assetID))
                        return null;
                }
                ids.Add(assetID);

                indx = note.IndexOf('}', indx); // skip to end of sale
                if (indx < 0)
                    return null;
                indx = getField(note, indx, "name", false, out valuestr); // avoid name contents
                if (indx < 0)
                    return null;
                indx = getField(note, indx, "desc", false, out valuestr); // avoid desc contents
                if (indx < 0)
                    return null;

                if(count > 1)
                {
                    indx = note.IndexOf("ext char index", indx); // skip to next
                    if (indx < 0)
                        return null;
                }
                --count;
            }

            indx = note.IndexOf("Text length",indx);
            if(indx > 0)
            {
                indx += 14;
                List<UUID> textIDs = Util.GetUUIDsOnString(note.AsSpan(indx, note.Length - indx));
                if (textIDs.Count > 0)
                    ids.AddRange(textIDs);
            }
            if (ids.Count == 0)
                return null;
            return ids;
        }

        /// <summary>
        /// Parse a notecard in Linden format to a list of ordinary lines for LSL
        /// </summary>
        /// <param name="rawInput"></param>
        /// <returns></returns>

        public static string[] ParseNotecardToArray(byte[] data)
        {
            // check of a valid notecard
            if (data == null || data.Length < 79)
                return new string[0];

            //LSL can't read notecards with embedded items
            if (data[58] != '0' || data[59] != '\n')
                return new string[0];

            string note = Util.UTF8.GetString(data);
            if (String.IsNullOrWhiteSpace(note))
                return new string[0];

            // waste some time checking rigid versions
            string tmpStr = note.Substring(0, 21);
            if (!tmpStr.Equals("Linden text version 2"))
                return new string[0];

            tmpStr = note.Substring(24, 25);
            if (!tmpStr.Equals("LLEmbeddedItems version 1"))
                return new string[0];

            tmpStr = note.Substring(52, 5);
            if (!tmpStr.Equals("count"))
                return new string[0];

            int indx = note.IndexOf("Text length", 60);
            if(indx < 0)
                return new string[0];

            indx += 12;
            int end = indx + 1;
            for (; end < note.Length && note[end] != '\n'; ++end);
            if (note[end] != '\n')
                return new string[0];

            tmpStr = note.Substring(indx, end - indx);
            if (!int.TryParse(tmpStr, out int textLen) || textLen == 0)
                return new string[0];

            indx = end + 1;
            if (textLen + indx > data.Length)
                return new string[0];
            // yeackk
            note = Util.UTF8.GetString(data, indx, textLen);
            textLen = note.Length;
            indx = 0;
            var lines = new List<string>();
            while (indx < textLen)
            {
                end = indx;
                for (; end < textLen && note[end] != '\n'; ++end);
                if(end == indx)
                    lines.Add(String.Empty);
                else
                    lines.Add(note.Substring(indx, end - indx));
                indx = end + 1;
            }
            // notes only seem to have one text section

            if(lines.Count == 0)
                return new string[0];
            return lines.ToArray();
        }

        // libomv has old names on ATTACH_LEFT_PEC and ATTACH_RIGHT_PEC
        public static readonly string[] AttachmentPointNames = new string[]
        {
            string.Empty,
            "ATTACH_CHEST", // 1
            "ATTACH_HEAD", // 2
            "ATTACH_LSHOULDER", // 3
            "ATTACH_RSHOULDER", // 4
            "ATTACH_LHAND", // 5
            "ATTACH_RHAND", // 6
            "ATTACH_LFOOT", // 7
            "ATTACH_RFOOT", // 8
            "ATTACH_BACK", // 9
            "ATTACH_PELVIS", // 10
            "ATTACH_MOUTH", // 11
            "ATTACH_CHIN", // 12
            "ATTACH_LEAR", // 13
            "ATTACH_REAR", // 14
            "ATTACH_LEYE", // 15
            "ATTACH_REYE", // 16
            "ATTACH_NOSE", // 17
            "ATTACH_RUARM", // 18
            "ATTACH_RLARM", // 19
            "ATTACH_LUARM", // 20
            "ATTACH_LLARM", // 21
            "ATTACH_RHIP", // 22
            "ATTACH_RULEG", // 23
            "ATTACH_RLLEG", // 24
            "ATTACH_LHIP", // 25
            "ATTACH_LULEG", // 26
            "ATTACH_LLLEG", // 27
            "ATTACH_BELLY", // 28
            "ATTACH_LEFT_PEC", // 29
            "ATTACH_RIGHT_PEC", // 30
            "ATTACH_HUD_CENTER_2", // 31
            "ATTACH_HUD_TOP_RIGHT", // 32
            "ATTACH_HUD_TOP_CENTER", // 33
            "ATTACH_HUD_TOP_LEFT", // 34
            "ATTACH_HUD_CENTER_1", // 35
            "ATTACH_HUD_BOTTOM_LEFT", // 36
            "ATTACH_HUD_BOTTOM", // 37
            "ATTACH_HUD_BOTTOM_RIGHT", // 38
            "ATTACH_NECK", // 39
            "ATTACH_AVATAR_CENTER", // 40
            "ATTACH_LHAND_RING1", // 41
            "ATTACH_RHAND_RING1", // 42
            "ATTACH_TAIL_BASE", // 43
            "ATTACH_TAIL_TIP", // 44
            "ATTACH_LWING", // 45
            "ATTACH_RWING", // 46
            "ATTACH_FACE_JAW", // 47
            "ATTACH_FACE_LEAR", // 48
            "ATTACH_FACE_REAR", // 49
            "ATTACH_FACE_LEYE", // 50
            "ATTACH_FACE_REYE", // 51
            "ATTACH_FACE_TONGUE", // 52
            "ATTACH_GROIN", // 53
            "ATTACH_HIND_LFOOT", // 54
            "ATTACH_HIND_RFOOT" // 55
        };

        public static string GetAttachmentName(int point)
        {
            if(point < AttachmentPointNames.Length)
                return AttachmentPointNames[point];
            return "Unknown";
        }
    }
}
