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
using System.Collections.Concurrent;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Packets;
using MutSea.Framework;
using MutSea.Services.Interfaces;

namespace MutSea.Region.Framework.Scenes
{
    public partial class Scene
    {
        /// <summary>
        /// Send chat to listeners.
        /// </summary>
        /// <param name='message'></param>
        /// <param name='type'>/param>
        /// <param name='channel'></param>
        /// <param name='fromPos'></param>
        /// <param name='fromName'></param>
        /// <param name='fromID'></param>
        /// <param name='targetID'></param>
        /// <param name='fromAgent'></param>
        /// <param name='broadcast'></param>
        public void SimChat(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                               UUID fromID, UUID targetID, bool fromAgent, bool broadcast)
        {
            OSChatMessage args = new OSChatMessage
            {
                Message = message,
                Channel = channel,
                Type = type,
                Position = fromPos,
                SenderUUID = fromID,
                Scene = this,
                Destination = targetID,
                From = fromName
            };

            if (fromAgent)
            {
                ScenePresence user = GetScenePresence(fromID);
                if (user != null)
                    args.Sender = user.ControllingClient;
            }
            else
            {
                SceneObjectPart obj = GetSceneObjectPart(fromID);
                args.SenderObject = obj;
            }

            //m_log.DebugFormat(
            //    "[SCENE]: Sending message {0} on channel {1}, type {2} from {3}, broadcast {4}",
            //    args.Message.Replace("\n", "\\n"), args.Channel, args.Type, fromName, broadcast);

            if (broadcast)
                EventManager.TriggerOnChatBroadcast(this, args);
            else
                EventManager.TriggerOnChatFromWorld(this, args);
        }

        protected void SimChat(byte[] message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                               UUID fromID, bool fromAgent, bool broadcast)
        {
            SimChat(Utils.BytesToString(message), type, channel, fromPos, fromName, fromID, UUID.Zero, fromAgent, broadcast);
        }

