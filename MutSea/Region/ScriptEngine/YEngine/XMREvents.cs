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
using MutSea.Framework;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.ScriptEngine.Shared;
//using log4net;

using LSL_Float = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = MutSea.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = MutSea.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = MutSea.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = MutSea.Region.ScriptEngine.Shared.LSL_Types.Vector3;

namespace MutSea.Region.ScriptEngine.Yengine
{
    /// <summary>
    /// Prepares events so they can be directly executed upon a script by EventQueueManager, then queues it.
    /// </summary>
    public partial class Yengine
    {
        public static readonly object[] zeroObjectArray = new object[0];
        public static readonly object[] oneObjectArrayOne = new object[1] { 1 };

        private void InitEvents()
        {
            m_log.Info("[YEngine] Hooking up to server events");
            EventManager eManager = this.World.EventManager;
            eManager.OnAttach += attach;
            eManager.OnObjectGrab += touch_start;
            eManager.OnObjectGrabbing += touch;
            eManager.OnObjectDeGrab += touch_end;
            eManager.OnScriptChangedEvent += changed;
            eManager.OnScriptAtTargetEvent += at_target;
            eManager.OnScriptNotAtTargetEvent += not_at_target;
            eManager.OnScriptAtRotTargetEvent += at_rot_target;
            eManager.OnScriptNotAtRotTargetEvent += not_at_rot_target;
            eManager.OnScriptMovingStartEvent += moving_start;
            eManager.OnScriptMovingEndEvent += moving_end;
            eManager.OnScriptControlEvent += control;
            eManager.OnScriptColliderStart += collision_start;
            eManager.OnScriptColliding += collision;
            eManager.OnScriptCollidingEnd += collision_end;
            eManager.OnScriptLandColliderStart += land_collision_start;
            eManager.OnScriptLandColliding += land_collision;
            eManager.OnScriptLandColliderEnd += land_collision_end;
            eManager.OnScriptListenEvent += script_listen;

            IMoneyModule money = this.World.RequestModuleInterface<IMoneyModule>();
            if(money != null)
            {
                money.OnObjectPaid += HandleObjectPaid;
            }
        }

        /// <summary>
        /// When an object gets paid by an avatar and generates the paid event, 
        /// this will pipe it to the script engine
        /// </summary>
        /// <param name="objectID">Object ID that got paid</param>
        /// <param name="agentID">Agent Id that did the paying</param>
        /// <param name="amount">Amount paid</param>
        private void HandleObjectPaid(UUID objectID, UUID agentID,
                int amount)
        {
            // Add to queue for all scripts in ObjectID object
            DetectParams[] det = new DetectParams[1];
            det[0] = new DetectParams();
            det[0].Key = agentID;
            det[0].Populate(this.World);

            // Since this is an event from a shared module, all scenes will
            // get it. But only one has the object in question. The others
            // just ignore it.
            //
            SceneObjectPart part = World.GetSceneObjectPart(objectID);

            if(part == null)
                return;

            if((part.ScriptEvents & scriptEvents.money) == 0)
                part = part.ParentGroup.RootPart;

            Verbose("Paid: " + objectID + " from " + agentID + ", amount " + amount);

            if(part != null)
            {
                money(part.LocalId, agentID, amount, det);
            }
        }

        /// <summary>
        /// Handles piping the proper stuff to The script engine for touching
        /// Including DetectedParams
        /// </summary>
        /// <param name="localID"></param>
        /// <param name="originalID"></param>
        /// <param name="offsetPos"></param>
        /// <param name="remoteClient"></param>
        /// <param name="surfaceArgs"></param>
        public void touch_start(uint localID, uint originalID, Vector3 offsetPos,
                IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            touches(localID, originalID, offsetPos, remoteClient, surfaceArgs, "touch_start");
        }

        public void touch(uint localID, uint originalID, Vector3 offsetPos,
                IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs)
        {
            touches(localID, originalID, offsetPos, remoteClient, surfaceArgs, "touch");
        }

        private static Vector3 zeroVec3 = new Vector3(0, 0, 0);
        public void touch_end(uint localID, uint originalID, IClientAPI remoteClient,
                              SurfaceTouchEventArgs surfaceArgs)
        {
            touches(localID, originalID, zeroVec3, remoteClient, surfaceArgs, "touch_end");
        }

        private void touches(uint localID, uint originalID, Vector3 offsetPos,
                IClientAPI remoteClient, SurfaceTouchEventArgs surfaceArgs, string eventname)
        {
            if (m_Exiting)
                return;

            SceneObjectPart part = World.GetSceneObjectPart(localID);
            if(part == null || part.IsDeleted)
                return;

            int linknum;
            if(originalID != 0 && originalID != localID)
            {
                SceneObjectPart srcpart = World.GetSceneObjectPart(originalID);
                if(srcpart == null || part.IsDeleted)
                    return;
                linknum = srcpart.LinkNum;
            }
            else
                linknum = part.LinkNum;

            DetectParams det = new DetectParams();
            det.Key = remoteClient.AgentId;
            det.Populate(World);
            det.OffsetPos = new LSL_Vector(offsetPos.X,
                                           offsetPos.Y,
                                           offsetPos.Z);
            det.LinkNum = linknum;

            if(surfaceArgs != null)
            {
                det.SurfaceTouchArgs = surfaceArgs;
            }

            // Add to queue for all scripts in ObjectID object
            PostPrimEvent(part, new EventParams(
                    eventname, oneObjectArrayOne,
                    new DetectParams[] { det }));
        }

