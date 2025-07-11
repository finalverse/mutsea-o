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
using System.Linq;
using System.Reflection;
using System.Threading;
using log4net;
using OpenMetaverse;
using MutSea.Framework;
using MutSea.Region.Framework.Interfaces;
using MutSea.Region.Framework.Scenes;
using MutSea.Region.PhysicsModules.SharedBase;

namespace MutSea.Region.Framework.Scenes.Animation
{
    /// <summary>
    /// Handle all animation duties for a scene presence
    /// </summary>
    public class ScenePresenceAnimator
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public AnimationSet Animations
        {
            get { return m_animations; }
        }
        protected AnimationSet m_animations = new AnimationSet();

        /// <value>
        /// The current movement animation
        /// </value>
        public string CurrentMovementAnimation { get; private set; }

        private int m_animTickFall;
        private int m_animTickLand;
        private int m_animTickJump;

        public bool isJumping;

        // private int m_landing = 0;

        /// <summary>
        /// Is the avatar falling?
        /// </summary>
        public bool Falling { get; private set; }

        private float m_lastFallVelocity;

        /// <value>
        /// The scene presence that this animator applies to
        /// </value>
        protected ScenePresence m_scenePresence;

        public ScenePresenceAnimator(ScenePresence sp)
        {
            m_scenePresence = sp;
            CurrentMovementAnimation = "STAND";
        }

        public void AddAnimation(UUID animID, UUID objectID)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            // m_log.DebugFormat("[SCENE PRESENCE ANIMATOR]: Adding animation {0} for {1}", animID, m_scenePresence.Name);
            if (m_scenePresence.Scene.DebugAnimations)
                m_log.DebugFormat(
                    "[SCENE PRESENCE ANIMATOR]: Adding animation {0} {1} for {2}",
                    GetAnimName(animID), animID, m_scenePresence.Name);

