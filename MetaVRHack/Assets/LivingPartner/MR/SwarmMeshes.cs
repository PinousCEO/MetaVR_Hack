using System.Collections.Generic;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    // The hood opening, eyes and horns are geometry shared by the desktop and room views.
    public static class SwarmMeshes
    {
        static Color Dye(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        static readonly Color Ink = Dye(0x251B2A), Ivory = Dye(0xFFF0D1), EmberEye = Dye(0xB27952),
            WingLining = Dye(0x925365), Brass = Dye(0xBC864B), Gold = Dye(0xFFE0A0), Shade = Dye(0x553445);
        public static readonly Color[] Body = { Dye(0x954455), Dye(0x706187), Dye(0x557369), Dye(0x955165), Dye(0xAD6346), Dye(0x8F3546) };

        sealed class Model
        {
            readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> indices = new List<int>();
            public Vector3 origin;
            public float size = 1f;

            int Point(Vector3 p, Vector3 n, Color c)
            {
                vertices.Add(origin + size * p); normals.Add(n.normalized); colors.Add(c);
                return vertices.Count - 1;
            }

            void Tri(int a, int b, int c)
            {
                var cross = Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]);
                if (cross.sqrMagnitude < 1e-17f) return;
                if (Vector3.Dot(cross, normals[a]+normals[b]+normals[c]) < 0f) { int t=b; b=c; c=t; }
                indices.Add(a); indices.Add(b); indices.Add(c);
            }

            void Join(int previous, int current, int n)
            {
                for (int i=0; i<n; i++)
                {
                    int j=(i+1)%n;
                    Tri(previous+i,current+i,current+j); Tri(previous+i,current+j,previous+j);
                }
            }

            public void Oval(Vector3 p, Vector3 r, Color dye, int rings=7, int sides=12, float tilt=0f)
            {
                Quaternion q=Quaternion.Euler(0f,0f,tilt);
                int last=0;
                for (int y=0; y<=rings; y++)
                {
                    float a=Mathf.PI*y/rings, sa=Mathf.Sin(a), ca=Mathf.Cos(a);
                    int row=vertices.Count;
                    for(int i=0;i<sides;i++)
                    {
                        float b=Mathf.PI*2*i/sides;
                        Vector3 d=new Vector3(sa*Mathf.Cos(b),ca,sa*Mathf.Sin(b));
                        Point(p+q*Vector3.Scale(d,r),q*new Vector3(d.x/r.x,d.y/r.y,d.z/r.z),dye);
                    }
                    if(y>0) Join(last,row,sides);
                    last=row;
                }
            }

            // A curved tapered sweep. An elliptical cross-section gives cloth edges and horns
            // actual thickness at every viewing angle. The horn bands follow the bend.
            public void Sweep(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float root, float tip, Color dye,
                              int steps=9, int sides=8, bool banded=false, float depth=1f)
            {
                int last=0;
                for(int j=0;j<=steps;j++)
                {
                    float t=j/(float)steps, u=1f-t;
                    var p=u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;
                    var axis=(u*u*(b-a)+2*u*t*(c-b)+t*t*(d-c)).normalized;
                    var side=Vector3.Cross(Vector3.forward,axis);
                    if(side.sqrMagnitude < .0001f) side=Vector3.Cross(Vector3.up,axis);
                    side.Normalize(); var back=Vector3.Cross(axis,side).normalized;
                    float r=Mathf.Lerp(root,tip,t);
                    var col=dye;
                    if(banded)
                    {
                        float ridge=Mathf.Pow(Mathf.Max(0f,Mathf.Cos(t*Mathf.PI*7f)),5f)*(1f-t);
                        r*=1f+.11f*ridge;
                        col=Color.Lerp(Color.Lerp(Brass,dye,Mathf.Clamp01(.30f+1.3f*t)),Shade,.23f*ridge);
                    }
                    int row=vertices.Count;
                    for(int k=0;k<sides;k++)
                    {
                        float h=k*Mathf.PI*2/sides, x=Mathf.Cos(h), z=Mathf.Sin(h);
                        var n=side*x+back*(z/depth);
                        Point(p+r*(side*x+back*(z*depth)),n,col);
                    }
                    if(j>0) Join(last,row,sides);
                    if(j==0 || j==steps)
                    {
                        int cap=Point(p,j==0?-axis:axis,col);
                        for(int k=0;k<sides;k++) Tri(cap,row+k,row+(k+1)%sides);
                    }
                    last=row;
                }
            }

            // A flared traveling cloak with a scalloped brass hem. The back hangs lower
            // than the front so the feet stay visible.
            public void Mantle(float width, Color cloth, float hemY)
            {
                const int sides=32, rings=8; int last=0;
                for(int k=0;k<=rings;k++)
                {
                    float t=k/(float)rings;
                    int row=vertices.Count;
                    for(int j=0;j<sides;j++)
                    {
                        float a=j*2*Mathf.PI/sides, ca=Mathf.Cos(a), sa=Mathf.Sin(a);
                        float pleat=Mathf.Cos(8*a), ease=Mathf.SmoothStep(0f,1f,t);
                        float rx=Mathf.Lerp(.195f,width,ease)+.018f*t*pleat;
                        float rz=Mathf.Lerp(.155f,width*.78f,ease)+.014f*t*pleat;
                        float y=Mathf.Lerp(.025f, hemY-.055f*sa+.023f*pleat,t);
                        var n=new Vector3(ca+.065f*t*Mathf.Sin(8*a)*sa,.23f,sa-.065f*t*Mathf.Sin(8*a)*ca);
                        Color baseColor=k==rings?Brass:cloth;
                        Color fold=Color.Lerp(baseColor,Shade,.12f*(1f-pleat)*t);
                        Point(new Vector3(rx*ca,y,.02f+rz*sa),n,fold);
                    }
                    if(k>0) Join(last,row,sides);
                    if(k==rings)
                    {
                        int cap=Point(new Vector3(0,hemY+.025f,.02f),Vector3.down,Shade);
                        for(int j=0;j<sides;j++) Tri(cap,row+j,row+(j+1)%sides);
                    }
                    last=row;
                }
            }

            // A convex double-sided bat wing with three scallops and raised radial veins.
            public void Wing(float sign, float span, Color edge)
            {
                Vector2[] contour={new Vector2(.20f,.02f),new Vector2(.28f,.23f),new Vector2(.43f,.30f),
                    new Vector2(.62f,.42f),new Vector2(.58f,.24f),new Vector2(.60f,.13f),new Vector2(.65f,.07f),
                    new Vector2(.53f,.09f),new Vector2(.46f,.06f),new Vector2(.42f,-.03f),
                    new Vector2(.34f,.02f),new Vector2(.29f,-.05f),new Vector2(.22f,-.14f)};
                Vector3 At(Vector2 p,float z) => new Vector3(sign*(.2f+(p.x-.2f)*span),p.y,z);
                Vector2 hub=new Vector2(.32f,.14f);
                Color membrane=Color.Lerp(edge,WingLining,.38f);
                int front=Point(At(hub,.065f),Vector3.back,membrane);
                int rear=Point(At(hub,.18f),Vector3.forward,edge);
                int start=vertices.Count;
                foreach(var p in contour)
                {
                    var away=new Vector3(sign*(p.x-hub.x),p.y-hub.y,-.24f).normalized;
                    Point(At(p,.125f),away,edge); Point(At(p,.14f),new Vector3(away.x,away.y,-away.z),edge);
                }
                for(int j=0;j<contour.Length;j++)
                {
                    int a=start+j*2,b=start+((j+1)%contour.Length)*2;
                    Tri(front,a,b); Tri(rear,b+1,a+1); Tri(a,a+1,b+1); Tri(a,b+1,b);
                }
                foreach(int k in new[]{3,6,9})
                {
                    Vector3 p=At(hub,.057f), q=At(contour[k],.113f);
                    Sweep(p,Vector3.Lerp(p,q,.3f)+Vector3.up*.021f,Vector3.Lerp(p,q,.7f),q,.018f,.002f,edge,6,6);
                }
            }

            public void Hood(Color cloth)
            {
                // The opening comes to a point above the brow. Its lining recedes into
                // the hood so it stays in shadow when a figure turns sideways.
                const int sides=12;
                float[] widths={.018f,.21f,.32f,.31f,.254f,.17f,.018f};
                float[] heights={.021f,.21f,.30f,.30f,.242f,.156f,.018f};
                float[] depths={.24f,.19f,.045f,-.19f,-.26f,-.21f,-.19f};
                int last=0;
                for(int k=0;k<widths.Length;k++)
                {
                    bool lining=k>=4;
                    int row=vertices.Count;
                    for(int i=0;i<sides;i++)
                    {
                        float a=2f*Mathf.PI*i/sides, x=Mathf.Cos(a), y=Mathf.Sin(a);
                        float peak=Mathf.Pow(Mathf.Max(0f,y),9f);
                        var p=new Vector3(widths[k]*x,.14f+heights[k]*(y+.17f*peak),depths[k]-(k==3?.055f*peak:0f));
                        var n=lining?new Vector3(-.12f*x,-.12f*y,-1f):new Vector3(x,y,k<2?.85f:k==2?.1f:-.6f);
                        Point(p,n,lining?Ink:cloth);
                    }
                    if(k>0) Join(last,row,sides);
                    if(k==0||k==widths.Length-1)
                    {
                        int cap=Point(new Vector3(0,.14f,depths[k]),lining?Vector3.back:Vector3.forward,lining?Ink:cloth);
                        for(int i=0;i<sides;i++) Tri(cap,row+i,row+(i+1)%sides);
                    }
                    last=row;
                }
                for(int side=-1;side<=1;side+=2)
                    Oval(new Vector3(side*.097f,.143f,-.222f),new Vector3(.028f,.0065f,.005f),EmberEye,3,8,side*4f);
            }

            public Mesh Bake(string name, bool flash)
            {
                var mesh=new Mesh {name=name};
                mesh.SetVertices(vertices); mesh.SetNormals(normals);
                if(flash)
                {
                    var bright=new List<Color>(colors.Count);
                    foreach(var c in colors) bright.Add(Color.Lerp(c,Ivory,.73f));
                    mesh.SetColors(bright);
                }
                else mesh.SetColors(colors);
                mesh.SetTriangles(indices,0); mesh.RecalculateBounds();
                return mesh;
            }
        }

        static void Imp(Model m,int kind,Color cloth)
        {
            bool warden=kind==SwarmKind.Tank, regent=kind==SwarmKind.Boss, moth=kind==SwarmKind.Flyer, ember=kind==SwarmKind.Mini;
            Color coat=Color.Lerp(cloth,Shade,.28f), boots=Color.Lerp(coat,Ink,.42f);
            m.Oval(new Vector3(0,-.17f,.02f),new Vector3(.218f,.225f,.176f),coat);
            m.Mantle(regent||warden?.33f:.255f,regent?Dye(0xA74960):cloth,regent?-.40f:-.305f);
            for(int side=-1;side<=1;side+=2)
            {
                float s=side;
                m.Oval(new Vector3(s*.12f,-.355f,-.033f),new Vector3(.084f,.079f,.107f),boots,6,10,s*8);
                m.Oval(new Vector3(s*.224f,-.10f,-.021f),new Vector3(.081f,.115f,.088f),coat,6,10,s*31);
                m.Oval(new Vector3(s*.265f,-.167f,-.067f),new Vector3(.053f,.067f,.057f),boots,6,8,s*15f);
                if(moth||regent||kind==SwarmKind.Hopper) m.Wing(s,moth?1.16f:regent?.92f:.56f,coat);
                float w=warden?.47f:regent?.49f:.35f;
                m.Sweep(new Vector3(s*.226f,.308f,.004f),new Vector3(s*w,.35f,.06f),
                    new Vector3(s*(w+.045f),warden?.62f:.57f,-.023f),new Vector3(s*(w-.083f),warden?.52f:.535f,-.071f),
                    warden||regent?.096f:.061f,.002f,Ivory,15,9,true);
                if(warden)
                    m.Oval(new Vector3(s*.219f,-.052f,.012f),new Vector3(.13f,.085f,.114f),Brass,6,10,s*21);
            }
            m.Hood(cloth);
            if(regent)
            {
                m.Oval(new Vector3(0,.411f,.003f),new Vector3(.155f,.028f,.134f),Brass);
                for(int i=0;i<5;i++)
                {
                    float a=i*2f*Mathf.PI/5f;
                    var p=new Vector3(.114f*Mathf.Cos(a),.415f,.095f*Mathf.Sin(a));
                    m.Sweep(p,p+Vector3.up*.064f,p+Vector3.up*.082f,p+new Vector3(.042f*Mathf.Cos(a),.145f,.042f*Mathf.Sin(a)),.038f,.008f,Gold,5,6);
                }
            }
            else if(ember || kind==SwarmKind.Hopper || moth)
            {
                m.Sweep(new Vector3(.04f,.38f,.035f),new Vector3(.105f,.52f,.03f),new Vector3(-.031f,.53f,-.01f),new Vector3(-.042f,.575f,-.024f),
                  ember?.091f:.056f,.003f,ember?Gold:cloth,9,8);
            }
            // The spade and the curve are visible when a demon swings around in the room.
            m.Sweep(new Vector3(-.02f,-.20f,.13f),new Vector3(-.24f,-.35f,.31f),new Vector3(-.40f,-.15f,.32f),new Vector3(-.33f,-.075f,.26f),.027f,.015f,coat,11,7);
            m.Oval(new Vector3(-.325f,-.064f,.26f),new Vector3(.036f,.054f,.018f),cloth,5,8,-30);
        }

        public static void Build(out Mesh[] normal,out Mesh[] flash)
        {
            normal=new Mesh[SwarmKind.Count]; flash=new Mesh[SwarmKind.Count];
            for(int kind=0;kind<SwarmKind.Count;kind++)
            {
                var m=new Model();
                if(kind==SwarmKind.Splitter)
                {
                    m.size=.66f; m.origin=new Vector3(-.168f,-.07f,.008f); Imp(m,kind,Body[kind]);
                    m.size=.72f; m.origin=new Vector3(.155f,.063f,-.045f); Imp(m,kind,Dye(0x8886B8));
                }
                else Imp(m,kind,Body[kind]);
                normal[kind]=m.Bake(SwarmKind.Names[kind],false);
                flash[kind]=m.Bake(SwarmKind.Names[kind]+" hit",true);
            }
        }
    }
}
