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
using System.IO;
using System.Reflection;

using Nini.Config;
using OpenMetaverse;
using log4net;
using Mono.Addins;

using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;

namespace MutSea.Region.CoreModules.World.Sound
{
    [Extension(Path = "/MutSea/RegionModules", NodeName = "RegionModule", Id = "SoundModule")]
    public class SoundModule : INonSharedRegionModule, ISoundModule
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;

        public enum SoundFlags: byte
        {
            NONE =         0,
            LOOP =         1 << 0,
            SYNC_MASTER =  1 << 1,
            SYNC_SLAVE =   1 << 2,
            SYNC_PENDING = 1 << 3,
            QUEUE =        1 << 4,
            STOP =         1 << 5,
            SYNC_MASK = SYNC_MASTER | SYNC_SLAVE | SYNC_PENDING
        }

        public bool Enabled { get; private set; }

        public float MaxDistance { get; private set; }

        #region INonSharedRegionModule

        public void Initialise(IConfigSource configSource)
        {
            IConfig config = configSource.Configs["Sounds"];

            if (config == null)
            {
                Enabled = true;
                MaxDistance = 100.0f;
            }
            else
            {
                Enabled = config.GetString("Module", "MutSea.Region.CoreModules.dll:SoundModule") ==
                        Path.GetFileName(Assembly.GetExecutingAssembly().Location)
                        + ":" + MethodBase.GetCurrentMethod().DeclaringType.Name;
                MaxDistance = config.GetFloat("MaxDistance", 100.0f);
            }
        }

        public void AddRegion(Scene scene) { }

        public void RemoveRegion(Scene scene)
        {
            m_scene.EventManager.OnNewClient -= OnNewClient;
        }

        public void RegionLoaded(Scene scene)
        {
            if (!Enabled)
                return;

            m_scene = scene;
            m_scene.EventManager.OnNewClient += OnNewClient;

            m_scene.RegisterModuleInterface<ISoundModule>(this);
        }

        public void Close() { }

        public Type ReplaceableInterface
        {
            get { return typeof(ISoundModule); }
        }

        public string Name { get { return "Sound Module"; } }

        #endregion

        #region Event Handlers

        private void OnNewClient(IClientAPI client)
        {
            client.OnSoundTrigger += TriggerSound;
        }

        #endregion

        #region ISoundModule

        public virtual void PlayAttachedSound(
            UUID soundID, UUID ownerID, UUID objectID, double gain, Vector3 position, byte flags)
        {
            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
                return;

            if (part.SoundRadius == 0)
                part.SoundRadius = MaxDistance;
            part.SoundFlags = 0;

            if (part.SoundQueueing)
                flags |= (byte)SoundFlags.QUEUE;

            SceneObjectGroup grp = part.ParentGroup;
            if(grp == null | grp.IsDeleted)
                return;

            if (grp.IsAttachment)
            {
                ScenePresence ssp = null;
                if (!m_scene.TryGetScenePresence(grp.AttachedAvatar, out ssp))
                    return;

                if (grp.HasPrivateAttachmentPoint)
                {
                    ssp.ControllingClient.SendPlayAttachedSound(soundID, objectID,
                        ownerID, (float)gain, flags);
                    return;
                }

                if (!ssp.ParcelAllowThisAvatarSounds)
                    return;
            }

            m_scene.ForEachRootScenePresence(delegate(ScenePresence sp)
            {
                sp.ControllingClient.SendPlayAttachedSound(soundID, objectID,
                        ownerID, (float)gain, flags);
            });
        }

        public virtual void TriggerSound(
            UUID soundId, UUID ownerID, UUID objectID, UUID parentID, double gain, Vector3 position, UInt64 handle)
        {
            float radius;
            ScenePresence ssp = null;
            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
            {
                if (!m_scene.TryGetScenePresence(ownerID, out ssp))
                    return;
                if (!ssp.ParcelAllowThisAvatarSounds)
                    return;

                radius = MaxDistance;
            }
            else
            {
                SceneObjectGroup grp = part.ParentGroup;

                if(grp.IsAttachment)
                {
                    if(!m_scene.TryGetScenePresence(grp.AttachedAvatar, out ssp))
                        return;

                    if(!ssp.ParcelAllowThisAvatarSounds)
                        return;

                }

                radius = (float)part.SoundRadius;
                if(radius == 0)
                {
                    radius = MaxDistance;
                    part.SoundRadius = MaxDistance;
                }
            }

            radius *= radius;
            m_scene.ForEachRootScenePresence(delegate(ScenePresence sp)
            {
                if (Vector3.DistanceSquared(sp.AbsolutePosition, position) > radius) // Max audio distance
                    return;

                sp.ControllingClient.SendTriggeredSound(soundId, ownerID,
                        objectID, parentID, handle, position,
                        (float)gain);
            });
        }

