using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;

internal static class NetStateFast
{
    private readonly record struct Field(int Offset, int Chain, bool Networked);

    private static readonly Dictionary<(string Class, string Name), Field> Fields = new();

    public static void Changed(CBaseEntity entity, string className, string fieldName, int extraOffset = 0)
    {
        if (entity == null || !entity.IsValid)
            return;

        if (!Fields.TryGetValue((className, fieldName), out var field))
        {
            bool networked = Schema.IsSchemaFieldNetworked(className, fieldName);
            field = networked
                ? new Field(Schema.GetSchemaOffset(className, fieldName), Schema.GetSchemaOffset(className, "__m_pChainEntity"), true)
                : new Field(0, 0, false);
            Fields[(className, fieldName)] = field;
        }

        if (!field.Networked)
            return;

        nint handle = entity.Handle;
        if (field.Chain != 0)
        {
            NativeAPI.SchemaNetworkStateChanged(handle + field.Chain, (uint)(field.Offset + extraOffset), uint.MaxValue, uint.MaxValue);
            return;
        }

        NativeAPI.SchemaSetStateChanged(handle, (uint)(field.Offset + extraOffset), uint.MaxValue, uint.MaxValue);
        entity.LastNetworkChange = Server.CurrentTime;
        entity.IsSteadyState.Clear();
    }
}