        public void changed(uint localID, uint change, object parameter)
        {
            int ch = (int)change;
            // Add to queue for all scripts in localID, Object pass change.
            if(parameter == null)
            {
                PostObjectEvent(localID, new EventParams(
                    "changed", new object[] { ch },
                    zeroDetectParams));
                return;
            }
            if ( parameter is UUID)
            {
                DetectParams det = new DetectParams();
                det.Key = (UUID)parameter;
                PostObjectEvent(localID, new EventParams(
                    "changed", new object[] { ch },
                    new DetectParams[] { det }));
                return;
            }
        }

        public void script_listen(UUID scriptID, int channel, string name, UUID id, string message)
        {
            object[] resobj = new object[]
            {
                new LSL_Types.LSLInteger(channel),
                new LSL_Types.LSLString(name),
                new LSL_Types.LSLString(id.ToString()),
                new LSL_Types.LSLString(message)
            };
            PostScriptEvent(scriptID, new EventParams("listen", resobj, new DetectParams[0]));
        }

        // state_entry: not processed here
        // state_exit: not processed here

        public void money(uint localID, UUID agentID, int amount, DetectParams[] det)
        {
            PostObjectEvent(localID, new EventParams(
                    "money", new object[] {
                    agentID.ToString(),
                    amount },
                    det));
        }

        public void collision_start(uint localID, ColliderArgs col)
        {
            collisions(localID, col, "collision_start");
        }

        public void collision(uint localID, ColliderArgs col)
        {
            collisions(localID, col, "collision");
        }

        public void collision_end(uint localID, ColliderArgs col)
        {
            collisions(localID, col, "collision_end");
        }

        private void collisions(uint localID, ColliderArgs col, string eventname)
        {
            int dc = col.Colliders.Count;
            if(dc > 0)
            {
                DetectParams[] det = new DetectParams[dc];
                int i = 0;
                foreach(DetectedObject detobj in col.Colliders)
                {
                    DetectParams d = new DetectParams();
                    det[i++] = d;

                    d.Key = detobj.keyUUID;
                    d.Populate(World, detobj);
                }

                this.PostObjectEvent(localID, new EventParams(
                        eventname,
                        new Object[] { dc },
                        det));
            }
        }

        public void land_collision_start(uint localID, ColliderArgs col)
        {
            land_collisions(localID, col, "land_collision_start");
        }

        public void land_collision(uint localID, ColliderArgs col)
        {
            land_collisions(localID, col, "land_collision");
        }

        public void land_collision_end(uint localID, ColliderArgs col)
        {
            land_collisions(localID, col, "land_collision_end");
        }

        private void land_collisions(uint localID, ColliderArgs col, string eventname)
        {
            foreach(DetectedObject detobj in col.Colliders)
            {
                LSL_Vector vec = new LSL_Vector(detobj.posVector.X,
                                                detobj.posVector.Y,
                                                detobj.posVector.Z);
                EventParams eps = new EventParams(eventname,
                                                  new Object[] { vec },
                                                  zeroDetectParams);
                this.PostObjectEvent(localID, eps);
            }
        }

        // timer: not handled here
        // listen: not handled here

        public void control(UUID itemID, UUID agentID, uint held, uint change)
        {
            this.PostScriptEvent(itemID, new EventParams(
                    "control", new object[] {
                    agentID.ToString(),
                    (int)held,
                    (int)change},
                    zeroDetectParams));
        }

        public void email(uint localID, UUID itemID, string timeSent,
                string address, string subject, string message, int numLeft)
        {
            this.PostObjectEvent(localID, new EventParams(
                    "email", new object[] {
                    timeSent,
                    address,
                    subject,
                    message,
                    numLeft},
                    zeroDetectParams));
        }

        public void at_target(UUID scriptID, uint handle, Vector3 targetpos, Vector3 atpos)
        {
            PostScriptEvent(scriptID, new EventParams(
                    "at_target", new object[] {
                    (int)handle,
                    new LSL_Vector(targetpos.X,targetpos.Y,targetpos.Z),
                    new LSL_Vector(atpos.X,atpos.Y,atpos.Z) },
                    zeroDetectParams));
        }

        public void not_at_target(UUID scriptID)
        {
            PostScriptEvent(scriptID, new EventParams(
                    "not_at_target", zeroObjectArray,
                    zeroDetectParams));
        }

        public void at_rot_target(UUID scriptID, uint handle, OpenMetaverse.Quaternion targetrot, OpenMetaverse.Quaternion atrot)
        {
            PostScriptEvent(scriptID, new EventParams(
                    "at_rot_target",
                    new object[] {
                        new LSL_Integer(handle),
                        new LSL_Rotation(targetrot.X, targetrot.Y, targetrot.Z, targetrot.W),
                        new LSL_Rotation(atrot.X, atrot.Y, atrot.Z, atrot.W)
                    },
                    zeroDetectParams));
        }

        public void not_at_rot_target(UUID scriptID)
        {
            PostScriptEvent(scriptID, new EventParams(
                    "not_at_rot_target", zeroObjectArray,
                    zeroDetectParams));
        }

        // run_time_permissions: not handled here

        public void attach(uint localID, UUID itemID, UUID avatar)
        {
            this.PostObjectEvent(localID, new EventParams(
                    "attach", new object[] {
                    avatar.ToString() },
                    zeroDetectParams));
        }

        // dataserver: not handled here
        // link_message: not handled here

        public void moving_start(uint localID)
        {
            this.PostObjectEvent(localID, new EventParams(
                    "moving_start", zeroObjectArray,
                    zeroDetectParams));
        }

        public void moving_end(uint localID)
        {
            this.PostObjectEvent(localID, new EventParams(
                    "moving_end", zeroObjectArray,
                    zeroDetectParams));
        }

        // object_rez: not handled here
        // remote_data: not handled here
        // http_response: not handled here
    }
}