        protected void SimChat(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                               UUID fromID, bool fromAgent, bool broadcast)
        {
            SimChat(message, type, channel, fromPos, fromName, fromID, UUID.Zero, fromAgent, broadcast);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        /// <param name="fromPos"></param>
        /// <param name="fromName"></param>
        /// <param name="fromAgentID"></param>
        public void SimChat(byte[] message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                            UUID fromID, bool fromAgent)
        {
            SimChat(Utils.BytesToString(message), type, channel, fromPos, fromName, fromID, UUID.Zero, fromAgent, false);
        }

        public void SimChat(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                            UUID fromID, bool fromAgent)
        {
            SimChat(message, type, channel, fromPos, fromName, fromID, UUID.Zero, fromAgent, false);
        }

        public void SimChat(string message, ChatTypeEnum type, Vector3 fromPos, string fromName, UUID fromID, bool fromAgent)
        {
            SimChat(message, type, 0, fromPos, fromName, fromID, UUID.Zero, fromAgent, false);
        }

        public void SimChat(string message, string fromName)
        {
            SimChat(message, ChatTypeEnum.Broadcast, 0, Vector3.Zero, fromName, UUID.Zero, UUID.Zero, false, false);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        /// <param name="fromPos"></param>
        /// <param name="fromName"></param>
        /// <param name="fromAgentID"></param>
        public void SimChatBroadcast(string message, ChatTypeEnum type, int channel, Vector3 fromPos, string fromName,
                                     UUID fromID, bool fromAgent)
        {
            SimChat(message, type, channel, fromPos, fromName, fromID, UUID.Zero, fromAgent, true);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="message"></param>
        /// <param name="type"></param>
        /// <param name="channel"></param>
        /// <param name="fromPos"></param>
        /// <param name="fromName"></param>
        /// <param name="fromAgentID"></param>
        /// <param name="targetID"></param>
        public void SimChatToAgent(UUID targetID, byte[] message, int channel, Vector3 fromPos, string fromName, UUID fromID, bool fromAgent)
        {
            SimChat(Utils.BytesToString(message), ChatTypeEnum.Region, channel, fromPos, fromName, fromID, targetID, fromAgent, false);
        }

        public void SimChatToAgent(UUID targetID, string message, int channel, Vector3 fromPos, string fromName, UUID fromID, bool fromAgent)
        {
            SimChat(message, ChatTypeEnum.Region, channel, fromPos, fromName, fromID, targetID, fromAgent, false);
        }

        /// <summary>
        /// Invoked when the client requests a prim.
        /// </summary>
        /// <param name="primLocalID"></param>
        /// <param name="remoteClient"></param>
        public void RequestPrim(uint primLocalID, IClientAPI remoteClient)
        {
            SceneObjectPart part = GetSceneObjectPart(primLocalID);
            if (part != null)
            {
                SceneObjectGroup sog = part.ParentGroup;
                if(!sog.IsDeleted)
                {
                    PrimUpdateFlags update = sog.RootPart.Shape.MeshFlagEntry ? 
                            PrimUpdateFlags.FullUpdatewithAnim : PrimUpdateFlags.FullUpdate;
                    if(part.Shape.RenderMaterials is not null)
                        update |= PrimUpdateFlags.MaterialOvr;
                    part.SendUpdate(remoteClient, update);
                }
            }

            //SceneObjectGroup sog = GetGroupByPrim(primLocalID);

            //if (sog != null)
            //sog.SendFullAnimUpdateToClient(remoteClient);
        }

        /// <summary>
        /// Invoked when the client selects a prim.
        /// </summary>
        /// <param name="primLocalID"></param>
        /// <param name="remoteClient"></param>
        public void SelectPrim(List<uint> primIDs, IClientAPI remoteClient)
        {
            foreach(uint primLocalID in primIDs)
            {
                SceneObjectPart part = GetSceneObjectPart(primLocalID);

                if (part == null)
                    continue;

                SceneObjectGroup sog = part.ParentGroup;
                if (sog == null)
                    continue;

                // waste of time because properties do not send prim flags as they should
                // if a friend got or lost edit rights after login, a full update is needed
                if(sog.OwnerID != remoteClient.AgentId)
                    part.SendFullUpdate(remoteClient);

                // A prim is only tainted if it's allowed to be edited by the person clicking it.
                if (Permissions.CanChangeSelectedState(part, (ScenePresence)remoteClient.SceneAgent))
                {
                    bool oldsel = part.IsSelected;
                    part.IsSelected = true;
                    if(!oldsel)
                        EventManager.TriggerParcelPrimCountTainted();
                }

                part.SendPropertiesToClient(remoteClient);
            }
        }

        /// <summary>
        /// Handle the update of an object's user group.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="groupID"></param>
        /// <param name="objectLocalID"></param>
        /// <param name="Garbage"></param>
        private void HandleObjectGroupUpdate(
            IClientAPI remoteClient, UUID groupID, uint objectLocalID, UUID Garbage)
        {
            if (m_groupsModule == null)
                return;

            // XXX: Might be better to get rid of this special casing and have GetMembershipData return something
            // reasonable for a UUID.Zero group.
            if (!groupID.IsZero())
            {
                GroupMembershipData gmd = m_groupsModule.GetMembershipData(groupID, remoteClient.AgentId);

                if (gmd == null)
                {
//                    m_log.WarnFormat(
//                        "[GROUPS]: User {0} is not a member of group {1} so they can't update {2} to this group",
//                        remoteClient.Name, GroupID, objectLocalID);

                    return;
                }
            }

            SceneObjectGroup so = ((Scene)remoteClient.Scene).GetGroupByPrim(objectLocalID);
            if (so != null)
            {
                if (so.OwnerID == remoteClient.AgentId)
                {
                    so.SetGroup(groupID, remoteClient);
                    EventManager.TriggerParcelPrimCountTainted();
                }
            }
        }

        /// <summary>
        /// Handle the deselection of a prim from the client.
        /// </summary>
        /// <param name="primLocalID"></param>
        /// <param name="remoteClient"></param>
        public void DeselectPrim(uint primLocalID, IClientAPI remoteClient)
        {
            SceneObjectPart part = GetSceneObjectPart(primLocalID);
            if (part == null)
                return;

            bool oldgprSelect = part.ParentGroup.IsSelected;
            part.IsSelected = false;
 
            if (oldgprSelect != part.ParentGroup.IsSelected)
            {
                if (!part.ParentGroup.IsAttachment )
                    EventManager.TriggerParcelPrimCountTainted();
            }

            // restore targetOmega
            if (part.AngularVelocity != Vector3.Zero)
                part.ScheduleTerseUpdate();
        }

        public virtual void ProcessMoneyTransferRequest(UUID source, UUID destination, int amount,
                                                        int transactiontype, string description)
        {
            EventManager.MoneyTransferArgs args = new EventManager.MoneyTransferArgs(source, destination, amount,
                                                                                     transactiontype, description);

            EventManager.TriggerMoneyTransfer(this, args);
        }

        public virtual void ProcessParcelBuy(UUID agentId, UUID groupId, bool final, bool groupOwned,
                bool removeContribution, int parcelLocalID, int parcelArea, int parcelPrice, bool authenticated)
        {
            EventManager.LandBuyArgs args = new EventManager.LandBuyArgs(agentId, groupId, final, groupOwned,
                                                                         removeContribution, parcelLocalID, parcelArea,
                                                                         parcelPrice, authenticated);

            // First, allow all validators a stab at it
            m_eventManager.TriggerValidateLandBuy(this, args);

            // Then, check validation and transfer
            m_eventManager.TriggerLandBuy(this, args);
        }

        public virtual void ProcessObjectGrab(uint localID, Vector3 offsetPos, IClientAPI remoteClient, List<SurfaceTouchEventArgs> surfaceArgs)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);

            if (part == null)
                return;

            SceneObjectGroup obj = part.ParentGroup;

            SurfaceTouchEventArgs surfaceArg = null;
            if (surfaceArgs != null && surfaceArgs.Count > 0)
                surfaceArg = surfaceArgs[0];

            // Currently only grab/touch for the single prim
            // the client handles rez correctly
            obj.ObjectGrabHandler(localID, offsetPos, remoteClient);

            uint partLocalId = part.LocalId;
            // If the touched prim handles touches, deliver it
            if ((part.ScriptEvents & scriptEvents.touch_start) != 0)
            {
                EventManager.TriggerObjectGrab(partLocalId, 0, offsetPos, remoteClient, surfaceArg);
                if(!part.PassTouches)
                    return;
            }

            // Deliver to the root prim if the touched prim doesn't handle touches
            // or if we're meant to pass on touches anyway.

            uint rootLocalId = obj.RootPart.LocalId;
            if (partLocalId != rootLocalId &&  (obj.RootPart.ScriptEvents & scriptEvents.touch_start) != 0)
            {
                EventManager.TriggerObjectGrab(rootLocalId, partLocalId, offsetPos, remoteClient, surfaceArg);
            }
        }

        public virtual void ProcessObjectGrabUpdate(
            UUID objectID, Vector3 offset, Vector3 pos, IClientAPI remoteClient, List<SurfaceTouchEventArgs> surfaceArgs)
        {
            SceneObjectPart part = GetSceneObjectPart(objectID);
            if (part == null)
                return;

            SceneObjectGroup group = part.ParentGroup;
            if(group == null || group.IsDeleted)
                return;

            if (Permissions.CanMoveObject(group, remoteClient))
            {
                group.GrabMovement(objectID, offset, pos, remoteClient);
            }

            // This is outside the above permissions condition
            // so that if the object is locked the client moving the object
            // get's it's position on the simulator even if it was the same as before
            // This keeps the moving user's client in sync with the rest of the world.
            group.SendGroupTerseUpdate();

            SurfaceTouchEventArgs surfaceArg = null;
            if (surfaceArgs != null && surfaceArgs.Count > 0)
                surfaceArg = surfaceArgs[0];

            Vector3 grabOffset = pos - part.AbsolutePosition;
            // If the touched prim handles touches, deliver it
            uint partLocalId = part.LocalId;
            if ((part.ScriptEvents & scriptEvents.touch) != 0)
            {
                EventManager.TriggerObjectGrabbing(partLocalId, 0, grabOffset, remoteClient, surfaceArg);
                if(!part.PassTouches)
                    return;
            }

            uint rootLocalId = group.RootPart.LocalId;
            // Deliver to the root prim if the touched prim doesn't handle touches
            // or if we're meant to pass on touches anyway.
            if (partLocalId != rootLocalId && (group.RootPart.ScriptEvents & scriptEvents.touch) != 0)
            {
                EventManager.TriggerObjectGrabbing(rootLocalId, partLocalId, grabOffset, remoteClient, surfaceArg);
            }
        }

        public virtual void ProcessObjectDeGrab(uint localID, IClientAPI remoteClient, List<SurfaceTouchEventArgs> surfaceArgs)
        {
            SceneObjectPart part = GetSceneObjectPart(localID);
            if (part == null)
                return;

            SceneObjectGroup grp = part.ParentGroup;

            SurfaceTouchEventArgs surfaceArg = null;
            if (surfaceArgs != null && surfaceArgs.Count > 0)
                surfaceArg = surfaceArgs[0];

            uint partLocalId = part.LocalId;
            // If the touched prim handles touches, deliver it
            if ((part.ScriptEvents & scriptEvents.touch_end) != 0)
            {
                EventManager.TriggerObjectDeGrab(partLocalId, 0, remoteClient, surfaceArg);
                if(!part.PassTouches)
                    return;
            }

            uint rootPartLocalId = grp.RootPart.LocalId;
            if (partLocalId != rootPartLocalId && (grp.RootPart.ScriptEvents & scriptEvents.touch_end) != 0)
            {
                EventManager.TriggerObjectDeGrab(rootPartLocalId, partLocalId, remoteClient, surfaceArg);
            }
        }

        /// <summary>
        /// Start spinning the given object
        /// </summary>
        /// <param name="objectID"></param>
        /// <param name="rotation"></param>
        /// <param name="remoteClient"></param>
        public virtual void ProcessSpinStart(UUID objectID, IClientAPI remoteClient)
        {
            SceneObjectGroup group = GetGroupByPrim(objectID);
            if (group != null)
            {
                if (Permissions.CanMoveObject(group, remoteClient))// && PermissionsMngr.)
                {
                    group.SpinStart(remoteClient);
                }
            }
        }

        /// <summary>
        /// Spin the given object
        /// </summary>
        /// <param name="objectID"></param>
        /// <param name="rotation"></param>
        /// <param name="remoteClient"></param>
        public virtual void ProcessSpinObject(UUID objectID, Quaternion rotation, IClientAPI remoteClient)
        {
            SceneObjectGroup group = GetGroupByPrim(objectID);
            if (group != null)
            {
                if (Permissions.CanMoveObject(group, remoteClient))// && PermissionsMngr.)
                {
                    group.SpinMovement(rotation, remoteClient);
                }
                // This is outside the above permissions condition
                // so that if the object is locked the client moving the object
                // get's it's position on the simulator even if it was the same as before
                // This keeps the moving user's client in sync with the rest of the world.
                group.SendGroupTerseUpdate();
            }
        }

        public virtual void ProcessSpinObjectStop(UUID objectID, IClientAPI remoteClient)
        {
/* no op for now
            SceneObjectGroup group = GetGroupByPrim(objectID);
            if (group != null)
            {
                if (Permissions.CanMoveObject(group.UUID, remoteClient.AgentId))// && PermissionsMngr.)
                {
//                    group.SpinMovement(rotation, remoteClient);
                }
                group.SendGroupTerseUpdate();
            }
*/
        }

        public void ProcessScriptReset(IClientAPI remoteClient, UUID objectID,
                UUID itemID)
        {
            SceneObjectPart part=GetSceneObjectPart(objectID);
            if (part == null)
                return;

            if (Permissions.CanResetScript(objectID, itemID, remoteClient.AgentId))
            {
                EventManager.TriggerScriptReset(part.LocalId, itemID);
            }
        }

        void ProcessViewerEffect(IClientAPI remoteClient, List<ViewerEffectEventHandlerArg> args)
        {
            // TODO: don't create new blocks if recycling an old packet
            bool discardableEffects = true;
            ViewerEffectPacket.EffectBlock[] effectBlockArray = new ViewerEffectPacket.EffectBlock[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                ViewerEffectPacket.EffectBlock effect = new ViewerEffectPacket.EffectBlock();
                effect.AgentID = args[i].AgentID;
                effect.Color = args[i].Color;
                effect.Duration = args[i].Duration;
                effect.ID = args[i].ID;
                effect.Type = args[i].Type;
                effect.TypeData = args[i].TypeData;
                effectBlockArray[i] = effect;

                if ((EffectType)effect.Type != EffectType.LookAt && (EffectType)effect.Type != EffectType.Beam)
                    discardableEffects = false;

                //m_log.DebugFormat("[YYY]: VE {0} {1} {2}", effect.AgentID, effect.Duration, (EffectType)effect.Type);
            }

            ForEachScenePresence(sp =>
                {
                    if (sp.ControllingClient.AgentId != remoteClient.AgentId)
                    {
                        if (!discardableEffects ||
                           (discardableEffects && ShouldSendDiscardableEffect(remoteClient, sp)))
                        {
                            //m_log.DebugFormat("[YYY]: Sending to {0}", sp.UUID);
                            sp.ControllingClient.SendViewerEffect(effectBlockArray);
                        }
                        //else
                        //    m_log.DebugFormat("[YYY]: Not sending to {0}", sp.UUID);
                    }
                });
        }

        private bool ShouldSendDiscardableEffect(IClientAPI thisClient, ScenePresence other)
        {
            return Vector3.DistanceSquared(other.CameraPosition, thisClient.SceneAgent.AbsolutePosition) < 100;
        }

        private class DescendentsRequestData
        {
            public IClientAPI RemoteClient;
            public UUID FolderID;
            //public UUID OwnerID;
            public bool FetchFolders;
            public bool FetchItems;
            //public int SortOrder;
        }

        static private ConcurrentQueue<DescendentsRequestData> m_descendentsRequestQueue = new ConcurrentQueue<DescendentsRequestData>();
        static private Object m_descendentsRequestLock = new Object();
        static private bool m_descendentsRequestProcessing = false;

        /// <summary>
        /// Tell the client about the various child items and folders contained in the requested folder.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="ownerID"></param>
        /// <param name="fetchFolders"></param>
        /// <param name="fetchItems"></param>
        /// <param name="sortOrder"></param>
        public void HandleFetchInventoryDescendents(IClientAPI remoteClient, UUID folderID, UUID ownerID,
                                                    bool fetchFolders, bool fetchItems, int sortOrder)
        {
//            m_log.DebugFormat(
//                "[USER INVENTORY]: HandleFetchInventoryDescendents() for {0}, folder={1}, fetchFolders={2}, fetchItems={3}, sortOrder={4}",
//                remoteClient.Name, folderID, fetchFolders, fetchItems, sortOrder);

            if (folderID.IsZero())
                return;

            // FIXME MAYBE: We're not handling sortOrder!

            // TODO: This code for looking in the folder for the library should be folded somewhere else
            // so that this class doesn't have to know the details (and so that multiple libraries, etc.
            // can be handled transparently).
            InventoryFolderImpl fold = null;
            if (LibraryService != null && LibraryService.LibraryRootFolder != null)
            {
                if ((fold = LibraryService.LibraryRootFolder.FindFolder(folderID)) != null)
                {
                    List<InventoryItemBase> its = fold.RequestListOfItems();
                    List<InventoryFolderBase> fds = fold.RequestListOfFolders();
                    remoteClient.SendInventoryFolderDetails(
                        fold.Owner, folderID, its, fds,
                        fold.Version, its.Count + fds.Count, fetchFolders, fetchItems);
                    return;
                }
            }

            DescendentsRequestData req = new DescendentsRequestData();
            req.RemoteClient = remoteClient;
            req.FolderID = folderID;
            //req.OwnerID = ownerID;
            req.FetchFolders = fetchFolders;
            req.FetchItems = fetchItems;
            //req.SortOrder = sortOrder;

            m_descendentsRequestQueue.Enqueue(req);

            if (Monitor.TryEnter(m_descendentsRequestLock))
            {
                if (!m_descendentsRequestProcessing)
                {
                    m_descendentsRequestProcessing = true;
                    Util.FireAndForget(x => SendInventoryAsync());
                }
                Monitor.Exit(m_descendentsRequestLock);
            }
        }

        void SendInventoryAsync()
        {
            lock(m_descendentsRequestLock)
            {
                try
                {
                    while(m_descendentsRequestQueue.TryDequeue(out DescendentsRequestData req))
                    {
                        if(!req.RemoteClient.IsActive)
                            continue;
                        SendInventoryUpdate(req.RemoteClient, new InventoryFolderBase(req.FolderID), req.FetchFolders, req.FetchItems);
                        Thread.Sleep(50);
                    }
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[AGENT INVENTORY]: Error in SendInventoryAsync(). Exception {0}", e);
                }
                m_descendentsRequestProcessing = false;
            }
        }

        /// <summary>
        /// Handle an inventory folder creation request from the client.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="folderType"></param>
        /// <param name="folderName"></param>
        /// <param name="parentID"></param>
        public void HandleCreateInventoryFolder(IClientAPI remoteClient, UUID folderID, ushort folderType,
                                                string folderName, UUID parentID)
        {
            InventoryFolderBase folder = new InventoryFolderBase(folderID, folderName, remoteClient.AgentId, (short)folderType, parentID, 1);
            if (!InventoryService.AddFolder(folder))
            {
                m_log.WarnFormat(
                     "[AGENT INVENTORY]: Failed to create folder for user {0} {1}",
                     remoteClient.Name, remoteClient.AgentId);
            }
        }

        /// <summary>
        /// Handle a client request to update the inventory folder
        /// </summary>
        ///
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        /// <param name="type"></param>
        /// <param name="name"></param>
        /// <param name="parentID"></param>
        public void HandleUpdateInventoryFolder(IClientAPI remoteClient, UUID folderID, ushort type, string name,
                                                UUID parentID)
        {
//            m_log.DebugFormat(
//                "[AGENT INVENTORY]: Updating inventory folder {0} {1} for {2} {3}", folderID, name, remoteClient.Name, remoteClient.AgentId);

            InventoryFolderBase folder = InventoryService.GetFolder(remoteClient.AgentId, folderID);
            if (folder != null)
            {
                folder.Name = name;
                folder.Type = (short)type;
                folder.ParentID = parentID;
                if (!InventoryService.UpdateFolder(folder))
                {
                    m_log.ErrorFormat(
                         "[AGENT INVENTORY]: Failed to update folder for user {0} {1}",
                         remoteClient.Name, remoteClient.AgentId);
                }
            }
        }

        public void HandleMoveInventoryFolder(IClientAPI remoteClient, UUID folderID, UUID parentID)
        {
            InventoryFolderBase folder = InventoryService.GetFolder(remoteClient.AgentId, folderID);
            if (folder != null)
            {
                folder.ParentID = parentID;
                if (!InventoryService.MoveFolder(folder))
                    m_log.WarnFormat("[AGENT INVENTORY]: could not move folder {0}", folderID);
                else
                    m_log.DebugFormat("[AGENT INVENTORY]: folder {0} moved to parent {1}", folderID, parentID);
            }
            else
            {
                m_log.WarnFormat("[AGENT INVENTORY]: request to move folder {0} but folder not found", folderID);
            }
        }

        /// <summary>
        /// This should delete all the items and folders in the given directory.
        /// </summary>
        /// <param name="remoteClient"></param>
        /// <param name="folderID"></param>
        public void HandlePurgeInventoryDescendents(IClientAPI remoteClient, UUID folderID)
        {
            try
            {
                UUID agent = remoteClient.AgentId;
                UUID folder = folderID;
                Util.FireAndForget(delegate
                {
                    PurgeFolderAsync(agent, folder);
                });
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[AGENT INVENTORY]: Exception on purge folder for user {0}: {1}", remoteClient.AgentId, e.Message);
            }
        }

        private void PurgeFolderAsync(UUID userID, UUID folderID)
        {
            InventoryFolderBase folder = new InventoryFolderBase(folderID, userID);

           try
            {
                if (InventoryService.PurgeFolder(folder))
                    m_log.DebugFormat("[AGENT INVENTORY]: folder {0} purged successfully", folderID);
                else
                    m_log.WarnFormat("[AGENT INVENTORY]: could not purge folder {0}", folderID);
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[AGENT INVENTORY]: Exception on async purge folder for user {0}: {1}", userID, e.Message);
            }
        }
    }
}
