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
using System.Collections.Generic;
using System.Net;
using OpenMetaverse;
using OpenMetaverse.Packets;
using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.CoreModules.World.Estate;
using log4net;
using System.Reflection;
using System.Xml;

namespace MutSea.Region.OptionalModules.World.NPC
{
    public class NPCAvatar : IClientAPI, INPC
    {
        public bool SenseAsAgent { get; set; }
        public UUID Owner
        {
            get { return m_ownerID;}
        }

        public delegate void ChatToNPC(
            string message, byte type, Vector3 fromPos, string fromName,
            UUID fromAgentID, UUID ownerID, byte source, byte audible);

        /// <summary>
        /// Fired when the NPC receives a chat message.
        /// </summary>
        public event ChatToNPC OnChatToNPC;

        public ViewerFlags ViewerFlags { get; private set; }

        /// <summary>
        /// Fired when the NPC receives an instant message.
        /// </summary>
        public event Action<GridInstantMessage> OnInstantMessageToNPC;

        private readonly string m_firstname;
        private readonly string m_lastname;
        private readonly Vector3 m_startPos;
        private UUID m_uuid = UUID.Random();
        private readonly Scene m_scene;
        private readonly UUID m_scopeID;
        private readonly UUID m_ownerID;
        private UUID m_hostGroupID;
        private string m_profileAbout = "";
        private UUID m_profileImage = UUID.Zero;
        private string m_born;
        public List<uint> SelectedObjects {get; private set;}

        public NPCAvatar(
            string firstname, string lastname, Vector3 position, UUID ownerID, bool senseAsAgent, Scene scene)
        {
            m_firstname = firstname;
            m_lastname = lastname;
            m_startPos = position;
            m_uuid = UUID.Random();
            m_scene = scene;
            m_scopeID = scene.RegionInfo.ScopeID;
            m_ownerID = ownerID;
            SenseAsAgent = senseAsAgent;
            m_hostGroupID = UUID.Zero;
        }

        public NPCAvatar(
            string firstname, string lastname, UUID agentID, Vector3 position, UUID ownerID, bool senseAsAgent, Scene scene)
        {
            m_firstname = firstname;
            m_lastname = lastname;
            m_startPos = position;
            m_uuid = agentID;
            m_scene = scene;
            m_ownerID = ownerID;
            SenseAsAgent = senseAsAgent;
            m_hostGroupID = UUID.Zero;
        }

        public string profileAbout
        {
            get { return m_profileAbout; }
            set
            {
                if(value.Length > 255)
                    m_profileAbout = value.Substring(0,255);
                else
                    m_profileAbout = value;
            }
        }

        public UUID profileImage
        {
            get { return m_profileImage; }
            set { m_profileImage = value; }
        }

        public IScene Scene
        {
            get { return m_scene; }
        }

        public UUID ScopeId
        {
            get { return m_scopeID; }
        }

        public int PingTimeMS { get { return 0; } }

        public UUID OwnerID
        {
            get { return m_ownerID; }
        }

        public ISceneAgent SceneAgent { get; set; }

        public void Say(string message)
        {
            SendOnChatFromClient(0, message, ChatTypeEnum.Say);
        }

        public void Say(int channel, string message)
        {
            SendOnChatFromClient(channel, message, ChatTypeEnum.Say);
        }

        public void Shout(int channel, string message)
        {
            SendOnChatFromClient(channel, message, ChatTypeEnum.Shout);
        }

        public void Whisper(int channel, string message)
        {
            SendOnChatFromClient(channel, message, ChatTypeEnum.Whisper);
        }

        public void Broadcast(string message)
        {
            SendOnChatFromClient(0, message, ChatTypeEnum.Broadcast);
        }

        public void GiveMoney(UUID target, int amount)
        {
            OnMoneyTransferRequest(m_uuid, target, amount, 1, "Payment");
        }

        public bool Touch(UUID target)
        {
            SceneObjectPart part = m_scene.GetSceneObjectPart(target);
            if (part == null)
                return false;
            bool objectTouchable = hasTouchEvents(part); // Only touch an object that is scripted to respond
            if (!objectTouchable && !part.IsRoot)
                objectTouchable = hasTouchEvents(part.ParentGroup.RootPart);
            if (!objectTouchable)
                return false;
            // Set up the surface args as if the touch is from a client that does not support this
            SurfaceTouchEventArgs surfaceArgs = new SurfaceTouchEventArgs()
            {
                FaceIndex = -1, // TOUCH_INVALID_FACE
                Binormal =  Vector3.Zero, // TOUCH_INVALID_VECTOR
                Normal =  Vector3.Zero, // TOUCH_INVALID_VECTOR
                STCoord = new Vector3(-1.0f, -1.0f, 0.0f), // TOUCH_INVALID_TEXCOORD
                UVCoord = new Vector3(-1.0f, -1.0f, 0.0f) // TOUCH_INVALID_TEXCOORD
            };
            List<SurfaceTouchEventArgs> touchArgs = new List<SurfaceTouchEventArgs>() { surfaceArgs };
            Vector3 offset = part.OffsetPosition * -1.0f;
            if (OnGrabObject == null)
                return false;
            OnGrabObject(part.LocalId, offset, this, touchArgs);
            OnGrabUpdate?.Invoke(part.UUID, offset, part.ParentGroup.RootPart.GroupPosition, this, touchArgs);
            OnDeGrabObject?.Invoke(part.LocalId, this, touchArgs);
            return true;
        }