        public virtual void TriggerCollisionSound(
            UUID soundId, UUID ownerID, UUID objectID, UUID parentID, double gain, Vector3 position, UInt64 handle)
        {
            float radius;
            ScenePresence ssp = null;
            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
            {
                if (!m_scene.TryGetScenePresence(ownerID, out ssp))
                    return;
                if (!ssp.ParcelAllowThisAvatarSounds)
                    return;
                radius = MaxDistance;
            }
            else
            {
                SceneObjectGroup grp = part.ParentGroup;

                if (grp.IsAttachment)
                {
                    if (!m_scene.TryGetScenePresence(grp.AttachedAvatar, out ssp))
                        return;

                    if (!ssp.ParcelAllowThisAvatarSounds)
                        return;
                }

                radius = (float)part.SoundRadius;
                if (radius == 0)
                {
                    radius = MaxDistance;
                    part.SoundRadius = MaxDistance;
                }
            }

            radius *= radius;
            m_scene.ForEachRootScenePresence(delegate (ScenePresence sp)
            {
                if(sp.MuteCollisions)
                    return;

                if (Vector3.DistanceSquared(sp.AbsolutePosition, position) > radius) // Max audio distance
                    return;

                sp.ControllingClient.SendTriggeredSound(soundId, ownerID,
                        objectID, parentID, handle, position,
                        (float)gain);
            });
        }

