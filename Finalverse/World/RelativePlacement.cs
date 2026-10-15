// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
using System.Text.Json;
namespace Finalverse.World;

// OpenSim faces local +X; local -Y is the avatar's right. Geometry is authority.
public static class RelativePlacement
{
    static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    public static double[] Resolve(IWorld world,string actor,JsonElement rule,double[] scale)
    {
        var fields=rule.EnumerateObject().Select(p=>p.Name).ToArray();
        Require(fields.Length==2&&fields.Distinct().Count()==2&&fields.Contains("direction")&&fields.Contains("distance"),"Placement accepts direction and distance only.");
        string direction=rule.GetProperty("direction").GetString()??"";
        double distance=rule.GetProperty("distance").GetDouble();
        Require(double.IsFinite(distance)&&distance>=.5&&distance<=12,"Placement distance must be 0.5–12 meters.");
        var q=world.Orientation(actor);var at=world.Avatar(actor);
        Require(q.Length==4&&q.All(double.IsFinite)&&Math.Abs(q.Sum(v=>v*v)-1)<.02,"Avatar orientation is unavailable. Try again after entering the world.");
        double yaw=Math.Atan2(2*(q[3]*q[2]+q[0]*q[1]),1-2*(q[1]*q[1]+q[2]*q[2]));
        double offset=direction switch{"forward"=>0,"right"=>-Math.PI/2,"left"=>Math.PI/2,"backward"=>Math.PI,_=>throw new InvalidDataException("Use forward, backward, left or right placement.")};
        double x=Math.Round(at[0]+distance*Math.Cos(yaw+offset),4),y=Math.Round(at[1]+distance*Math.Sin(yaw+offset),4);
        var position=new[]{x,y,Ground(world,x,y,scale)+scale[2]/2};
        Validate(world,actor,position,scale);
        return position;
    }
    static double Ground(IWorld world,double x,double y,double[] scale)
    {
        Require(scale.Length==3&&scale.All(s=>double.IsFinite(s)&&s>=.05&&s<=12),"Invalid primitive size.");
        double low=double.MaxValue,high=double.MinValue;
        foreach(double u in new[]{-1d,0,1})foreach(double v in new[]{-1d,0,1})
        {
            double px=x+u*scale[0]/2,py=y+v*scale[1]/2;
            Require(px>0&&py>0&&px<world.SizeX&&py<world.SizeY,"Requested placement crosses the region boundary.");
            double z=world.Height(px,py);
            Require(double.IsFinite(z)&&z>world.WaterHeight+.3,"Requested placement needs dry ground.");
            low=Math.Min(low,z);high=Math.Max(high,z);
        }
        Require(high-low<=.35,"Requested placement is too steep. Choose another site.");
        return high;
    }
    public static void Validate(IWorld world,string actor,double[] position,double[] scale)
    {
        double ground=Ground(world,position[0],position[1],scale);
        Require(Math.Abs(position[2]-scale[2]/2-ground)<.01,"Ground changed after preview. Plan again.");
        double radius=Math.Sqrt(scale[0]*scale[0]+scale[1]*scale[1])/2;
        var at=world.Avatar(actor);
        Require(Math.Sqrt(Math.Pow(at[0]-position[0],2)+Math.Pow(at[1]-position[1],2))>radius+.45,"Requested placement would overlap your avatar.");
        foreach(var e in world.Entities(actor))
        {
            double reach=e.body_radius??Math.Max(e.clearance_radius,Math.Sqrt(e.scale[0]*e.scale[0]+e.scale[1]*e.scale[1])/2);
            if(reach<=0||e.position[2]+e.scale[2]/2<ground+.3||e.position[2]-e.scale[2]/2>position[2]+scale[2]/2)continue;
            Require(Math.Sqrt(Math.Pow(e.position[0]-position[0],2)+Math.Pow(e.position[1]-position[1],2))>radius+reach+.1,"Requested placement is occupied. Choose another direction or distance.");
        }
    }
}
