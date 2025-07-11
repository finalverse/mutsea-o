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
using System.Collections.Frozen;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Xml;
using log4net;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Serialization.External;

namespace MutSea.Region.Framework.Scenes.Serialization
{
    /// <summary>
    /// Serialize and deserialize scene objects.
    /// </summary>
    /// This should really be in MutSea.Framework.Serialization but this would mean circular dependency problems
    /// right now - hopefully this isn't forever.
    public class SceneObjectSerializer
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static IUserManagement m_UserManagement;

        /// <summary>
        /// Deserialize a scene object from the original xml format
        /// </summary>
        /// <param name="xmlData"></param>
        /// <returns>The scene object deserialized.  Null on failure.</returns>
        public static SceneObjectGroup FromOriginalXmlFormat(string xmlData)
        {
            string fixedData = ExternalRepresentationUtils.SanitizeXml(xmlData);
            using (XmlTextReader wrappedReader = new(fixedData, XmlNodeType.Element, null))
            {
                using (XmlReader reader = XmlReader.Create(wrappedReader, Util.SharedXmlReaderSettings))
                {
                    try
                    {
                        return FromOriginalXmlFormat(reader);
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[SERIALIZER]: Deserialization of xml failed ", e);
                        Util.LogFailedXML("[SERIALIZER]:", fixedData);
                        return null;
                    }
                }
            }
        }
        /*
        public static SceneObjectGroup FromOriginalXmlData(byte[] data)
        {
            int len = data.Length;
            if(len < 32)
                return null;
            if(data[len -1 ] == 0)
                --len;

            XmlReaderSettings xset = new XmlReaderSettings() { IgnoreWhitespace = true, ConformanceLevel = ConformanceLevel.Fragment, CloseInput = true };
            XmlParserContext xpc = new XmlParserContext(null, null, null, XmlSpace.None);
            xpc.Encoding = Util.UTF8NoBomEncoding;
            MemoryStream ms = new MemoryStream(data, 0, len, false);
            using (XmlReader reader = XmlReader.Create(ms, xset, xpc))
            {
                try
                {
                    return FromOriginalXmlFormat(reader);
                }
                catch (Exception e)
                {
                    m_log.Error("[SERIALIZER]: Deserialization of xml data failed ", e);
                    return null;
                }
            }
        }
        */