        public virtual void StopSound(UUID objectID)
        {
            if (m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart m_host))
                StopSound(m_host);
        }

        public void StopSound(SceneObjectPart m_host)
        {
            m_host.Sound = UUID.Zero;
            m_host.SoundFlags = (byte)SoundFlags.STOP;
            m_host.SoundGain = 0;
            m_host.ScheduleFullUpdate();
            m_host.SendFullUpdateToAllClients();
        }

        public virtual void PreloadSound(UUID objectID, UUID soundID)
        {
            if (soundID.IsZero() || !m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
                return;

            float radius = (float)part.SoundRadius;
            if (radius == 0)
            {
                radius = MaxDistance;
                part.SoundRadius = radius;
            }

            radius *= 4.0f * radius; // avatars and prims do move
            m_scene.ForEachRootScenePresence(delegate (ScenePresence sp)
            {
                if (Vector3.DistanceSquared(sp.AbsolutePosition, part.AbsolutePosition) < radius)
                    sp.ControllingClient.SendPreLoadSound(objectID, part.OwnerID, soundID);
            });
        }

        public virtual void PreloadSound(SceneObjectPart part, UUID soundID)
        {
            if (soundID.IsZero())
                return;

            float radius = (float)part.SoundRadius;
            if (radius == 0)
            {
                radius = MaxDistance;
                part.SoundRadius = radius;
            }

            radius *= 4.0f * radius; // avatars and prims do move
            m_scene.ForEachRootScenePresence(delegate (ScenePresence sp)
            {
                if (Vector3.DistanceSquared(sp.AbsolutePosition, part.AbsolutePosition) < radius)
                    sp.ControllingClient.SendPreLoadSound(part.UUID, part.OwnerID, soundID);
            });
        }

        // Xantor 20080528 we should do this differently.
        // 1) apply the sound to the object
        // 2) schedule full update
        // just sending the sound out once doesn't work so well when other avatars come in view later on
        // or when the prim gets moved, changed, sat on, whatever
        // see large number of mantises (mantes?)
        // 20080530 Updated to remove code duplication
        // 20080530 Stop sound if there is one, otherwise volume only changes don't work
        public void LoopSound(UUID objectID, UUID soundID,
                double volume, bool isMaster, bool isSlave)
        {
            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart m_host))
                return;

            byte iflags = 1; // looping
            if (isMaster)
                iflags |= (byte)SoundFlags.SYNC_MASTER;
            // TODO check viewer seems to accept both
            if (isSlave)
                iflags |= (byte)SoundFlags.SYNC_SLAVE;
            if (m_host.SoundQueueing)
                iflags |= (byte)SoundFlags.QUEUE;

            m_host.Sound = soundID;
            m_host.SoundGain = volume;
            m_host.SoundFlags = iflags;
            if (m_host.SoundRadius == 0)
                m_host.SoundRadius = MaxDistance;

            m_host.ScheduleFullUpdate();
            m_host.SendFullUpdateToAllClients();
        }

        public void LoopSound(SceneObjectPart host, UUID soundID,
                double volume, bool isMaster, bool isSlave)
        {
            byte iflags = 1; // looping
            if (isMaster)
                iflags |= (byte)SoundFlags.SYNC_MASTER;
            // TODO check viewer seems to accept both
            if (isSlave)
                iflags |= (byte)SoundFlags.SYNC_SLAVE;
            if (host.SoundQueueing)
                iflags |= (byte)SoundFlags.QUEUE;

            host.Sound = soundID;
            host.SoundGain = volume;
            host.SoundFlags = iflags;
            if (host.SoundRadius == 0)
                host.SoundRadius = MaxDistance;

            host.ScheduleFullUpdate();
            host.SendFullUpdateToAllClients();
        }

        public void SendSound(UUID objectID, UUID soundID, double volume,
                bool triggered, byte flags, bool useMaster,
                bool isMaster)
        {
            if (soundID.IsZero())
                return;

            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
                return;

            volume = Utils.Clamp(volume, 0, 1);

            Vector3 position = part.AbsolutePosition; // region local
            ulong regionHandle = m_scene.RegionInfo.RegionHandle;

            if (triggered)
                TriggerSound(soundID, part.OwnerID, part.UUID, part.ParentGroup.UUID, volume, position, regionHandle);
            else
            {
                byte bflags = 0;

                if (isMaster)
                    bflags |= (byte)SoundFlags.SYNC_MASTER;
                // TODO check viewer seems to accept both
                if (useMaster)
                    bflags |= (byte)SoundFlags.SYNC_SLAVE;
                PlayAttachedSound(soundID, part.OwnerID, part.UUID, volume, position, bflags);
            }
        }

        public void SendSound(SceneObjectPart part, UUID soundID, double volume,
                bool triggered, byte flags, bool useMaster,
                bool isMaster)
        {
            if (soundID.IsZero())
                return;

            volume = Utils.Clamp(volume, 0, 1);

            Vector3 position = part.AbsolutePosition; // region local
            ulong regionHandle = m_scene.RegionInfo.RegionHandle;

            if (triggered)
                TriggerSound(soundID, part.OwnerID, part.UUID, part.ParentGroup.UUID, volume, position, regionHandle);
            else
            {
                byte bflags = 0;

                if (isMaster)
                    bflags |= (byte)SoundFlags.SYNC_MASTER;
                // TODO check viewer seems to accept both
                if (useMaster)
                    bflags |= (byte)SoundFlags.SYNC_SLAVE;
                PlayAttachedSound(soundID, part.OwnerID, part.UUID, volume, position, bflags);
            }
        }

        public void TriggerSoundLimited(UUID objectID, UUID sound,
                double volume, Vector3 min, Vector3 max)
        {
            if (sound.IsZero())
                return;

            if (!m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
                return;

            m_scene.ForEachRootScenePresence(delegate(ScenePresence sp)
            {
                double dis = Util.GetDistanceTo(sp.AbsolutePosition, part.AbsolutePosition);

                if (dis > MaxDistance) // Max audio distance
                    return;

                else if (!Util.IsInsideBox(sp.AbsolutePosition, min, max))
                    return;

                // Scale by distance
                double thisSpGain = volume * ((MaxDistance - dis) / MaxDistance);

                sp.ControllingClient.SendTriggeredSound(sound, part.OwnerID,
                        part.UUID, part.ParentGroup.UUID,
                        m_scene.RegionInfo.RegionHandle,
                        part.AbsolutePosition, (float)thisSpGain);
            });
        }

        public void SetSoundQueueing(UUID objectID, bool shouldQueue)
        {
            if (m_scene.TryGetSceneObjectPart(objectID, out SceneObjectPart part))
                part.SoundQueueing = shouldQueue;
        }

        #endregion
    }
}
