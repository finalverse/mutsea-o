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

using log4net;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MutSea.Framework;
using OpenMetaverse;

namespace MutSea.Region.PhysicsModules.SharedBase
{
    public delegate void PositionUpdate(Vector3 position);
    public delegate void VelocityUpdate(Vector3 velocity);
    public delegate void OrientationUpdate(Quaternion orientation);

    public enum ActorTypes : int
    {
        Unknown = 0,
        Agent = 1,
        Prim = 2,
        Ground = 3,
        Water = 4
    }

    public enum PIDHoverType
    {
        Ground,
        GroundAndWater,
        Water,
        Absolute
    }

    public class CameraData
    {
        public Quaternion CameraRotation;
        public Vector3 CameraAtAxis;
        public bool MouseLook;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 16)]
    public struct AABB2D
    {
        public float minx;
        public float maxx;
        public float miny;
        public float maxy;
    }

    public struct ContactPoint
    {
        public Vector3 Position;
        public Vector3 SurfaceNormal;
        public float PenetrationDepth;
        public float RelativeSpeed;
        public bool CharacterFeet;

        public ContactPoint(Vector3 position, Vector3 surfaceNormal, float penetrationDepth)
        {
            Position = position;
            SurfaceNormal = surfaceNormal;
            PenetrationDepth = penetrationDepth;
            RelativeSpeed = 0f; // for now let this one be set explicity
            CharacterFeet = true;  // keep other plugins work as before
        }

        public ContactPoint(Vector3 position, Vector3 surfaceNormal, float penetrationDepth, bool feet)
        {
            Position = position;
            SurfaceNormal = surfaceNormal;
            PenetrationDepth = penetrationDepth;
            RelativeSpeed = 0f; // for now let this one be set explicity
            CharacterFeet = feet;  // keep other plugins work as before
        }
    }

    public struct ContactData
    {
        public float mu;
        public float bounce;
        public bool softcolide;

        public ContactData(float _mu, float _bounce, bool _softcolide)
        {
            mu = _mu;
            bounce = _bounce;
            softcolide = _softcolide;
        }
    }
    /// <summary>
    /// Used to pass collision information to OnCollisionUpdate listeners.
    /// </summary>
    public class CollisionEventUpdate : EventArgs
    {
        /// <summary>
        /// Number of collision events in this update.
        /// </summary>
        public int Count { get { return m_objCollisionList.Count; } }

        public bool CollisionsOnPreviousFrame { get; private set; }

        public Dictionary<uint, ContactPoint> m_objCollisionList;

        public CollisionEventUpdate(Dictionary<uint, ContactPoint> objCollisionList)
        {
            m_objCollisionList = objCollisionList;
        }

        public CollisionEventUpdate()
        {
            m_objCollisionList = new Dictionary<uint, ContactPoint>();
        }

        public void AddCollider(uint localID, ContactPoint contact)
        {
            ref ContactPoint curcp = ref CollectionsMarshal.GetValueRefOrAddDefault(m_objCollisionList, localID, out bool ex);
            if (ex)
            {
                if (curcp.PenetrationDepth < contact.PenetrationDepth)
                {
                    if (Math.Abs(curcp.PenetrationDepth) > Math.Abs(contact.RelativeSpeed))
                        contact.RelativeSpeed = curcp.PenetrationDepth;
                    curcp = contact;
                }
                else if (MathF.Abs(curcp.RelativeSpeed) < MathF.Abs(contact.RelativeSpeed))
                {
                    curcp.RelativeSpeed = contact.RelativeSpeed;
                }
            }
            else
                curcp = contact;
        }

        /// <summary>
        /// Clear added collision events.
        /// </summary>
        public void Clear()
        {
            m_objCollisionList.Clear();
        }
    }




    public abstract class PhysicsActor
    {
        //private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public delegate void RequestTerseUpdate();
        public delegate void CollisionUpdate(EventArgs e);
        public delegate void OutOfBounds(Vector3 pos);
        public delegate CameraData GetCameraData();

        // disable warning: public events
#pragma warning disable 67
        public event PositionUpdate OnPositionUpdate;
        public event VelocityUpdate OnVelocityUpdate;
        public event OrientationUpdate OnOrientationUpdate;
        public event RequestTerseUpdate OnRequestTerseUpdate;
        public event GetCameraData OnPhysicsRequestingCameraData;

        /// <summary>
        /// Subscribers to this event must synchronously handle the dictionary of collisions received, since the event
        /// object is reused in subsequent physics frames.
        /// </summary>
        public event CollisionUpdate OnCollisionUpdate;

        public event OutOfBounds OnOutOfBounds;
#pragma warning restore 67


        public CameraData TryGetCameraData()
        {
            GetCameraData handler = OnPhysicsRequestingCameraData;
            return (handler == null) ? null : handler();
        }

        public static PhysicsActor Null
        {
            get { return new NullPhysicsActor(); }
        }

        public virtual bool Building { get; set; }

        public virtual void getContactData(ref ContactData cdata)
        {
            cdata.mu = 0;
            cdata.bounce = 0;
        }


        public abstract bool Stopped { get; }

        public abstract Vector3 Size { get; set; }

        public virtual void setAvatarSize(Vector3 size, float feetOffset)
        {
            Size = size;
        }

        public virtual bool Phantom { get; set; }

        public virtual bool IsVolumeDtc
        {
            get { return false; }
            set { return; }
        }

        public virtual byte PhysicsShapeType { get; set; }

        public abstract PrimitiveBaseShape Shape { set; }

        public uint m_baseLocalID;
        public virtual uint LocalID
        {
            set { m_baseLocalID = value; }
            get { return m_baseLocalID; }
        }

        public abstract bool Grabbed { set; }

        public abstract bool Selected { set; }

        /// <summary>
        /// Name of this actor.
        /// </summary>
        /// <remarks>
        /// XXX: Bizarrely, this cannot be "Terrain" or "Water" right now unless it really is simulating terrain or
        /// water.  This is not a problem due to the formatting of names given by prims and avatars.
        /// </remarks>
        public string Name { get; set; }

        /// <summary>
        /// This is being used by ODE joint code.
        /// </summary>
        public string SOPName;

        public virtual void CrossingStart() { }
        public abstract void CrossingFailure();

        public abstract void link(PhysicsActor obj);

        public abstract void delink();

        public abstract void LockAngularMotion(byte axislocks);

        public virtual void RequestPhysicsterseUpdate()
        {
            OnRequestTerseUpdate?.Invoke();
        }

        public virtual void RaiseOutOfBounds(Vector3 pos)
        {
            OnOutOfBounds?.Invoke(pos);
        }

        public virtual void SendCollisionUpdate(EventArgs e)
        {
            OnCollisionUpdate?.Invoke(e);
        }

        public virtual void SetMaterial (int material) { }
        public virtual float Density { get; set; }
        public virtual float GravModifier { get; set; }
        public virtual float Friction { get; set; }
        public virtual float Restitution { get; set; }

        /// <summary>
        /// Position of this actor.
        /// </summary>
        /// <remarks>
        /// Setting this directly moves the actor to a given position.
        /// Getting this retrieves the position calculated by physics scene updates, using factors such as velocity and
        /// collisions.
        /// </remarks>
        public abstract Vector3 Position { get; set; }

        public abstract float Mass { get; }
        public abstract Vector3 Force { get; set; }

        public abstract int VehicleType { get; set; }
        public abstract void VehicleFloatParam(int param, float value);
        public abstract void VehicleVectorParam(int param, Vector3 value);
        public abstract void VehicleRotationParam(int param, Quaternion rotation);
        public abstract void VehicleFlags(int param, bool remove);

        // This is an overridable version of SetVehicle() that works for all physics engines.
        // This is VERY inefficient. It behoves any physics engine to override this and
        //     implement a more efficient setting of all the vehicle parameters.
        public virtual void SetVehicle(object pvdata)
        {
            VehicleData vdata = (VehicleData)pvdata;
            // vehicleActor.ProcessSetVehicle((VehicleData)vdata);

            this.VehicleType = (int)vdata.m_type;
            this.VehicleFlags(-1, false);   // clears all flags
            this.VehicleFlags((int)vdata.m_flags, false);

            // Linear properties
            this.VehicleVectorParam((int)Vehicle.LINEAR_MOTOR_DIRECTION, vdata.m_linearMotorDirection);
            this.VehicleVectorParam((int)Vehicle.LINEAR_FRICTION_TIMESCALE, vdata.m_linearFrictionTimescale);
            this.VehicleFloatParam((int)Vehicle.LINEAR_MOTOR_DECAY_TIMESCALE, vdata.m_linearMotorDecayTimescale);
            this.VehicleFloatParam((int)Vehicle.LINEAR_MOTOR_TIMESCALE, vdata.m_linearMotorTimescale);
            this.VehicleVectorParam((int)Vehicle.LINEAR_MOTOR_OFFSET, vdata.m_linearMotorOffset);

            //Angular properties
            this.VehicleVectorParam((int)Vehicle.ANGULAR_MOTOR_DIRECTION, vdata.m_angularMotorDirection);
            this.VehicleFloatParam((int)Vehicle.ANGULAR_MOTOR_TIMESCALE, vdata.m_angularMotorTimescale);
            this.VehicleFloatParam((int)Vehicle.ANGULAR_MOTOR_DECAY_TIMESCALE, vdata.m_angularMotorDecayTimescale);
            this.VehicleVectorParam((int)Vehicle.ANGULAR_FRICTION_TIMESCALE, vdata.m_angularFrictionTimescale);

            //Deflection properties
            this.VehicleFloatParam((int)Vehicle.ANGULAR_DEFLECTION_EFFICIENCY, vdata.m_angularDeflectionEfficiency);
            this.VehicleFloatParam((int)Vehicle.ANGULAR_DEFLECTION_TIMESCALE, vdata.m_angularDeflectionTimescale);
            this.VehicleFloatParam((int)Vehicle.LINEAR_DEFLECTION_EFFICIENCY, vdata.m_linearDeflectionEfficiency);
            this.VehicleFloatParam((int)Vehicle.LINEAR_DEFLECTION_TIMESCALE, vdata.m_linearDeflectionTimescale);

            //Banking properties
            this.VehicleFloatParam((int)Vehicle.BANKING_EFFICIENCY, vdata.m_bankingEfficiency);
            this.VehicleFloatParam((int)Vehicle.BANKING_MIX, vdata.m_bankingMix);
            this.VehicleFloatParam((int)Vehicle.BANKING_TIMESCALE, vdata.m_bankingTimescale);

            //Hover and Buoyancy properties
            this.VehicleFloatParam((int)Vehicle.HOVER_HEIGHT, vdata.m_VhoverHeight);
            this.VehicleFloatParam((int)Vehicle.HOVER_EFFICIENCY, vdata.m_VhoverEfficiency);
            this.VehicleFloatParam((int)Vehicle.HOVER_TIMESCALE, vdata.m_VhoverTimescale);
            this.VehicleFloatParam((int)Vehicle.BUOYANCY, vdata.m_VehicleBuoyancy);

            //Attractor properties
            this.VehicleFloatParam((int)Vehicle.VERTICAL_ATTRACTION_EFFICIENCY, vdata.m_verticalAttractionEfficiency);
            this.VehicleFloatParam((int)Vehicle.VERTICAL_ATTRACTION_TIMESCALE, vdata.m_verticalAttractionTimescale);

            this.VehicleRotationParam((int)Vehicle.REFERENCE_FRAME, vdata.m_referenceFrame);
        }


        /// <summary>
        /// Allows the detection of collisions with inherently non-physical prims. see llVolumeDetect for more
        /// </summary>
        public abstract void SetVolumeDetect(int param);

        public abstract Vector3 GeometricCenter { get; }
        public abstract Vector3 CenterOfMass { get; }

        public virtual float PhysicsCost
        {
            get
            {
                return 0.1f;
            }
        }

        public virtual float StreamCost
        {
            get
            {
                return 1.0f;
            }
        }

        /// <summary>
        /// The desired velocity of this actor.
        /// </summary>
        /// <remarks>
        /// Setting this provides a target velocity for physics scene updates.
        /// Getting this returns the last set target. Fetch Velocity to get the current velocity.
        /// </remarks>
        protected Vector3 m_targetVelocity;
        public virtual Vector3 TargetVelocity
        {
            get { return m_targetVelocity; }
            set {
                m_targetVelocity = value;
                Velocity = m_targetVelocity;
            }
        }

        public abstract Vector3 Velocity { get; set; }
        public virtual Vector3 rootVelocity { get { return Vector3.Zero; } }

        public abstract Vector3 Torque { get; set; }
        public abstract float CollisionScore { get; set;}
        public abstract Vector3 Acceleration { get; set; }
        public abstract Quaternion Orientation { get; set; }
        public abstract int PhysicsActorType { get; set; }
        public abstract bool IsPhysical { get; set; }
        public abstract bool Flying { get; set; }
        public abstract bool SetAlwaysRun { get; set; }
        public abstract bool ThrottleUpdates { get; set; }
        public abstract bool IsColliding { get; set; }
        public abstract bool CollidingGround { get; set; }
        public abstract bool CollidingObj { get; set; }
        public virtual bool FloatOnWater { set { return; } }
        public abstract Vector3 RotationalVelocity { get; set; }
        public abstract bool Kinematic { get; set; }
        public abstract float Buoyancy { get; set; }

        // Used for MoveTo
        public abstract Vector3 PIDTarget { set; }
        public abstract bool PIDActive { get; set; }
        public abstract float PIDTau { set; }

        // Used for llSetHoverHeight and maybe vehicle height
        // Hover Height will override MoveTo target's Z
        public abstract bool PIDHoverActive {get; set;}
        public abstract float PIDHoverHeight { set;}
        public abstract PIDHoverType PIDHoverType { set;}
        public abstract float PIDHoverTau { set;}

        // For RotLookAt
        public abstract Quaternion APIDTarget { set;}
        public abstract bool APIDActive { set;}
        public abstract float APIDStrength { set;}
        public abstract float APIDDamping { set;}

        public abstract void AddForce(Vector3 force, bool pushforce);
        public abstract void AvatarJump(float forceZ);
        public abstract void AddAngularForce(Vector3 force, bool pushforce);
        public abstract void SetMomentum(Vector3 momentum);
        public abstract void SubscribeEvents(int ms);
        public abstract void UnSubscribeEvents();
        public abstract bool SubscribedEvents();

        public virtual void AddCollisionEvent(uint CollidedWith, ContactPoint contact) { }
        public virtual void AddVDTCCollisionEvent(uint CollidedWith, ContactPoint contact) { }

        public virtual PhysicsInertiaData GetInertiaData()
        {
            PhysicsInertiaData data = new()
            {
                TotalMass = Mass,
                CenterOfMass = CenterOfMass - Position,
                Inertia = Vector3.Zero,
                InertiaRotation = Vector4.Zero
            };
            return data;
        }

        public virtual void SetInertiaData(PhysicsInertiaData inertia)
        {
        }

        public virtual float SimulationSuspended { get; set; }

        // Warning in a parent part it returns itself, not null
        public virtual PhysicsActor ParentActor { get { return this; } }


        // Extendable interface for new, physics engine specific operations
        public virtual object Extension(string pFunct, params object[] pParams)
        {
            // A NOP of the physics engine does not implement this feature
            return null;
        }
    }

    public class NullPhysicsActor : PhysicsActor
    {
        private ActorTypes m_actorType = ActorTypes.Unknown;

        public override bool Stopped
        {
            get{ return true; }
        }

        public override Vector3 Position
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override bool SetAlwaysRun
        {
            get { return false; }
            set { return; }
        }

        public override uint LocalID
        {
            get { return 0; }
            set { return; }
        }

        public override bool Grabbed
        {
            set { return; }
        }

        public override bool Selected
        {
            set { return; }
        }

        public override float Buoyancy
        {
            get { return 0f; }
            set { return; }
        }

        public override bool CollidingGround
        {
            get { return false; }
            set { return; }
        }

        public override bool CollidingObj
        {
            get { return false; }
            set { return; }
        }

        public override Vector3 Size
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override float Mass
        {
            get { return 0f; }
        }

        public override Vector3 Force
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override int VehicleType
        {
            get { return 0; }
            set { return; }
        }

        public override void VehicleFloatParam(int param, float value) {}
        public override void VehicleVectorParam(int param, Vector3 value) { }
        public override void VehicleRotationParam(int param, Quaternion rotation) { }
        public override void VehicleFlags(int param, bool remove) { }
        public override void SetVolumeDetect(int param) {}
        public override void SetMaterial(int material) {}
        public override Vector3 CenterOfMass { get { return Vector3.Zero; }}

        public override Vector3 GeometricCenter { get { return Vector3.Zero; }}

        public override PrimitiveBaseShape Shape { set { return; }}

        public override Vector3 Velocity
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override Vector3 Torque
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override float CollisionScore
        {
            get { return 0f; }
            set { }
        }

        public override void CrossingFailure() {}

        public override Quaternion Orientation
        {
            get { return Quaternion.Identity; }
            set { }
        }

        public override Vector3 Acceleration
        {
            get { return Vector3.Zero; }
            set { }
        }

        public override bool IsPhysical
        {
            get { return false; }
            set { return; }
        }

        public override bool Flying
        {
            get { return false; }
            set { return; }
        }

        public override bool ThrottleUpdates
        {
            get { return false; }
            set { return; }
        }

        public override bool IsColliding
        {
            get { return false; }
            set { return; }
        }

        public override int PhysicsActorType
        {
            get { return (int)m_actorType; }
            set {
                ActorTypes type = (ActorTypes)value;
                m_actorType = type switch
                {
                    ActorTypes.Ground or ActorTypes.Water => type,
                    _ => ActorTypes.Unknown,
                };
            }
        }

        public override bool Kinematic
        {
            get { return true; }
            set { return; }
        }

        public override void link(PhysicsActor obj) { }
        public override void delink() { }
        public override void LockAngularMotion(byte axislocks) { }
        public override void AvatarJump(float forceZ) { }
        public override void AddForce(Vector3 force, bool pushforce) { }
        public override void AddAngularForce(Vector3 force, bool pushforce) { }

        public override Vector3 RotationalVelocity
        {
            get { return Vector3.Zero; }
            set { return; }
        }

        public override Vector3 PIDTarget { set { return; } }

        public override bool PIDActive
        {
            get { return false; }
            set { return; }
        }

        public override float PIDTau { set { return; } }

        public override float PIDHoverHeight { set { return; } }
        public override bool PIDHoverActive {get {return false;} set { return; } }
        public override PIDHoverType PIDHoverType { set { return; } }
        public override float PIDHoverTau { set { return; } }

        public override Quaternion APIDTarget { set { return; } }
        public override bool APIDActive { set { return; } }
        public override float APIDStrength { set { return; } }
        public override float APIDDamping { set { return; } }

        public override void SetMomentum(Vector3 momentum) { }

        public override void SubscribeEvents(int ms) { }
        public override void UnSubscribeEvents() { }
        public override bool SubscribedEvents() { return false; }
    }
}