        /// <summary>
        /// Deserialize a scene object from the original xml format
        /// </summary>
        /// <param name="xmlData"></param>
        /// <returns>The scene object deserialized.  Null on failure.</returns>
        public static SceneObjectGroup FromOriginalXmlFormat(XmlReader reader)
        {
            //m_log.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = System.Environment.TickCount;

            int linkNum;

            reader.ReadToFollowing("RootPart");
            reader.ReadToFollowing("SceneObjectPart");
            SceneObjectGroup sceneObject = new(SceneObjectPart.FromXml(reader));
            reader.ReadToFollowing("OtherParts");

            if (reader.ReadToDescendant("Part"))
            {
                do
                {
                    if (reader.ReadToDescendant("SceneObjectPart"))
                    {
                        SceneObjectPart part = SceneObjectPart.FromXml(reader);
                        linkNum = part.LinkNum;
                        sceneObject.AddPart(part);
                        part.LinkNum = linkNum;
                        part.TrimPermissions();
                    }
                }
                while (reader.ReadToNextSibling("Part"));
                reader.ReadEndElement();
            }
            else
                reader.Read();

            if (reader.Name == "KeyframeMotion" && reader.NodeType == XmlNodeType.Element)
            {
                string innerkeytxt = reader.ReadElementContentAsString();
                sceneObject.RootPart.KeyframeMotion = KeyframeMotion.FromData(sceneObject, Convert.FromBase64String(innerkeytxt));
            }

            if (reader.Name == "lnkstdt" && reader.NodeType == XmlNodeType.Element)
            {
                string innerlnkstdttxt = reader.ReadElementContentAsString();
                sceneObject.LinksetData = LinksetData.FromXML(innerlnkstdttxt.AsSpan());
            }

            if (reader.Name == "StartStr" && reader.NodeType == XmlNodeType.Element)
            {
                sceneObject.RezStringParameter = reader.ReadElementContentAsString();
            }

            // Script state may, or may not, exist. Not having any, is NOT
            // ever a problem.
            sceneObject.LoadScriptState(reader);

            sceneObject.InvalidateDeepEffectivePerms();
            return sceneObject;
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject)
        {
            return ToOriginalXmlFormat(sceneObject, true);
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="doScriptStates">Control whether script states are also serialized.</para>
        /// <returns></returns>
        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject, bool doScriptStates)
        {
            using (StringWriter sw = new())
            {
                using (XmlTextWriter writer = new(sw))
                {
                    ToOriginalXmlFormat(sceneObject, writer, doScriptStates);
                }

                return sw.ToString();
            }
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static void ToOriginalXmlFormat(SceneObjectGroup sceneObject, XmlTextWriter writer, bool doScriptStates)
        {
            ToOriginalXmlFormat(sceneObject, writer, doScriptStates, false);
        }

        public static string ToOriginalXmlFormat(SceneObjectGroup sceneObject, string scriptedState)
        {
            using (StringWriter sw = new())
            {
                using (XmlTextWriter writer = new(sw))
                {
                    writer.WriteStartElement(string.Empty, "SceneObjectGroup", string.Empty);

                    ToOriginalXmlFormat(sceneObject, writer, false, true);

                    writer.WriteRaw(scriptedState);

                    writer.WriteEndElement();
                }
                return sw.ToString();
            }
        }

        /// <summary>
        /// Serialize a scene object to the original xml format
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <param name="writer"></param>
        /// <param name="noRootElement">If false, don't write the enclosing SceneObjectGroup element</param>
        /// <returns></returns>
        public static void ToOriginalXmlFormat(
            SceneObjectGroup sceneObject, XmlTextWriter writer, bool doScriptStates, bool noRootElement)
        {
            //m_log.DebugFormat("[SERIALIZER]: Starting serialization of {0}", sceneObject.Name);
            //int time = System.Environment.TickCount;

            if (!noRootElement)
                writer.WriteStartElement(string.Empty, "SceneObjectGroup", string.Empty);

            writer.WriteStartElement(string.Empty, "RootPart", string.Empty);
            ToXmlFormat(sceneObject.RootPart, writer);
            writer.WriteEndElement();
            writer.WriteStartElement(string.Empty, "OtherParts", string.Empty);

            SceneObjectPart[] parts = sceneObject.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                SceneObjectPart part = parts[i];
                if (part.UUID != sceneObject.RootPart.UUID)
                {
                    writer.WriteStartElement(string.Empty, "Part", string.Empty);
                    ToXmlFormat(part, writer);
                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement(); // OtherParts

            if (sceneObject.RootPart.KeyframeMotion != null)
            {
                byte[] data = sceneObject.RootPart.KeyframeMotion.Serialize();

                writer.WriteStartElement(string.Empty, "KeyframeMotion", string.Empty);
                writer.WriteBase64(data, 0, data.Length);
                writer.WriteEndElement();
            }

            if(sceneObject.RezStringParameter is not null)
                writer.WriteElementString("StartStr", sceneObject.RezStringParameter);

            sceneObject.LinksetData?.ToXML(writer);

            if (doScriptStates)
                sceneObject.SaveScriptedState(writer);

            if (!noRootElement)
                writer.WriteEndElement(); // SceneObjectGroup

            //m_log.DebugFormat("[SERIALIZER]: Finished serialization of SOG {0}, {1}ms", sceneObject.Name, System.Environment.TickCount - time);
        }

        protected static void ToXmlFormat(SceneObjectPart part, XmlTextWriter writer)
        {
            SOPToXml2(writer, part, new Dictionary<string, object>());
        }

        public static SceneObjectGroup FromXml2Format(string xmlData)
        {
            //m_log.DebugFormat("[SOG]: Starting deserialization of SOG");
            //int time = System.Environment.TickCount;

            try
            {
                XmlDocument doc = new();
                doc.LoadXml(xmlData);

                XmlNodeList parts = doc.GetElementsByTagName("SceneObjectPart");

                if (parts.Count == 0)
                {
                    m_log.Error("[SERIALIZER]: Deserialization of xml failed: No SceneObjectPart nodes");
                    Util.LogFailedXML("[SERIALIZER]:", xmlData);
                    return null;
                }

                SceneObjectGroup sceneObject;
                using(StringReader sr = new(parts[0].OuterXml))
                {
                    using(XmlTextReader reader = new(sr))
                    {
                        reader.DtdProcessing = DtdProcessing.Ignore;
                        sceneObject = new SceneObjectGroup(SceneObjectPart.FromXml(reader));
                    }
                }

                // Then deal with the rest
                SceneObjectPart part;
                for (int i = 1; i < parts.Count; i++)
                {
                    using(StringReader sr = new(parts[i].OuterXml))
                    {
                        using(XmlTextReader reader = new(sr))
                        {
                            reader.DtdProcessing = DtdProcessing.Ignore;
                            part = SceneObjectPart.FromXml(reader);
                        }
                    }

                    int originalLinkNum = part.LinkNum;

                    sceneObject.AddPart(part);

                    // SceneObjectGroup.AddPart() tries to be smart and automatically set the LinkNum.
                    // We override that here
                    if (originalLinkNum != 0)
                        part.LinkNum = originalLinkNum;
                }

                XmlNodeList keymotion = doc.GetElementsByTagName("KeyframeMotion");
                if (keymotion.Count > 0)
                    sceneObject.RootPart.KeyframeMotion = KeyframeMotion.FromData(sceneObject, Convert.FromBase64String(keymotion[0].InnerText));

                XmlNodeList keylinksetdata = doc.GetElementsByTagName("lnkstdt");
                if (keylinksetdata.Count > 0)
                    sceneObject.LinksetData = LinksetData.FromXML(keylinksetdata[0].InnerText.AsSpan());

                XmlNodeList StartStr = doc.GetElementsByTagName("StartStr");
                if (StartStr.Count > 0)
                    sceneObject.RezStringParameter = StartStr[0].InnerText;

                // Script state may, or may not, exist. Not having any, is NOT
                // ever a problem.
                sceneObject.LoadScriptState(doc);
                //sceneObject.AggregatePerms();
                return sceneObject;
            }
            catch (Exception e)
            {
                m_log.Error("[SERIALIZER]: Deserialization of xml failed ", e);
                Util.LogFailedXML("[SERIALIZER]:", xmlData);
                return null;
            }
        }

        /// <summary>
        /// Serialize a scene object to the 'xml2' format.
        /// </summary>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        public static string ToXml2Format(SceneObjectGroup sceneObject)
        {
            using (StringWriter sw = new())
            {
                using (XmlTextWriter writer = new(sw))
                {
                    SOGToXml2(writer, sceneObject, new Dictionary<string,object>());
                }

                return sw.ToString();
            }
        }

        /// <summary>
        /// Modifies a SceneObjectGroup.
        /// </summary>
        /// <param name="sog">The object</param>
        /// <returns>Whether the object was actually modified</returns>
        public delegate bool SceneObjectModifier(SceneObjectGroup sog);

        /// <summary>
        /// Modifies an object by deserializing it; applying 'modifier' to each SceneObjectGroup; and reserializing.
        /// </summary>
        /// <param name="assetId">The object's UUID</param>
        /// <param name="data">Serialized data</param>
        /// <param name="modifier">The function to run on each SceneObjectGroup</param>
        /// <returns>The new serialized object's data, or null if an error occurred</returns>
        public static byte[] ModifySerializedObject(UUID assetId, byte[] data, SceneObjectModifier modifier)
        {
            List<SceneObjectGroup> sceneObjects = new();

            string xmlData = ExternalRepresentationUtils.SanitizeXml(Utils.BytesToString(data));
            if (CoalescedSceneObjectsSerializer.TryFromXml(xmlData, out CoalescedSceneObjects coa))
            {
                // m_log.DebugFormat("[SERIALIZER]: Loaded coalescence {0} has {1} objects", assetId, coa.Count);

                if (coa.Objects.Count == 0)
                {
                    m_log.WarnFormat("[SERIALIZER]: Aborting load of coalesced object from asset {0} as it has zero loaded components", assetId);
                    return null;
                }

                sceneObjects.AddRange(coa.Objects);
            }
            else
            {
                SceneObjectGroup deserializedObject = FromOriginalXmlFormat(xmlData);

                if (deserializedObject is not null)
                {
                    sceneObjects.Add(deserializedObject);
                }
                else
                {
                    m_log.WarnFormat("[SERIALIZER]: Aborting load of object from asset {0} as deserialization failed", assetId);
                    return null;
                }
            }

            bool modified = false;
            foreach (SceneObjectGroup sog in sceneObjects)
            {
                if (modifier(sog))
                    modified = true;
            }

            if (modified)
            {
                if (coa is not null)
                    data = Utils.StringToBytes(CoalescedSceneObjectsSerializer.ToXml(coa));
                else
                    data = Utils.StringToBytes(ToOriginalXmlFormat(sceneObjects[0]));
            }

            return data;
        }

        #region manual serialization

        private static readonly FrozenDictionary<string, Action<SceneObjectPart, XmlReader>> m_SOPXmlProcessors = new Dictionary<string, Action<SceneObjectPart, XmlReader>>()
        {
            {"AllowedDrop", ProcessAllowedDrop },
            {"CreatorID", ProcessCreatorID },
            {"CreatorData", ProcessCreatorData },
            {"FolderID", ProcessFolderID },
            {"InventorySerial", ProcessInventorySerial },
            {"TaskInventory", ProcessTaskInventory },
            {"UUID", ProcessUUID },
            {"LocalId", ProcessLocalId },
            {"Name", ProcessName },
            {"Material", ProcessMaterial },
            {"PassTouches", ProcessPassTouches },
            {"PassCollisions", ProcessPassCollisions },
            {"RegionHandle", ProcessRegionHandle },
            {"ScriptAccessPin", ProcessScriptAccessPin },
            {"GroupPosition", ProcessGroupPosition },
            {"OffsetPosition", ProcessOffsetPosition },
            {"RotationOffset", ProcessRotationOffset },
            {"Velocity", ProcessVelocity },
            {"AngularVelocity", ProcessAngularVelocity },
            {"Acceleration", ProcessAcceleration },
            {"Description", ProcessDescription },
            {"Color", ProcessColor },
            {"Text", ProcessText },
            {"SitName", ProcessSitName },
            {"TouchName", ProcessTouchName },
            {"LinkNum", ProcessLinkNum },
            {"ClickAction", ProcessClickAction },
            {"Shape", ProcessShape },
            {"Scale", ProcessScale },
            {"SitTargetOrientation", ProcessSitTargetOrientation },
            {"SitTargetPosition", ProcessSitTargetPosition },
            {"SitTargetPositionLL", ProcessSitTargetPositionLL },
            {"SitTargetOrientationLL", ProcessSitTargetOrientationLL },
            {"StandTarget", ProcessStandTarget },
            {"ParentID", ProcessParentID },
            {"CreationDate", ProcessCreationDate },
            {"Category", ProcessCategory },
            {"SalePrice", ProcessSalePrice },
            {"ObjectSaleType", ProcessObjectSaleType },
            {"OwnershipCost", ProcessOwnershipCost },
            {"GroupID", ProcessGroupID },
            {"OwnerID", ProcessOwnerID },
            {"LastOwnerID", ProcessLastOwnerID },
            {"RezzerID", ProcessRezzerID },
            {"BaseMask", ProcessBaseMask },
            {"OwnerMask", ProcessOwnerMask },
            {"GroupMask", ProcessGroupMask },
            {"EveryoneMask", ProcessEveryoneMask },
            {"NextOwnerMask", ProcessNextOwnerMask },
            {"Flags", ProcessFlags },
            {"CollisionSound", ProcessCollisionSound },
            {"CollisionSoundVolume", ProcessCollisionSoundVolume },
            {"MediaUrl", ProcessMediaUrl },
            {"AttachedPos", ProcessAttachedPos },
            {"DynAttrs", ProcessDynAttrs },
            {"TextureAnimation", ProcessTextureAnimation },
            {"ParticleSystem", ProcessParticleSystem },
            {"PayPrice0", ProcessPayPrice0 },
            {"PayPrice1", ProcessPayPrice1 },
            {"PayPrice2", ProcessPayPrice2 },
            {"PayPrice3", ProcessPayPrice3 },
            {"PayPrice4", ProcessPayPrice4 },

            {"Buoyancy", ProcessBuoyancy },
            {"Force", ProcessForce },
            {"Torque", ProcessTorque },
            {"VolumeDetectActive", ProcessVolumeDetectActive },

            {"Vehicle", ProcessVehicle },

            {"PhysicsInertia", ProcessPhysicsInertia },

            {"RotationAxisLocks", ProcessRotationAxisLocks },
            {"PhysicsShapeType", ProcessPhysicsShapeType },
            {"Density", ProcessDensity },
            {"Friction", ProcessFriction },
            {"Bounce", ProcessBounce },
            {"GravityModifier", ProcessGravityModifier },
            {"CameraEyeOffset", ProcessCameraEyeOffset },
            {"CameraAtOffset", ProcessCameraAtOffset },

            {"SoundID", ProcessSoundID },
            {"SoundGain", ProcessSoundGain },
            {"SoundFlags", ProcessSoundFlags },
            {"SoundRadius", ProcessSoundRadius },
            {"SoundQueueing", ProcessSoundQueueing },

            {"SOPAnims", ProcessSOPAnims },

            {"SitActRange", ProcessSitActRange }
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<string, Action<TaskInventoryItem, XmlReader>> m_TaskInventoryXmlProcessors = new Dictionary<string, Action<TaskInventoryItem, XmlReader>>()
        {
            {"AssetID", ProcessTIAssetID },
            {"BasePermissions", ProcessTIBasePermissions },
            {"CreationDate", ProcessTICreationDate },
            {"CreatorID", ProcessTICreatorID },
            {"CreatorData", ProcessTICreatorData },
            {"Description", ProcessTIDescription },
            {"EveryonePermissions", ProcessTIEveryonePermissions },
            {"Flags", ProcessTIFlags },
            {"GroupID", ProcessTIGroupID },
            {"GroupPermissions", ProcessTIGroupPermissions },
            {"InvType", ProcessTIInvType },
            {"ItemID", ProcessTIItemID },
            {"OldItemID", ProcessTIOldItemID },
            {"LastOwnerID", ProcessTILastOwnerID },
            {"Name", ProcessTIName },
            {"NextPermissions", ProcessTINextPermissions },
            {"OwnerID", ProcessTIOwnerID },
            {"CurrentPermissions", ProcessTICurrentPermissions },
            {"ParentID", ProcessTIParentID },
            {"ParentPartID", ProcessTIParentPartID },
            {"PermsGranter", ProcessTIPermsGranter },
            {"PermsMask", ProcessTIPermsMask },
            {"Type", ProcessTIType },
            {"OwnerChanged", ProcessTIOwnerChanged }
        }.ToFrozenDictionary();

        private static readonly FrozenDictionary<string, Action<PrimitiveBaseShape, XmlReader>> m_ShapeXmlProcessors = new Dictionary<string, Action<PrimitiveBaseShape, XmlReader>>()
        {
            {"ProfileCurve", ProcessShpProfileCurve },
            {"TextureEntry", ProcessShpTextureEntry },
            {"ExtraParams", ProcessShpExtraParams },
            {"PathBegin", ProcessShpPathBegin },
            {"PathCurve", ProcessShpPathCurve },
            {"PathEnd", ProcessShpPathEnd },
            {"PathRadiusOffset", ProcessShpPathRadiusOffset },
            {"PathRevolutions", ProcessShpPathRevolutions },
            {"PathScaleX", ProcessShpPathScaleX },
            {"PathScaleY", ProcessShpPathScaleY },
            {"PathShearX", ProcessShpPathShearX },
            {"PathShearY", ProcessShpPathShearY },
            {"PathSkew", ProcessShpPathSkew },
            {"PathTaperX", ProcessShpPathTaperX },
            {"PathTaperY", ProcessShpPathTaperY },
            {"PathTwist", ProcessShpPathTwist },
            {"PathTwistBegin", ProcessShpPathTwistBegin },
            {"PCode", ProcessShpPCode },
            {"ProfileBegin", ProcessShpProfileBegin },
            {"ProfileEnd", ProcessShpProfileEnd },
            {"ProfileHollow", ProcessShpProfileHollow },
            {"Scale", ProcessShpScale },
            {"LastAttachPoint", ProcessShpLastAttach },
            {"State", ProcessShpState },
            {"ProfileShape", ProcessShpProfileShape },
            {"HollowShape", ProcessShpHollowShape },
            {"SculptTexture", ProcessShpSculptTexture },
            {"SculptType", ProcessShpSculptType },
            // Ignore "SculptData"; this element is deprecated
            {"FlexiSoftness", ProcessShpFlexiSoftness },
            {"FlexiTension", ProcessShpFlexiTension },
            {"FlexiDrag", ProcessShpFlexiDrag },
            {"FlexiGravity", ProcessShpFlexiGravity },
            {"FlexiWind", ProcessShpFlexiWind },
            {"FlexiForceX", ProcessShpFlexiForceX },
            {"FlexiForceY", ProcessShpFlexiForceY },
            {"FlexiForceZ", ProcessShpFlexiForceZ },
            {"LightColorR", ProcessShpLightColorR },
            {"LightColorG", ProcessShpLightColorG },
            {"LightColorB", ProcessShpLightColorB },
            {"LightColorA", ProcessShpLightColorA },
            {"LightRadius", ProcessShpLightRadius },
            {"LightCutoff", ProcessShpLightCutoff },
            {"LightFalloff", ProcessShpLightFalloff },
            {"LightIntensity", ProcessShpLightIntensity },
            {"FlexiEntry", ProcessShpFlexiEntry },
            {"LightEntry", ProcessShpLightEntry },
            {"SculptEntry", ProcessShpSculptEntry },
            {"Media", ProcessShpMedia },
            {"MatOvrd", ProcessShpMatOvrd }
        }.ToFrozenDictionary();

        #region SOPXmlProcessors
        private static void ProcessAllowedDrop(SceneObjectPart obj, XmlReader reader)
        {
            obj.AllowedDrop = Util.ReadBoolean(reader);
        }

        private static void ProcessCreatorID(SceneObjectPart obj, XmlReader reader)
        {
            obj.CreatorID = Util.ReadUUID(reader, "CreatorID");
        }

        private static void ProcessCreatorData(SceneObjectPart obj, XmlReader reader)
        {
            obj.CreatorData = reader.ReadElementContentAsString("CreatorData", string.Empty);
        }

        private static void ProcessFolderID(SceneObjectPart obj, XmlReader reader)
        {
            obj.FolderID = Util.ReadUUID(reader, "FolderID");
        }

        private static void ProcessInventorySerial(SceneObjectPart obj, XmlReader reader)
        {
            obj.InventorySerial = (uint)reader.ReadElementContentAsInt("InventorySerial", string.Empty);
        }

        private static void ProcessTaskInventory(SceneObjectPart obj, XmlReader reader)
        {
            obj.TaskInventory = ReadTaskInventory(reader, "TaskInventory");
        }

        private static void ProcessUUID(SceneObjectPart obj, XmlReader reader)
        {
            obj.UUID = Util.ReadUUID(reader, "UUID");
        }

        private static void ProcessLocalId(SceneObjectPart obj, XmlReader reader)
        {
            obj.LocalId = (uint)reader.ReadElementContentAsLong("LocalId", string.Empty);
        }

        private static void ProcessName(SceneObjectPart obj, XmlReader reader)
        {
            obj.Name = reader.ReadElementString("Name");
        }

        private static void ProcessMaterial(SceneObjectPart obj, XmlReader reader)
        {
            obj.Material = (byte)reader.ReadElementContentAsInt("Material", string.Empty);
        }

        private static void ProcessPassTouches(SceneObjectPart obj, XmlReader reader)
        {
            obj.PassTouches = Util.ReadBoolean(reader);
        }

        private static void ProcessPassCollisions(SceneObjectPart obj, XmlReader reader)
        {
            obj.PassCollisions = Util.ReadBoolean(reader);
        }

        private static void ProcessRegionHandle(SceneObjectPart obj, XmlReader reader)
        {
            obj.RegionHandle = (ulong)reader.ReadElementContentAsLong("RegionHandle", string.Empty);
        }

        private static void ProcessScriptAccessPin(SceneObjectPart obj, XmlReader reader)
        {
            obj.ScriptAccessPin = reader.ReadElementContentAsInt("ScriptAccessPin", string.Empty);
        }

        private static void ProcessGroupPosition(SceneObjectPart obj, XmlReader reader)
        {
            obj.GroupPosition = Util.ReadVector(reader, "GroupPosition");
        }

        private static void ProcessOffsetPosition(SceneObjectPart obj, XmlReader reader)
        {
            obj.OffsetPosition = Util.ReadVector(reader, "OffsetPosition"); ;
        }

        private static void ProcessRotationOffset(SceneObjectPart obj, XmlReader reader)
        {
            obj.RotationOffset = Util.ReadQuaternion(reader, "RotationOffset");
        }

        private static void ProcessVelocity(SceneObjectPart obj, XmlReader reader)
        {
            obj.Velocity = Util.ReadVector(reader, "Velocity");
        }

        private static void ProcessAngularVelocity(SceneObjectPart obj, XmlReader reader)
        {
            obj.AngularVelocity = Util.ReadVector(reader, "AngularVelocity");
        }

        private static void ProcessAcceleration(SceneObjectPart obj, XmlReader reader)
        {
            obj.Acceleration = Util.ReadVector(reader, "Acceleration");
        }

        private static void ProcessDescription(SceneObjectPart obj, XmlReader reader)
        {
            obj.Description = reader.ReadElementString("Description");
        }

        private static void ProcessColor(SceneObjectPart obj, XmlReader reader)
        {
            reader.ReadStartElement("Color");
            if (reader.Name == "R")
            {
                float r = reader.ReadElementContentAsFloat("R", string.Empty);
                float g = reader.ReadElementContentAsFloat("G", string.Empty);
                float b = reader.ReadElementContentAsFloat("B", string.Empty);
                float a = reader.ReadElementContentAsFloat("A", string.Empty);
                obj.Color = Color.FromArgb((int)a, (int)r, (int)g, (int)b);
                reader.ReadEndElement();
            }
        }

        private static void ProcessText(SceneObjectPart obj, XmlReader reader)
        {
            obj.Text = reader.ReadElementString("Text", string.Empty);
        }

        private static void ProcessSitName(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitName = reader.ReadElementString("SitName", string.Empty);
        }

        private static void ProcessTouchName(SceneObjectPart obj, XmlReader reader)
        {
            obj.TouchName = reader.ReadElementString("TouchName", string.Empty);
        }

        private static void ProcessLinkNum(SceneObjectPart obj, XmlReader reader)
        {
            obj.LinkNum = reader.ReadElementContentAsInt("LinkNum", string.Empty);
        }

        private static void ProcessClickAction(SceneObjectPart obj, XmlReader reader)
        {
            obj.ClickAction = (byte)reader.ReadElementContentAsInt("ClickAction", string.Empty);
        }

        private static void ProcessRotationAxisLocks(SceneObjectPart obj, XmlReader reader)
        {
            obj.RotationAxisLocks = (byte)reader.ReadElementContentAsInt("RotationAxisLocks", string.Empty);
        }

        private static void ProcessPhysicsShapeType(SceneObjectPart obj, XmlReader reader)
        {
            obj.PhysicsShapeType = (byte)reader.ReadElementContentAsInt("PhysicsShapeType", string.Empty);
        }

        private static void ProcessDensity(SceneObjectPart obj, XmlReader reader)
        {
            obj.Density = reader.ReadElementContentAsFloat("Density", string.Empty);
        }

        private static void ProcessFriction(SceneObjectPart obj, XmlReader reader)
        {
            obj.Friction = reader.ReadElementContentAsFloat("Friction", string.Empty);
        }

        private static void ProcessBounce(SceneObjectPart obj, XmlReader reader)
        {
            obj.Restitution = reader.ReadElementContentAsFloat("Bounce", string.Empty);
        }

        private static void ProcessGravityModifier(SceneObjectPart obj, XmlReader reader)
        {
            obj.GravityModifier = reader.ReadElementContentAsFloat("GravityModifier", string.Empty);
        }

        private static void ProcessCameraEyeOffset(SceneObjectPart obj, XmlReader reader)
        {
            obj.SetCameraEyeOffset(Util.ReadVector(reader, "CameraEyeOffset"));
        }

        private static void ProcessCameraAtOffset(SceneObjectPart obj, XmlReader reader)
        {
            obj.SetCameraAtOffset(Util.ReadVector(reader, "CameraAtOffset"));
        }

        private static void ProcessSoundID(SceneObjectPart obj, XmlReader reader)
        {
            obj.Sound = Util.ReadUUID(reader, "SoundID");
        }

        private static void ProcessSoundGain(SceneObjectPart obj, XmlReader reader)
        {
            obj.SoundGain = reader.ReadElementContentAsDouble("SoundGain", string.Empty);
        }

        private static void ProcessSoundFlags(SceneObjectPart obj, XmlReader reader)
        {
            obj.SoundFlags = (byte)reader.ReadElementContentAsInt("SoundFlags", string.Empty);
        }

        private static void ProcessSoundRadius(SceneObjectPart obj, XmlReader reader)
        {
            obj.SoundRadius = reader.ReadElementContentAsDouble("SoundRadius", string.Empty);
        }

        private static void ProcessSoundQueueing(SceneObjectPart obj, XmlReader reader)
        {
            obj.SoundQueueing = Util.ReadBoolean(reader);
        }

        private static void ProcessSitActRange(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitActiveRange = reader.ReadElementContentAsFloat("SitActRange", string.Empty);
        }

        private static void ProcessVehicle(SceneObjectPart obj, XmlReader reader)
        {
            SOPVehicle vehicle = SOPVehicle.FromXml2(reader);
            if (vehicle == null)
            {
                obj.VehicleParams = null;
                m_log.DebugFormat(
                    "[SceneObjectSerializer]: Parsing Vehicle for object part {0} {1} encountered errors.  Please see earlier log entries.",
                    obj.Name, obj.UUID);
            }
            else
            {
                obj.VehicleParams = vehicle;
            }
        }

        private static void ProcessPhysicsInertia(SceneObjectPart obj, XmlReader reader)
        {
            PhysicsInertiaData pdata = PhysicsInertiaData.FromXml2(reader);

            if (pdata == null)
            {
                obj.PhysicsInertia = null;
                m_log.DebugFormat(
                    "[SceneObjectSerializer]: Parsing PhysicsInertiaData for object part {0} {1} encountered errors.  Please see earlier log entries.",
                    obj.Name, obj.UUID);
            }
            else
            {
                obj.PhysicsInertia = pdata;
            }
        }

        private static void ProcessSOPAnims(SceneObjectPart obj, XmlReader reader)
        {
            obj.Animations = null;
            try
            {
                string datastr = reader.ReadElementContentAsString();
                if (string.IsNullOrEmpty(datastr))
                    return;

                byte[] pdata = Convert.FromBase64String(datastr);
                obj.DeSerializeAnimations(pdata);
                return;
            }
            catch { }

            m_log.DebugFormat(
                    "[SceneObjectSerializer]: Parsing ProcessSOPAnims for object part {0} {1} encountered errors",
                    obj.Name, obj.UUID);
        }

        private static void ProcessShape(SceneObjectPart obj, XmlReader reader)
        {
            obj.Shape = ReadShape(reader, "Shape", out List<string> errorNodeNames, obj);

            if (errorNodeNames != null)
            {
                m_log.DebugFormat(
                    "[SceneObjectSerializer]: Parsing PrimitiveBaseShape for object part {0} {1} encountered errors in properties {2}.",
                    obj.Name, obj.UUID, string.Join(", ", errorNodeNames.ToArray()));
            }
        }

        private static void ProcessScale(SceneObjectPart obj, XmlReader reader)
        {
            obj.Scale = Util.ReadVector(reader, "Scale");
        }

        private static void ProcessSitTargetOrientation(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitTargetOrientation = Util.ReadQuaternion(reader, "SitTargetOrientation");
        }

        private static void ProcessSitTargetPosition(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitTargetPosition = Util.ReadVector(reader, "SitTargetPosition");
        }

        private static void ProcessSitTargetPositionLL(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitTargetPositionLL = Util.ReadVector(reader, "SitTargetPositionLL");
        }

        private static void ProcessSitTargetOrientationLL(SceneObjectPart obj, XmlReader reader)
        {
            obj.SitTargetOrientationLL = Util.ReadQuaternion(reader, "SitTargetOrientationLL");
        }

        private static void ProcessStandTarget(SceneObjectPart obj, XmlReader reader)
        {
            obj.StandOffset = Util.ReadVector(reader, "StandTarget");
        }

        private static void ProcessParentID(SceneObjectPart obj, XmlReader reader)
        {
            string str = reader.ReadElementContentAsString("ParentID", string.Empty);
            obj.ParentID = Convert.ToUInt32(str);
        }

        private static void ProcessCreationDate(SceneObjectPart obj, XmlReader reader)
        {
            obj.CreationDate = reader.ReadElementContentAsInt("CreationDate", string.Empty);
        }

        private static void ProcessCategory(SceneObjectPart obj, XmlReader reader)
        {
            obj.Category = (uint)reader.ReadElementContentAsInt("Category", string.Empty);
        }

        private static void ProcessSalePrice(SceneObjectPart obj, XmlReader reader)
        {
            obj.SalePrice = reader.ReadElementContentAsInt("SalePrice", string.Empty);
        }

        private static void ProcessObjectSaleType(SceneObjectPart obj, XmlReader reader)
        {
            obj.ObjectSaleType = (byte)reader.ReadElementContentAsInt("ObjectSaleType", string.Empty);
        }

        private static void ProcessOwnershipCost(SceneObjectPart obj, XmlReader reader)
        {
            obj.OwnershipCost = reader.ReadElementContentAsInt("OwnershipCost", string.Empty);
        }

        private static void ProcessGroupID(SceneObjectPart obj, XmlReader reader)
        {
            obj.GroupID = Util.ReadUUID(reader, "GroupID");
        }

        private static void ProcessOwnerID(SceneObjectPart obj, XmlReader reader)
        {
            obj.OwnerID = Util.ReadUUID(reader, "OwnerID");
        }

        private static void ProcessLastOwnerID(SceneObjectPart obj, XmlReader reader)
        {
            obj.LastOwnerID = Util.ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessRezzerID(SceneObjectPart obj, XmlReader reader)
        {
            obj.RezzerID = Util.ReadUUID(reader, "RezzerID");
        }

        private static void ProcessBaseMask(SceneObjectPart obj, XmlReader reader)
        {
            obj.BaseMask = (uint)reader.ReadElementContentAsInt("BaseMask", string.Empty);
        }

        private static void ProcessOwnerMask(SceneObjectPart obj, XmlReader reader)
        {
            obj.OwnerMask = (uint)reader.ReadElementContentAsInt("OwnerMask", string.Empty);
        }

        private static void ProcessGroupMask(SceneObjectPart obj, XmlReader reader)
        {
            obj.GroupMask = (uint)reader.ReadElementContentAsInt("GroupMask", string.Empty);
        }

        private static void ProcessEveryoneMask(SceneObjectPart obj, XmlReader reader)
        {
            obj.EveryoneMask = (uint)reader.ReadElementContentAsInt("EveryoneMask", string.Empty);
        }

        private static void ProcessNextOwnerMask(SceneObjectPart obj, XmlReader reader)
        {
            obj.NextOwnerMask = (uint)reader.ReadElementContentAsInt("NextOwnerMask", string.Empty);
        }

        private static void ProcessFlags(SceneObjectPart obj, XmlReader reader)
        {
            obj.Flags = Util.ReadEnum<PrimFlags>(reader, "Flags");
        }

        private static void ProcessCollisionSound(SceneObjectPart obj, XmlReader reader)
        {
            obj.CollisionSound = Util.ReadUUID(reader, "CollisionSound");
        }

        private static void ProcessCollisionSoundVolume(SceneObjectPart obj, XmlReader reader)
        {
            obj.CollisionSoundVolume = reader.ReadElementContentAsFloat("CollisionSoundVolume", string.Empty);
        }

        private static void ProcessMediaUrl(SceneObjectPart obj, XmlReader reader)
        {
            obj.MediaUrl = reader.ReadElementContentAsString("MediaUrl", string.Empty);
        }

        private static void ProcessAttachedPos(SceneObjectPart obj, XmlReader reader)
        {
            obj.AttachedPos = Util.ReadVector(reader, "AttachedPos");
        }

        private static void ProcessDynAttrs(SceneObjectPart obj, XmlReader reader)
        {
            DAMap waste = new();
            waste.ReadXml(reader);
            if(waste.CountNamespaces > 0)
                obj.DynAttrs = waste;
            else
                obj.DynAttrs = null;
        }

        private static void ProcessTextureAnimation(SceneObjectPart obj, XmlReader reader)
        {
            obj.TextureAnimation = Convert.FromBase64String(reader.ReadElementContentAsString("TextureAnimation", string.Empty));
        }

        private static void ProcessParticleSystem(SceneObjectPart obj, XmlReader reader)
        {
            obj.ParticleSystem = Convert.FromBase64String(reader.ReadElementContentAsString("ParticleSystem", string.Empty));
        }

        private static void ProcessPayPrice0(SceneObjectPart obj, XmlReader reader)
        {
            obj.PayPrice[0] = (int)reader.ReadElementContentAsInt("PayPrice0", string.Empty);
        }

        private static void ProcessPayPrice1(SceneObjectPart obj, XmlReader reader)
        {
            obj.PayPrice[1] = (int)reader.ReadElementContentAsInt("PayPrice1", string.Empty);
        }

        private static void ProcessPayPrice2(SceneObjectPart obj, XmlReader reader)
        {
            obj.PayPrice[2] = (int)reader.ReadElementContentAsInt("PayPrice2", string.Empty);
        }

        private static void ProcessPayPrice3(SceneObjectPart obj, XmlReader reader)
        {
            obj.PayPrice[3] = (int)reader.ReadElementContentAsInt("PayPrice3", string.Empty);
        }

        private static void ProcessPayPrice4(SceneObjectPart obj, XmlReader reader)
        {
            obj.PayPrice[4] = (int)reader.ReadElementContentAsInt("PayPrice4", string.Empty);
        }

        private static void ProcessBuoyancy(SceneObjectPart obj, XmlReader reader)
        {
            obj.Buoyancy = (float)reader.ReadElementContentAsFloat("Buoyancy", string.Empty);
        }

        private static void ProcessForce(SceneObjectPart obj, XmlReader reader)
        {
            obj.Force = Util.ReadVector(reader, "Force");
        }
        private static void ProcessTorque(SceneObjectPart obj, XmlReader reader)
        {
            obj.Torque = Util.ReadVector(reader, "Torque");
        }

        private static void ProcessVolumeDetectActive(SceneObjectPart obj, XmlReader reader)
        {
            obj.VolumeDetectActive = Util.ReadBoolean(reader);
        }

        #endregion

        #region TaskInventoryXmlProcessors
        private static void ProcessTIAssetID(TaskInventoryItem item, XmlReader reader)
        {
            item.AssetID = Util.ReadUUID(reader, "AssetID");
        }

        private static void ProcessTIBasePermissions(TaskInventoryItem item, XmlReader reader)
        {
            item.BasePermissions = (uint)reader.ReadElementContentAsInt("BasePermissions", string.Empty);
        }

        private static void ProcessTICreationDate(TaskInventoryItem item, XmlReader reader)
        {
            item.CreationDate = (uint)reader.ReadElementContentAsInt("CreationDate", string.Empty);
        }

        private static void ProcessTICreatorID(TaskInventoryItem item, XmlReader reader)
        {
            item.CreatorID = Util.ReadUUID(reader, "CreatorID");
        }

        private static void ProcessTICreatorData(TaskInventoryItem item, XmlReader reader)
        {
            item.CreatorData = reader.ReadElementContentAsString("CreatorData", string.Empty);
        }

        private static void ProcessTIDescription(TaskInventoryItem item, XmlReader reader)
        {
            item.Description = reader.ReadElementContentAsString("Description", string.Empty);
        }

        private static void ProcessTIEveryonePermissions(TaskInventoryItem item, XmlReader reader)
        {
            item.EveryonePermissions = (uint)reader.ReadElementContentAsInt("EveryonePermissions", string.Empty);
        }

        private static void ProcessTIFlags(TaskInventoryItem item, XmlReader reader)
        {
            item.Flags = (uint)reader.ReadElementContentAsInt("Flags", string.Empty);
        }

        private static void ProcessTIGroupID(TaskInventoryItem item, XmlReader reader)
        {
            item.GroupID = Util.ReadUUID(reader, "GroupID");
        }

        private static void ProcessTIGroupPermissions(TaskInventoryItem item, XmlReader reader)
        {
            item.GroupPermissions = (uint)reader.ReadElementContentAsInt("GroupPermissions", string.Empty);
        }

        private static void ProcessTIInvType(TaskInventoryItem item, XmlReader reader)
        {
            item.InvType = reader.ReadElementContentAsInt("InvType", string.Empty);
        }

        private static void ProcessTIItemID(TaskInventoryItem item, XmlReader reader)
        {
            item.ItemID = Util.ReadUUID(reader, "ItemID");
        }

        private static void ProcessTIOldItemID(TaskInventoryItem item, XmlReader reader)
        {
            item.OldItemID = Util.ReadUUID(reader, "OldItemID");
        }

        private static void ProcessTILastOwnerID(TaskInventoryItem item, XmlReader reader)
        {
            item.LastOwnerID = Util.ReadUUID(reader, "LastOwnerID");
        }

        private static void ProcessTIName(TaskInventoryItem item, XmlReader reader)
        {
            item.Name = reader.ReadElementContentAsString("Name", string.Empty);
        }

        private static void ProcessTINextPermissions(TaskInventoryItem item, XmlReader reader)
        {
            item.NextPermissions = (uint)reader.ReadElementContentAsInt("NextPermissions", string.Empty);
        }

        private static void ProcessTIOwnerID(TaskInventoryItem item, XmlReader reader)
        {
            item.OwnerID = Util.ReadUUID(reader, "OwnerID");
        }

        private static void ProcessTICurrentPermissions(TaskInventoryItem item, XmlReader reader)
        {
            item.CurrentPermissions = (uint)reader.ReadElementContentAsInt("CurrentPermissions", string.Empty);
        }

        private static void ProcessTIParentID(TaskInventoryItem item, XmlReader reader)
        {
            item.ParentID = Util.ReadUUID(reader, "ParentID");
        }

        private static void ProcessTIParentPartID(TaskInventoryItem item, XmlReader reader)
        {
            item.ParentPartID = Util.ReadUUID(reader, "ParentPartID");
        }

        private static void ProcessTIPermsGranter(TaskInventoryItem item, XmlReader reader)
        {
            item.PermsGranter = Util.ReadUUID(reader, "PermsGranter");
        }

        private static void ProcessTIPermsMask(TaskInventoryItem item, XmlReader reader)
        {
            item.PermsMask = reader.ReadElementContentAsInt("PermsMask", string.Empty);
        }

        private static void ProcessTIType(TaskInventoryItem item, XmlReader reader)
        {
            item.Type = reader.ReadElementContentAsInt("Type", string.Empty);
        }

        private static void ProcessTIOwnerChanged(TaskInventoryItem item, XmlReader reader)
        {
            item.OwnerChanged = Util.ReadBoolean(reader);
        }

        #endregion

        #region ShapeXmlProcessors
        private static void ProcessShpProfileCurve(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ProfileCurve = (byte)reader.ReadElementContentAsInt("ProfileCurve", string.Empty);
        }

        private static void ProcessShpTextureEntry(PrimitiveBaseShape shp, XmlReader reader)
        {
            byte[] teData = Convert.FromBase64String(reader.ReadElementString("TextureEntry"));
            shp.Textures = new Primitive.TextureEntry(teData, 0, teData.Length);
        }

        private static void ProcessShpExtraParams(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ExtraParams = Convert.FromBase64String(reader.ReadElementString("ExtraParams"));
        }

        private static void ProcessShpPathBegin(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathBegin = (ushort)reader.ReadElementContentAsInt("PathBegin", string.Empty);
        }

        private static void ProcessShpPathCurve(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathCurve = (byte)reader.ReadElementContentAsInt("PathCurve", string.Empty);
        }

        private static void ProcessShpPathEnd(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathEnd = (ushort)reader.ReadElementContentAsInt("PathEnd", string.Empty);
        }

        private static void ProcessShpPathRadiusOffset(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathRadiusOffset = (sbyte)reader.ReadElementContentAsInt("PathRadiusOffset", string.Empty);
        }

        private static void ProcessShpPathRevolutions(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathRevolutions = (byte)reader.ReadElementContentAsInt("PathRevolutions", string.Empty);
        }

        private static void ProcessShpPathScaleX(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathScaleX = (byte)reader.ReadElementContentAsInt("PathScaleX", string.Empty);
        }

        private static void ProcessShpPathScaleY(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathScaleY = (byte)reader.ReadElementContentAsInt("PathScaleY", string.Empty);
        }

        private static void ProcessShpPathShearX(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathShearX = (byte)reader.ReadElementContentAsInt("PathShearX", string.Empty);
        }

        private static void ProcessShpPathShearY(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathShearY = (byte)reader.ReadElementContentAsInt("PathShearY", string.Empty);
        }

        private static void ProcessShpPathSkew(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathSkew = (sbyte)reader.ReadElementContentAsInt("PathSkew", string.Empty);
        }

        private static void ProcessShpPathTaperX(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathTaperX = (sbyte)reader.ReadElementContentAsInt("PathTaperX", string.Empty);
        }

        private static void ProcessShpPathTaperY(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathTaperY = (sbyte)reader.ReadElementContentAsInt("PathTaperY", string.Empty);
        }

        private static void ProcessShpPathTwist(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathTwist = (sbyte)reader.ReadElementContentAsInt("PathTwist", string.Empty);
        }

        private static void ProcessShpPathTwistBegin(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PathTwistBegin = (sbyte)reader.ReadElementContentAsInt("PathTwistBegin", string.Empty);
        }

        private static void ProcessShpPCode(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.PCode = (byte)reader.ReadElementContentAsInt("PCode", string.Empty);
        }

        private static void ProcessShpProfileBegin(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ProfileBegin = (ushort)reader.ReadElementContentAsInt("ProfileBegin", string.Empty);
        }

        private static void ProcessShpProfileEnd(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ProfileEnd = (ushort)reader.ReadElementContentAsInt("ProfileEnd", string.Empty);
        }

        private static void ProcessShpProfileHollow(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ProfileHollow = (ushort)reader.ReadElementContentAsInt("ProfileHollow", string.Empty);
        }

        private static void ProcessShpScale(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.Scale = Util.ReadVector(reader, "Scale");
        }

        private static void ProcessShpState(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.State = (byte)reader.ReadElementContentAsInt("State", string.Empty);
        }

        private static void ProcessShpLastAttach(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LastAttachPoint = (byte)reader.ReadElementContentAsInt("LastAttachPoint", string.Empty);
        }

        private static void ProcessShpProfileShape(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.ProfileShape = Util.ReadEnum<ProfileShape>(reader, "ProfileShape");
        }

        private static void ProcessShpHollowShape(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.HollowShape = Util.ReadEnum<HollowShape>(reader, "HollowShape");
        }

        private static void ProcessShpSculptTexture(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.SculptTexture = Util.ReadUUID(reader, "SculptTexture");
        }

        private static void ProcessShpSculptType(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.SculptType = (byte)reader.ReadElementContentAsInt("SculptType", string.Empty);
        }

        private static void ProcessShpFlexiSoftness(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiSoftness = reader.ReadElementContentAsInt("FlexiSoftness", string.Empty);
        }

        private static void ProcessShpFlexiTension(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiTension = reader.ReadElementContentAsFloat("FlexiTension", string.Empty);
        }

        private static void ProcessShpFlexiDrag(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiDrag = reader.ReadElementContentAsFloat("FlexiDrag", string.Empty);
        }

        private static void ProcessShpFlexiGravity(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiGravity = reader.ReadElementContentAsFloat("FlexiGravity", string.Empty);
        }

        private static void ProcessShpFlexiWind(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiWind = reader.ReadElementContentAsFloat("FlexiWind", string.Empty);
        }

        private static void ProcessShpFlexiForceX(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiForceX = reader.ReadElementContentAsFloat("FlexiForceX", string.Empty);
        }

        private static void ProcessShpFlexiForceY(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiForceY = reader.ReadElementContentAsFloat("FlexiForceY", string.Empty);
        }

        private static void ProcessShpFlexiForceZ(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiForceZ = reader.ReadElementContentAsFloat("FlexiForceZ", string.Empty);
        }

        private static void ProcessShpLightColorR(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightColorR = reader.ReadElementContentAsFloat("LightColorR", string.Empty);
        }

        private static void ProcessShpLightColorG(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightColorG = reader.ReadElementContentAsFloat("LightColorG", string.Empty);
        }

        private static void ProcessShpLightColorB(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightColorB = reader.ReadElementContentAsFloat("LightColorB", string.Empty);
        }

        private static void ProcessShpLightColorA(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightColorA = reader.ReadElementContentAsFloat("LightColorA", string.Empty);
        }

        private static void ProcessShpLightRadius(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightRadius = reader.ReadElementContentAsFloat("LightRadius", string.Empty);
        }

        private static void ProcessShpLightCutoff(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightCutoff = reader.ReadElementContentAsFloat("LightCutoff", string.Empty);
        }

        private static void ProcessShpLightFalloff(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightFalloff = reader.ReadElementContentAsFloat("LightFalloff", string.Empty);
        }

        private static void ProcessShpLightIntensity(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightIntensity = reader.ReadElementContentAsFloat("LightIntensity", string.Empty);
        }

        private static void ProcessShpFlexiEntry(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.FlexiEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpLightEntry(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.LightEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpSculptEntry(PrimitiveBaseShape shp, XmlReader reader)
        {
            shp.SculptEntry = Util.ReadBoolean(reader);
        }

        private static void ProcessShpMedia(PrimitiveBaseShape shp, XmlReader reader)
        {
            string value;
            try
            {
                // The STANDARD content of Media elemet is escaped XML string (with &gt; etc).
                value = reader.ReadElementContentAsString("Media", string.Empty);
                shp.Media = PrimitiveBaseShape.MediaList.FromXml(value);
            }
            catch (XmlException)
            {
                // There are versions of OAR files that contain unquoted XML.
                // ie ONE comercial fork that never wanted their oars to be read by our code
                try
                {
                    value = reader.ReadInnerXml();
                    shp.Media = PrimitiveBaseShape.MediaList.FromXml(value);
                }
                catch
                {
                    m_log.ErrorFormat("[SERIALIZER] Failed parsing halcyon MOAP information");
                }
            }
        }

        private static void ProcessShpMatOvrd(PrimitiveBaseShape shp, XmlReader reader)
        {
            try
            {
                string datastr = reader.ReadElementContentAsString();
                if (!string.IsNullOrEmpty(datastr))
                {
                    byte[] pdata = Convert.FromBase64String(datastr);
                    shp.RenderMaterialsOvrFromRawBin(pdata);
                }
                return;
            }
            catch
            {
                shp.RenderMaterialsOvrFromRawBin(null);
            }
        }

        #endregion

        ////////// Write /////////

        public static void SOGToXml2(XmlTextWriter writer, SceneObjectGroup sog, Dictionary<string, object>options)
        {
            writer.WriteStartElement(string.Empty, "SceneObjectGroup", string.Empty);
            SOPToXml2(writer, sog.RootPart, options);
            writer.WriteStartElement(string.Empty, "OtherParts", string.Empty);

            sog.ForEachPart(delegate(SceneObjectPart sop)
            {
                if (sop.UUID != sog.RootPart.UUID)
                    SOPToXml2(writer, sop, options);
            });

            writer.WriteEndElement();

            if (sog.RootPart.KeyframeMotion != null)
            {
                Byte[] data = sog.RootPart.KeyframeMotion.Serialize();

                writer.WriteStartElement(string.Empty, "KeyframeMotion", string.Empty);
                writer.WriteBase64(data, 0, data.Length);
                writer.WriteEndElement();
            }

            if(sog.RezStringParameter is not null)
                writer.WriteElementString("StartStr", sog.RezStringParameter);

            sog.LinksetData?.ToXML(writer);

            writer.WriteEndElement();
        }

        public static void SOPToXml2(XmlTextWriter writer, SceneObjectPart sop, Dictionary<string, object> options)
        {
            writer.WriteStartElement("SceneObjectPart");
            writer.WriteAttributeString("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
            writer.WriteAttributeString("xmlns:xsd", "http://www.w3.org/2001/XMLSchema");

            writer.WriteElementString("AllowedDrop", sop.AllowedDrop.ToString().ToLower());

            WriteUUID(writer, "CreatorID", sop.CreatorID, options);

            if (!string.IsNullOrEmpty(sop.CreatorData))
                writer.WriteElementString("CreatorData", sop.CreatorData);
            else if (options.TryGetValue("home", out object ohome))
            {
                m_UserManagement ??= sop.ParentGroup.Scene.RequestModuleInterface<IUserManagement>();
                string name = m_UserManagement.GetUserName(sop.CreatorID);
                writer.WriteElementString("CreatorData", ExternalRepresentationUtils.CalcCreatorData((string)ohome, name));
            }

            WriteUUID(writer, "FolderID", sop.FolderID, options);
            writer.WriteElementString("InventorySerial", sop.InventorySerial.ToString());

            WriteTaskInventory(writer, sop.TaskInventory, options, sop.ParentGroup.Scene);

            WriteUUID(writer, "UUID", sop.UUID, options);
            writer.WriteElementString("LocalId", sop.LocalId.ToString());
            writer.WriteElementString("Name", sop.Name);
            writer.WriteElementString("Material", sop.Material.ToString());
            writer.WriteElementString("PassTouches", sop.PassTouches.ToString().ToLower());
            writer.WriteElementString("PassCollisions", sop.PassCollisions.ToString().ToLower());
            writer.WriteElementString("RegionHandle", sop.RegionHandle.ToString());
            writer.WriteElementString("ScriptAccessPin", sop.ScriptAccessPin.ToString());

            WriteVector(writer, "GroupPosition", sop.GroupPosition);
            WriteVector(writer, "OffsetPosition", sop.OffsetPosition);

            WriteQuaternion(writer, "RotationOffset", sop.RotationOffset);
            WriteVector(writer, "Velocity", sop.Velocity);
            WriteVector(writer, "AngularVelocity", sop.AngularVelocity);
            WriteVector(writer, "Acceleration", sop.Acceleration);
            writer.WriteElementString("Description", sop.Description);

            writer.WriteStartElement("Color");
            writer.WriteElementString("R", sop.Color.R.ToString(Culture.FormatProvider));
            writer.WriteElementString("G", sop.Color.G.ToString(Culture.FormatProvider));
            writer.WriteElementString("B", sop.Color.B.ToString(Culture.FormatProvider));
            writer.WriteElementString("A", sop.Color.A.ToString(Culture.FormatProvider));
            writer.WriteEndElement();

            writer.WriteElementString("Text", sop.Text);
            writer.WriteElementString("SitName", sop.SitName);
            writer.WriteElementString("TouchName", sop.TouchName);

            writer.WriteElementString("LinkNum", sop.LinkNum.ToString());
            writer.WriteElementString("ClickAction", sop.ClickAction.ToString());

            WriteShape(writer, sop.Shape, options);

            WriteVector(writer, "Scale", sop.Scale);
            WriteQuaternion(writer, "SitTargetOrientation", sop.SitTargetOrientation);
            WriteVector(writer, "SitTargetPosition", sop.SitTargetPosition);
            WriteVector(writer, "SitTargetPositionLL", sop.SitTargetPositionLL);
            WriteQuaternion(writer, "SitTargetOrientationLL", sop.SitTargetOrientationLL);
            if(sop.StandOffset.IsNotZero())
                WriteVector(writer, "StandTarget", sop.StandOffset);
            writer.WriteElementString("ParentID", sop.ParentID.ToString());
            writer.WriteElementString("CreationDate", sop.CreationDate.ToString());
            writer.WriteElementString("Category", sop.Category.ToString());
            writer.WriteElementString("SalePrice", sop.SalePrice.ToString());
            writer.WriteElementString("ObjectSaleType", sop.ObjectSaleType.ToString());
            writer.WriteElementString("OwnershipCost", sop.OwnershipCost.ToString());

            UUID groupID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.GroupID;
            WriteUUID(writer, "GroupID", groupID, options);

            UUID ownerID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.OwnerID;
            WriteUUID(writer, "OwnerID", ownerID, options);

            UUID lastOwnerID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.LastOwnerID;
            WriteUUID(writer, "LastOwnerID", lastOwnerID, options);

            UUID rezzerID = options.ContainsKey("wipe-owners") ? UUID.Zero : sop.RezzerID;
            WriteUUID(writer, "RezzerID", rezzerID, options);

            writer.WriteElementString("BaseMask", sop.BaseMask.ToString());
            writer.WriteElementString("OwnerMask", sop.OwnerMask.ToString());
            writer.WriteElementString("GroupMask", sop.GroupMask.ToString());
            writer.WriteElementString("EveryoneMask", sop.EveryoneMask.ToString());
            writer.WriteElementString("NextOwnerMask", sop.NextOwnerMask.ToString());
            WriteFlags(writer, "Flags", sop.Flags.ToString(), options);
            WriteUUID(writer, "CollisionSound", sop.CollisionSound, options);
            writer.WriteElementString("CollisionSoundVolume", sop.CollisionSoundVolume.ToString(Culture.FormatProvider));
            if (!string.IsNullOrEmpty(sop.MediaUrl))
                writer.WriteElementString("MediaUrl", sop.MediaUrl.ToString());
            WriteVector(writer, "AttachedPos", sop.AttachedPos);

            if (sop.DynAttrs != null && sop.DynAttrs.CountNamespaces > 0)
            {
                writer.WriteStartElement("DynAttrs");
                sop.DynAttrs.WriteXml(writer);
                writer.WriteEndElement();
            }

            WriteBytes(writer, "TextureAnimation", sop.TextureAnimation);
            WriteBytes(writer, "ParticleSystem", sop.ParticleSystem);
            writer.WriteElementString("PayPrice0", sop.PayPrice[0].ToString());
            writer.WriteElementString("PayPrice1", sop.PayPrice[1].ToString());
            writer.WriteElementString("PayPrice2", sop.PayPrice[2].ToString());
            writer.WriteElementString("PayPrice3", sop.PayPrice[3].ToString());
            writer.WriteElementString("PayPrice4", sop.PayPrice[4].ToString());

            writer.WriteElementString("Buoyancy", sop.Buoyancy.ToString(Culture.FormatProvider));

            WriteVector(writer, "Force", sop.Force);
            WriteVector(writer, "Torque", sop.Torque);

            writer.WriteElementString("VolumeDetectActive", sop.VolumeDetectActive.ToString().ToLower());

            sop.VehicleParams?.ToXml2(writer);

            sop.PhysicsInertia?.ToXml2(writer);

            if(sop.IsRoot && sop.RotationAxisLocks != 0)
                writer.WriteElementString("RotationAxisLocks", sop.RotationAxisLocks.ToString().ToLower());
            writer.WriteElementString("PhysicsShapeType", sop.PhysicsShapeType.ToString().ToLower());
            if (sop.Density != 1000.0f)
                writer.WriteElementString("Density", sop.Density.ToString(Culture.FormatProvider));
            if (sop.Friction != 0.6f)
                writer.WriteElementString("Friction", sop.Friction.ToString(Culture.FormatProvider));
            if (sop.Restitution != 0.5f)
                writer.WriteElementString("Bounce", sop.Restitution.ToString(Culture.FormatProvider));
            if (sop.GravityModifier != 1.0f)
                writer.WriteElementString("GravityModifier", sop.GravityModifier.ToString(Culture.FormatProvider));
            WriteVector(writer, "CameraEyeOffset", sop.GetCameraEyeOffset());
            WriteVector(writer, "CameraAtOffset", sop.GetCameraAtOffset());

 //           if (sop.Sound != UUID.Zero)  force it till sop crossing does clear it on child prim
            {
                WriteUUID(writer, "SoundID", sop.Sound, options);
                writer.WriteElementString("SoundGain", sop.SoundGain.ToString(Culture.FormatProvider));
                writer.WriteElementString("SoundFlags", sop.SoundFlags.ToString().ToLower());
                writer.WriteElementString("SoundRadius", sop.SoundRadius.ToString(Culture.FormatProvider));
            }
            writer.WriteElementString("SoundQueueing", sop.SoundQueueing.ToString().ToLower());

            if (sop.Animations != null)
            {
                Byte[] data = sop.SerializeAnimations();
                if(data != null && data.Length > 0)
                    writer.WriteElementString("SOPAnims", Convert.ToBase64String(data));
            }
            if(Math.Abs(sop.SitActiveRange) > 1e-5)
                writer.WriteElementString("SitActRange", sop.SitActiveRange.ToString(Culture.FormatProvider));
            writer.WriteEndElement();
        }

        static void WriteUUID(XmlTextWriter writer, string name, UUID id, Dictionary<string, object> options)
        {
            writer.WriteStartElement(name);
            if (options.ContainsKey("old-guids"))
                writer.WriteElementString("Guid", id.ToString());
            else
                writer.WriteElementString("UUID", id.ToString());
            writer.WriteEndElement();
        }

        static void WriteVector(XmlTextWriter writer, string name, Vector3 vec)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", vec.X.ToString(Culture.FormatProvider));
            writer.WriteElementString("Y", vec.Y.ToString(Culture.FormatProvider));
            writer.WriteElementString("Z", vec.Z.ToString(Culture.FormatProvider));
            writer.WriteEndElement();
        }

        static void WriteQuaternion(XmlTextWriter writer, string name, Quaternion quat)
        {
            writer.WriteStartElement(name);
            writer.WriteElementString("X", quat.X.ToString(Culture.FormatProvider));
            writer.WriteElementString("Y", quat.Y.ToString(Culture.FormatProvider));
            writer.WriteElementString("Z", quat.Z.ToString(Culture.FormatProvider));
            writer.WriteElementString("W", quat.W.ToString(Culture.FormatProvider));
            writer.WriteEndElement();
        }

        static void WriteBytes(XmlTextWriter writer, string name, byte[] data)
        {
            writer.WriteStartElement(name);
            byte[] d;
            if (data != null)
                d = data;
            else
                d = Utils.EmptyBytes;
            writer.WriteBase64(d, 0, d.Length);
            writer.WriteEndElement(); // name

        }

        static void WriteFlags(XmlTextWriter writer, string name, string flagsStr, Dictionary<string, object> options)
        {
            // Older versions of serialization can't cope with commas, so we eliminate the commas
            writer.WriteElementString(name, flagsStr.Replace(",", ""));
        }

        public static void WriteTaskInventory(XmlTextWriter writer, TaskInventoryDictionary tinv, Dictionary<string, object> options, Scene scene)
        {
            if (tinv.Count > 0) // otherwise skip this
            {
                writer.WriteStartElement("TaskInventory");

                foreach (TaskInventoryItem item in tinv.Values)
                {
                    writer.WriteStartElement("TaskInventoryItem");

                    WriteUUID(writer, "AssetID", item.AssetID, options);
                    writer.WriteElementString("BasePermissions", item.BasePermissions.ToString());
                    writer.WriteElementString("CreationDate", item.CreationDate.ToString());

                    WriteUUID(writer, "CreatorID", item.CreatorID, options);

                    if (!string.IsNullOrEmpty(item.CreatorData))
                        writer.WriteElementString("CreatorData", item.CreatorData);
                    else if (options.TryGetValue("home", out object ohome))
                    {
                        m_UserManagement ??= scene.RequestModuleInterface<IUserManagement>();
                        string name = m_UserManagement.GetUserName(item.CreatorID);
                        writer.WriteElementString("CreatorData", ExternalRepresentationUtils.CalcCreatorData((string)ohome, name));
                    }

                    writer.WriteElementString("Description", item.Description);
                    writer.WriteElementString("EveryonePermissions", item.EveryonePermissions.ToString());
                    writer.WriteElementString("Flags", item.Flags.ToString());

                    UUID groupID = options.ContainsKey("wipe-owners") ? UUID.Zero : item.GroupID;
                    WriteUUID(writer, "GroupID", groupID, options);

                    writer.WriteElementString("GroupPermissions", item.GroupPermissions.ToString());
                    writer.WriteElementString("InvType", item.InvType.ToString());
                    WriteUUID(writer, "ItemID", item.ItemID, options);
                    WriteUUID(writer, "OldItemID", item.OldItemID, options);

                    UUID lastOwnerID = options.ContainsKey("wipe-owners") ? UUID.Zero : item.LastOwnerID;
                    WriteUUID(writer, "LastOwnerID", lastOwnerID, options);

                    writer.WriteElementString("Name", item.Name);
                    writer.WriteElementString("NextPermissions", item.NextPermissions.ToString());

                    UUID ownerID = options.ContainsKey("wipe-owners") ? UUID.Zero : item.OwnerID;
                    WriteUUID(writer, "OwnerID", ownerID, options);

                    writer.WriteElementString("CurrentPermissions", item.CurrentPermissions.ToString());
                    WriteUUID(writer, "ParentID", item.ParentID, options);
                    WriteUUID(writer, "ParentPartID", item.ParentPartID, options);
                    WriteUUID(writer, "PermsGranter", item.PermsGranter, options);
                    writer.WriteElementString("PermsMask", item.PermsMask.ToString());
                    writer.WriteElementString("Type", item.Type.ToString());

                    bool ownerChanged = !options.ContainsKey("wipe-owners") && item.OwnerChanged;
                    writer.WriteElementString("OwnerChanged", ownerChanged.ToString().ToLower());

                    writer.WriteEndElement(); // TaskInventoryItem
                }

                writer.WriteEndElement(); // TaskInventory
            }
        }

        public static void WriteShape(XmlTextWriter writer, PrimitiveBaseShape shp, Dictionary<string, object> options)
        {
            if (shp != null)
            {
                writer.WriteStartElement("Shape");

                writer.WriteElementString("ProfileCurve", shp.ProfileCurve.ToString());

                writer.WriteStartElement("TextureEntry");
                byte[] te;
                if (shp.TextureEntry != null)
                    te = shp.TextureEntry;
                else
                    te = Utils.EmptyBytes;
                writer.WriteBase64(te, 0, te.Length);
                writer.WriteEndElement(); // TextureEntry

                writer.WriteStartElement("ExtraParams");
                byte[] ep;
                if (shp.ExtraParams != null)
                    ep = shp.ExtraParams;
                else
                    ep = Utils.EmptyBytes;
                writer.WriteBase64(ep, 0, ep.Length);
                writer.WriteEndElement(); // ExtraParams

                writer.WriteElementString("PathBegin", shp.PathBegin.ToString());
                writer.WriteElementString("PathCurve", shp.PathCurve.ToString());
                writer.WriteElementString("PathEnd", shp.PathEnd.ToString());
                writer.WriteElementString("PathRadiusOffset", shp.PathRadiusOffset.ToString());
                writer.WriteElementString("PathRevolutions", shp.PathRevolutions.ToString());
                writer.WriteElementString("PathScaleX", shp.PathScaleX.ToString());
                writer.WriteElementString("PathScaleY", shp.PathScaleY.ToString());
                writer.WriteElementString("PathShearX", shp.PathShearX.ToString());
                writer.WriteElementString("PathShearY", shp.PathShearY.ToString());
                writer.WriteElementString("PathSkew", shp.PathSkew.ToString());
                writer.WriteElementString("PathTaperX", shp.PathTaperX.ToString());
                writer.WriteElementString("PathTaperY", shp.PathTaperY.ToString());
                writer.WriteElementString("PathTwist", shp.PathTwist.ToString());
                writer.WriteElementString("PathTwistBegin", shp.PathTwistBegin.ToString());
                writer.WriteElementString("PCode", shp.PCode.ToString());
                writer.WriteElementString("ProfileBegin", shp.ProfileBegin.ToString());
                writer.WriteElementString("ProfileEnd", shp.ProfileEnd.ToString());
                writer.WriteElementString("ProfileHollow", shp.ProfileHollow.ToString());
                writer.WriteElementString("State", shp.State.ToString());
                writer.WriteElementString("LastAttachPoint", shp.LastAttachPoint.ToString());

                WriteFlags(writer, "ProfileShape", shp.ProfileShape.ToString(), options);
                WriteFlags(writer, "HollowShape", shp.HollowShape.ToString(), options);

                WriteUUID(writer, "SculptTexture", shp.SculptTexture, options);
                writer.WriteElementString("SculptType", shp.SculptType.ToString());
                // Don't serialize SculptData. It's just a copy of the asset, which can be loaded separately using 'SculptTexture'.

                writer.WriteElementString("FlexiSoftness", shp.FlexiSoftness.ToString());
                writer.WriteElementString("FlexiTension", shp.FlexiTension.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiDrag", shp.FlexiDrag.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiGravity", shp.FlexiGravity.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiWind", shp.FlexiWind.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiForceX", shp.FlexiForceX.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiForceY", shp.FlexiForceY.ToString(Culture.FormatProvider));
                writer.WriteElementString("FlexiForceZ", shp.FlexiForceZ.ToString(Culture.FormatProvider));

                writer.WriteElementString("LightColorR", shp.LightColorR.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightColorG", shp.LightColorG.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightColorB", shp.LightColorB.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightColorA", shp.LightColorA.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightRadius", shp.LightRadius.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightCutoff", shp.LightCutoff.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightFalloff", shp.LightFalloff.ToString(Culture.FormatProvider));
                writer.WriteElementString("LightIntensity", shp.LightIntensity.ToString(Culture.FormatProvider));

                writer.WriteElementString("FlexiEntry", shp.FlexiEntry.ToString().ToLower());
                writer.WriteElementString("LightEntry", shp.LightEntry.ToString().ToLower());
                writer.WriteElementString("SculptEntry", shp.SculptEntry.ToString().ToLower());

                if (shp.Media is not null)
                    writer.WriteElementString("Media", shp.Media.ToXml());

                byte[] matoverrides = shp.RenderMaterialsOvrToRawBin();
                if(matoverrides is not null)
                { 
                    writer.WriteStartElement("MatOvrd");
                    writer.WriteBase64(matoverrides, 0, matoverrides.Length);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // Shape
            }
        }

        public static SceneObjectPart Xml2ToSOP(XmlReader reader)
        {
            SceneObjectPart obj = new();

            reader.ReadStartElement("SceneObjectPart");

            bool errors = ExternalRepresentationUtils.ExecuteReadProcessors(
                obj,
                m_SOPXmlProcessors,
                reader,
                (o, nodeName, e) => {
                    m_log.Debug(string.Format("[SceneObjectSerializer]: Error while parsing element {0} in object {1} {2} ",
                        nodeName, ((SceneObjectPart)o).Name, ((SceneObjectPart)o).UUID), e);
                });

            if (errors)
                throw new XmlException(string.Format("Error parsing object {0} {1}", obj.Name, obj.UUID));

            reader.ReadEndElement(); // SceneObjectPart

            obj.AggregateInnerPerms();
            // m_log.DebugFormat("[SceneObjectSerializer]: parsed SOP {0} {1}", obj.Name, obj.UUID);
            return obj;
        }

        public static TaskInventoryDictionary ReadTaskInventory(XmlReader reader, string name)
        {
            TaskInventoryDictionary tinv = new();

            reader.ReadStartElement(name, string.Empty);

            while (reader.Name == "TaskInventoryItem")
            {
                reader.ReadStartElement("TaskInventoryItem", string.Empty); // TaskInventory

                TaskInventoryItem item = new();

                ExternalRepresentationUtils.ExecuteReadProcessors(
                    item,
                    m_TaskInventoryXmlProcessors,
                    reader);

                reader.ReadEndElement(); // TaskInventoryItem
                tinv.Add(item.ItemID, item);

            }

            if (reader.NodeType == XmlNodeType.EndElement)
                reader.ReadEndElement(); // TaskInventory

            return tinv;
        }

        /// <summary>
        /// Read a shape from xml input
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="name">The name of the xml element containing the shape</param>
        /// <param name="errors">a list containing the failing node names.  If no failures then null.</param>
        /// <returns>The shape parsed</returns>
        public static PrimitiveBaseShape ReadShape(XmlReader reader, string name, out List<string> errorNodeNames, SceneObjectPart obj)
        {
            List<string> internalErrorNodeNames = null;

            PrimitiveBaseShape shape = new();

            if (reader.IsEmptyElement)
            {
                reader.Read();
                errorNodeNames = null;
                return shape;
            }

            reader.ReadStartElement(name, string.Empty); // Shape

            ExternalRepresentationUtils.ExecuteReadProcessors(
                shape,
                m_ShapeXmlProcessors,
                reader,
                (o, nodeName, e) => {
                    m_log.Debug(string.Format("[SceneObjectSerializer]: Error while parsing element {0} in Shape property of object {1} {2} ",
                        nodeName, obj.Name, obj.UUID), e);

                    internalErrorNodeNames ??= new List<string>();
                    internalErrorNodeNames.Add(nodeName);
                });

            reader.ReadEndElement(); // Shape

            errorNodeNames = internalErrorNodeNames;

            return shape;
        }

        #endregion
    }
}
