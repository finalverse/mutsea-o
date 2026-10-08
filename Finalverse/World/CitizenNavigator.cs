// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
namespace Finalverse.World;

// Bounded deterministic navigation for the primitive embodiment, not a physics/navmesh replacement.
public static class CitizenNavigator
{
    public static double Distance(double[] a,double[] b)=>Math.Sqrt(a.Zip(b,(x,y)=>(x-y)*(x-y)).Sum());
    public static bool Clear(IWorld world,string owner,string embodiment,double[] point)
    {
        if(point.Length!=3||point.Any(x=>!double.IsFinite(x))||point[0]<1||point[1]<1||point[0]>world.SizeX-2||point[1]>world.SizeY-2)return false;
        if(world.Height(point[0],point[1])<world.WaterHeight+.4)return false;
        foreach(var e in world.Entities(owner))
        {
            if(e.id==embodiment)continue;
            // Roads/ground surfaces are traversable; solids at body height are obstacles.
            if(e.position[2]+e.scale[2]/2<point[2]-.55||e.position[2]-e.scale[2]/2>point[2]+.9)continue;
            double r=Math.Max(e.clearance_radius,Math.Sqrt(e.scale[0]*e.scale[0]+e.scale[1]*e.scale[1])/2)+.45;
            if(Math.Abs(e.position[0]-point[0])<r&&Math.Abs(e.position[1]-point[1])<r)return false;
        }
        return true;
    }
    public static double[][] Route(IWorld world,string owner,string embodiment,double[] start,double[] end)
    {
        if(!Clear(world,owner,embodiment,start)||!Clear(world,owner,embodiment,end)||Distance(start,end)>96)throw new InvalidDataException("Destination is unsafe or exceeds the 96m navigation budget.");
        // Two-metre four-neighbour grid; bounded search, no diagonal corner cutting.
        (int,int) Cell(double[] p)=>((int)Math.Round(p[0]/2),(int)Math.Round(p[1]/2));
        double[] Point((int x,int y) c)=>[c.x*2,c.y*2,world.Height(Math.Clamp(c.x*2,0,world.SizeX-1),Math.Clamp(c.y*2,0,world.SizeY-1))+.9];
        var first=Cell(start);var last=Cell(end);var queue=new PriorityQueue<(int,int),double>();queue.Enqueue(first,0);
        var cost=new Dictionary<(int,int),double>{{first,0}};var previous=new Dictionary<(int,int),(int,int)>();int visited=0;
        while(queue.Count>0&&visited++<16384)
        {
            var current=queue.Dequeue();if(current==last)break;
            foreach(var offset in new[]{(1,0),(-1,0),(0,1),(0,-1)})
            {
                var next=(current.Item1+offset.Item1,current.Item2+offset.Item2);var p=Point(next);
                if(!Clear(world,owner,embodiment,p)||Math.Abs(p[2]-Point(current)[2])>.6)continue;
                var candidate=cost[current]+2;
                if(candidate>128||(cost.TryGetValue(next,out var old)&&old<=candidate))continue;
                cost[next]=candidate;previous[next]=current;queue.Enqueue(next,candidate+Math.Abs(next.Item1-last.Item1)*2+Math.Abs(next.Item2-last.Item2)*2);
            }
        }
        if(first!=last&&!previous.ContainsKey(last))throw new InvalidDataException("No safe bounded route to that destination.");
        var reverse=new List<double[]>();var cursor=last;
        while(cursor!=first){reverse.Add(Point(cursor));cursor=previous[cursor];}
        reverse.Reverse();reverse.Add(end);var route=reverse.ToArray();var from=start;double length=0;
        // Validate every half-metre sample, including connections to non-grid endpoints.
        foreach(var p in route){double distance=Distance(from,p);for(int i=1;i<=Math.Ceiling(distance/.4);i++){double t=Math.Min(1,i*.4/Math.Max(.001,distance));var sample=from.Zip(p,(a,b)=>a+(b-a)*t).ToArray();if(!Clear(world,owner,embodiment,sample))throw new InvalidDataException("Route connection intersects an obstacle.");}length+=distance;from=p;}
        if(length>128)throw new InvalidDataException("Route exceeds navigation distance budget.");return route;
    }
}
