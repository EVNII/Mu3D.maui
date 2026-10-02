using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using static Mu3D.Color.Printing.CmykProfile;
namespace Mu3D.Color.Printing;

// Deliberately local to the optional package: no CMYK parser is compiled into Mu3D.Color.
internal sealed class IccPipeline
{
    private readonly List<Func<float[],float[]>> stages = [];
    internal bool LegacyLab16 { get; }
    internal IccPipeline(ReadOnlySpan<byte> tag, int input, int output, string pcs)
    {
        if (tag.Length<12 || tag[8]!=input || tag[9]!=output) throw new InvalidDataException("ICC pipeline channel mismatch.");
        string kind=Encoding.ASCII.GetString(tag[..4]);
        if (kind is "mft1" or "mft2")
        {
            bool wide=kind=="mft2"; int start=wide?52:48;
            if(tag.Length<start) throw new InvalidDataException("Truncated LUT header.");
            if(!wide && pcs=="XYZ ") throw new NotSupportedException("8-bit XYZ PCS is not supported.");
            LegacyLab16=wide && pcs=="Lab ";
            // ICC legacy matrix acts only on a three-channel PCSXYZ input, before input tables.
            if(input==3 && pcs=="XYZ ") AddMatrix(tag.Slice(12,36),false);
            int ni=wide?BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(48,2)):256;
            int no=wide?BinaryPrimitives.ReadUInt16BigEndian(tag.Slice(50,2)):256;
            int offset=start;
            AddTables(tag,ref offset,input,ni,wide?2:1);
            int[] grid=Enumerable.Repeat((int)tag[10],input).ToArray();
            AddClut(tag,ref offset,grid,output,wide?2:1);
            AddTables(tag,ref offset,output,no,wide?2:1);
        }
        else if(kind is "mAB " or "mBA ")
        {
            if(tag.Length<32) throw new InvalidDataException("Truncated multi-stage LUT.");
            bool forward=kind=="mAB ";
            if(forward!=(input==4)) throw new InvalidDataException("ICC LUT direction mismatch.");
            int b=U32(tag,12),matrix=U32(tag,16),m=U32(tag,20),clut=U32(tag,24),a=U32(tag,28);
            foreach(int o in new[]{b,matrix,m,clut,a}) if(o!=0 && (o<32||o%4!=0||o>=tag.Length)) throw new InvalidDataException("Invalid stage offset.");
            if(b==0 || clut==0 || a==0) throw new NotSupportedException("CMYK LUT requires B curves, CLUT and A curves.");
            if((matrix==0)!=(m==0)) throw new InvalidDataException("Matrix and M curves must be paired.");
            if(forward)
            {
                AddCurves(tag,a,input); AddModernClut(tag,clut,input,output);
                if(m!=0) { AddCurves(tag,m,output); AddMatrix(tag.Slice(matrix),true); }
                AddCurves(tag,b,output);
            }
            else
            {
                AddCurves(tag,b,input);
                if(m!=0) { AddMatrix(tag.Slice(matrix),true); AddCurves(tag,m,input); }
                AddModernClut(tag,clut,input,output); AddCurves(tag,a,output);
            }
        }
        else throw new NotSupportedException($"Unsupported CMYK ICC pipeline {kind}; expected mft1/mft2/mAB/mBA.");
    }
    internal float[] Run(float[] values)
    {
        foreach(var stage in stages) values=stage(values);
        if(values.Any(v=>!float.IsFinite(v))) throw new InvalidDataException("ICC transform produced non-finite values.");
        return values;
    }
    private void AddMatrix(ReadOnlySpan<byte> b,bool offsets)
    {
        if(b.Length<(offsets?48:36)) throw new InvalidDataException("Truncated ICC matrix.");
        float[] m= new float[12]; for(int i=0;i<(offsets?12:9);i++) m[i]=Fixed(b,i*4);
        stages.Add(v => [m[0]*v[0]+m[1]*v[1]+m[2]*v[2]+m[9],m[3]*v[0]+m[4]*v[1]+m[5]*v[2]+m[10],m[6]*v[0]+m[7]*v[1]+m[8]*v[2]+m[11]]);
    }
    private void AddTables(ReadOnlySpan<byte> b,ref int offset,int channels,int entries,int precision)
    {
        if(entries<2) throw new InvalidDataException("ICC table requires at least two entries.");
        var tables=new float[channels][];
        for(int i=0;i<channels;i++) tables[i]=ReadSamples(b,ref offset,entries,precision);
        stages.Add(v=>v.Select((x,i)=>Sample(tables[i],x)).ToArray());
    }
    private void AddModernClut(ReadOnlySpan<byte> b,int offset,int input,int output)
    {
        if(offset>b.Length-20) throw new InvalidDataException("Truncated CLUT.");
        int[] grid=b.Slice(offset,input).ToArray().Select(x=>(int)x).ToArray();
        int precision=b[offset+16];offset+=20;AddClut(b,ref offset,grid,output,precision);
    }
    private void AddClut(ReadOnlySpan<byte> b,ref int offset,int[] grid,int output,int precision)
    {
        long size=output;
        foreach(int n in grid) { if(n<2) throw new InvalidDataException("Invalid CLUT grid."); size*=n; if(size>32*1024*1024) throw new InvalidDataException("CLUT too large."); }
        float[] data=ReadSamples(b,ref offset,(int)size,precision);
        // Four-dimensional multilinear decode; tetrahedral interpolation for three-dimensional separation.
        stages.Add(v=>
        {
            int dims=grid.Length; Span<int> low=stackalloc int[4];Span<float> f=stackalloc float[4];
            for(int d=0;d<dims;d++) { float x=Math.Clamp(v[d],0,1)*(grid[d]-1);low[d]=Math.Min((int)x,grid[d]-2);f[d]=x-low[d]; }
            var result=new float[output];
            if(dims==3)
            {
                Span<int> axes=stackalloc int[3] {0,1,2};
                for(int i=0;i<2;i++)for(int j=i+1;j<3;j++)if(f[axes[j]]>f[axes[i]])(axes[i],axes[j])=(axes[j],axes[i]);
                Span<int> vertex=stackalloc int[3];low[..3].CopyTo(vertex);
                for(int step=0;step<4;step++)
                {
                    float weight=step==0?1-f[axes[0]]:step==3?f[axes[2]]:f[axes[step-1]]-f[axes[step]];
                    int index=(vertex[0]*grid[1]+vertex[1])*grid[2]+vertex[2];
                    for(int c=0;c<output;c++)result[c]+=weight*data[index*output+c];
                    if(step<3)vertex[axes[step]]++;
                }
                return result;
            }
            for(int corner=0;corner<(1<<dims);corner++)
            {
                float weight=1;int index=0;
                for(int d=0;d<dims;d++) { int bit=(corner>>d)&1;weight*=bit==0?1-f[d]:f[d];index=index*grid[d]+low[d]+bit; }
                for(int c=0;c<output;c++) result[c]+=weight*data[index*output+c];
            }
            return result;
        });
    }
    private static float[] ReadSamples(ReadOnlySpan<byte> b,ref int offset,int count,int precision)
    {
        if(precision is not(1 or 2) || offset<0 || (long)offset+(long)count*precision>b.Length) throw new InvalidDataException("Invalid ICC sample extent or precision.");
        var a=new float[count];for(int i=0;i<count;i++) {a[i]=precision==1?b[offset]/255f:BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset,2))/65535f;offset+=precision;}
        return a;
    }
    private void AddCurves(ReadOnlySpan<byte> b,int offset,int count)
    {
        var curves=new Func<float,float>[count];
        for(int i=0;i<count;i++)
        {
            if(offset>b.Length-12) throw new InvalidDataException("Truncated curve.");
            string type=Encoding.ASCII.GetString(b.Slice(offset,4));
            if(type=="curv")
            {
                int n=U32(b,offset+8);offset+=12;
                if(n==0) curves[i]=x=>x;
                else if(n==1) { if(offset>b.Length-2) throw new InvalidDataException("Truncated gamma.");float g=BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset,2))/256f;offset+=2;curves[i]=x=>MathF.Pow(Math.Max(0,x),g); }
                else { var a=ReadSamples(b,ref offset,n,2);curves[i]=x=>Sample(a,x); }
            }
            else if(type=="para")
            {
                int kind=BinaryPrimitives.ReadUInt16BigEndian(b.Slice(offset+8,2));
                int n=kind switch {0=>1,1=>3,2=>4,3=>5,4=>7,_=>throw new NotSupportedException("Unknown ICC parametric curve.")};
                offset+=12;if(offset>b.Length-n*4) throw new InvalidDataException("Truncated parametric curve.");
                float[] p=new float[n];for(int j=0;j<n;j++) p[j]=Fixed(b,offset+j*4);offset+=n*4;
                if(p[0]<=0 || (kind!=0 && p[1]==0)) throw new InvalidDataException("Invalid parametric curve.");
                curves[i]=x=>kind switch {
                    0=>MathF.Pow(Math.Max(x,0),p[0]),
                    1=>x>=-p[2]/p[1]?MathF.Pow(Math.Max(0,p[1]*x+p[2]),p[0]):0,
                    2=>(x>=-p[2]/p[1]?MathF.Pow(Math.Max(0,p[1]*x+p[2]),p[0]):0)+p[3],
                    3=>x>=p[4]?MathF.Pow(Math.Max(0,p[1]*x+p[2]),p[0]):p[3]*x,
                    _=>x>=p[4]?MathF.Pow(Math.Max(0,p[1]*x+p[2]),p[0])+p[5]:p[3]*x+p[6]};
            }
            else throw new NotSupportedException($"Unsupported ICC curve {type}.");
            offset=checked((offset+3)&~3);
        }
        stages.Add(v=>v.Select((x,i)=>curves[i](x)).ToArray());
    }
    private static float Sample(float[] table,float value)
    { float x=Math.Clamp(value,0,1)*(table.Length-1);int i=Math.Min((int)x,table.Length-2);return table[i]+(table[i+1]-table[i])*(x-i); }
    internal Vector3 DecodePcs(float[] v,string pcs)
    {
        if(pcs=="XYZ ") return new Vector3(v[0],v[1],v[2])*(65535f/32768f);
        float scale=LegacyLab16?65535f/65280f:1;
        float fy=(v[0]*scale*100+16)/116,fx=fy+(v[1]*scale*255-128)/500,fz=fy-(v[2]*scale*255-128)/200;
        return new(.9642f*Inv(fx),Inv(fy),.8249f*Inv(fz));
    }
    internal float[] EncodePcs(Vector3 xyz,string pcs)
    {
        if(pcs=="XYZ ") return [xyz.X/(65535f/32768f),xyz.Y/(65535f/32768f),xyz.Z/(65535f/32768f)];
        float fx=F(xyz.X/.9642f),fy=F(xyz.Y),fz=F(xyz.Z/.8249f),s=LegacyLab16?65280f/65535f:1;
        return [(116*fy-16)/100*s,(500*(fx-fy)+128)/255*s,(200*(fy-fz)+128)/255*s];
    }
    internal static Vector3 Lab(Vector3 xyz) {float x=F(xyz.X/.9642f),y=F(xyz.Y),z=F(xyz.Z/.8249f);return new(116*y-16,500*(x-y),200*(y-z));}
    private static float F(float x)=>x>216f/24389f?MathF.Cbrt(x):(24389f/27f*x+16)/116;
    private static float Inv(float x)=>x>6f/29f?x*x*x:(x-16f/116f)*108f/841f;
}