        private bool hasTouchEvents(SceneObjectPart part)
        {
           return (part.ScriptEvents & scriptEvents.anytouch) != 0;
        }

        public void InstantMessage(UUID target, string message)
        {
            OnInstantMessage(this, new GridInstantMessage(m_scene,
                    m_uuid, m_firstname + " " + m_lastname,
                    target, 0, false, message,
                    UUID.Zero, false, Position, Array.Empty<byte>(), true));
        }

        public void SendAgentOffline(UUID[] agentIDs)
        {

        }

        public void SendAgentOnline(UUID[] agentIDs)
        {

        }

        public void SendFindAgent(UUID HunterID, UUID PreyID, double GlobalX, double GlobalY)
        {

        }

        public void SendSitResponse(UUID TargetID, Vector3 OffsetPos,
                    Quaternion SitOrientation, bool autopilot,
                    Vector3 CameraAtOffset, Vector3 CameraEyeOffset, bool ForceMouseLook)
        {

        }

        public void SendAdminResponse(UUID Token, uint AdminLevel)
        {

        }

        public void SendGroupMembership(GroupMembershipData[] GroupMembership)
        {

        }

        public Vector3 Position
        {
            get { return m_scene.Entities[m_uuid].AbsolutePosition; }
            set { m_scene.Entities[m_uuid].AbsolutePosition = value; }
        }

        public bool SendLogoutPacketWhenClosing
        {
            set { }
        }

        #region Internal Functions

        private void SendOnChatFromClient(int channel, string message, ChatTypeEnum chatType)
        {
            if(OnChatFromClient == null)
                return;

            message = message.Trim();
            if (channel == 0 && message.Length == 0)
                return;

            OSChatMessage chatFromClient = new OSChatMessage()
            {
                Channel = channel,
                From = Name,
                Message = message,
                //Position = StartPos,
                Position = this.Position,
                Scene = m_scene,
                Sender = this,
                SenderUUID = AgentId,
                Type = chatType
            };

            OnChatFromClient?.Invoke(this, chatFromClient);
        }

        #endregion

        #region Event Definitions IGNORE

// disable warning: public events constituting public API
#pragma warning disable 67
        public event Action<IClientAPI> OnLogout;
        public event ObjectPermissions OnObjectPermissions;
        public event MoveItemsAndLeaveCopy OnMoveItemsAndLeaveCopy;
        public event MoneyTransferRequest OnMoneyTransferRequest;
        public event ParcelBuy OnParcelBuy;
        public event Action<IClientAPI> OnConnectionClosed;
        public event GenericMessage OnGenericMessage;
        public event ImprovedInstantMessage OnInstantMessage;
        public event ChatMessage OnChatFromClient;
        public event TextureRequest OnRequestTexture;
        public event RezObject OnRezObject;
        public event ModifyTerrain OnModifyTerrain;
        public event SetAppearance OnSetAppearance;
        public event AvatarNowWearing OnAvatarNowWearing;
        public event RezSingleAttachmentFromInv OnRezSingleAttachmentFromInv;
        public event RezMultipleAttachmentsFromInv OnRezMultipleAttachmentsFromInv;
        public event UUIDNameRequest OnDetachAttachmentIntoInv;
        public event ObjectAttach OnObjectAttach;
        public event ObjectDeselect OnObjectDetach;
        public event ObjectDrop OnObjectDrop;
        public event StartAnim OnStartAnim;
        public event StopAnim OnStopAnim;
        public event ChangeAnim OnChangeAnim;
        public event LinkObjects OnLinkObjects;
        public event DelinkObjects OnDelinkObjects;
        public event RequestMapBlocks OnRequestMapBlocks;
        public event RequestMapName OnMapNameRequest;
        public event TeleportLocationRequest OnTeleportLocationRequest;
        public event TeleportLandmarkRequest OnTeleportLandmarkRequest;
        public event TeleportCancel OnTeleportCancel;
        public event DisconnectUser OnDisconnectUser;
        public event RequestAvatarProperties OnRequestAvatarProperties;
        public event SetAlwaysRun OnSetAlwaysRun;

        public event DeRezObject OnDeRezObject;
        public event RezRestoreToWorld OnRezRestoreToWorld;
        public event Action<IClientAPI> OnRegionHandShakeReply;
        public event GenericCall1 OnRequestWearables;
        public event Action<IClientAPI, bool> OnCompleteMovementToRegion;
        public event UpdateAgent OnPreAgentUpdate;
        public event UpdateAgent OnAgentUpdate;
        public event UpdateAgent OnAgentCameraUpdate;
        public event AgentRequestSit OnAgentRequestSit;
        public event AgentSit OnAgentSit;
        public event AvatarPickerRequest OnAvatarPickerRequest;
        public event Action<IClientAPI> OnRequestAvatarsData;
        public event AddNewPrim OnAddPrim;
        public event RequestGodlikePowers OnRequestGodlikePowers;
        public event GodKickUser OnGodKickUser;
        public event ObjectDuplicate OnObjectDuplicate;
        public event GrabObject OnGrabObject;
        public event DeGrabObject OnDeGrabObject;
        public event MoveObject OnGrabUpdate;
        public event SpinStart OnSpinStart;
        public event SpinObject OnSpinUpdate;
        public event SpinStop OnSpinStop;
        public event ViewerEffectEventHandler OnViewerEffect;