            if (m_animations.Add(animID, m_scenePresence.ControllingClient.NextAnimationSequenceNumber, objectID))
            {
                SendAnimPack();
                m_scenePresence.TriggerScenePresenceUpdated();
            }
        }

        // Called from scripts
        public void AddAnimation(string name, UUID objectID)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            UUID animID = DefaultAvatarAnimations.GetDefaultAnimation(name);
            if (animID.IsZero())
                return;

            // m_log.DebugFormat("[SCENE PRESENCE ANIMATOR]: Adding animation {0} {1} for {2}", animID, name, m_scenePresence.Name);

            AddAnimation(animID, objectID);
        }

        /// <summary>
        /// Remove the specified animation
        /// </summary>
        /// <param name='animID'></param>
        /// <param name='allowNoDefault'>
        /// If true, then the default animation can be entirely removed.
        /// If false, then removing the default animation will reset it to the simulator default (currently STAND).
        /// </param>
        public void RemoveAnimation(UUID animID, bool allowNoDefault)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            if (m_scenePresence.Scene.DebugAnimations)
                m_log.DebugFormat(
                    "[SCENE PRESENCE ANIMATOR]: Removing animation {0} {1} for {2}",
                    GetAnimName(animID), animID, m_scenePresence.Name);

            if (m_animations.Remove(animID, allowNoDefault))
            {
                SendAnimPack();
                m_scenePresence.TriggerScenePresenceUpdated();
            }
        }

        public void avnChangeAnim(UUID animID, bool addRemove, bool sendPack)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            if (!animID.IsZero())
            {
                if (addRemove)
                    m_animations.Add(animID, m_scenePresence.ControllingClient.NextAnimationSequenceNumber, UUID.Zero);
                else
                    m_animations.Remove(animID, false);
            }
            if (sendPack)
                SendAnimPack();
        }

        // Called from scripts
        public void RemoveAnimation(string name)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            UUID animID = DefaultAvatarAnimations.GetDefaultAnimation(name);
            if (animID.IsZero())
                return;

            RemoveAnimation(animID, true);
        }

        public void ResetAnimations()
        {
            if (m_scenePresence.Scene.DebugAnimations)
                m_log.DebugFormat(
                    "[SCENE PRESENCE ANIMATOR]: Resetting animations for {0} in {1}",
                    m_scenePresence.Name, m_scenePresence.Scene.RegionInfo.RegionName);

            m_animations.Clear();
        }


        UUID aoSitGndAnim = UUID.Zero;

        /// <summary>
        /// The movement animation is reserved for "main" animations
        /// that are mutually exclusive, e.g. flying and sitting.
        /// </summary>
        /// <returns>'true' if the animation was updated</returns>
        ///
        public bool TrySetMovementAnimation(string anim)
        {
            if (m_scenePresence.IsChildAgent)
            {
                m_log.WarnFormat(
                    "[SCENE PRESENCE ANIMATOR]: Tried to set movement animation {0} on child presence {1}",
                    anim, m_scenePresence.Name);
                return false;
            }

            //m_log.DebugFormat(
            //    "[SCENE PRESENCE ANIMATOR]: Setting movement animation {0} for {1}",
            //        anim, m_scenePresence.Name);

            if (!aoSitGndAnim.IsZero())
            {
                avnChangeAnim(aoSitGndAnim, false, true);
                aoSitGndAnim = UUID.Zero;
            }

            if (m_scenePresence.Overrides.TryGetOverriddenAnimation(anim, out UUID overridenAnim))
            {
                if (anim.Equals("SITGROUND"))
                {
                    UUID defsit = DefaultAvatarAnimations.AnimsUUIDbyName["SIT_GROUND_CONSTRAINED"];
                    if (defsit.IsZero())
                        return false;
                    m_animations.SetDefaultAnimation(defsit, m_scenePresence.ControllingClient.NextAnimationSequenceNumber, m_scenePresence.UUID);
                    aoSitGndAnim = overridenAnim;
                    avnChangeAnim(overridenAnim, true, false);
                }
                else
                {
                    m_animations.SetDefaultAnimation(overridenAnim, m_scenePresence.ControllingClient.NextAnimationSequenceNumber, m_scenePresence.UUID);
                }
                m_scenePresence.SendScriptChangedEventToAttachments(Changed.ANIMATION);
                SendAnimPack();
                return true;
            }

            // translate sit and sitground state animations
            if (anim.Equals("SIT") || anim.Equals("SITGROUND"))
                anim = m_scenePresence.sitAnimation;

            if (m_animations.TrySetDefaultAnimation(anim, m_scenePresence.ControllingClient.NextAnimationSequenceNumber, m_scenePresence.UUID))
            {
                //m_log.DebugFormat(
                //    "[SCENE PRESENCE ANIMATOR]: Updating movement animation to {0} for {1}",
                //       anim, m_scenePresence.Name);

                m_scenePresence.SendScriptChangedEventToAttachments(Changed.ANIMATION);
                SendAnimPack();
                return true;
            }
            return false;
        }

        public enum motionControlStates : byte
        {
            sitted = 0,
            flying,
            falling,
            prejumping,
            jumping,
            landing,
            onsurface
        }

        public motionControlStates currentControlState = motionControlStates.onsurface;

        /// <summary>
        /// This method determines the proper movement related animation
        /// </summary>
        private string DetermineMovementAnimation()
        {
            const int FALL_DELAY = 800;
            const int PREJUMP_DELAY = 450;
            const int JUMP_PERIOD = 1050;
            #region Inputs

            if (m_scenePresence.IsInTransit)
                return CurrentMovementAnimation;

            if (m_scenePresence.SitGround)
            {
                currentControlState = motionControlStates.sitted;
                isJumping = false;
                Falling = false;
                return "SITGROUND";
            }
            if (m_scenePresence.ParentID != 0 || !m_scenePresence.ParentUUID.IsZero())
            {
                currentControlState = motionControlStates.sitted;
                isJumping = false;
                Falling = false;
                return "SIT";
            }

            AgentManager.ControlFlags controlFlags = (AgentManager.ControlFlags)m_scenePresence.AgentControlFlags;

            const AgentManager.ControlFlags ANYXYMASK = (
                AgentManager.ControlFlags.AGENT_CONTROL_AT_POS | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_POS |
                AgentManager.ControlFlags.AGENT_CONTROL_AT_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_AT_NEG |
                AgentManager.ControlFlags.AGENT_CONTROL_LEFT_POS | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_POS |
                AgentManager.ControlFlags.AGENT_CONTROL_LEFT_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_LEFT_NEG
                );
            const AgentManager.ControlFlags ANYXYZMASK = (ANYXYMASK |
                AgentManager.ControlFlags.AGENT_CONTROL_UP_POS | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_POS |
                AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG
                );

             // excluded nudge up so it doesn't trigger jump state
            bool heldUp = ((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_UP_POS)) != 0);
            bool heldDown = ((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_UP_NEG | AgentManager.ControlFlags.AGENT_CONTROL_NUDGE_UP_NEG)) != 0);
            //bool flying = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_FLY) == AgentManager.ControlFlags.AGENT_CONTROL_FLY;
            //bool mouselook = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_MOUSELOOK) == AgentManager.ControlFlags.AGENT_CONTROL_MOUSELOOK;

            bool heldOnXY = ((controlFlags & ANYXYMASK) != 0);
            bool heldTurnLeft;
            bool heldTurnRight;
            if ((controlFlags & ANYXYZMASK) != 0)
            {
                heldTurnLeft = false;
                heldTurnRight = false;
            }
            else
            {
                heldTurnLeft = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_TURN_LEFT) != 0;
                heldTurnRight = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_TURN_RIGHT) != 0;
                //heldTurnLeft = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_YAW_POS) != 0;
                //heldTurnRight = (controlFlags & AgentManager.ControlFlags.AGENT_CONTROL_YAW_NEG) != 0;
            }


            #endregion Inputs

            PhysicsActor actor = m_scenePresence.PhysicsActor;
            // no physics actor case
            if (actor == null)
            {
                isJumping = false;
                Falling = false;

                // well what to do?
                currentControlState = motionControlStates.onsurface;
                if (heldOnXY)
                    return "WALK";

                return "STAND";
            }

            bool isColliding = actor.IsColliding;

            #region Flying
            if (actor.Flying)
            {
                m_animTickFall = 0;
                m_animTickJump = 0;
                isJumping = false;
                Falling = false;

                currentControlState = motionControlStates.flying;

                if (heldOnXY)
                {
                    return (m_scenePresence.Scene.m_useFlySlow ? "FLYSLOW" : "FLY");
                }
                else if (heldUp)
                {
                    return "HOVER_UP";
                }
                else if (heldDown)
                {
                    if (isColliding)
                    {
                        actor.Flying = false;
                        currentControlState = motionControlStates.landing;
                        m_animTickLand = Environment.TickCount;
                        return "LAND";
                    }
                    else
                        return "HOVER_DOWN";
                }
                else
                {
                    return "HOVER";
                }
            }
            else
            {
                if (isColliding && currentControlState == motionControlStates.flying)
                {
                    currentControlState = motionControlStates.landing;
                    m_animTickLand = Environment.TickCount;
                    return "LAND";
                }
            }

            #endregion Flying

            #region Falling/Floating/Landing

            if (!isColliding && currentControlState != motionControlStates.jumping)
            {
                
                if(actor.PIDHoverActive)
                {
                    m_animTickFall = 0;
                    m_animTickJump = 0;
                    Falling = false;
                    currentControlState = motionControlStates.flying;
                    m_lastFallVelocity = 0f;
                    return "HOVER";
                }

                float fallVelocity = actor.Velocity.Z;
                if (fallVelocity < -2.5f)
                    Falling = true;

                if (m_animTickFall == 0 || (fallVelocity >= -0.5f))
                {
                    m_animTickFall = Environment.TickCount;
                }
                else
                {
                    int fallElapsed = (Environment.TickCount - m_animTickFall);
                    if ((fallElapsed > FALL_DELAY) && (fallVelocity < -3.0f))
                    {
                        currentControlState = motionControlStates.falling;
                        m_lastFallVelocity = fallVelocity;
                        // Falling long enough to trigger the animation
                        return "FALLDOWN";
                    }
                }

                // Check if the user has stopped walking just now
                if (CurrentMovementAnimation == "WALK" && !heldOnXY && !heldDown && !heldUp)
                    return "STAND";

                return CurrentMovementAnimation;
            }

            m_animTickFall = 0;

            #endregion Falling/Floating/Landing

            #region Jumping     // section added for jumping...

            if (isColliding && heldUp && !isJumping && !actor.PIDHoverActive)
            {
                isJumping = true;
                Falling = false;
                if ((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_FINISH_ANIM)) == 0)
                {
                    currentControlState = motionControlStates.prejumping;
                    m_animTickJump = Environment.TickCount;
                    // Start jumping, prejump
                    return "PREJUMP";
                }

                m_animTickJump = Environment.TickCount - PREJUMP_DELAY - 1;
                currentControlState = motionControlStates.jumping;
                m_scenePresence.Jump(9.4f);
                return "JUMP";
            }

            if (currentControlState == motionControlStates.prejumping)
            {
                if((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_FINISH_ANIM)) == 0)
                {
                    int jumptime = Environment.TickCount - m_animTickJump;
                    if (jumptime < PREJUMP_DELAY)
                        return CurrentMovementAnimation;
                }

                // jump up
                isJumping = true;
                currentControlState = motionControlStates.jumping;
                m_scenePresence.Jump(9.4f);
                return "JUMP";
            }

            if (currentControlState == motionControlStates.jumping)
            {
                int jumptime = Environment.TickCount - m_animTickJump;
                if ((jumptime > (JUMP_PERIOD * 1.5f)) && actor.IsColliding)
                {
                    // end jumping
                    isJumping = false;
                    Falling = false;
                    actor.Selected = false;      // borrowed for jumping flag
                    m_animTickLand = Environment.TickCount;
                    currentControlState = motionControlStates.landing;
                    return "LAND";
                }
                else if (jumptime > JUMP_PERIOD)
                {
                    // jump down
                    return "JUMP";
                }
                return CurrentMovementAnimation;
            }

            #endregion Jumping

            #region Ground Movement

            if (currentControlState == motionControlStates.falling)
            {
                Falling = false;
                currentControlState = motionControlStates.landing;

                if ((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_FINISH_ANIM)) == 0)
                {
                    float fallVsq = m_lastFallVelocity * m_lastFallVelocity;
                    if (fallVsq > 300f) // aprox 20*h
                    {
                        m_animTickLand = Environment.TickCount + 3000;
                        return "STANDUP";
                    }
                    if (fallVsq > 160f)
                    {
                        m_animTickLand = Environment.TickCount + 1500;
                        return "SOFT_LAND";
                    }
                }
                m_animTickLand = Environment.TickCount + 600;
                return "LAND";
            }

            if (currentControlState == motionControlStates.landing)
            {
                Falling = false;

                if ((controlFlags & (AgentManager.ControlFlags.AGENT_CONTROL_FINISH_ANIM)) == 0)
                {
                    if ((m_animTickLand != 0) && (m_animTickLand > Environment.TickCount))
                        return CurrentMovementAnimation;
                }

                currentControlState = motionControlStates.onsurface;
                m_animTickLand = 0;
                return "STAND";
            }

            // next section moved outside paren. and realigned for jumping

            if (heldOnXY)
            {
                currentControlState = motionControlStates.onsurface;
                Falling = false;
                // Walking / crouchwalking / running
                if (heldDown)
                {
                    return "CROUCHWALK";
                }
                // We need to prevent these animations if the user tries to make their avatar walk or run whilst
                // specifying AGENT_CONTROL_STOP (pressing down space on viewers).
                else if (!m_scenePresence.AgentControlStopActive)
                {
                    if (m_scenePresence.SetAlwaysRun)
                        return "RUN";
                    else
                        return "WALK";
                }
            }
            else
            {
                currentControlState = motionControlStates.onsurface;
                Falling = false;
                // Not walking
                if (heldDown)
                    return "CROUCH";
                else if (heldTurnLeft)
                    return "TURNLEFT";
                else if (heldTurnRight)
                    return "TURNRIGHT";
                else
                    return "STAND";
            }
            #endregion Ground Movement

            return CurrentMovementAnimation;
        }

        /// <summary>
        /// Update the movement animation of this avatar according to its current state
        /// </summary>
        /// <returns>'true' if the animation was changed</returns>
        public bool UpdateMovementAnimations()
        {
            // m_log.DebugFormat("[SCENE PRESENCE ANIMATOR]: Updating movement animations for {0}", m_scenePresence.Name);
            lock (m_animations)
            {
                string newMovementAnimation = DetermineMovementAnimation();
                if (CurrentMovementAnimation.Equals(newMovementAnimation))
                    return false;

                CurrentMovementAnimation = newMovementAnimation;
                //m_log.DebugFormat(
                //    "[SCENE PRESENCE ANIMATOR]: Determined animation {0} for {1} {2} {3} in UpdateMovementAnimations()",
                //    CurrentMovementAnimation, m_scenePresence.Name, isJumping, Falling);

                // Only set it if it's actually changed, give a script
                // a chance to stop a default animation
                return TrySetMovementAnimation(CurrentMovementAnimation);
            }
        }

        public bool ForceUpdateMovementAnimations()
        {
            lock (m_animations)
            {
                CurrentMovementAnimation = DetermineMovementAnimation();
                return TrySetMovementAnimation(CurrentMovementAnimation);
            }
        }

        public bool SetMovementAnimations(string motionState)
        {
            lock (m_animations)
            {
                CurrentMovementAnimation = motionState;
                return TrySetMovementAnimation(CurrentMovementAnimation);
            }
        }

        public UUID[] GetAnimationArray()
        {
            m_animations.GetAnimationIDsArray(out UUID[] animIDs);
            return animIDs;
        }

        public bool HasAnimation(UUID animID)
        {
            return m_animations.HasAnimation(animID);
        }

        public BinBVHAnimation GenerateRandomAnimation()
        {
            int rnditerations = 3;
            BinBVHAnimation anim = new BinBVHAnimation();
            List<string> parts = new List<string>();

            /// Torso and Head
            parts.Add("mPelvis");
            parts.Add("mTorso");
            parts.Add("mChest");
            parts.Add("mNeck");
            parts.Add("mHead");
            parts.Add("mSkull");
            parts.Add("mEyeRight");
            parts.Add("mEyeLeft");
            /// Arms
            parts.Add("mCollarLeft");
            parts.Add("mShoulderLeft");
            parts.Add("mElbowLeft");
            parts.Add("mWristLeft");
            parts.Add("mCollarRight");
            parts.Add("mShoulderRight");
            parts.Add("mElbowRight");
            parts.Add("mWristRight");
            /// Legs
            parts.Add("mHipLeft");
            parts.Add("mKneeLeft");
            parts.Add("mAnkleLeft");
            parts.Add("mFootLeft");
            parts.Add("mToeLeft");
            parts.Add("mHipRight");
            parts.Add("mKneeRight");
            parts.Add("mAnkleRight");
            parts.Add("mFootRight");
            parts.Add("mToeRight");
            ///Hands
            parts.Add("mHandThumb1Left");
            parts.Add("mHandThumb1Right");
            parts.Add("mHandThumb2Left");
            parts.Add("mHandThumb2Right");
            parts.Add("mHandThumb3Left");
            parts.Add("mHandThumb3Right");
            parts.Add("mHandIndex1Left");
            parts.Add("mHandIndex1Right");
            parts.Add("mHandIndex2Left");
            parts.Add("mHandIndex2Right");
            parts.Add("mHandIndex3Left");
            parts.Add("mHandIndex3Right");
            parts.Add("mHandMiddle1Left");
            parts.Add("mHandMiddle1Right");
            parts.Add("mHandMiddle2Left");
            parts.Add("mHandMiddle2Right");
            parts.Add("mHandMiddle3Left");
            parts.Add("mHandMiddle3Right");
            parts.Add("mHandRing1Left");
            parts.Add("mHandRing1Right");
            parts.Add("mHandRing2Left");
            parts.Add("mHandRing2Right");
            parts.Add("mHandRing3Left");
            parts.Add("mHandRing3Right");
            parts.Add("mHandPinky1Left");
            parts.Add("mHandPinky1Right");
            parts.Add("mHandPinky2Left");
            parts.Add("mHandPinky2Right");
            parts.Add("mHandPinky3Left");
            parts.Add("mHandPinky3Right");
            ///Face
            parts.Add("mFaceForeheadLeft");
            parts.Add("mFaceForeheadCenter");
            parts.Add("mFaceForeheadRight");
            parts.Add("mFaceEyebrowOuterLeft");
            parts.Add("mFaceEyebrowCenterLeft");
            parts.Add("mFaceEyebrowInnerLeft");
            parts.Add("mFaceEyebrowOuterRight");
            parts.Add("mFaceEyebrowCenterRight");
            parts.Add("mFaceEyebrowInnerRight");
            parts.Add("mFaceEyeLidUpperLeft");
            parts.Add("mFaceEyeLidLowerLeft");
            parts.Add("mFaceEyeLidUpperRight");
            parts.Add("mFaceEyeLidLowerRight");
            parts.Add("mFaceEyeAltLeft");
            parts.Add("mFaceEyeAltRight");
            parts.Add("mFaceEyecornerInnerLeft");
            parts.Add("mFaceEyecornerInnerRight");
            parts.Add("mFaceEar1Left");
            parts.Add("mFaceEar2Left");
            parts.Add("mFaceEar1Right");
            parts.Add("mFaceEar2Right");
            parts.Add("mFaceNoseLeft");
            parts.Add("mFaceNoseCenter");
            parts.Add("mFaceNoseRight");
            parts.Add("mFaceNoseBase");
            parts.Add("mFaceNoseBridge");
            parts.Add("mFaceCheekUpperInnerLeft");
            parts.Add("mFaceCheekUpperOuterLeft");
            parts.Add("mFaceCheekUpperInnerRight");
            parts.Add("mFaceCheekUpperOuterRight");
            parts.Add("mFaceJaw");
            parts.Add("mFaceLipUpperLeft");
            parts.Add("mFaceLipUpperCenter");
            parts.Add("mFaceLipUpperRight");
            parts.Add("mFaceLipCornerLeft");
            parts.Add("mFaceLipCornerRight");
            parts.Add("mFaceTongueBase");
            parts.Add("mFaceTongueTip");
            parts.Add("mFaceLipLowerLeft");
            parts.Add("mFaceLipLowerCenter");
            parts.Add("mFaceLipLowerRight");
            parts.Add("mFaceTeethLower");
            parts.Add("mFaceTeethUpper");
            parts.Add("mFaceChin");
            ///Spine
            parts.Add("mSpine1");
            parts.Add("mSpine2");
            parts.Add("mSpine3");
            parts.Add("mSpine4");
            ///Wings
            parts.Add("mWingsRoot");
            parts.Add("mWing1Left");
            parts.Add("mWing2Left");
            parts.Add("mWing3Left");
            parts.Add("mWing4Left");
            parts.Add("mWing1Right");
            parts.Add("mWing2Right");
            parts.Add("mWing3Right");
            parts.Add("mWing4Right");
            parts.Add("mWing4FanRight");
            parts.Add("mWing4FanLeft");
            ///Hind Limbs
            parts.Add("mHindLimbsRoot");
            parts.Add("mHindLimb1Left");
            parts.Add("mHindLimb2Left");
            parts.Add("mHindLimb3Left");
            parts.Add("mHindLimb4Left");
            parts.Add("mHindLimb1Right");
            parts.Add("mHindLimb2Right");
            parts.Add("mHindLimb3Right");
            parts.Add("mHindLimb4Right");
            ///Tail
            parts.Add("mTail1");
            parts.Add("mTail2");
            parts.Add("mTail3");
            parts.Add("mTail4");
            parts.Add("mTail5");
            parts.Add("mTail6");

            anim.HandPose = 1;
            anim.InPoint = 0;
            anim.OutPoint = (rnditerations * .10f);
            anim.Priority = 7;
            anim.Loop = false;
            anim.Length = (rnditerations * .10f);
            anim.ExpressionName = "afraid";
            anim.EaseInTime = 0;
            anim.EaseOutTime = 0;

            string[] strjoints = parts.ToArray();
            anim.Joints = new binBVHJoint[strjoints.Length];
            for (int j = 0; j < strjoints.Length; j++)
            {
                anim.Joints[j] = new binBVHJoint();
                anim.Joints[j].Name = strjoints[j];
                anim.Joints[j].Priority = 7;
                anim.Joints[j].positionkeys = new binBVHJointKey[rnditerations];
                anim.Joints[j].rotationkeys = new binBVHJointKey[rnditerations];
                for (int i = 0; i < rnditerations; i++)
                {
                    anim.Joints[j].rotationkeys[i] = new binBVHJointKey();
                    anim.Joints[j].rotationkeys[i].time = (i * .10f);
                    anim.Joints[j].rotationkeys[i].key_element.X = ((float)Random.Shared.NextDouble() * 2 - 1);
                    anim.Joints[j].rotationkeys[i].key_element.Y = ((float)Random.Shared.NextDouble() * 2 - 1);
                    anim.Joints[j].rotationkeys[i].key_element.Z = ((float)Random.Shared.NextDouble() * 2 - 1);
                    anim.Joints[j].positionkeys[i] = new binBVHJointKey();
                    anim.Joints[j].positionkeys[i].time = (i * .10f);
                    anim.Joints[j].positionkeys[i].key_element.X = 0;
                    anim.Joints[j].positionkeys[i].key_element.Y = 0;
                    anim.Joints[j].positionkeys[i].key_element.Z = 0;
                }
            }

            AssetBase Animasset = new AssetBase(UUID.Random(), "Random Animation", (sbyte)AssetType.Animation, m_scenePresence.UUID.ToString());
            Animasset.Data = anim.ToBytes();
            Animasset.Temporary = true;
            Animasset.Local = true;
            Animasset.Description = "dance";
            //BinBVHAnimation bbvhanim = new BinBVHAnimation(Animasset.Data);

            m_scenePresence.Scene.AssetService.Store(Animasset);
            AddAnimation(Animasset.FullID, m_scenePresence.UUID);
            return anim;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="animations"></param>
        /// <param name="seqs"></param>
        /// <param name="objectIDs"></param>
        public void SendAnimPack(UUID[] animations, int[] seqs, UUID[] objectIDs)
        {
            m_scenePresence.SendAnimPack(animations, seqs, objectIDs);
        }

        public void GetArrays(out UUID[] animIDs, out int[] sequenceNums, out UUID[] objectIDs)
        {
            animIDs = null;
            sequenceNums = null;
            objectIDs = null;

            if (m_animations != null)
                m_animations.GetArrays(out animIDs, out sequenceNums, out objectIDs);
        }

        public void SendAnimPackToClient(IClientAPI client)
        {
            if (m_scenePresence.IsChildAgent)
                return;

            UUID[] animIDs;
            int[] sequenceNums;
            UUID[] objectIDs;

            m_animations.GetArrays(out animIDs, out sequenceNums, out objectIDs);
            client.SendAnimations(animIDs, sequenceNums, m_scenePresence.ControllingClient.AgentId, objectIDs);
        }

        /// <summary>
        /// Send animation information about this avatar to all clients.
        /// </summary>
        public void SendAnimPack()
        {
            //m_log.Debug("Sending animation pack to all");

            if (m_scenePresence.IsChildAgent)
                return;

            UUID[] animIDs;
            int[] sequenceNums;
            UUID[] objectIDs;

            m_animations.GetArrays(out animIDs, out sequenceNums, out objectIDs);

            m_scenePresence.SendAnimPack(animIDs, sequenceNums, objectIDs);
        }

        public string GetAnimName(UUID animId)
        {
            string animName;

            if (!DefaultAvatarAnimations.AnimsNamesbyUUID.TryGetValue(animId, out animName))
            {
                AssetMetadata amd = m_scenePresence.Scene.AssetService.GetMetadata(animId.ToString());
                if (amd != null)
                    animName = amd.Name;
                else
                    animName = "Unknown";
            }

            return animName;
        }
    }
}
