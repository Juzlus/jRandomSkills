using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Core;

namespace src.utils
{
    public sealed class TransmitList
    {
        private readonly List<(CCheckTransmitInfo info, CCSPlayerController? player)> items = [];

        public int Count => items.Count;

        public (CCheckTransmitInfo info, CCSPlayerController? player) this[int index] => items[index];

        private static int slotOffset = -1;

        public unsafe void Load(CCheckTransmitInfoList source)
        {
            items.Clear();
            if (slotOffset < 0) slotOffset = GameData.GetOffset("CheckTransmitPlayerSlot");

            nint* inner = (nint*)source.Handle;
            int count = (int)inner[1];
            for (int i = 0; i < count; i++)
            {
                nint entry = *(nint*)(*inner + (nint)i * sizeof(nint));
                var info = Unsafe.Read<CCheckTransmitInfo>((void*)entry);
                int slot = *(int*)(entry + slotOffset);
                items.Add((info, PlayerManager.GetControllerBySlot(slot)));
            }
        }

        public List<(CCheckTransmitInfo info, CCSPlayerController? player)>.Enumerator GetEnumerator() => items.GetEnumerator();
    }
}