        public event AgentDataUpdate OnAgentDataUpdateRequest;
        public event TeleportLocationRequest OnSetStartLocationRequest;

        public event UpdateShape OnUpdatePrimShape;
        public event ObjectExtraParams OnUpdateExtraParams;
        public event RequestObjectPropertiesFamily OnRequestObjectPropertiesFamily;
        public event ObjectRequest OnObjectRequest;
        public event ObjectSelect OnObjectSelect;
        public event GenericCall7 OnObjectDescription;
        public event GenericCall7 OnObjectName;
        public event GenericCall7 OnObjectClickAction;
        public event GenericCall7 OnObjectMaterial;
        public event UpdatePrimFlags OnUpdatePrimFlags;
        public event UpdatePrimTexture OnUpdatePrimTexture;
        public event UpdateVector OnUpdatePrimGroupPosition;
        public event UpdateVector OnUpdatePrimSinglePosition;
        public event ClientChangeObject onClientChangeObject;
        public event UpdatePrimRotation OnUpdatePrimGroupRotation;
        public event UpdatePrimSingleRotationPosition OnUpdatePrimSingleRotationPosition;
        public event UpdatePrimSingleRotation OnUpdatePrimSingleRotation;
        public event UpdatePrimGroupRotation OnUpdatePrimGroupMouseRotation;
        public event UpdateVector OnUpdatePrimScale;
        public event UpdateVector OnUpdatePrimGroupScale;
        public event StatusChange OnChildAgentStatus;
        public event GenericCall2 OnStopMovement;
        public event Action<UUID> OnRemoveAvatar;

        public event CreateNewInventoryItem OnCreateNewInventoryItem;
        public event LinkInventoryItem OnLinkInventoryItem;
        public event CreateInventoryFolder OnCreateNewInventoryFolder;
        public event UpdateInventoryFolder OnUpdateInventoryFolder;
        public event MoveInventoryFolder OnMoveInventoryFolder;
        public event RemoveInventoryFolder OnRemoveInventoryFolder;
        public event RemoveInventoryItem OnRemoveInventoryItem;
        public event FetchInventoryDescendents OnFetchInventoryDescendents;
        public event PurgeInventoryDescendents OnPurgeInventoryDescendents;
        public event FetchInventory OnFetchInventory;
        public event RequestTaskInventory OnRequestTaskInventory;
        public event UpdateInventoryItem OnUpdateInventoryItem;
        public event CopyInventoryItem OnCopyInventoryItem;
        public event MoveInventoryItem OnMoveInventoryItem;
        public event UDPAssetUploadRequest OnAssetUploadRequest;
        public event XferReceive OnXferReceive;
        public event RequestXfer OnRequestXfer;
        public event AbortXfer OnAbortXfer;
        public event ConfirmXfer OnConfirmXfer;
        public event RezScript OnRezScript;
        public event UpdateTaskInventory OnUpdateTaskInventory;
        public event MoveTaskInventory OnMoveTaskItem;
        public event RemoveTaskInventory OnRemoveTaskItem;
        public event RequestAsset OnRequestAsset;

        public event UUIDNameRequest OnNameFromUUIDRequest;
        public event UUIDNameRequest OnUUIDGroupNameRequest;

        public event ParcelPropertiesRequest OnParcelPropertiesRequest;
        public event ParcelDivideRequest OnParcelDivideRequest;
        public event ParcelJoinRequest OnParcelJoinRequest;
        public event ParcelPropertiesUpdateRequest OnParcelPropertiesUpdateRequest;
        public event ParcelAbandonRequest OnParcelAbandonRequest;
        public event ParcelGodForceOwner OnParcelGodForceOwner;
        public event ParcelReclaim OnParcelReclaim;
        public event ParcelReturnObjectsRequest OnParcelReturnObjectsRequest;
        public event ParcelAccessListRequest OnParcelAccessListRequest;
        public event ParcelAccessListUpdateRequest OnParcelAccessListUpdateRequest;
        public event ParcelSelectObjects OnParcelSelectObjects;
        public event ParcelObjectOwnerRequest OnParcelObjectOwnerRequest;
        public event ParcelDeedToGroup OnParcelDeedToGroup;
        public event ObjectDeselect OnObjectDeselect;
        public event RegionInfoRequest OnRegionInfoRequest;
        public event EstateCovenantRequest OnEstateCovenantRequest;
        public event RequestTerrain OnRequestTerrain;
        public event RequestTerrain OnUploadTerrain;
        public event ObjectDuplicateOnRay OnObjectDuplicateOnRay;

        public event FriendActionDelegate OnApproveFriendRequest;
        public event FriendActionDelegate OnDenyFriendRequest;
        public event FriendshipTermination OnTerminateFriendship;
        public event GrantUserFriendRights OnGrantUserRights;

        public event EconomyDataRequest OnEconomyDataRequest;
        public event MoneyBalanceRequest OnMoneyBalanceRequest;
        public event UpdateAvatarProperties OnUpdateAvatarProperties;

        public event ObjectIncludeInSearch OnObjectIncludeInSearch;
        public event UUIDNameRequest OnTeleportHomeRequest;

        public event ScriptAnswer OnScriptAnswer;
        public event RequestPayPrice OnRequestPayPrice;
        public event ObjectSaleInfo OnObjectSaleInfo;
        public event ObjectBuy OnObjectBuy;
        public event BuyObjectInventory OnBuyObjectInventory;
        public event AgentSit OnUndo;
        public event AgentSit OnRedo;
        public event LandUndo OnLandUndo;

