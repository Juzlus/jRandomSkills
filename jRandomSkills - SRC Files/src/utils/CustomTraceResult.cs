using RayTraceAPI;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace jRandomSkills.src.utils
{
    public struct CustomTraceResult (TraceResult result, Vector startPos, ulong mask, ulong contents, bool drawBeam)
    {
        public float StartPosX = startPos.X;
        public float StartPosY = startPos.Y;
        public float StartPosZ = startPos.Z;

        public float EndPosX = result.EndPosX;
        public float EndPosY = result.EndPosY;
        public float EndPosZ = result.EndPosZ;

        public nint HitEntity = result.HitEntity;
        public float Fraction = result.Fraction;
        public int AllSolid = result.AllSolid;

        public float NormalX = result.NormalX;
        public float NormalY = result.NormalY;
        public float NormalZ = result.NormalZ;

        public ulong InteractsWith = mask;
        public ulong InteractsExclude = contents;
        public bool DrawBeam = drawBeam;

        public readonly Vector3 StartPos => new(StartPosX, StartPosY, StartPosZ);
        public readonly Vector3 EndPos => new(EndPosX, EndPosY, EndPosZ);
        public readonly Vector3 Normal => new(NormalX, NormalY, NormalZ);
        public readonly float Distance => Vector3.Distance(StartPos, EndPos);
        public readonly bool DidHit => Fraction < 1f;
        public readonly bool IsAllSolid => AllSolid != 0;
    }
}
