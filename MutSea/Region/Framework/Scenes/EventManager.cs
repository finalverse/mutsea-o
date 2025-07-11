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
using System.Reflection;
using log4net;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Framework.Client;
using MutSea.Region.Framework.Interfaces;
using Caps = MutSea.Framework.Capabilities.Caps;
using GridRegion = MutSea.Services.Interfaces.GridRegion;

namespace MutSea.Region.Framework.Scenes
{
    /// <summary>
    /// A class for triggering remote scene events.
    /// </summary>
    public class EventManager
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Triggered on each sim frame.
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.Update"/>
        /// Core uses it for things like Sun, Wind & Clouds
        /// The MRM module also uses it.
        /// </remarks>
        public delegate void OnFrameDelegate();
        public event OnFrameDelegate OnFrame;

        /// <summary>
        /// Trigerred when an agent moves.
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="MutSea.Region.Framework.Scenes.ScenePresence.HandleAgentUpdate"/>
        /// prior to <see cref="MutSea.Region.Framework.Scenes.ScenePresence.TriggerScenePresenceUpdated"/>
        /// </remarks>
        public delegate void ClientMovement(ScenePresence client);
        public event ClientMovement OnClientMovement;

        /// <summary>
        /// Triggered if the terrain has been edited
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="MutSea.Region.CoreModules.World.Terrain.CheckForTerrainUpdates"/>
        /// after it determines that an update has been made.
        /// </remarks>
        public delegate void OnTerrainTaintedDelegate();
        public event OnTerrainTaintedDelegate OnTerrainTainted;

        /// <summary>
        /// Triggered if the terrain has been edited
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.UpdateTerrain"/>
        /// but is used by core solely to update the physics engine.
        /// </remarks>
        public delegate void OnTerrainTickDelegate();
        public event OnTerrainTickDelegate OnTerrainTick;

        public delegate void OnTerrainCheckUpdatesDelegate();
        public event OnTerrainCheckUpdatesDelegate OnTerrainCheckUpdates;

        public delegate void OnTerrainUpdateDelegate();
        public event OnTerrainUpdateDelegate OnTerrainUpdate;

        /// <summary>
        /// Triggered when a region is backed up/persisted to storage
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.Backup"/>
        /// and is fired before the persistence occurs.
        /// </remarks>
        public delegate void OnBackupDelegate(ISimulationDataService datastore, bool forceBackup);
        public event OnBackupDelegate OnBackup;

        /// <summary>
        /// Triggered when a new client connects to the scene.
        /// </summary>
        /// <remarks>
        /// This gets triggered in <see cref="TriggerOnNewClient"/>,
        /// which checks if an instance of <see cref="MutSea.Framework.IClientAPI"/>
        /// also implements <see cref="MutSea.Framework.Client.IClientCore"/> and as such,
        /// is not triggered by <see cref="MutSea.Region.OptionalModules.World.NPC">NPCs</see>.
        /// </remarks>
        public delegate void OnClientConnectCoreDelegate(IClientCore client);
        public event OnClientConnectCoreDelegate OnClientConnect;

        /// <summary>
        /// Triggered when a new client is added to the scene.
        /// </summary>
        /// <remarks>
        /// This is triggered for both child and root agent client connections.
        ///
        /// Triggered before OnClientLogin.
        ///
        /// This is triggered under per-agent lock.  So if you want to perform any long-running operations, please
        /// do this on a separate thread.
        /// </remarks>
        public delegate void OnNewClientDelegate(IClientAPI client);
        public event OnNewClientDelegate OnNewClient;

        /// <summary>
        /// Fired if the client entering this sim is doing so as a new login
        /// </summary>
        /// <remarks>
        /// This is triggered under per-agent lock.  So if you want to perform any long-running operations, please
        /// do this on a separate thread.
        /// </remarks>
        public Action<IClientAPI> OnClientLogin;

        /// <summary>
        /// Triggered when a new presence is added to the scene
        /// </summary>
        /// <remarks>
        /// Triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.AddNewAgent"/> which is used by both
        /// <see cref="MutSea.Framework.PresenceType.User">users</see> and <see cref="MutSea.Framework.PresenceType.Npc">NPCs</see>
        /// </remarks>
        public delegate void OnNewPresenceDelegate(ScenePresence presence);
        public event OnNewPresenceDelegate OnNewPresence;

        /// <summary>
        /// Triggered when a presence is removed from the scene
        /// </summary>
        /// <remarks>
        /// Triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.AddNewAgent"/> which is used by both
        /// <see cref="MutSea.Framework.PresenceType.User">users</see> and <see cref="MutSea.Framework.PresenceType.Npc">NPCs</see>
        ///
        /// Triggered under per-agent lock.  So if you want to perform any long-running operations, please
        /// do this on a separate thread.
        /// </remarks>
        public delegate void OnRemovePresenceDelegate(UUID agentId);
        public event OnRemovePresenceDelegate OnRemovePresence;

        /// <summary>
        /// Triggered whenever the prim count may have been altered, or prior
        /// to an action that requires the current prim count to be accurate.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerParcelPrimCountUpdate"/> in
        /// <see cref="MutSea.MutSeaBase.CreateRegion"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandManagementModule.EventManagerOnRequestParcelPrimCountUpdate"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandManagementModule.ClientOnParcelObjectOwnerRequest"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandObject.GetPrimsFree"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandObject.UpdateLandSold"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandObject.DeedToGroup"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandObject.SendLandUpdateToClient"/>
        /// </remarks>
        public delegate void OnParcelPrimCountUpdateDelegate();
        public event OnParcelPrimCountUpdateDelegate OnParcelPrimCountUpdate;

        /// <summary>
        /// Triggered in response to <see cref="OnParcelPrimCountUpdate"/> for
        /// objects that actually contribute to parcel prim count.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerParcelPrimCountAdd"/> in
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandManagementModule.EventManagerOnParcelPrimCountUpdate"/>
        /// </remarks>
        public delegate void OnParcelPrimCountAddDelegate(SceneObjectGroup obj);
        public event OnParcelPrimCountAddDelegate OnParcelPrimCountAdd;

        /// <summary>
        /// Triggered after <see cref="MutSea.IApplicationPlugin.PostInitialise"/>
        /// has been called for all <see cref="MutSea.IApplicationPlugin"/>
        /// loaded via <see cref="MutSea.MutSeaBase.LoadPlugins"/>.
        /// Handlers for this event are typically used to parse the arguments
        /// from <see cref="OnPluginConsoleDelegate"/> in order to process or
        /// filter the arguments and pass them onto <see cref="MutSea.Region.CoreModules.Framework.InterfaceCommander.Commander.ProcessConsoleCommand"/>
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerOnPluginConsole"/> in
        /// <see cref="Scene.SendCommandToPlugins"/> via
        /// <see cref="SceneManager.SendCommandToPluginModules"/> via
        /// <see cref="MutSea.MutSeaBase.HandleCommanderCommand"/> via
        /// <see cref="MutSea.MutSeaBase.AddPluginCommands"/> via
        /// <see cref="MutSea.MutSeaBase.StartupSpecific"/>
        /// </remarks>
        public delegate void OnPluginConsoleDelegate(string[] args);
        public event OnPluginConsoleDelegate OnPluginConsole;

        /// <summary>
        /// Triggered when the entire simulator is shutdown.
        /// </summary>
        public Action OnShutdown;

        /// <summary>
        /// Triggered before the grunt work for adding a root agent to a
        /// scene has been performed (resuming attachment scripts, physics,
        /// animations etc.)
        /// </summary>
        /// <remarks>
        /// Triggered before <see cref="OnMakeRootAgent"/>
        /// by <see cref="TriggerSetRootAgentScene"/>
        /// in <see cref="ScenePresence.MakeRootAgent"/>
        /// via <see cref="Scene.AgentCrossing"/>
        /// and <see cref="ScenePresence.CompleteMovement"/>
        /// </remarks>
        public delegate void OnSetRootAgentSceneDelegate(UUID agentID, Scene scene);
        public event OnSetRootAgentSceneDelegate OnSetRootAgentScene;

        /// <summary>
        /// Triggered after parcel properties have been updated.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerOnParcelPropertiesUpdateRequest"/> in
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandManagementModule.ClientOnParcelPropertiesUpdateRequest"/>,
        /// <see cref="MutSea.Region.CoreModules.World.Land.LandManagementModule.ProcessPropertiesUpdate"/>
        /// </remarks>
        public event ParcelPropertiesUpdateRequest OnParcelPropertiesUpdateRequest;

        /// <summary>
        /// Triggered when an individual scene is shutdown.
        /// </summary>
        /// <remarks>
        /// This does not automatically mean that the entire simulator is shutting down.  Listen to OnShutdown for that
        /// notification.
        /// </remarks>
        public Action<Scene> OnSceneShuttingDown;

        /// <summary>
        /// Fired when an object is touched/grabbed.
        /// </summary>
        /// <remarks>
        /// The originalID is the local ID of the part that was actually touched.  The localID itself is always that of
        /// the root part.
        /// Triggerd in response to <see cref="MutSea.Framework.IClientAPI.OnGrabObject"/>
        /// via <see cref="TriggerObjectGrab"/>
        /// in <see cref="Scene.ProcessObjectGrab"/>
        /// </remarks>
        public delegate void ObjectGrabDelegate(uint localID, uint originalID, Vector3 offsetPos, IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs);
        public event ObjectGrabDelegate OnObjectGrab;

        /// <summary>
        /// Triggered when an object is being touched/grabbed continuously.
        /// </summary>
        /// <remarks>
        /// Triggered in response to <see cref="MutSea.Framework.IClientAPI.OnGrabUpdate"/>
        /// via <see cref="TriggerObjectGrabbing"/>
        /// in <see cref="Scene.ProcessObjectGrabUpdate"/>
        /// </remarks>
        public event ObjectGrabDelegate OnObjectGrabbing;