        public event ForceReleaseControls OnForceReleaseControls;
        public event GodLandStatRequest OnLandStatRequest;
        public event RequestObjectPropertiesFamily OnObjectGroupRequest;

        public event DetailedEstateDataRequest OnDetailedEstateDataRequest;
        public event SetEstateFlagsRequest OnSetEstateFlagsRequest;
        public event SetEstateTerrainBaseTexture OnSetEstateTerrainBaseTexture;
        public event SetEstateTerrainDetailTexture OnSetEstateTerrainDetailTexture;
        public event SetEstateTerrainTextureHeights OnSetEstateTerrainTextureHeights;
        public event CommitEstateTerrainTextureRequest OnCommitEstateTerrainTextureRequest;
        public event SetRegionTerrainSettings OnSetRegionTerrainSettings;
        public event BakeTerrain OnBakeTerrain;
        public event EstateRestartSimRequest OnEstateRestartSimRequest;
        public event EstateChangeCovenantRequest OnEstateChangeCovenantRequest;
        public event UpdateEstateAccessDeltaRequest OnUpdateEstateAccessDeltaRequest;
        public event SimulatorBlueBoxMessageRequest OnSimulatorBlueBoxMessageRequest;
        public event EstateBlueBoxMessageRequest OnEstateBlueBoxMessageRequest;
        public event EstateDebugRegionRequest OnEstateDebugRegionRequest;
        public event EstateTeleportOneUserHomeRequest OnEstateTeleportOneUserHomeRequest;
        public event EstateTeleportAllUsersHomeRequest OnEstateTeleportAllUsersHomeRequest;
        public event EstateChangeInfo OnEstateChangeInfo;
        public event EstateManageTelehub OnEstateManageTelehub;
        public event CachedTextureRequest OnCachedTextureRequest;
        public event ScriptReset OnScriptReset;
        public event GetScriptRunning OnGetScriptRunning;
        public event SetScriptRunning OnSetScriptRunning;
        public event Action<Vector3, bool, bool> OnAutoPilotGo;

        public event TerrainUnacked OnUnackedTerrain;

        public event RegionHandleRequest OnRegionHandleRequest;
        public event ParcelInfoRequest OnParcelInfoRequest;

        public event ActivateGesture OnActivateGesture;
        public event DeactivateGesture OnDeactivateGesture;
        public event ObjectOwner OnObjectOwner;

        public event DirPlacesQuery OnDirPlacesQuery;
        public event DirFindQuery OnDirFindQuery;
        public event DirLandQuery OnDirLandQuery;
        public event DirPopularQuery OnDirPopularQuery;
        public event DirClassifiedQuery OnDirClassifiedQuery;
        public event EventInfoRequest OnEventInfoRequest;
        public event ParcelSetOtherCleanTime OnParcelSetOtherCleanTime;

        public event MapItemRequest OnMapItemRequest;

        public event OfferCallingCard OnOfferCallingCard;
        public event AcceptCallingCard OnAcceptCallingCard;
        public event DeclineCallingCard OnDeclineCallingCard;
        public event SoundTrigger OnSoundTrigger;

        public event StartLure OnStartLure;
        public event TeleportLureRequest OnTeleportLureRequest;
        public event NetworkStats OnNetworkStatsUpdate;

        public event ClassifiedInfoRequest OnClassifiedInfoRequest;
        public event ClassifiedInfoUpdate OnClassifiedInfoUpdate;
        public event ClassifiedDelete OnClassifiedDelete;
        public event ClassifiedGodDelete OnClassifiedGodDelete;

        public event EventNotificationAddRequest OnEventNotificationAddRequest;
        public event EventNotificationRemoveRequest OnEventNotificationRemoveRequest;
        public event EventGodDelete OnEventGodDelete;

        public event ParcelDwellRequest OnParcelDwellRequest;

        public event UserInfoRequest OnUserInfoRequest;
        public event UpdateUserInfo OnUpdateUserInfo;

        public event RetrieveInstantMessages OnRetrieveInstantMessages;

        public event PickDelete OnPickDelete;
        public event PickGodDelete OnPickGodDelete;
        public event PickInfoUpdate OnPickInfoUpdate;
        public event AvatarNotesUpdate OnAvatarNotesUpdate;

        public event MuteListRequest OnMuteListRequest;

        public event AvatarInterestUpdate OnAvatarInterestUpdate;

        public event PlacesQuery OnPlacesQuery;

        public event FindAgentUpdate OnFindAgent;
        public event TrackAgentUpdate OnTrackAgent;
        public event NewUserReport OnUserReport;
        public event SaveStateHandler OnSaveState;
        public event GroupAccountSummaryRequest OnGroupAccountSummaryRequest;
        public event GroupAccountDetailsRequest OnGroupAccountDetailsRequest;
        public event GroupAccountTransactionsRequest OnGroupAccountTransactionsRequest;
        public event FreezeUserUpdate OnParcelFreezeUser;
        public event EjectUserUpdate OnParcelEjectUser;
        public event ParcelBuyPass OnParcelBuyPass;
        public event ParcelGodMark OnParcelGodMark;
        public event GroupActiveProposalsRequest OnGroupActiveProposalsRequest;
        public event GroupVoteHistoryRequest OnGroupVoteHistoryRequest;
        public event SimWideDeletesDelegate OnSimWideDeletes;
        public event SendPostcard OnSendPostcard;
        public event ChangeInventoryItemFlags OnChangeInventoryItemFlags;
        public event MuteListEntryUpdate OnUpdateMuteListEntry;
        public event MuteListEntryRemove OnRemoveMuteListEntry;
        public event GodlikeMessage onGodlikeMessage;
        public event GodUpdateRegionInfoUpdate OnGodUpdateRegionInfoUpdate;
        public event GenericCall2 OnUpdateThrottles;
        public event AgentFOV OnAgentFOV;

#pragma warning restore 67

