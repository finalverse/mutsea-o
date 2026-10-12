// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text;

namespace Finalverse.World;

// Strict structural validator for package-supplied native keyframe motions
// (LLKeyframeMotion v1.0 serialization). Package motions may not carry emotes,
// hand poses or constraints, may translate only the pelvis and must be fully
// consumed; joint names can be checked against the pinned skeleton.
public static class NativeKeyframe
{
    public sealed record Summary(double duration,bool loop,int priority,string[] joints);
    public const int MaxBytes=64*1024,MaxJoints=64,MaxKeys=1024;

    public static Summary Validate(byte[] data,IReadOnlySet<string>? allowedJoints=null)
    {
        ContentCatalog.Require(data is {Length:>0 and <=MaxBytes},"Keyframe motion size is outside bounds.");
        int at=0;
        T Take<T>(int size,Func<int,T> read){ContentCatalog.Require(at+size<=data.Length,"Truncated keyframe motion.");var v=read(at);at+=size;return v;}
        ushort U16()=>Take(2,i=>BitConverter.ToUInt16(data,i));
        int S32()=>Take(4,i=>BitConverter.ToInt32(data,i));
        uint U32()=>Take(4,i=>BitConverter.ToUInt32(data,i));
        float F32(){var v=Take(4,i=>BitConverter.ToSingle(data,i));ContentCatalog.Require(float.IsFinite(v),"Non-finite keyframe value.");return v;}
        string Str(int limit)
        {
            int end=Array.IndexOf(data,(byte)0,at);ContentCatalog.Require(end>=at&&end-at<=limit,"Unterminated keyframe string.");
            var s=Encoding.UTF8.GetString(data,at,end-at);at=end+1;return s;
        }
        ContentCatalog.Require(U16()==1&&U16()==0,"Only keyframe motion version 1.0 is accepted.");
        int priority=S32();ContentCatalog.Require(priority is >=0 and <=4,"Motion priority must be 0-4.");
        double duration=F32();ContentCatalog.Require(duration is >0 and <=30,"Motion duration must be within 30 seconds.");
        ContentCatalog.Require(Str(64)=="","Package motions may not trigger emotes.");
        double loopIn=F32(),loopOut=F32();bool loop=S32() switch{0=>false,1=>true,_=>throw new InvalidDataException("Invalid loop flag.")};
        double easeIn=F32(),easeOut=F32();
        ContentCatalog.Require(loopIn>=0&&loopIn<=loopOut&&loopOut<=duration+1e-4&&easeIn>=0&&easeOut>=0&&easeIn<=duration&&easeOut<=duration,"Invalid loop or ease range.");
        ContentCatalog.Require(U32()==0,"Package motions may not set hand poses.");
        uint count=U32();ContentCatalog.Require(count is >0 and <=MaxJoints,"Motion joint count is outside bounds.");
        var joints=new List<string>();
        for(int j=0;j<count;j++)
        {
            string name=Str(64);
            ContentCatalog.Require(name.Length>0&&!joints.Contains(name)&&(allowedJoints==null||allowedJoints.Contains(name)),"Unknown or duplicate motion joint.");
            joints.Add(name);
            int jointPriority=S32();ContentCatalog.Require(jointPriority is >=-1 and <=4,"Joint priority must be -1..4.");
            int rot=S32();ContentCatalog.Require(rot is >=0 and <=MaxKeys,"Rotation key count is outside bounds.");
            ContentCatalog.Require(at+(long)rot*8<=data.Length,"Truncated rotation keys.");at+=rot*8;
            int pos=S32();ContentCatalog.Require(pos is >=0 and <=MaxKeys&&(pos==0||name=="mPelvis"),"Only the pelvis may be translated.");
            ContentCatalog.Require(at+(long)pos*8<=data.Length,"Truncated position keys.");at+=pos*8;
        }
        ContentCatalog.Require(S32()==0,"Package motions may not carry constraints.");
        ContentCatalog.Require(at==data.Length,"Keyframe motion has trailing bytes.");
        return new(duration,loop,priority,joints.ToArray());
    }
}
