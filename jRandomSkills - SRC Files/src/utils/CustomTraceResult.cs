using CounterStrikeSharp.API.Modules.Utils;
using System.Numerics;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace jRandomSkills.src.utils
{
    public struct CustomTraceResult
    {
        public float StartPosX;
        public float StartPosY;
        public float StartPosZ;

        public float EndPosX;
        public float EndPosY;
        public float EndPosZ;

        public nint HitEntity;
        public float Fraction;
        public int AllSolid;

        public float NormalX;
        public float NormalY;
        public float NormalZ;

        public ulong InteractsWith;
        public ulong InteractsExclude;
        public bool DrawBeam;

        public CustomTraceResult(TraceResult result, Vector startPos, ulong mask, ulong contents, bool drawBeam)
        {
            StartPosX = startPos.X;
            StartPosY = startPos.Y;
            StartPosZ = startPos.Z;

            var end = result.EndPos;
            EndPosX = end.X;
            EndPosY = end.Y;
            EndPosZ = end.Z;

            var normal = result.Normal;
            NormalX = normal.X;
            NormalY = normal.Y;
            NormalZ = normal.Z;

            HitEntity = result.HitEntity().Handle;
            Fraction = result.Fraction;
            AllSolid = result.IsAllSolid ? 1 : 0;

            InteractsWith = mask;
            InteractsExclude = contents;
            DrawBeam = drawBeam;
        }

        public readonly Vector3 StartPos => new(StartPosX, StartPosY, StartPosZ);
        public readonly Vector3 EndPos => new(EndPosX, EndPosY, EndPosZ);
        public readonly Vector3 Normal => new(NormalX, NormalY, NormalZ);
        public readonly float Distance => Vector3.Distance(StartPos, EndPos);
        public readonly bool DidHit => Fraction < 1f;
        public readonly bool IsAllSolid => AllSolid != 0;
    }
}