        #endregion

        public void ActivateGesture(UUID assetId, UUID gestureId)
        {
        }
        public void DeactivateGesture(UUID assetId, UUID gestureId)
        {
        }

        #region Overrriden Methods IGNORE

        public virtual Vector3 StartPos
        {
            get { return m_startPos; }
            set { }
        }

        public float StartFar { get; set; }
        public float FOV { get; set; } = 1.25f;
        public int viewHeight { get; set; } = 480;
        public int viewWidth { get; set; } = 640;

        public virtual UUID AgentId
        {
            get { return m_uuid; }
            set { m_uuid = value; }
        }

        public UUID SessionId
        {
            get { return UUID.Zero; }
        }

        public UUID SecureSessionId
        {
            get { return UUID.Zero; }
        }

        public virtual string FirstName
        {
            get { return m_firstname; }
        }

        public virtual string LastName
        {
            get { return m_lastname; }
        }

        public virtual String Name
        {
            get { return FirstName + " " + LastName; }
        }

        public bool IsActive
        {
            get { return true; }
            set { }
        }

        public bool IsLoggingOut
        {
            get { return false; }
            set { }
        }
        public UUID ActiveGroupId
        {
            get { return m_hostGroupID; }
            set { m_hostGroupID = value; }
        }

        public string ActiveGroupName
        {
            get { return String.Empty; }
            set { }
        }

        public ulong ActiveGroupPowers
        {
            get { return 0; }
            set { }
        }

        public string Born
        {
            get { return m_born; }
            set { m_born = value; }
        }

        public bool IsGroupMember(UUID groupID)
        {
            return m_hostGroupID.Equals(groupID);
        }

        public Dictionary<UUID, ulong> GetGroupPowers()
        {
            return new Dictionary<UUID, ulong>();
        }

        public void SetGroupPowers(Dictionary<UUID, ulong> powers) { }

        public ulong GetGroupPowers(UUID groupID)
        {
            return 0;
        }

        public virtual int NextAnimationSequenceNumber
        {
            get { return 1; }
            set { }
        }

        public virtual void SendWearables(AvatarWearable[] wearables, int serial)
        {
        }

        public virtual void SendAppearance(UUID agentID, byte[] visualParams, byte[] textureEntry, float hover)
        {
        }

        public void SendCachedTextureResponse(ISceneEntity avatar, int serial, List<CachedTextureResponseArg> cachedTextures)
        {

        }

        public virtual void Kick(string message)
        {
        }

        public virtual void SendAvatarPickerReply(UUID QueryID, List<UserData> users)
        {
        }

        public virtual void SendAgentDataUpdate(UUID agentid, UUID activegroupid, string firstname, string lastname, ulong grouppowers, string groupname, string grouptitle)
        {

        }

        public virtual void SendKillObject(List<uint> localID)
        {
        }

        public virtual void SetChildAgentThrottle(byte[] throttle)
        {
        }

        public virtual void SetChildAgentThrottle(byte[] throttle, float factor)
        {

        }

        public void SetAgentThrottleSilent(int throttle, int setting)
        {


        }
        public byte[] GetThrottlesPacked(float multiplier)
        {
            return Array.Empty<byte>();
        }


        public virtual void SendAnimations(UUID[] animations, int[] seqs, UUID sourceAgentId, UUID[] objectIDs)
        {
        }

        public virtual void SendChatMessage(
            string message, byte type, Vector3 fromPos, string fromName,
            UUID fromAgentID, UUID ownerID, byte source, byte audible)
        {
            OnChatToNPC?.Invoke(message, type, fromPos, fromName, fromAgentID, ownerID, source, audible);
        }

        public void SendInstantMessage(GridInstantMessage im)
        {
            OnInstantMessageToNPC?.Invoke(im);
        }

        public void SendGenericMessage(string method, UUID invoice, List<string> message)
        {

        }

        public void SendGenericMessage(string method, UUID invoice, List<byte[]> message)
        {

        }

        public virtual bool CanSendLayerData()
        {
            return false;
        }

        public virtual void SendLayerData()
        {
        }

        public void SendLayerData(int[] map)
        {
        }

        public virtual void SendWindData(int version, Vector2[] windSpeeds) { }


        public virtual void MoveAgentIntoRegion(RegionInfo regInfo, Vector3 pos, Vector3 look)
        {
        }

        public virtual void InformClientOfNeighbour(ulong neighbourHandle, IPEndPoint neighbourExternalEndPoint)
        {
        }

        public virtual AgentCircuitData RequestClientInfo()
        {
            return new AgentCircuitData();
        }

        public virtual void CrossRegion(ulong newRegionHandle, Vector3 pos, Vector3 lookAt,
                                        IPEndPoint newRegionExternalEndPoint, string capsURL)
        {
        }

        public virtual void SendMapBlock(List<MapBlockData> mapBlocks, uint flag)
        {
        }

        public virtual void SendLocalTeleport(Vector3 position, Vector3 lookAt, uint flags)
        {
        }

