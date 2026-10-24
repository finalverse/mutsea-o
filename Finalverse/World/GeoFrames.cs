// Copyright (c) 2026 Finalverse contributors. SPDX-License-Identifier: BSD-3-Clause
namespace Finalverse.World;

// WGS84 geodesy in f64. Every formula mirrors SpatialDB's spatialdb-types frames.rs term
// for term, so a binding verified here and a publication compiled there share one Earth.
public sealed record Geodetic(double longitude_degrees,double latitude_degrees,double height_metres);

public static class GeoFrames
{
    public const double A=6378137.0,F=1/298.257223563,E2=F*(2-F);

    public static void Validate(Geodetic p)
    {
        ContentCatalog.Require(p!=null&&double.IsFinite(p.longitude_degrees)&&double.IsFinite(p.latitude_degrees)&&double.IsFinite(p.height_metres)
            &&p.longitude_degrees>=-180&&p.longitude_degrees<=180&&p.latitude_degrees>=-90&&p.latitude_degrees<=90&&p.height_metres>=-12000&&p.height_metres<=100000000,
            "Geographic positions need finite WGS84 degrees and ellipsoidal metres.");
    }

    // Rows are the east, north and up unit vectors in ECEF.
    public static double[][] Enu(double longitudeDegrees,double latitudeDegrees)
    {
        double l=Rad(longitudeDegrees),p=Rad(latitudeDegrees),sl=Math.Sin(l),cl=Math.Cos(l),sp=Math.Sin(p),cp=Math.Cos(p);
        return [[-sl,cl,0],[-sp*cl,-sp*sl,cp],[cp*cl,cp*sl,sp]];
    }

    public static double[] Ecef(Geodetic p)
    {
        double l=Rad(p.longitude_degrees),q=Rad(p.latitude_degrees),sl=Math.Sin(l),cl=Math.Cos(l),sp=Math.Sin(q),cp=Math.Cos(q);
        double n=A/Math.Sqrt(1-E2*sp*sp);
        return [(n+p.height_metres)*cp*cl,(n+p.height_metres)*cp*sl,(n*(1-E2)+p.height_metres)*sp];
    }

    // Fixed-point latitude; the height form p·cosφ + z·sinφ − a·√(1 − e²sin²φ) stays well
    // conditioned at the poles, where p / cosφ − N loses millimetres.
    public static Geodetic FromEcef(double[] x)
    {
        double p=Math.Sqrt(x[0]*x[0]+x[1]*x[1]),longitude=Math.Atan2(x[1],x[0]);
        if(p<1e-9)return new(0,x[2]<0?-90:90,Math.Abs(x[2])-A*(1-F));
        double latitude=Math.Atan2(x[2],p*(1-E2));
        for(int i=0;i<32;i++)
        {
            double s0=Math.Sin(latitude),n=A/Math.Sqrt(1-E2*s0*s0),next=Math.Atan2(x[2]+E2*n*s0,p);
            bool converged=Math.Abs(next-latitude)<1e-15;latitude=next;if(converged)break;
        }
        double s=Math.Sin(latitude),c=Math.Cos(latitude);
        return new(Deg(longitude),Deg(latitude),p*c+x[2]*s-A*Math.Sqrt(1-E2*s*s));
    }

    // Column-major affine 4x4 for a local tangent plane whose +x,+y,+z axes are the given
    // east/north/up combinations (rows of `axes`), with the origin at `origin`.
    public static double[] TangentToEcef(Geodetic origin,double[][] axes)
    {
        var enu=Enu(origin.longitude_degrees,origin.latitude_degrees);var t=Ecef(origin);var m=new double[16];
        for(int c=0;c<3;c++)for(int r=0;r<3;r++)m[c*4+r]=axes[c][0]*enu[0][r]+axes[c][1]*enu[1][r]+axes[c][2]*enu[2][r];
        m[12]=t[0];m[13]=t[1];m[14]=t[2];m[15]=1;return m;
    }

    public static double[] Apply(double[] m,double[] p)=>
        [m[0]*p[0]+m[4]*p[1]+m[8]*p[2]+m[12],m[1]*p[0]+m[5]*p[1]+m[9]*p[2]+m[13],m[2]*p[0]+m[6]*p[1]+m[10]*p[2]+m[14]];
    public static double[] ApplyLinear(double[] m,double[] v)=>
        [m[0]*v[0]+m[4]*v[1]+m[8]*v[2],m[1]*v[0]+m[5]*v[1]+m[9]*v[2],m[2]*v[0]+m[6]*v[1]+m[10]*v[2]];

    // Inverse of a similarity (uniform scale times rotation plus translation): exact up to rounding.
    public static double[] InverseSimilarity(double[] m)
    {
        double s2=m[0]*m[0]+m[1]*m[1]+m[2]*m[2];ContentCatalog.Require(s2>0&&double.IsFinite(s2),"Degenerate geographic transform.");
        var r=new double[16];
        for(int c=0;c<3;c++)for(int k=0;k<3;k++)r[c*4+k]=m[k*4+c]/s2;
        var t=ApplyLinear(r,[m[12],m[13],m[14]]);r[12]=-t[0];r[13]=-t[1];r[14]=-t[2];r[15]=1;return r;
    }

    public static double[] Multiply(double[] a,double[] b)
    {
        var r=new double[16];
        for(int c=0;c<4;c++)for(int k=0;k<4;k++){double v=0;for(int i=0;i<4;i++)v+=a[i*4+k]*b[c*4+i];r[c*4+k]=v;}
        return r;
    }

    public static double Distance(double[] a,double[] b)=>Math.Sqrt((a[0]-b[0])*(a[0]-b[0])+(a[1]-b[1])*(a[1]-b[1])+(a[2]-b[2])*(a[2]-b[2]));
    public static double Rad(double d)=>d*Math.PI/180;
    public static double Deg(double r)=>r*180/Math.PI;
}
