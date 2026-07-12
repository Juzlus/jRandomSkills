// https://discord.com/channels/1160907911501991946/1508172390863994910/1508180670659166348

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

namespace jRandomSkills.src.utils
{

    #region Native Structs

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CUtlMemory<T> where T : unmanaged
    {
        public T* m_pMemory;
        public int m_nAllocationCount;
        public int m_nGrowSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct CUtlVector<T> where T : unmanaged
    {
        public int m_Size;
        public CUtlMemory<T> m_Memory;

        public int Count => m_Size;

        public ref T Element(int index)
        {
            if (index < 0 || index >= m_Size)
                throw new IndexOutOfRangeException();

            if (m_Memory.m_pMemory == null)
                throw new NullReferenceException("m_pMemory is null. The vector is empty or the GameData offset is outdated.");

            return ref m_Memory.m_pMemory[index];
        }
    }

    #endregion

    #region Network Services

    public class INetworkServerService : NativeObject
    {
        private readonly VirtualFunctionWithReturn<nint, nint> GetIGameServerFunc;

        public INetworkServerService() : base(NativeAPI.GetValveInterface(0, "NetworkServerService_001"))
        {
            GetIGameServerFunc = new VirtualFunctionWithReturn<nint, nint>(Handle, GameData.GetOffset("INetworkServerService_GetIGameServer"));
        }

        public INetworkGameServer GetIGameServer()
        {
            return new INetworkGameServer(GetIGameServerFunc.Invoke(Handle));
        }
    }

    public unsafe class INetworkGameServer(nint ptr) : NativeObject(ptr)
    {
        private static readonly int SlotsOffset = GameData.GetOffset("INetworkGameServer_Slots");

        private ref CUtlVector<nint> Slots => ref Unsafe.AsRef<CUtlVector<nint>>((void*)(Handle + SlotsOffset));

        public CServerSideClient? GetClientBySlot(int slot)
        {
            if (Handle == nint.Zero)
                return null;

            if (slot < 0 || slot >= Slots.Count || Slots.m_Memory.m_pMemory == null)
                return null;

            var ptr = Slots.Element(slot);

            if (ptr == nint.Zero)
                return null;

            return new CServerSideClient(ptr);
        }
    }

    #endregion

    #region CServerSideClient

    public unsafe class CServerSideClient(nint ptr) : NativeObject(ptr)
    {
        private static readonly int m_nDeltaTick = GameData.GetOffset("CServerSideClient_m_nDeltaTick");

        private ref T Field<T>(int offset) where T : unmanaged
        {
            return ref Unsafe.AsRef<T>((void*)(Handle + offset));
        }

        public int DeltaTick
        {
            get => Field<int>(m_nDeltaTick);
            set => Field<int>(m_nDeltaTick) = value;
        }

        public void ForceFullUpdate() => DeltaTick = -1;
    }

    #endregion
}