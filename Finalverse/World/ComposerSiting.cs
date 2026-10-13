// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
namespace Finalverse.World;

public sealed record ComposeSite(double[] anchor,double yaw_degrees,string rule,double? water_distance);
// Bounded runtime terrain search, not model coordinates. Full native collision and
// parcel validation still runs for each candidate and again at commit.
public static class ComposerSiting
{
    public const int MaxCandidates=32;
    public static bool Fits(IWorld world,PlaceTemplate place,double[] anchor,double yawDegrees)
    {
        double cx=anchor[0],cy=anchor[1],yaw=yawDegrees*Math.PI/180;
        foreach(var (u,v) in new[]{(0d,0d),(-1d,-1d),(1d,-1d),(-1d,1d),(1d,1d),(0d,-1d),(0d,1d),(-1d,0d),(1d,0d)})
        {
            double x=cx+u*place.half_extent[0]*Math.Cos(yaw)-v*place.half_extent[1]*Math.Sin(yaw),y=cy+u*place.half_extent[0]*Math.Sin(yaw)+v*place.half_extent[1]*Math.Cos(yaw);
            if(x<=1||y<=1||x>=world.SizeX-1||y>=world.SizeY-1)return false;
            double height=world.Height(x,y);
            if(!double.IsFinite(height)||height<=world.WaterHeight+.3||Math.Abs(height-anchor[2])>.6)return false;
        }
        return true;
    }
    public static double? WaterDistance(IWorld world,double[] anchor)
    {
        foreach(double reach in new[]{4d,8d,12d})for(int i=0;i<16;i++)
        {
            double x=anchor[0]+reach*Math.Cos(i*Math.PI/8),y=anchor[1]+reach*Math.Sin(i*Math.PI/8);
            if(x<0||y<0||x>=world.SizeX||y>=world.SizeY)continue;
            double height=world.Height(x,y);
            if(double.IsFinite(height)&&height<=world.WaterHeight+.3)return reach;
        }
        return null;
    }
    public static IEnumerable<ComposeSite> Candidates(IWorld world,PlaceTemplate place,string actor,string rule)
    {
        ContentCatalog.Require(rule is "in_front_of_actor" or "near_actor" or "near_water","Unsupported place siting rule.");
        var at=world.Avatar(actor);var q=world.Orientation(actor);
        double heading=Math.Atan2(2*(q[3]*q[2]+q[0]*q[1]),1-2*(q[1]*q[1]+q[2]*q[2]));
        var entrance=place.entrances.FirstOrDefault()?.position??[0,-place.half_extent[1]-1,0];
        double distance=Math.Sqrt(entrance[0]*entrance[0]+entrance[1]*entrance[1])+1;
        double toward=Math.Atan2(entrance[1],entrance[0]);
        double[] offsets=rule=="in_front_of_actor"?[0]:[0,Math.PI/4,-Math.PI/4,Math.PI/2,-Math.PI/2,Math.PI,3*Math.PI/4,-3*Math.PI/4];
        double[] rings=rule=="in_front_of_actor"?[0]:[0,8,16,24];
        foreach(double ring in rings)foreach(double offset in offsets)
        {
            double radius=distance+ring;if(radius>64)continue;
            double direction=heading+offset,yaw=Math.Atan2(-Math.Sin(direction),-Math.Cos(direction))-toward;
            double cx=Math.Round(at[0]+radius*Math.Cos(direction),3),cy=Math.Round(at[1]+radius*Math.Sin(direction),3);
            var anchor=new[]{cx,cy,0d};
            if(cx<=1||cy<=1||cx>=world.SizeX-1||cy>=world.SizeY-1)continue;
            anchor[2]=Math.Round(world.Height(cx,cy),3);
            if(!double.IsFinite(anchor[2])||anchor[2]<=world.WaterHeight+.3)continue;
            double yawDegrees=Math.Round(Math.IEEERemainder(yaw*180/Math.PI,360),4);
            if(!Fits(world,place,anchor,yawDegrees))continue;
            double? water=null;
            if(rule=="near_water")
            {
                // Actual region water plane/terrain; no labels or invented lake IDs.
                water=WaterDistance(world,anchor);
                if(water==null)continue;
            }
            yield return new(anchor,yawDegrees,rule,water);
        }
    }
}