        public virtual void SendRegionTeleport(ulong regionHandle, byte simAccess, IPEndPoint regionExternalEndPoint,
                                               uint locationID, uint flags, string capsURL)
        {
        }

        public virtual void SendTeleportFailed(string reason)
        {
        }

        public virtual void SendTeleportStart(uint flags)
        {
        }

        public virtual void SendTeleportProgress(uint flags, string message)
        {
        }

        public virtual void SendMoneyBalance(UUID transaction, bool success, byte[] description, int balance, int transactionType, UUID sourceID, bool sourceIsGroup, UUID destID, bool destIsGroup, int amount, string item)
        {
        }

        public virtual void SendPayPrice(UUID objectID, int[] payPrice)
        {
        }

        public virtual void SendCoarseLocationUpdate(List<UUID> users, List<Vector3> CoarseLocations)
        {
        }

        public virtual void SendDialog(string objectname, UUID objectID, UUID ownerID, string ownerFirstName, string ownerLastName, string msg, UUID textureID, int ch, string[] buttonlabels)
        {
        }

        public void SendEntityFullUpdateImmediate(ISceneEntity avatar)
        {
        }

        public void SendEntityTerseUpdateImmediate(ISceneEntity ent)
        {
        }

        public void SendEntityUpdate(ISceneEntity entity, PrimUpdateFlags updateFlags)
        {
        }

        public void ReprioritizeUpdates()
        {
        }

        public void FlushPrimUpdates()
        {
        }

        public virtual void SendInventoryFolderDetails(UUID ownerID, UUID folderID,
                                                       List<InventoryItemBase> items,
                                                       List<InventoryFolderBase> folders,
                                                       int version,
                                                       int descendents,
                                                       bool fetchFolders,
                                                       bool fetchItems)
        {
        }

        public void SendInventoryItemDetails(InventoryItemBase[] items)
        {
        }

        public void SendInventoryItemCreateUpdate(InventoryItemBase Item, uint callbackID)
        {
        }

        public void SendInventoryItemCreateUpdate(InventoryItemBase Item, UUID transactionID, uint callbackId)
        {
        }

        public void SendRemoveInventoryItem(UUID itemID)
        {
        }

        public void SendRemoveInventoryItems(UUID[] items)
        {
        }

        public void SendBulkUpdateInventory(InventoryNodeBase node, UUID? transactionID = null)
        {
        }

        public void SendBulkUpdateInventory(InventoryFolderBase[] folders, InventoryItemBase[] items)
        {
        }

        public void SendTakeControls(int controls, bool passToAgent, bool TakeControls)
        {
        }

        public void SendTaskInventory(UUID taskID, short serial, byte[] fileName)
        {
        }

        public virtual void SendXferPacket(ulong xferID, uint packet,
                byte[] XferData, int XferDataOffset, int XferDatapktLen, bool isTaskInventory)
        {
        }

        public virtual void SendAbortXferPacket(ulong xferID)
        {

        }

        public virtual void SendEconomyData(float EnergyEfficiency, int ObjectCapacity, int ObjectCount, int PriceEnergyUnit,
                                            int PriceGroupCreate, int PriceObjectClaim, float PriceObjectRent, float PriceObjectScaleFactor,
                                            int PriceParcelClaim, float PriceParcelClaimFactor, int PriceParcelRent, int PricePublicObjectDecay,
                                            int PricePublicObjectDelete, int PriceRentLight, int PriceUpload, int TeleportMinPrice, float TeleportPriceExponent)
        {

        }
        public virtual void SendNameReply(UUID profileId, string firstname, string lastname)
        {
        }

        public virtual void SendPreLoadSound(UUID objectID, UUID ownerID, UUID soundID)
        {
        }

        public virtual void SendPlayAttachedSound(UUID soundID, UUID objectID, UUID ownerID, float gain,
                                                  byte flags)
        {
        }

        public void SendTriggeredSound(UUID soundID, UUID ownerID, UUID objectID, UUID parentID, ulong handle, Vector3 position, float gain)
        {
        }

        public void SendAttachedSoundGainChange(UUID objectID, float gain)
        {

        }

        public void SendAlertMessage(string message)
        {
        }

        public void SendAgentAlertMessage(string message, bool modal)
        {
        }

        public void SendAlertMessage(string message, string info)
        {
        }

        public void SendSystemAlertMessage(string message)
        {
        }

        public void SendLoadURL(string objectname, UUID objectID, UUID ownerID, bool groupOwned, string message,
                                string url)
        {
        }

        public virtual void SendRegionHandshake()
        {
            OnRegionHandShakeReply?.Invoke(this);
        }

        public void SendAssetUploadCompleteMessage(sbyte AssetType, bool Success, UUID AssetFullID)
        {
        }

        public void SendConfirmXfer(ulong xferID, uint PacketID)
        {
        }

        public void SendXferRequest(ulong XferID, short AssetType, UUID vFileID, byte FilePath, byte[] FileName)
        {
        }

        public void SendInitiateDownload(string simFileName, string clientFileName)
        {
        }

        public void SendImageFirstPart(ushort numParts, UUID ImageUUID, uint ImageSize, byte[] ImageData, byte imageCodec)
        {
        }

        public void SendImageNotFound(UUID imageid)
        {
        }

        public void SendImageNextPart(ushort partNumber, UUID imageUuid, byte[] imageData)
        {
        }