        /// <summary>
        /// Triggered when an object stops being touched/grabbed.
        /// </summary>
        /// <remarks>
        /// Triggered in response to <see cref="MutSea.Framework.IClientAPI.OnDeGrabObject"/>
        /// via <see cref="TriggerObjectDeGrab"/>
        /// in <see cref="Scene.ProcessObjectDeGrab"/>
        /// </remarks>
        public delegate void ObjectDeGrabDelegate(uint localID, uint originalID, IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs);
        public event ObjectDeGrabDelegate OnObjectDeGrab;

        public delegate void OnPermissionErrorDelegate(UUID user, string reason);
        public event OnPermissionErrorDelegate OnPermissionError;

        /// <summary>
        /// Triggered when a script resets.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptReset"/>
        /// in <see cref="Scene.ProcessScriptReset"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnScriptReset"/>
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.LLClientView.HandleScriptReset"/>
        /// </remarks>
        public delegate void ScriptResetDelegate(uint localID, UUID itemID);
        public event ScriptResetDelegate OnScriptReset;

        /// <summary>
        /// Fired when a script is run.
        /// </summary>
        /// <remarks>
        /// Occurs after OnNewScript.
        /// Triggered by <see cref="TriggerRezScript"/>
        /// in <see cref="SceneObjectPartInventory.CreateScriptInstance"/>
        /// </remarks>
        public delegate void NewRezScript(uint localID, UUID itemID, string script, int startParam, bool postOnRez, string engine, int stateSource);
        public event NewRezScript OnRezScript;

        /// <summary>
        /// Triggered when a script is removed from an object.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerRemoveScript"/>
        /// in <see cref="Scene.RemoveTaskInventory"/>,
        /// <see cref="Scene.CreateAgentInventoryItemFromTask"/>,
        /// <see cref="SceneObjectPartInventory.RemoveScriptInstance"/>,
        /// <see cref="SceneObjectPartInventory.RemoveInventoryItem"/>
        /// </remarks>
        public delegate void RemoveScript(uint localID, UUID itemID);
        public event RemoveScript OnRemoveScript;

        /// <summary>
        /// Triggered when a script starts.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerStartScript"/>
        /// in <see cref="Scene.SetScriptRunning"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnSetScriptRunning"/>,
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.HandleSetScriptRunning"/>
        /// </remarks>
        public delegate void StartScript(uint localID, UUID itemID);
        public event StartScript OnStartScript;

        /// <summary>
        /// Triggered when a script stops.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerStopScript"/>,
        /// in <see cref="SceneObjectPartInventory.CreateScriptInstance"/>,
        /// <see cref="SceneObjectPartInventory.StopScriptInstance"/>,
        /// <see cref="Scene.SetScriptRunning"/>
        /// </remarks>
        public delegate void StopScript(uint localID, UUID itemID);
        public event StopScript OnStopScript;

        /// <summary>
        /// Triggered when an object is moved.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerGroupMove"/>
        /// in <see cref="SceneObjectGroup.UpdateGroupPosition"/>,
        /// <see cref="SceneObjectGroup.GrabMovement"/>
        /// </remarks>
        public delegate bool SceneGroupMoved(UUID groupID, Vector3 delta);
        public event SceneGroupMoved OnSceneGroupMove;

        /// <summary>
        /// Triggered when an object is grabbed.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerGroupGrab"/>
        /// in <see cref="SceneObjectGroup.OnGrabGroup"/>
        /// via <see cref="SceneObjectGroup.ObjectGrabHandler"/>
        /// via <see cref="Scene.ProcessObjectGrab"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnGrabObject"/>
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.LLClientView.HandleObjectGrab"/>
        /// </remarks>
        public delegate void SceneGroupGrabed(UUID groupID, Vector3 offset, UUID userID);
        public event SceneGroupGrabed OnSceneGroupGrab;

        /// <summary>
        /// Triggered when an object starts to spin.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerGroupSpinStart"/>
        /// in <see cref="SceneObjectGroup.SpinStart"/>
        /// via <see cref="SceneGraph.SpinStart"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnSpinStart"/>
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.LLClientView.HandleObjectSpinStart"/>
        /// </remarks>
        public delegate bool SceneGroupSpinStarted(UUID groupID);
        public event SceneGroupSpinStarted OnSceneGroupSpinStart;

        /// <summary>
        /// Triggered when an object is being spun.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerGroupSpin"/>
        /// in <see cref="SceneObjectGroup.SpinMovement"/>
        /// via <see cref="SceneGraph.SpinObject"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnSpinUpdate"/>
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.LLClientView.HandleObjectSpinUpdate"/>
        /// </remarks>
        public delegate bool SceneGroupSpun(UUID groupID, Quaternion rotation);
        public event SceneGroupSpun OnSceneGroupSpin;

        public delegate void LandObjectAdded(ILandObject newParcel);
        public event LandObjectAdded OnLandObjectAdded;

        public delegate void LandObjectRemoved(UUID globalID);
        public event LandObjectRemoved OnLandObjectRemoved;

        public delegate void AvatarEnteringNewParcel(ScenePresence avatar, int localLandID, UUID regionID);
        public event AvatarEnteringNewParcel OnAvatarEnteringNewParcel;

        public delegate void AvatarAppearanceChange(ScenePresence avatar);
        public event AvatarAppearanceChange OnAvatarAppearanceChange;

        public Action<ScenePresence> OnSignificantClientMovement;

        public delegate void IncomingInstantMessage(GridInstantMessage message);
        public event IncomingInstantMessage OnIncomingInstantMessage;
        public event IncomingInstantMessage OnUnhandledInstantMessage;

        public delegate void CrossAgentToNewRegion(ScenePresence sp, bool isFlying, GridRegion newRegion);
        public event CrossAgentToNewRegion OnCrossAgentToNewRegion;

        /// <summary>
        /// Fired when a client is removed from a scene whether it's a child or a root agent.
        /// </summary>
        /// <remarks>
        /// At the point of firing, the scene still contains the client's scene presence.
        ///
        /// This is triggered under per-agent lock.  So if you want to perform any long-running operations, please
        /// do this on a separate thread.
        /// </remarks>
        public delegate void ClientClosed(UUID clientID, Scene scene);
        public event ClientClosed OnClientClosed;