        public void SendShutdownConnectionNotice()
        {
        }

        public void SendSimStats(SimStats stats)
        {
        }

        public void SendObjectPropertiesFamilyData(ISceneEntity Entity, uint RequestFlags)
        {
        }

        public void SendObjectPropertiesReply(ISceneEntity entity)
        {
        }

        public void SendViewerTime(Vector3 sunDir, float sunphase)
        {
        }

        public void SendViewerEffect(ViewerEffectPacket.EffectBlock[] effectBlocks)
        {
        }

        public void SendAvatarProperties(UUID avatarID, string aboutText, string bornOn, Byte[] membershipType,
                                         string flAbout, uint flags, UUID flImageID, UUID imageID, string profileURL,
                                         UUID partnerID)
        {
        }

        public void SendAsset(AssetRequestToClient req)
        {
        }

        public void SendTexture(AssetBase TextureAsset)
        {
        }

        public int DebugPacketLevel { get; set; }

        public void InPacket(object NewPack)
        {
        }

        public void ProcessInPacket(Packet NewPack)
        {
        }

        public void Close()
        {
            Close(true, false);
        }

        public void Close(bool sendStop, bool force)
        {
            // Remove ourselves from the scene
            m_scene.RemoveClient(AgentId, false);
        }

        public void Disconnect(string reason)
        {
            Close(true, false);
        }

        public void Start()
        {
            // We never start the client, so always fail.
            throw new NotImplementedException();
        }

        public void Stop()
        {
        }

        private uint m_circuitCode;
        private IPEndPoint m_remoteEndPoint;

        public uint CircuitCode
        {
            get { return m_circuitCode; }
            set
            {
                m_circuitCode = value;
                m_remoteEndPoint = new IPEndPoint(IPAddress.Loopback, (ushort)m_circuitCode);
            }
        }

        public IPEndPoint RemoteEndPoint
        {
            get { return m_remoteEndPoint; }
        }

        public void SendBlueBoxMessage(UUID FromAvatarID, String FromAvatarName, String Message)
        {

        }
        public void SendLogoutPacket()
        {
        }

        public void Terminate()
        {
        }

        public ClientInfo GetClientInfo()
        {
            return null;
        }

        public void SetClientInfo(ClientInfo info)
        {
        }

        public void SendScriptQuestion(UUID objectID, string taskName, string ownerName, UUID itemID, int question)
        {
        }
        public void SendHealth(float health)
        {
        }

        public void SendEstateList(UUID invoice, int code, UUID[] Data, uint estateID)
        {
        }

        public void SendBannedUserList(UUID invoice, EstateBan[] banlist, uint estateID)
        {
        }

        public void SendRegionInfoToEstateMenu(RegionInfoForEstateMenuArgs args)
        {
        }
        public void SendEstateCovenantInformation(UUID covenant)
        {
        }
        public void SendTelehubInfo(UUID ObjectID, string ObjectName, Vector3 ObjectPos, Quaternion ObjectRot, List<Vector3> SpawnPoint)
        {
        }
        public void SendDetailedEstateData(UUID invoice, string estateName, uint estateID, uint parentEstate, uint estateFlags, uint sunPosition, UUID covenant, uint covenantChanged, string abuseEmail, UUID estateOwner)
        {
        }

        public void SendLandProperties(int sequence_id, bool snap_selection, int request_result, ILandObject lo, float simObjectBonusFactor,int parcelObjectCapacity, int simObjectCapacity, uint regionFlags)
        {
        }
        public void SendLandAccessListData(List<LandAccessEntry> accessList, uint accessFlag, int localLandID)
        {
        }
        public void SendForceClientSelectObjects(List<uint> objectIDs)
        {
        }
        public void SendCameraConstraint(Vector4 ConstraintPlane)
        {
        }
        public void SendLandObjectOwners(LandData land, List<UUID> groups, Dictionary<UUID, int> ownersAndCount)
        {
        }
        public void SendLandParcelOverlay(byte[] data, int sequence_id)
        {
        }

        public void SendGroupNameReply(UUID groupLLUID, string GroupName)
        {
        }

        public void SendScriptRunningReply(UUID objectID, UUID itemID, bool running)
        {
        }

        public void SendLandStatReply(uint reportType, uint requestFlags, uint resultCount, LandStatReportItem[] lsrpia)
        {
        }
        #endregion


        public void SendParcelMediaCommand(uint flags, ParcelMediaCommandEnum command, float time)
        {
        }

        public void SendParcelMediaUpdate(string mediaUrl, UUID mediaTextureID,
                                   byte autoScale, string mediaType, string mediaDesc, int mediaWidth, int mediaHeight,
                                   byte mediaLoop)
        {
        }

        public void SendSetFollowCamProperties (UUID objectID, SortedDictionary<int, float> parameters)
        {
        }

        public void SendClearFollowCamProperties (UUID objectID)
        {
        }

        public void SendRegionHandle (UUID regoinID, ulong handle)
        {
        }

        public void SendParcelInfo (RegionInfo info, LandData land, UUID parcelID, uint x, uint y)
        {
        }

        public void SetClientOption(string option, string value)
        {
        }

        public string GetClientOption(string option)
        {
            return string.Empty;
        }

        public void SendScriptTeleportRequest (string objName, string simName, Vector3 pos, Vector3 lookAt)
        {
        }

        public void SendDirPlacesReply(UUID queryID, DirPlacesReplyData[] data)
        {
        }