        /// <summary>
        /// Fired when a script is created.
        /// </summary>
        /// <remarks>
        /// Occurs before OnRezScript
        /// Triggered by <see cref="TriggerNewScript"/>
        /// in <see cref="Scene.RezScriptFromAgentInventory"/>,
        /// <see cref="Scene.RezNewScript"/>
        /// </remarks>
        public delegate void NewScript(UUID clientID, SceneObjectPart part, UUID itemID);
        public event NewScript OnNewScript;
        public virtual void TriggerNewScript(UUID clientID, SceneObjectPart part, UUID itemID)
        {
            NewScript handlerNewScript = OnNewScript;
            if (handlerNewScript != null)
            {
                foreach (NewScript d in handlerNewScript.GetInvocationList())
                {
                    try
                    {
                        d(clientID, part, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerNewScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        /// <summary>
        /// An indication that the script has changed.
        /// </summary>
        /// <remarks>
        /// Triggered after the scene receives a client's upload of an updated script and has stored it in an asset.
        /// Triggered by <see cref="TriggerUpdateScript"/>
        /// in <see cref="Scene.CapsUpdateTaskInventoryScriptAsset"/>
        /// via <see cref="Scene.CapsUpdateTaskInventoryScriptAsset"/>
        /// via <see cref="MutSea.Region.ClientStack.Linden.BunchOfCaps.TaskScriptUpdated"/>
        /// via <see cref="MutSea.Region.ClientStack.Linden.TaskInventoryScriptUpdater.OnUpLoad"/>
        /// via <see cref="MutSea.Region.ClientStack.Linden.TaskInventoryScriptUpdater.uploaderCaps"/>
        /// </remarks>
        public delegate void UpdateScript(UUID clientID, UUID itemId, UUID primId, bool isScriptRunning, UUID newAssetID);
        public event UpdateScript OnUpdateScript;
        public virtual void TriggerUpdateScript(UUID clientId, UUID itemId, UUID primId, bool isScriptRunning, UUID newAssetID)
        {
            UpdateScript handlerUpdateScript = OnUpdateScript;
            if (handlerUpdateScript != null)
            {
                foreach (UpdateScript d in handlerUpdateScript.GetInvocationList())
                {
                    try
                    {
                        d(clientId, itemId, primId, isScriptRunning, newAssetID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerUpdateScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        /// <summary>
        /// Triggered when some scene object properties change.
        /// </summary>
        /// <remarks>
        /// ScriptChangedEvent is fired when a scene object property that a script might be interested
        /// in (such as color, scale or inventory) changes.  Only enough information sent is for the LSL changed event.
        /// This is not an indication that the script has changed (see OnUpdateScript for that).
        /// This event is sent to a script to tell it that some property changed on
        /// the object the script is in. See http://lslwiki.net/lslwiki/wakka.php?wakka=changed .
        /// Triggered by <see cref="TriggerOnScriptChangedEvent"/>
        /// in <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.TeleportAgentWithinRegion"/>,
        /// <see cref="SceneObjectPart.TriggerScriptChangedEvent"/>
        /// </remarks>
        public delegate void ScriptChangedEvent(uint localID, uint change, object data);
        public event ScriptChangedEvent OnScriptChangedEvent;


        /// <summary>
        /// Triggered when a script receives control input from an agent.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerControlEvent"/>
        /// in <see cref="ScenePresence.SendControlsToScripts"/>
        /// via <see cref="ScenePresence.HandleAgentUpdate"/>
        /// via <see cref="MutSea.Framework.IClientAPI.OnAgentUpdate"/>
        /// via <see cref="MutSea.Region.ClientStack.LindenUDP.LLClientView.HandleAgentUpdate"/>
        /// </remarks>
        public delegate void ScriptControlEvent(UUID item, UUID avatarID, uint held, uint changed);
        public event ScriptControlEvent OnScriptControlEvent;

        /// <summary>
        /// TODO: Should be triggered when a physics object starts moving.
        /// </summary>
        public delegate void ScriptMovingStartEvent(uint localID);
        public event ScriptMovingStartEvent OnScriptMovingStartEvent;

        /// <summary>
        /// TODO: Should be triggered when a physics object stops moving.
        /// </summary>
        public delegate void ScriptMovingEndEvent(uint localID);
        public event ScriptMovingEndEvent OnScriptMovingEndEvent;

        /// <summary>
        /// Triggered when an object has arrived within a tolerance distance
        /// of a motion target.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerAtTargetEvent"/>
        /// in <see cref="SceneObjectGroup.CheckAtTargets"/>
        /// via <see cref="SceneObjectGroup.ScheduleGroupForFullUpdate"/>,
        /// <see cref="Scene.CheckAtTargets"/> via <see cref="Scene.Update"/>
        /// </remarks>
        public delegate void ScriptAtTargetEvent(UUID scriptID, uint handle, Vector3 targetpos, Vector3 atpos);
        public event ScriptAtTargetEvent OnScriptAtTargetEvent;

        /// <summary>
        /// Triggered when an object has a motion target but has not arrived
        /// within a tolerance distance.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerNotAtTargetEvent"/>
        /// in <see cref="SceneObjectGroup.CheckAtTargets"/>
        /// via <see cref="SceneObjectGroup.ScheduleGroupForFullUpdate"/>,
        /// <see cref="Scene.CheckAtTargets"/> via <see cref="Scene.Update"/>
        /// </remarks>
        public delegate void ScriptNotAtTargetEvent(UUID scriptID);
        public event ScriptNotAtTargetEvent OnScriptNotAtTargetEvent;

        /// <summary>
        /// Triggered when an object has arrived within a tolerance rotation
        /// of a rotation target.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerAtRotTargetEvent"/>
        /// in <see cref="SceneObjectGroup.CheckAtTargets"/>
        /// via <see cref="SceneObjectGroup.ScheduleGroupForFullUpdate"/>,
        /// <see cref="Scene.CheckAtTargets"/> via <see cref="Scene.Update"/>
        /// </remarks>
        public delegate void ScriptAtRotTargetEvent(UUID scriptID, uint handle, Quaternion targetrot, Quaternion atrot);
        public event ScriptAtRotTargetEvent OnScriptAtRotTargetEvent;

        /// <summary>
        /// Triggered when an object has a rotation target but has not arrived
        /// within a tolerance rotation.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerNotAtRotTargetEvent"/>
        /// in <see cref="SceneObjectGroup.CheckAtTargets"/>
        /// via <see cref="SceneObjectGroup.ScheduleGroupForFullUpdate"/>,
        /// <see cref="Scene.CheckAtTargets"/> via <see cref="Scene.Update"/>
        /// </remarks>
        public delegate void ScriptNotAtRotTargetEvent(UUID scriptID);
        public event ScriptNotAtRotTargetEvent OnScriptNotAtRotTargetEvent;


        public delegate void ScriptColliding(uint localID, ColliderArgs colliders);

        /// <summary>
        /// Triggered when a physical collision has started between a prim
        /// and something other than the region terrain.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptCollidingStart"/>
        /// in <see cref="SceneObjectPart.SendCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptColliderStart;

        /// <summary>
        /// Triggered when something that previously collided with a prim has
        /// not stopped colliding with it.
        /// </summary>
        /// <remarks>
        /// <seealso cref="OnScriptColliderStart"/>
        /// Triggered by <see cref="TriggerScriptColliding"/>
        /// in <see cref="SceneObjectPart.SendCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptColliding;

        /// <summary>
        /// Triggered when something that previously collided with a prim has
        /// stopped colliding with it.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptCollidingEnd"/>
        /// in <see cref="SceneObjectPart.SendCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptCollidingEnd;

        /// <summary>
        /// Triggered when a physical collision has started between an object
        /// and the region terrain.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptLandCollidingStart"/>
        /// in <see cref="SceneObjectPart.SendLandCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptLandColliderStart;

        /// <summary>
        /// Triggered when an object that previously collided with the region
        /// terrain has not yet stopped colliding with it.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptLandColliding"/>
        /// in <see cref="SceneObjectPart.SendLandCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptLandColliding;

        /// <summary>
        /// Triggered when an object that previously collided with the region
        /// terrain has stopped colliding with it.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerScriptLandCollidingEnd"/>
        /// in <see cref="SceneObjectPart.SendLandCollisionEvent"/>
        /// via <see cref="SceneObjectPart.PhysicsCollision"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.OnCollisionUpdate"/>
        /// via <see cref="MutSea.Region.PhysicsModule.SharedBase.PhysicsActor.SendCollisionUpdate"/>
        /// </remarks>
        public event ScriptColliding OnScriptLandColliderEnd;

        /// <summary>
        /// Triggered when an agent has been made a child agent of a scene.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerOnMakeChildAgent"/>
        /// in <see cref="ScenePresence.MakeChildAgent"/>
        /// via <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.CrossAgentToNewRegionAsync"/>,
        /// <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.DoTeleport"/>,
        /// <see cref="MutSea.Region.CoreModules.InterGrid.KillAUser.ShutdownNoLogout"/>
        /// </remarks>
        public delegate void OnMakeChildAgentDelegate(ScenePresence presence);
        public event OnMakeChildAgentDelegate OnMakeChildAgent;

        /// <summary>
        /// Triggered after the grunt work for adding a root agent to a
        /// scene has been performed (resuming attachment scripts, physics,
        /// animations etc.)
        /// </summary>
        /// <remarks>
        /// This event is on the critical path for transferring an avatar from one region to another.  Try and do
        /// as little work on this event as possible, or do work asynchronously.
        /// Triggered after <see cref="OnSetRootAgentScene"/>
        /// by <see cref="TriggerOnMakeRootAgent"/>
        /// in <see cref="ScenePresence.MakeRootAgent"/>
        /// via <see cref="Scene.AgentCrossing"/>
        /// and <see cref="ScenePresence.CompleteMovement"/>
        /// </remarks>
        public Action<ScenePresence> OnMakeRootAgent;

        /// <summary>
        /// Triggered when an object or attachment enters a scene
        /// </summary>
        public delegate void OnIncomingSceneObjectDelegate(SceneObjectGroup so);
        public event OnIncomingSceneObjectDelegate OnIncomingSceneObject;

        public delegate void NewInventoryItemUploadComplete(InventoryItemBase item, int userlevel);
        public event NewInventoryItemUploadComplete OnNewInventoryItemUploadComplete;

        public delegate void RequestChangeWaterHeight(float height);
        public event RequestChangeWaterHeight OnRequestChangeWaterHeight;

        /// <summary>
        /// Fired if any avatar is 'killed' due to its health falling to zero
        /// </summary>
        public delegate void AvatarKillData(uint KillerLocalID, ScenePresence avatar);
        public event AvatarKillData OnAvatarKilled;

        /*
        public delegate void ScriptTimerEvent(uint localID, double timerinterval);
        /// <summary>
        /// Used to be triggered when the LSL timer event fires.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerTimerEvent"/>
        /// via <see cref="SceneObjectPart.handleTimerAccounting"/>
        /// </remarks>
        public ScriptTimerEvent OnScriptTimerEvent;
         */

        public delegate void EstateToolsSunUpdate(ulong regionHandle);
        public event EstateToolsSunUpdate OnEstateToolsSunUpdate;

        public delegate void GetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID);

        /// <summary>
        /// Triggered when an object is added to the scene.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerObjectAddedToScene"/>
        /// in <see cref="Scene.AddNewSceneObject"/>,
        /// <see cref="Scene.DuplicateObject"/>,
        /// <see cref="Scene.doObjectDuplicateOnRay"/>
        /// </remarks>
        public Action<SceneObjectGroup> OnObjectAddedToScene;

        /// <summary>
        /// Triggered when a client sends a derez request for an object inworld
        ///  but before the object is deleted
        /// </summary>
        /// <param name="remoteClient">The client question (it can be null)</param>
        /// <param name="obj">The object in question</param>
        /// <param name="action">The exact derez action</param>
        /// <returns>Flag indicating whether the object should be deleted from the scene or not</returns>
        public event DeRezRequested OnDeRezRequested;
        public delegate bool DeRezRequested(IClientAPI remoteClient, List<SceneObjectGroup> objs, DeRezAction action);

        /// <summary>
        /// Triggered when an object is removed from the scene.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerObjectBeingRemovedFromScene"/>
        /// in <see cref="Scene.DeleteSceneObject"/>
        /// </remarks>
        public delegate void ObjectBeingRemovedFromScene(SceneObjectGroup obj);
        public event ObjectBeingRemovedFromScene OnObjectBeingRemovedFromScene;

        /// <summary>
        /// Triggered when an object is placed into the physical scene (PhysicsActor created).
        /// </summary>
        public Action<SceneObjectPart> OnObjectAddedToPhysicalScene;
        /// <summary>
        /// Triggered when an object is removed from the physical scene (PhysicsActor destroyed).
        /// </summary>
        /// <remarks>
        /// Note: this is triggered just before the PhysicsActor is removed from the
        /// physics engine so the receiver can do any necessary cleanup before its destruction.
        /// </remarks>
        public Action<SceneObjectPart> OnObjectRemovedFromPhysicalScene;

        public delegate void NoticeNoLandDataFromStorage();
        public event NoticeNoLandDataFromStorage OnNoticeNoLandDataFromStorage;

        public delegate void IncomingLandDataFromStorage(List<LandData> data);
        public event IncomingLandDataFromStorage OnIncomingLandDataFromStorage;

        public delegate void SetAllowForcefulBan(bool allow);
        public event SetAllowForcefulBan OnSetAllowForcefulBan;

        public delegate void RequestParcelPrimCountUpdate();
        public event RequestParcelPrimCountUpdate OnRequestParcelPrimCountUpdate;

        /// <summary>
        /// Triggered when the parcel prim count has been altered.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerParcelPrimCountTainted"/> in
        /// <see cref="MutSea.Region.CoreModules.Avatar.Attachments.AttachmentsModule.DetachSingleAttachmentToGround"/>,
        /// <see cref="MutSea.Region.CoreModules.Avatar.Attachments.AttachmentsModule.AttachToAgent"/>,
        /// <see cref="Scene.DeleteSceneObject"/>,
        /// <see cref="Scene.SelectPrim"/>,
        /// <see cref="Scene.DeselectPrim"/>,
        /// <see cref="SceneObjectGroup.UpdateFlags"/>,
        /// <see cref="SceneObjectGroup.AbsolutePosition"/>
        /// </remarks>
        public delegate void ParcelPrimCountTainted();
        public event ParcelPrimCountTainted OnParcelPrimCountTainted;

        public event GetScriptRunning OnGetScriptRunning;

        public delegate void ThrottleUpdate(ScenePresence scenePresence);
        public event ThrottleUpdate OnThrottleUpdate;

        /// <summary>
        /// RegisterCapsEvent is called by Scene after the Caps object
        /// has been instantiated and before it is return to the
        /// client and provides region modules to add their caps.
        /// </summary>
        public delegate void RegisterCapsEvent(UUID agentID, Caps caps);
        public event RegisterCapsEvent OnRegisterCaps;

        /// <summary>
        /// DeregisterCapsEvent is called by Scene when the caps
        /// handler for an agent are removed.
        /// </summary>
        public delegate void DeregisterCapsEvent(UUID agentID, Caps caps);
        public event DeregisterCapsEvent OnDeregisterCaps;

        /// <summary>
        /// ChatFromWorldEvent is called via Scene when a chat message
        /// from world comes in.
        /// </summary>
        public delegate void ChatFromWorldEvent(Object sender, OSChatMessage chat);
        public event ChatFromWorldEvent OnChatFromWorld;

        /// <summary>
        /// ChatFromClientEvent is triggered via ChatModule (or
        /// substitutes thereof) when a chat message
        /// from the client  comes in.
        /// </summary>
        public delegate void ChatFromClientEvent(Object sender, OSChatMessage chat);
        public event ChatFromClientEvent OnChatFromClient;

        /// <summary>
        /// ChatBroadcastEvent is called via Scene when a broadcast chat message
        /// from world comes in
        /// </summary>
        public delegate void ChatBroadcastEvent(Object sender, OSChatMessage chat);
        public event ChatBroadcastEvent OnChatBroadcast;

        public delegate float SunLindenHour();
        public event SunLindenHour OnGetCurrentTimeAsLindenSunHour;

        /// <summary>
        /// Called when oar file has finished loading, although
        /// the scripts may not have started yet
        /// Message is non empty string if there were problems loading the oar file
        /// </summary>
        public delegate void OarFileLoaded(Guid guid, List<UUID> loadedScenes, string message);
        public event OarFileLoaded OnOarFileLoaded;

        /// <summary>
        /// Called when an oar file has finished saving
        /// Message is non empty string if there were problems saving the oar file
        /// If a guid was supplied on the original call to identify, the request, this is returned.  Otherwise
        /// Guid.Empty is returned.
        /// </summary>
        public delegate void OarFileSaved(Guid guid, string message);
        public event OarFileSaved OnOarFileSaved;

        /// <summary>
        /// Called when the script compile queue becomes empty
        /// Returns the number of scripts which failed to start
        /// </summary>
        public delegate void EmptyScriptCompileQueue(int numScriptsFailed, string message);
        public event EmptyScriptCompileQueue OnEmptyScriptCompileQueue;

        /// <summary>
        /// Called whenever an object is attached, or detached from an in-world presence.
        /// </summary>
        /// If the object is being attached, then the avatarID will be present.  If the object is being detached then
        /// the avatarID is UUID.Zero (I know, this doesn't make much sense but now it's historical).
        public delegate void Attach(uint localID, UUID itemID, UUID avatarID);
        public event Attach OnAttach;

        /// <summary>
        /// Called immediately after an object is loaded from storage.
        /// </summary>
        public delegate void SceneObjectDelegate(SceneObjectGroup so);
        public event SceneObjectDelegate OnSceneObjectLoaded;

        /// <summary>
        /// Called immediately before an object is saved to storage.
        /// </summary>
        /// <param name="persistingSo">
        /// The scene object being persisted.
        /// This is actually a copy of the original scene object so changes made here will be saved to storage but will not be kept in memory.
        /// </param>
        /// <param name="originalSo">
        /// The original scene object being persisted.  Changes here will stay in memory but will not be saved to storage on this save.
        /// </param>
        public delegate void SceneObjectPreSaveDelegate(SceneObjectGroup persistingSo, SceneObjectGroup originalSo);
        public event SceneObjectPreSaveDelegate OnSceneObjectPreSave;

        /// <summary>
        /// Called when a scene object part is cloned within the region.
        /// </summary>
        /// <param name="copy"></param>
        /// <param name="original"></param>
        /// <param name="userExposed">True if the duplicate will immediately be in the scene, false otherwise</param>
        /// <remarks>
        /// Triggered in <see cref="MutSea.Region.Framework.Scenes.SceneObjectPart.Copy"/>
        /// </remarks>
        public delegate void SceneObjectPartCopyDelegate(SceneObjectPart copy, SceneObjectPart original, bool userExposed);
        public event SceneObjectPartCopyDelegate OnSceneObjectPartCopy;

        public delegate void SceneObjectPartUpdated(SceneObjectPart sop, bool full);
        public event SceneObjectPartUpdated OnSceneObjectPartUpdated;

        public delegate void ScenePresenceUpdated(ScenePresence sp);
        public event ScenePresenceUpdated OnScenePresenceUpdated;

        public delegate void RegionUp(GridRegion region);
        public event RegionUp OnRegionUp;

        public delegate void RegionStarted(Scene scene);
        public event RegionStarted OnRegionStarted;

        public delegate void RegionHeartbeatStart(Scene scene);
        public event RegionHeartbeatStart OnRegionHeartbeatStart;

        public delegate void RegionHeartbeatEnd(Scene scene);
        public event RegionHeartbeatEnd OnRegionHeartbeatEnd;

        /// <summary>
        /// Fired when logins to a region are enabled or disabled.
        /// </summary>
        /// <remarks>
        ///
        /// </remarks>
        /// Fired
        public delegate void RegionLoginsStatusChange(IScene scene);
        public event RegionLoginsStatusChange OnRegionLoginsStatusChange;

        /// <summary>
        /// Fired when a region is considered ready for use.
        /// </summary>
        /// <remarks>
        /// A region is considered ready when startup operations such as loading of scripts already on the region
        /// have been completed.
        /// </remarks>
        public Action<IScene> OnRegionReadyStatusChange;

        public delegate void PrimsLoaded(Scene s);
        public event PrimsLoaded OnPrimsLoaded;

        /// <summary>
        /// Triggered when a teleport starts
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerTeleportStart"/>
        /// in <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.CreateAgent"/>
        /// and <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.HGEntityTransferModule.CreateAgent"/>
        /// via <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.DoTeleport"/>
        /// </remarks>
        public delegate void TeleportStart(IClientAPI client, GridRegion destination, GridRegion finalDestination, uint teleportFlags, bool gridLogout);
        public event TeleportStart OnTeleportStart;

        /// <summary>
        /// Trigered when a teleport fails.
        /// </summary>
        /// <remarks>
        /// Triggered by <see cref="TriggerTeleportFail"/>
        /// in <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.Fail"/>
        /// via <see cref="MutSea.Region.CoreModules.Framework.EntityTransfer.EntityTransferModule.DoTeleport"/>
        /// </remarks>
        public delegate void TeleportFail(IClientAPI client, bool gridLogout);
        public event TeleportFail OnTeleportFail;

        public class MoneyTransferArgs : EventArgs
        {
            public UUID sender;
            public UUID receiver;
            public bool authenticated = false; // Always false
            public int amount;
            public int transactiontype;
            public string description;

            public MoneyTransferArgs(UUID asender, UUID areceiver, int aamount, int atransactiontype, string adescription)
            {
                sender = asender;
                receiver = areceiver;
                amount = aamount;
                transactiontype = atransactiontype;
                description = adescription;
            }
        }


        /// <summary>
        /// Triggered when an attempt to transfer grid currency occurs
        /// </summary>
        /// <remarks>
        /// Triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.ProcessMoneyTransferRequest"/>
        /// via <see cref="MutSea.Region.Framework.Scenes.Scene.SubscribeToClientGridEvents"/>
        /// via <see cref="MutSea.Region.Framework.Scenes.Scene.SubscribeToClientEvents"/>
        /// via <see cref="MutSea.Region.Framework.Scenes.Scene.AddNewAgent"/>
        /// </remarks>
        public delegate void MoneyTransferEvent(Object sender, MoneyTransferArgs e);
        public event MoneyTransferEvent OnMoneyTransfer;

        public class LandBuyArgs : EventArgs
        {
            public UUID agentId = UUID.Zero;
            public UUID groupId = UUID.Zero;
            public UUID parcelOwnerID = UUID.Zero;
            public bool final = false;
            public bool groupOwned = false;
            public bool removeContribution = false;
            public int parcelLocalID = 0;
            public int parcelArea = 0;
            public int parcelPrice = 0;
            public bool authenticated = false;
            public bool landValidated = false;
            public bool economyValidated = false;
            public int transactionID = 0;
            public int amountDebited = 0;

            public LandBuyArgs(UUID pagentId, UUID pgroupId, bool pfinal, bool pgroupOwned,
                bool premoveContribution, int pparcelLocalID, int pparcelArea, int pparcelPrice,
                bool pauthenticated)
            {
                agentId = pagentId;
                groupId = pgroupId;
                final = pfinal;
                groupOwned = pgroupOwned;
                removeContribution = premoveContribution;
                parcelLocalID = pparcelLocalID;
                parcelArea = pparcelArea;
                parcelPrice = pparcelPrice;
                authenticated = pauthenticated;
            }
        }

        /// <summary>
        /// Triggered after after <see cref="OnValidateLandBuy"/>
        /// </summary>
        public delegate void LandBuy(Object sender, LandBuyArgs e);
        public event LandBuy OnLandBuy;

        /// <summary>
        /// Triggered to allow or prevent a real estate transaction
        /// </summary>
        /// <remarks>
        /// Triggered in <see cref="MutSea.Region.Framework.Scenes.Scene.ProcessParcelBuy"/>
        /// <seealso cref="MutSea.Region.OptionalModules.World.MoneyModule.SampleMoneyModule.ValidateLandBuy"/>
        /// </remarks>
        public event LandBuy OnValidateLandBuy;

        public void TriggerOnAttach(uint localID, UUID itemID, UUID avatarID)
        {
            Attach handlerOnAttach = OnAttach;
            if (handlerOnAttach != null)
            {
                foreach (Attach d in handlerOnAttach.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID, avatarID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnAttach failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerGetScriptRunning(IClientAPI controllingClient, UUID objectID, UUID itemID)
        {
            GetScriptRunning handlerGetScriptRunning = OnGetScriptRunning;
            if (handlerGetScriptRunning != null)
            {
                foreach (GetScriptRunning d in handlerGetScriptRunning.GetInvocationList())
                {
                    try
                    {
                        d(controllingClient, objectID, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerGetScriptRunning failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnScriptChangedEvent(uint localID, uint change, object parameter = null)
        {
            ScriptChangedEvent handlerScriptChangedEvent = OnScriptChangedEvent;
            if (handlerScriptChangedEvent != null)
            {
                foreach (ScriptChangedEvent d in handlerScriptChangedEvent.GetInvocationList())
                {
                    try
                    {
                        d(localID, change, parameter);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnScriptChangedEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnClientMovement(ScenePresence avatar)
        {
            ClientMovement handlerClientMovement = OnClientMovement;
            if (handlerClientMovement != null)
            {
                foreach (ClientMovement d in handlerClientMovement.GetInvocationList())
                {
                    try
                    {
                        d(avatar);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnClientMovement failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerPermissionError(UUID user, string reason)
        {
            OnPermissionErrorDelegate handlerPermissionError = OnPermissionError;
            if (handlerPermissionError != null)
            {
                foreach (OnPermissionErrorDelegate d in handlerPermissionError.GetInvocationList())
                {
                    try
                    {
                        d(user, reason);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerPermissionError failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnPluginConsole(string[] args)
        {
            OnPluginConsoleDelegate handlerPluginConsole = OnPluginConsole;
            if (handlerPluginConsole != null)
            {
                foreach (OnPluginConsoleDelegate d in handlerPluginConsole.GetInvocationList())
                {
                    try
                    {
                        d(args);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnPluginConsole failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnFrame()
        {
            OnFrameDelegate handlerFrame = OnFrame;
            if (handlerFrame != null)
            {
                foreach (OnFrameDelegate d in handlerFrame.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnFrame failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnNewClient(IClientAPI client)
        {
            OnNewClientDelegate handlerNewClient = OnNewClient;
            if (handlerNewClient != null)
            {
                foreach (OnNewClientDelegate d in handlerNewClient.GetInvocationList())
                {
                    try
                    {
                        d(client);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnNewClient failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            // to be removed
            if (client is IClientCore)
            {
                OnClientConnectCoreDelegate handlerClientConnect = OnClientConnect;
                if (handlerClientConnect != null)
                {
                    foreach (OnClientConnectCoreDelegate d in handlerClientConnect.GetInvocationList())
                    {
                        try
                        {
                            d((IClientCore)client);
                        }
                        catch (Exception e)
                        {
                            m_log.ErrorFormat(
                                "[EVENT MANAGER]: Delegate for TriggerOnNewClient (IClientCore) failed - continuing.  {0} {1}",
                                e.Message, e.StackTrace);
                        }
                    }
                }
            }
        }

        public void TriggerOnClientLogin(IClientAPI client)
        {
            Action<IClientAPI> handlerClientLogin = OnClientLogin;
            if (handlerClientLogin != null)
            {
                foreach (Action<IClientAPI> d in handlerClientLogin.GetInvocationList())
                {
                    try
                    {
                        d(client);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnClientLogin failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

        }

        public void TriggerOnNewPresence(ScenePresence presence)
        {
            OnNewPresenceDelegate handlerNewPresence = OnNewPresence;
            if (handlerNewPresence != null)
            {
                foreach (OnNewPresenceDelegate d in handlerNewPresence.GetInvocationList())
                {
                    try
                    {
                        d(presence);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnNewPresence failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnRemovePresence(UUID agentId)
        {
            OnRemovePresenceDelegate handlerRemovePresence = OnRemovePresence;
            if (handlerRemovePresence != null)
            {
                foreach (OnRemovePresenceDelegate d in handlerRemovePresence.GetInvocationList())
                {
                    try
                    {
//                        m_log.ErrorFormat("[EVENT MANAGER]: OnRemovePresenceDelegate: {0}",d.Target.ToString());
                        d(agentId);
//                        m_log.ErrorFormat("[EVENT MANAGER]: OnRemovePresenceDelegate done ");
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnRemovePresence failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnBackup(ISimulationDataService dstore, bool forced)
        {
            OnBackupDelegate handlerOnAttach = OnBackup;
            if (handlerOnAttach != null)
            {
                foreach (OnBackupDelegate d in handlerOnAttach.GetInvocationList())
                {
                    try
                    {
                        d(dstore, forced);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnBackup failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerParcelPrimCountUpdate()
        {
            OnParcelPrimCountUpdateDelegate handlerParcelPrimCountUpdate = OnParcelPrimCountUpdate;
            if (handlerParcelPrimCountUpdate != null)
            {
                foreach (OnParcelPrimCountUpdateDelegate d in handlerParcelPrimCountUpdate.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerParcelPrimCountUpdate failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerMoneyTransfer(Object sender, MoneyTransferArgs args)
        {
            MoneyTransferEvent handlerMoneyTransfer = OnMoneyTransfer;
            if (handlerMoneyTransfer != null)
            {
                foreach (MoneyTransferEvent d in handlerMoneyTransfer.GetInvocationList())
                {
                    try
                    {
                        d(sender, args);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerMoneyTransfer failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }
        public void TriggerTerrainUpdate()
        {
            OnTerrainUpdateDelegate handlerTerrainUpdate = OnTerrainUpdate;
            if (handlerTerrainUpdate != null)
            {
                foreach (OnTerrainUpdateDelegate d in handlerTerrainUpdate.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerTerrainUpdate failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerTerrainTick()
        {
            OnTerrainTickDelegate handlerTerrainTick = OnTerrainTick;
            if (handlerTerrainTick != null)
            {
                foreach (OnTerrainTickDelegate d in handlerTerrainTick.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerTerrainTick failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerTerrainCheckUpdates()
        {
            OnTerrainCheckUpdatesDelegate TerrainCheckUpdates = OnTerrainCheckUpdates;
            if (TerrainCheckUpdates != null)
            {
                foreach (OnTerrainCheckUpdatesDelegate d in TerrainCheckUpdates.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TerrainCheckUpdates failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerTerrainTainted()
        {
            OnTerrainTaintedDelegate handlerTerrainTainted = OnTerrainTainted;
            if (handlerTerrainTainted != null)
            {
                foreach (OnTerrainTaintedDelegate d in handlerTerrainTainted.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerTerrainTainted failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerParcelPrimCountAdd(SceneObjectGroup obj)
        {
            OnParcelPrimCountAddDelegate handlerParcelPrimCountAdd = OnParcelPrimCountAdd;
            if (handlerParcelPrimCountAdd != null)
            {
                foreach (OnParcelPrimCountAddDelegate d in handlerParcelPrimCountAdd.GetInvocationList())
                {
                    try
                    {
                        d(obj);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerParcelPrimCountAdd failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerObjectAddedToScene(SceneObjectGroup obj)
        {
            Action<SceneObjectGroup> handler = OnObjectAddedToScene;
            if (handler != null)
            {
                foreach (Action<SceneObjectGroup> d in handler.GetInvocationList())
                {
                    try
                    {
                        d(obj);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectAddedToScene failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public bool TriggerDeRezRequested(IClientAPI client, List<SceneObjectGroup> objs, DeRezAction action)
        {
            bool canDeRez = true;

            DeRezRequested handlerDeRezRequested = OnDeRezRequested;
            if (handlerDeRezRequested != null)
            {
                foreach (DeRezRequested d in handlerDeRezRequested.GetInvocationList())
                {
                    try
                    {
                        canDeRez &= d(client, objs, action);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerDeRezRequested failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            return canDeRez;
        }

        public void TriggerObjectBeingRemovedFromScene(SceneObjectGroup obj)
        {
            ObjectBeingRemovedFromScene handlerObjectBeingRemovedFromScene = OnObjectBeingRemovedFromScene;
            if (handlerObjectBeingRemovedFromScene != null)
            {
                foreach (ObjectBeingRemovedFromScene d in handlerObjectBeingRemovedFromScene.GetInvocationList())
                {
                    try
                    {
                        d(obj);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectBeingRemovedFromScene failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerObjectAddedToPhysicalScene(SceneObjectPart obj)
        {
            Action<SceneObjectPart> handler = OnObjectAddedToPhysicalScene;
            if (handler != null)
            {
                foreach (Action<SceneObjectPart> d in handler.GetInvocationList())
                {
                    try
                    {
                        d(obj);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectAddedToPhysicalScene failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerObjectRemovedFromPhysicalScene(SceneObjectPart obj)
        {
            Action<SceneObjectPart> handler = OnObjectRemovedFromPhysicalScene;
            if (handler != null)
            {
                foreach (Action<SceneObjectPart> d in handler.GetInvocationList())
                {
                    try
                    {
                        d(obj);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectRemovedFromPhysicalScene failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerShutdown()
        {
            Action handlerShutdown = OnShutdown;
            if (handlerShutdown != null)
            {
                foreach (Action d in handlerShutdown.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerShutdown failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerObjectGrab(uint localID, uint originalID, Vector3 offsetPos, IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            ObjectGrabDelegate handlerObjectGrab = OnObjectGrab;
            if (handlerObjectGrab != null)
            {
                foreach (ObjectGrabDelegate d in handlerObjectGrab.GetInvocationList())
                {
                    try
                    {
                        d(localID, originalID, offsetPos, remoteClient, surfaceArgs);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectGrab failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerObjectGrabbing(uint localID, uint originalID, Vector3 offsetPos, IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            ObjectGrabDelegate handlerObjectGrabbing = OnObjectGrabbing;
            if (handlerObjectGrabbing != null)
            {
                foreach (ObjectGrabDelegate d in handlerObjectGrabbing.GetInvocationList())
                {
                    try
                    {
                        d(localID, originalID, offsetPos, remoteClient, surfaceArgs);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectGrabbing failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
         }

        public void TriggerObjectDeGrab(uint localID, uint originalID, IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            ObjectDeGrabDelegate handlerObjectDeGrab = OnObjectDeGrab;
            if (handlerObjectDeGrab != null)
            {
                foreach (ObjectDeGrabDelegate d in handlerObjectDeGrab.GetInvocationList())
                {
                    try
                    {
                        d(localID, originalID, remoteClient, surfaceArgs);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerObjectDeGrab failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptReset(uint localID, UUID itemID)
        {
            ScriptResetDelegate handlerScriptReset = OnScriptReset;
            if (handlerScriptReset != null)
            {
                foreach (ScriptResetDelegate d in handlerScriptReset.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptReset failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRezScript(uint localID, UUID itemID, string script, int startParam, bool postOnRez, string engine, int stateSource)
        {
            NewRezScript handlerRezScript = OnRezScript;
            if (handlerRezScript != null)
            {
                foreach (NewRezScript d in handlerRezScript.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID, script, startParam, postOnRez, engine, stateSource);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerRezScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerStartScript(uint localID, UUID itemID)
        {
            StartScript handlerStartScript = OnStartScript;
            if (handlerStartScript != null)
            {
                foreach (StartScript d in handlerStartScript.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerStartScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerStopScript(uint localID, UUID itemID)
        {
            StopScript handlerStopScript = OnStopScript;
            if (handlerStopScript != null)
            {
                foreach (StopScript d in handlerStopScript.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerStopScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRemoveScript(uint localID, UUID itemID)
        {
            RemoveScript handlerRemoveScript = OnRemoveScript;
            if (handlerRemoveScript != null)
            {
                foreach (RemoveScript d in handlerRemoveScript.GetInvocationList())
                {
                    try
                    {
                        d(localID, itemID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerRemoveScript failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                        m_log.ErrorFormat(Environment.StackTrace);
                    }
                }
            }
        }

        public bool TriggerGroupMove(UUID groupID, Vector3 delta)
        {
            bool result = true;

            SceneGroupMoved handlerSceneGroupMove = OnSceneGroupMove;
            if (handlerSceneGroupMove != null)
            {
                foreach (SceneGroupMoved d in handlerSceneGroupMove.GetInvocationList())
                {
                    try
                    {
                        if (d(groupID, delta) == false)
                            result = false;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerGroupMove failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            return result;
        }

        public bool TriggerGroupSpinStart(UUID groupID)
        {
            bool result = true;

            SceneGroupSpinStarted handlerSceneGroupSpinStarted = OnSceneGroupSpinStart;
            if (handlerSceneGroupSpinStarted != null)
            {
                foreach (SceneGroupSpinStarted d in handlerSceneGroupSpinStarted.GetInvocationList())
                {
                    try
                    {
                        if (d(groupID) == false)
                            result = false;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerGroupSpinStart failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            return result;
        }

        public bool TriggerGroupSpin(UUID groupID, Quaternion rotation)
        {
            bool result = true;

            SceneGroupSpun handlerSceneGroupSpin = OnSceneGroupSpin;
            if (handlerSceneGroupSpin != null)
            {
                foreach (SceneGroupSpun d in handlerSceneGroupSpin.GetInvocationList())
                {
                    try
                    {
                        if (d(groupID, rotation) == false)
                            result = false;
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerGroupSpin failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            return result;
        }

        public void TriggerGroupGrab(UUID groupID, Vector3 offset, UUID userID)
        {
            SceneGroupGrabed handlerSceneGroupGrab = OnSceneGroupGrab;
            if (handlerSceneGroupGrab != null)
            {
                foreach (SceneGroupGrabed d in handlerSceneGroupGrab.GetInvocationList())
                {
                    try
                    {
                        d(groupID, offset, userID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerGroupGrab failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerLandObjectAdded(ILandObject newParcel)
        {
            LandObjectAdded handlerLandObjectAdded = OnLandObjectAdded;
            if (handlerLandObjectAdded != null)
            {
                foreach (LandObjectAdded d in handlerLandObjectAdded.GetInvocationList())
                {
                    try
                    {
                        d(newParcel);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerLandObjectAdded failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerLandObjectRemoved(UUID globalID)
        {
            LandObjectRemoved handlerLandObjectRemoved = OnLandObjectRemoved;
            if (handlerLandObjectRemoved != null)
            {
                foreach (LandObjectRemoved d in handlerLandObjectRemoved.GetInvocationList())
                {
                    try
                    {
                        d(globalID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerLandObjectRemoved failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerLandObjectUpdated(uint localParcelID, ILandObject newParcel)
        {
            TriggerLandObjectAdded(newParcel);
        }

        public void TriggerAvatarEnteringNewParcel(ScenePresence avatar, int localLandID, UUID regionID)
        {
            AvatarEnteringNewParcel handlerAvatarEnteringNewParcel = OnAvatarEnteringNewParcel;
            if (handlerAvatarEnteringNewParcel != null)
            {
                foreach (AvatarEnteringNewParcel d in handlerAvatarEnteringNewParcel.GetInvocationList())
                {
                    try
                    {
                        d(avatar, localLandID, regionID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerAvatarEnteringNewParcel failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerAvatarAppearanceChanged(ScenePresence avatar)
        {
            AvatarAppearanceChange handler = OnAvatarAppearanceChange;
            if (handler != null)
            {
                foreach (AvatarAppearanceChange d in handler.GetInvocationList())
                {
                    try
                    {
                        d(avatar);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerAvatarAppearanceChanged failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerCrossAgentToNewRegion(ScenePresence agent, bool isFlying, GridRegion newRegion)
        {
            CrossAgentToNewRegion handlerCrossAgentToNewRegion = OnCrossAgentToNewRegion;
            if (handlerCrossAgentToNewRegion != null)
            {
                foreach (CrossAgentToNewRegion d in handlerCrossAgentToNewRegion.GetInvocationList())
                {
                    try
                    {
                        d(agent, isFlying, newRegion);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerCrossAgentToNewRegion failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerIncomingInstantMessage(GridInstantMessage message)
        {
            IncomingInstantMessage handlerIncomingInstantMessage = OnIncomingInstantMessage;
            if (handlerIncomingInstantMessage != null)
            {
                foreach (IncomingInstantMessage d in handlerIncomingInstantMessage.GetInvocationList())
                {
                    try
                    {
                        d(message);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerIncomingInstantMessage failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerUnhandledInstantMessage(GridInstantMessage message)
        {
            IncomingInstantMessage handlerUnhandledInstantMessage = OnUnhandledInstantMessage;
            if (handlerUnhandledInstantMessage != null)
            {
                foreach (IncomingInstantMessage d in handlerUnhandledInstantMessage.GetInvocationList())
                {
                    try
                    {
                        d(message);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnAttach failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerClientClosed(UUID ClientID, Scene scene)
        {
            ClientClosed handlerClientClosed = OnClientClosed;
            if (handlerClientClosed != null)
            {
                foreach (ClientClosed d in handlerClientClosed.GetInvocationList())
                {
                    try
                    {
//                        m_log.ErrorFormat("[EVENT MANAGER]: TriggerClientClosed: {0}", d.Target.ToString());
                        d(ClientID, scene);
//                        m_log.ErrorFormat("[EVENT MANAGER]: TriggerClientClosed done ");

                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerClientClosed failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnMakeChildAgent(ScenePresence presence)
        {
            OnMakeChildAgentDelegate handlerMakeChildAgent = OnMakeChildAgent;
            if (handlerMakeChildAgent != null)
            {
                foreach (OnMakeChildAgentDelegate d in handlerMakeChildAgent.GetInvocationList())
                {
                    try
                    {
                        d(presence);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnMakeChildAgent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnMakeRootAgent(ScenePresence presence)
        {
            Action<ScenePresence> handlerMakeRootAgent = OnMakeRootAgent;
            if (handlerMakeRootAgent != null)
            {
                foreach (Action<ScenePresence> d in handlerMakeRootAgent.GetInvocationList())
                {
                    try
                    {
                        d(presence);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnMakeRootAgent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnIncomingSceneObject(SceneObjectGroup so)
        {
            OnIncomingSceneObjectDelegate handlerIncomingSceneObject = OnIncomingSceneObject;
            if (handlerIncomingSceneObject != null)
            {
                foreach (OnIncomingSceneObjectDelegate d in handlerIncomingSceneObject.GetInvocationList())
                {
                    try
                    {
                        d(so);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnIncomingSceneObject failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnRegisterCaps(UUID agentID, Caps caps)
        {
            RegisterCapsEvent handlerRegisterCaps = OnRegisterCaps;
            if (handlerRegisterCaps != null)
            {
                foreach (RegisterCapsEvent d in handlerRegisterCaps.GetInvocationList())
                {
                    try
                    {
                        d(agentID, caps);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnRegisterCaps failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnDeregisterCaps(UUID agentID, Caps caps)
        {
            DeregisterCapsEvent handlerDeregisterCaps = OnDeregisterCaps;
            if (handlerDeregisterCaps != null)
            {
                foreach (DeregisterCapsEvent d in handlerDeregisterCaps.GetInvocationList())
                {
                    try
                    {
                        d(agentID, caps);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnDeregisterCaps failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnNewInventoryItemUploadComplete(InventoryItemBase item, int userlevel)
        {
            NewInventoryItemUploadComplete handlerNewInventoryItemUpdateComplete = OnNewInventoryItemUploadComplete;
            if (handlerNewInventoryItemUpdateComplete != null)
            {
                foreach (NewInventoryItemUploadComplete d in handlerNewInventoryItemUpdateComplete.GetInvocationList())
                {
                    try
                    {
                        d(item, userlevel);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnNewInventoryItemUploadComplete failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerLandBuy(Object sender, LandBuyArgs args)
        {
            LandBuy handlerLandBuy = OnLandBuy;
            if (handlerLandBuy != null)
            {
                foreach (LandBuy d in handlerLandBuy.GetInvocationList())
                {
                    try
                    {
                        d(sender, args);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerLandBuy failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerValidateLandBuy(Object sender, LandBuyArgs args)
        {
            LandBuy handlerValidateLandBuy = OnValidateLandBuy;
            if (handlerValidateLandBuy != null)
            {
                foreach (LandBuy d in handlerValidateLandBuy.GetInvocationList())
                {
                    try
                    {
                        d(sender, args);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerValidateLandBuy failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerAtTargetEvent(UUID scriptID, uint handle, Vector3 targetpos, Vector3 currentpos)
        {
            ScriptAtTargetEvent handlerScriptAtTargetEvent = OnScriptAtTargetEvent;
            if (handlerScriptAtTargetEvent != null)
            {
                foreach (ScriptAtTargetEvent d in handlerScriptAtTargetEvent.GetInvocationList())
                {
                    try
                    {
                        d(scriptID, handle, targetpos, currentpos);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerAtTargetEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerNotAtTargetEvent(UUID scriptID)
        {
            ScriptNotAtTargetEvent handlerScriptNotAtTargetEvent = OnScriptNotAtTargetEvent;
            if (handlerScriptNotAtTargetEvent != null)
            {
                foreach (ScriptNotAtTargetEvent d in handlerScriptNotAtTargetEvent.GetInvocationList())
                {
                    try
                    {
                        d(scriptID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerNotAtTargetEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerAtRotTargetEvent(UUID scriptID, uint handle, Quaternion targetrot, Quaternion currentrot)
        {
            ScriptAtRotTargetEvent handlerScriptAtRotTargetEvent = OnScriptAtRotTargetEvent;
            if (handlerScriptAtRotTargetEvent != null)
            {
                foreach (ScriptAtRotTargetEvent d in handlerScriptAtRotTargetEvent.GetInvocationList())
                {
                    try
                    {
                        d(scriptID, handle, targetrot, currentrot);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerAtRotTargetEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerNotAtRotTargetEvent(UUID scriptID)
        {
            ScriptNotAtRotTargetEvent handlerScriptNotAtRotTargetEvent = OnScriptNotAtRotTargetEvent;
            if (handlerScriptNotAtRotTargetEvent != null)
            {
                foreach (ScriptNotAtRotTargetEvent d in handlerScriptNotAtRotTargetEvent.GetInvocationList())
                {
                    try
                    {
                        d(scriptID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerNotAtRotTargetEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerMovingStartEvent(uint localID)
        {
            ScriptMovingStartEvent handlerScriptMovingStartEvent = OnScriptMovingStartEvent;
            if (handlerScriptMovingStartEvent != null)
            {
                foreach (ScriptMovingStartEvent d in handlerScriptMovingStartEvent.GetInvocationList())
                {
                    try
                    {
                        d(localID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerMovingStartEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerMovingEndEvent(uint localID)
        {
            ScriptMovingEndEvent handlerScriptMovingEndEvent = OnScriptMovingEndEvent;
            if (handlerScriptMovingEndEvent != null)
            {
                foreach (ScriptMovingEndEvent d in handlerScriptMovingEndEvent.GetInvocationList())
                {
                    try
                    {
                        d(localID);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerMovingEndEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRequestChangeWaterHeight(float height)
        {
            if (height < 0)
            {
                // ignore negative water height
                return;
            }

            RequestChangeWaterHeight handlerRequestChangeWaterHeight = OnRequestChangeWaterHeight;
            if (handlerRequestChangeWaterHeight != null)
            {
                foreach (RequestChangeWaterHeight d in handlerRequestChangeWaterHeight.GetInvocationList())
                {
                    try
                    {
                        d(height);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerRequestChangeWaterHeight failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerAvatarKill(uint KillerObjectLocalID, ScenePresence DeadAvatar)
        {
            AvatarKillData handlerAvatarKill = OnAvatarKilled;
            if (handlerAvatarKill != null)
            {
                foreach (AvatarKillData d in handlerAvatarKill.GetInvocationList())
                {
                    try
                    {
                        d(KillerObjectLocalID, DeadAvatar);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerAvatarKill failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerSignificantClientMovement(ScenePresence presence)
        {
            Action<ScenePresence> handlerSignificantClientMovement = OnSignificantClientMovement;
            if (handlerSignificantClientMovement != null)
            {
                foreach (Action<ScenePresence> d in handlerSignificantClientMovement.GetInvocationList())
                {
                    try
                    {
                        d(presence);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerSignificantClientMovement failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnChatFromWorld(Object sender, OSChatMessage chat)
        {
            ChatFromWorldEvent handlerChatFromWorld = OnChatFromWorld;
            if (handlerChatFromWorld != null)
            {
                foreach (ChatFromWorldEvent d in handlerChatFromWorld.GetInvocationList())
                {
                    try
                    {
                        d(sender, chat);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnChatFromWorld failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnChatFromClient(Object sender, OSChatMessage chat)
        {
            ChatFromClientEvent handlerChatFromClient = OnChatFromClient;
            if (handlerChatFromClient != null)
            {
                foreach (ChatFromClientEvent d in handlerChatFromClient.GetInvocationList())
                {
                    try
                    {
                        d(sender, chat);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnChatFromClient failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnChatBroadcast(Object sender, OSChatMessage chat)
        {
            ChatBroadcastEvent handlerChatBroadcast = OnChatBroadcast;
            if (handlerChatBroadcast != null)
            {
                foreach (ChatBroadcastEvent d in handlerChatBroadcast.GetInvocationList())
                {
                    try
                    {
                        d(sender, chat);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnChatBroadcast failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        internal void TriggerControlEvent(UUID scriptUUID, UUID avatarID, uint held, uint _changed)
        {
            ScriptControlEvent handlerScriptControlEvent = OnScriptControlEvent;
            if (handlerScriptControlEvent != null)
            {
                foreach (ScriptControlEvent d in handlerScriptControlEvent.GetInvocationList())
                {
                    try
                    {
                        d(scriptUUID,  avatarID, held, _changed);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerControlEvent failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerNoticeNoLandDataFromStorage()
        {
            NoticeNoLandDataFromStorage handlerNoticeNoLandDataFromStorage = OnNoticeNoLandDataFromStorage;
            if (handlerNoticeNoLandDataFromStorage != null)
            {
                foreach (NoticeNoLandDataFromStorage d in handlerNoticeNoLandDataFromStorage.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerNoticeNoLandDataFromStorage failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerIncomingLandDataFromStorage(List<LandData> landData)
        {
            IncomingLandDataFromStorage handlerIncomingLandDataFromStorage = OnIncomingLandDataFromStorage;
            if (handlerIncomingLandDataFromStorage != null)
            {
                foreach (IncomingLandDataFromStorage d in handlerIncomingLandDataFromStorage.GetInvocationList())
                {
                    try
                    {
                        d(landData);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerIncomingLandDataFromStorage failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerSetAllowForcefulBan(bool allow)
        {
            SetAllowForcefulBan handlerSetAllowForcefulBan = OnSetAllowForcefulBan;
            if (handlerSetAllowForcefulBan != null)
            {
                foreach (SetAllowForcefulBan d in handlerSetAllowForcefulBan.GetInvocationList())
                {
                    try
                    {
                        d(allow);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerSetAllowForcefulBan failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRequestParcelPrimCountUpdate()
        {
            RequestParcelPrimCountUpdate handlerRequestParcelPrimCountUpdate = OnRequestParcelPrimCountUpdate;
            if (handlerRequestParcelPrimCountUpdate != null)
            {
                foreach (RequestParcelPrimCountUpdate d in handlerRequestParcelPrimCountUpdate.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerRequestParcelPrimCountUpdate failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerParcelPrimCountTainted()
        {
            ParcelPrimCountTainted handlerParcelPrimCountTainted = OnParcelPrimCountTainted;
            if (handlerParcelPrimCountTainted != null)
            {
                foreach (ParcelPrimCountTainted d in handlerParcelPrimCountTainted.GetInvocationList())
                {
                    try
                    {
                        d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerParcelPrimCountTainted failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        /// <summary>
        /// this lets us keep track of nasty script events like timer, etc.
        /// </summary>
        /// <param name="objLocalID"></param>
        /// <param name="Interval"></param>
        public void TriggerTimerEvent(uint objLocalID, double Interval)
        {
            throw new NotImplementedException("TriggerTimerEvent was thought to be not used anymore and the registration for the event from scene object part has been commented out due to a memory leak");
            //handlerScriptTimerEvent = OnScriptTimerEvent;
            //if (handlerScriptTimerEvent != null)
            //{
            //    handlerScriptTimerEvent(objLocalID, Interval);
            //}
        }

        /// <summary>
        /// Called when the sun's position parameters have changed in the Region and/or Estate
        /// </summary>
        /// <param name="regionHandle">The region that changed</param>
        public void TriggerEstateToolsSunUpdate(ulong regionHandle)
        {
            EstateToolsSunUpdate handlerEstateToolsSunUpdate = OnEstateToolsSunUpdate;
            if (handlerEstateToolsSunUpdate != null)
            {
                foreach (EstateToolsSunUpdate d in handlerEstateToolsSunUpdate.GetInvocationList())
                {
                    try
                    {
                        d(regionHandle);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerEstateToolsSunUpdate failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public float GetCurrentTimeAsSunLindenHour()
        {
            SunLindenHour handlerCurrentTimeAsLindenSunHour = OnGetCurrentTimeAsLindenSunHour;
            if (handlerCurrentTimeAsLindenSunHour != null)
            {
                foreach (SunLindenHour d in handlerCurrentTimeAsLindenSunHour.GetInvocationList())
                {
                    try
                    {
                        return d();
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnAttach failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }

            return 6;
        }

        public void TriggerOarFileLoaded(Guid requestId, List<UUID> loadedScenes, string message)
        {
            OarFileLoaded handlerOarFileLoaded = OnOarFileLoaded;
            if (handlerOarFileLoaded != null)
            {
                foreach (OarFileLoaded d in handlerOarFileLoaded.GetInvocationList())
                {
                    try
                    {
                        d(requestId, loadedScenes, message);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOarFileLoaded failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOarFileSaved(Guid requestId, string message)
        {
            OarFileSaved handlerOarFileSaved = OnOarFileSaved;
            if (handlerOarFileSaved != null)
            {
                foreach (OarFileSaved d in handlerOarFileSaved.GetInvocationList())
                {
                    try
                    {
                        d(requestId, message);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOarFileSaved failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerEmptyScriptCompileQueue(int numScriptsFailed, string message)
        {
            EmptyScriptCompileQueue handlerEmptyScriptCompileQueue = OnEmptyScriptCompileQueue;
            if (handlerEmptyScriptCompileQueue != null)
            {
                foreach (EmptyScriptCompileQueue d in handlerEmptyScriptCompileQueue.GetInvocationList())
                {
                    try
                    {
                        d(numScriptsFailed, message);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerEmptyScriptCompileQueue failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptCollidingStart(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerCollidingStart = OnScriptColliderStart;
            if (handlerCollidingStart != null)
            {
                foreach (ScriptColliding d in handlerCollidingStart.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptCollidingStart failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptColliding(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerColliding = OnScriptColliding;
            if (handlerColliding != null)
            {
                foreach (ScriptColliding d in handlerColliding.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptColliding failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptCollidingEnd(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerCollidingEnd = OnScriptCollidingEnd;
            if (handlerCollidingEnd != null)
            {
                foreach (ScriptColliding d in handlerCollidingEnd.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptCollidingEnd failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptLandCollidingStart(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerLandCollidingStart = OnScriptLandColliderStart;
            if (handlerLandCollidingStart != null)
            {
                foreach (ScriptColliding d in handlerLandCollidingStart.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptLandCollidingStart failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptLandColliding(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerLandColliding = OnScriptLandColliding;
            if (handlerLandColliding != null)
            {
                foreach (ScriptColliding d in handlerLandColliding.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptLandColliding failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScriptLandCollidingEnd(uint localId, ColliderArgs colliders)
        {
            ScriptColliding handlerLandCollidingEnd = OnScriptLandColliderEnd;
            if (handlerLandCollidingEnd != null)
            {
                foreach (ScriptColliding d in handlerLandCollidingEnd.GetInvocationList())
                {
                    try
                    {
                        d(localId, colliders);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScriptLandCollidingEnd failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerSetRootAgentScene(UUID agentID, Scene scene)
        {
            OnSetRootAgentSceneDelegate handlerSetRootAgentScene = OnSetRootAgentScene;
            if (handlerSetRootAgentScene != null)
            {
                foreach (OnSetRootAgentSceneDelegate d in handlerSetRootAgentScene.GetInvocationList())
                {
                    try
                    {
                        d(agentID, scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerSetRootAgentScene failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnRegionUp(GridRegion otherRegion)
        {
            RegionUp handlerOnRegionUp = OnRegionUp;
            if (handlerOnRegionUp != null)
            {
                foreach (RegionUp d in handlerOnRegionUp.GetInvocationList())
                {
                    try
                    {
                        d(otherRegion);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnRegionUp failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnSceneObjectLoaded(SceneObjectGroup so)
        {
            SceneObjectDelegate handler = OnSceneObjectLoaded;
            if (handler != null)
            {
                foreach (SceneObjectDelegate d in handler.GetInvocationList())
                {
                    try
                    {
                        d(so);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnSceneObjectLoaded failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnSceneObjectPreSave(SceneObjectGroup persistingSo, SceneObjectGroup originalSo)
        {
            SceneObjectPreSaveDelegate handler = OnSceneObjectPreSave;
            if (handler != null)
            {
                foreach (SceneObjectPreSaveDelegate d in handler.GetInvocationList())
                {
                    try
                    {
                        d(persistingSo, originalSo);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnSceneObjectPreSave failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnSceneObjectPartCopy(SceneObjectPart copy, SceneObjectPart original, bool userExposed)
        {
            SceneObjectPartCopyDelegate handler = OnSceneObjectPartCopy;
            if (handler != null)
            {
                foreach (SceneObjectPartCopyDelegate d in handler.GetInvocationList())
                {
                    try
                    {
                        d(copy, original, userExposed);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnSceneObjectPartCopy failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerSceneObjectPartUpdated(SceneObjectPart sop, bool full)
        {
            SceneObjectPartUpdated handler = OnSceneObjectPartUpdated;
            if (handler != null)
            {
                foreach (SceneObjectPartUpdated d in handler.GetInvocationList())
                {
                    try
                    {
                        d(sop, full);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerSceneObjectPartUpdated failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerScenePresenceUpdated(ScenePresence sp)
        {
            ScenePresenceUpdated handler = OnScenePresenceUpdated;
            if (handler != null)
            {
                foreach (ScenePresenceUpdated d in handler.GetInvocationList())
                {
                    try
                    {
                        d(sp);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerScenePresenceUpdated failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerOnParcelPropertiesUpdateRequest(LandUpdateArgs args,
                        int local_id, IClientAPI remote_client)
        {
            ParcelPropertiesUpdateRequest handler = OnParcelPropertiesUpdateRequest;
            if (handler != null)
            {
                foreach (ParcelPropertiesUpdateRequest d in handler.GetInvocationList())
                {
                    try
                    {
                        d(args, local_id, remote_client);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerOnSceneObjectPartCopy failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerSceneShuttingDown(Scene s)
        {
            Action<Scene> handler = OnSceneShuttingDown;
            if (handler != null)
            {
                foreach (Action<Scene> d in handler.GetInvocationList())
                {
                    m_log.InfoFormat("[EVENT MANAGER]: TriggerSceneShuttingDown invoke {0}", d.Method.Name.ToString());
                    try
                    {
                        d(s);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat(
                            "[EVENT MANAGER]: Delegate for TriggerSceneShuttingDown failed - continuing.  {0} {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
            m_log.Info("[EVENT MANAGER]: TriggerSceneShuttingDown done");
        }

        public void TriggerOnRegionStarted(Scene scene)
        {
            RegionStarted handler = OnRegionStarted;

            if (handler != null)
            {
                foreach (RegionStarted d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for RegionStarted failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRegionHeartbeatStart(Scene scene)
        {
            RegionHeartbeatStart handler = OnRegionHeartbeatStart;

            if (handler != null)
            {
                foreach (RegionHeartbeatStart d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for OnRegionHeartbeatStart failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRegionHeartbeatEnd(Scene scene)
        {
            RegionHeartbeatEnd handler = OnRegionHeartbeatEnd;

            if (handler != null)
            {
                foreach (RegionHeartbeatEnd d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for OnRegionHeartbeatEnd failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRegionLoginsStatusChange(IScene scene)
        {
            RegionLoginsStatusChange handler = OnRegionLoginsStatusChange;

            if (handler != null)
            {
                foreach (RegionLoginsStatusChange d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for OnRegionLoginsStatusChange failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerRegionReadyStatusChange(IScene scene)
        {
            Action<IScene> handler = OnRegionReadyStatusChange;

            if (handler != null)
            {
                foreach (Action<IScene> d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for OnRegionReadyStatusChange failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerPrimsLoaded(Scene s)
        {
            PrimsLoaded handler = OnPrimsLoaded;

            if (handler != null)
            {
                foreach (PrimsLoaded d in handler.GetInvocationList())
                {
                    try
                    {
                        d(s);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for PrimsLoaded failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerTeleportStart(IClientAPI client, GridRegion destination, GridRegion finalDestination, uint teleportFlags, bool gridLogout)
        {
            TeleportStart handler = OnTeleportStart;

            if (handler != null)
            {
                foreach (TeleportStart d in handler.GetInvocationList())
                {
                    try
                    {
                        d(client, destination, finalDestination, teleportFlags, gridLogout);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for TeleportStart failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerTeleportFail(IClientAPI client, bool gridLogout)
        {
            TeleportFail handler = OnTeleportFail;

            if (handler != null)
            {
                foreach (TeleportFail d in handler.GetInvocationList())
                {
                    try
                    {
                        d(client, gridLogout);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for TeleportFail failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public delegate void ExtraSettingChangedDelegate(Scene scene, string name, string value);
        public event ExtraSettingChangedDelegate OnExtraSettingChanged;

        public void TriggerExtraSettingChanged(Scene scene, string name, string val)
        {
            ExtraSettingChangedDelegate handler = OnExtraSettingChanged;
            if (handler != null)
            {
                foreach (ExtraSettingChangedDelegate d in handler.GetInvocationList())
                {
                    try
                    {
                        d(scene, name, val);
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for ExtraSettingChanged failed - continuing {0} - {1}",
                            e.Message, e.StackTrace);
                    }
                }
            }
        }

        public void TriggerThrottleUpdate(ScenePresence scenePresence)
        {
            OnThrottleUpdate?.Invoke(scenePresence);
        }

        //        public void TriggerGatherUuids(SceneObjectPart sop, IDictionary<UUID, AssetType> assetUuids)
        //        {
        //            GatherUuids handler = OnGatherUuids;
        //
        //            if (handler != null)
        //            {
        //                foreach (GatherUuids d in handler.GetInvocationList())
        //                {
        //                    try
        //                    {
        //                        d(sop, assetUuids);
        //                    }
        //                    catch (Exception e)
        //                    {
        //                        m_log.ErrorFormat("[EVENT MANAGER]: Delegate for TriggerUuidGather failed - continuing {0} - {1}",
        //                            e.Message, e.StackTrace);
        //                    }
        //                }
        //            }
        //        }

        public delegate void ScriptListen(UUID scriptID, int channel, string name, UUID id, string message);
        public event ScriptListen OnScriptListenEvent;

        public void TriggerScriptListen(UUID scriptID, int channel, string name, UUID id, string message)
        {
            OnScriptListenEvent?.Invoke(scriptID, channel, name, id, message);
        }
    }
}