        public void SendDirPeopleReply(UUID queryID, DirPeopleReplyData[] data)
        {
        }

        public void SendDirEventsReply(UUID queryID, DirEventsReplyData[] data)
        {
        }

        public void SendDirGroupsReply(UUID queryID, DirGroupsReplyData[] data)
        {
        }

        public void SendDirClassifiedReply(UUID queryID, DirClassifiedReplyData[] data)
        {
        }

        public void SendDirLandReply(UUID queryID, DirLandReplyData[] data)
        {
        }

        public void SendDirPopularReply(UUID queryID, DirPopularReplyData[] data)
        {
        }

        public void SendMapItemReply(mapItemReply[] replies, uint mapitemtype, uint flags)
        {
        }

        public void SendEventInfoReply (EventData info)
        {
        }

        public void SendOfferCallingCard (UUID destID, UUID transactionID)
        {
        }

        public void SendAcceptCallingCard (UUID transactionID)
        {
        }

        public void SendDeclineCallingCard (UUID transactionID)
        {
        }

        public void SendJoinGroupReply(UUID groupID, bool success)
        {
        }

        public void SendEjectGroupMemberReply(UUID agentID, UUID groupID, bool success)
        {
        }

        public void SendLeaveGroupReply(UUID groupID, bool success)
        {
        }

        public void SendAvatarGroupsReply(UUID avatarID, GroupMembershipData[] data)
        {
        }

        public void SendAgentGroupDataUpdate(UUID avatarID, GroupMembershipData[] data)
        {
        }

        public void SendTerminateFriend(UUID exFriendID)
        {
        }

        #region IClientAPI Members


        public bool AddGenericPacketHandler(string MethodName, GenericMessage handler)
        {
            //throw new NotImplementedException();
            return false;
        }

        public void SendAvatarClassifiedReply(UUID targetID, UUID[] classifiedID, string[] name)
        {
        }

        public void SendClassifiedInfoReply(UUID classifiedID, UUID creatorID, uint creationDate, uint expirationDate, uint category, string name, string description, UUID parcelID, uint parentEstate, UUID snapshotID, string simName, Vector3 globalPos, string parcelName, byte classifiedFlags, int price)
        {
        }

        public void SendAgentDropGroup(UUID groupID)
        {
        }

        public void SendAvatarNotesReply(UUID targetID, string text)
        {
        }

        public void SendAvatarPicksReply(UUID targetID, Dictionary<UUID, string> picks)
        {
        }

        public void SendAvatarClassifiedReply(UUID targetID, Dictionary<UUID, string> classifieds)
        {
        }

        public void SendParcelDwellReply(int localID, UUID parcelID, float dwell)
        {
        }

        public void SendUserInfoReply(bool imViaEmail, bool visible, string email)
        {
        }

        public void SendCreateGroupReply(UUID groupID, bool success, string message)
        {
        }

        public void RefreshGroupMembership()
        {
        }

        public void UpdateGroupMembership(GroupMembershipData[] data)
        {
        }

        public void GroupMembershipRemove(UUID GroupID)
        {
        }

        public void GroupMembershipAddReplace(UUID GroupID,ulong GroupPowers)
        {
        }

        public void SendUseCachedMuteList()
        {
        }

        public void SendEmpytMuteList()
        {
        }

        public void SendMuteListUpdate(string filename)
        {
        }

        public void SendPickInfoReply(UUID pickID,UUID creatorID, bool topPick, UUID parcelID, string name, string desc, UUID snapshotID, string user, string originalName, string simName, Vector3d posGlobal, int sortOrder, bool enabled)
        {
        }
        #endregion

        public void SendRebakeAvatarTextures(UUID textureID)
        {
        }

        public void SendAvatarInterestsReply(UUID avatarID, uint wantMask, string wantText, uint skillsMask, string skillsText, string languages)
        {
        }

        public void SendGroupAccountingDetails(IClientAPI sender,UUID groupID, UUID transactionID, UUID sessionID, int amt)
        {
        }

        public void SendGroupAccountingSummary(IClientAPI sender,UUID groupID, uint moneyAmt, int totalTier, int usedTier)
        {
        }

        public void SendGroupTransactionsSummaryDetails(IClientAPI sender,UUID groupID, UUID transactionID, UUID sessionID,int amt)
        {
        }

        public void SendGroupVoteHistory(UUID groupID, UUID transactionID, GroupVoteHistory[] Votes)
        {
        }

        public void SendGroupActiveProposals(UUID groupID, UUID transactionID, GroupActiveProposals[] Proposals)
        {
        }

        public void SendChangeUserRights(UUID agentID, UUID friendID, int rights)
        {
        }

        public void SendTextBoxRequest(string message, int chatChannel, string objectname, UUID ownerID, string ownerFirstName, string ownerLastName, UUID objectId)
        {
        }

        public void SendAgentTerseUpdate(ISceneEntity presence)
        {
        }

        public void SendPlacesReply(UUID queryID, UUID transactionID, PlacesReplyData[] data)
        {
        }

        public void SendSelectedPartsProprieties(List<ISceneEntity> parts)
        {
        }

        public void SendPartPhysicsProprieties(ISceneEntity entity)
        {
        }

        public void SendPartFullUpdate(ISceneEntity ent, uint? parentID)
        {
        }

        public int GetAgentThrottleSilent(int throttle)
        {
            return 0;
        }

        public uint GetViewerCaps()
        {
            return 0;
        }

    }
}
