using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.UserMessages;

namespace src.player;

public interface ISkill
{
    public static void LoadSkill() { }
    public static void EnableSkill(CCSPlayerController _) { }
    public static void DisableSkill(CCSPlayerController _) { }
    public static void UseSkill(CCSPlayerController _) { }
    public static void TypeSkill(CCSPlayerController _, string[] __) { }

    public static void OnTakeDamage(DynamicHook _) { }
    public static void OnEntitySpawned(CEntityInstance _) { }
    public static void OnTick() { }
    public static void CheckTransmit([CastFrom(typeof(nint))] CCheckTransmitInfoList _) { }

    public static void NewRound() { }
    public static void PlayerMakeSound(UserMessage _) { }
    public static void PlayerBlind(EventPlayerBlind _) { }
    public static void PlayerHurt(EventPlayerHurt _) { }
    public static void PlayerDeath(EventPlayerDeath _) { }
    public static void PlayerJump(EventPlayerJump _) { }

    public static void WeaponFire(EventWeaponFire _) { }
    public static void WeaponEquip(EventItemEquip _) { }
    public static void WeaponPickup(EventItemPickup _) { }
    public static void WeaponReload(EventWeaponReload _) { }
    public static void GrenadeThrown(EventGrenadeThrown _) { }

    public static void BombBeginplant(EventBombBeginplant _) { }
    public static void BombPlanted(EventBombPlanted _) { }
    public static void BombBegindefuse(EventBombBegindefuse _) { }

    public static void DecoyStarted(EventDecoyStarted _) { }
    public static void DecoyDetonate(EventDecoyDetonate _) { }

    public static void SmokegrenadeDetonate(EventSmokegrenadeDetonate _) { }
    public static void SmokegrenadeExpired(EventSmokegrenadeExpired _) { }

    public static void OnTriggerEnter(CBaseTrigger _, CBaseEntity __) { }
    public static void OnTriggerExit(CBaseTrigger _, CBaseEntity __) { }

    public class SkillConfig { }
}

public enum Skills
{
    None,
    Aimbot,
    Anomaly,
    AntyFlash,
    AntyHead,
    AreaReaper,
    Armored,
    Assassin,
    Astronaut,
    Bankrupt,
    Baseball,
    Behind,
    BladeMaster,
    BunnyHop,
    C4Camouflage,
    CarefulBullets,
    Catapult,
    Chicken,
    ChillOut,
    Cutter,
    Cypher,
    Darkness,
    Deactivator,
    Deaf,
    Disarmament,
    Distancer,
    Dash,
    Dracula,
    Duplicator,
    Dwarf,
    Earthquake,
    EnemySpawn,
    ExplosiveShot,
    FalconEye,
    FastReload,
    Flash,
    Fortnite,
    FragileBomb,
    FriendlyFire,
    FrozenDecoy,
    Gambler,
    Ghost,
    Glaz,
    Glitch,
    Glue,
    GodMode,
    Grenadier,
    HealingSmoke,
    Hermit,
    HolyHandGrenade,
    Iana,
    Impostor,
    InfiniteAmmo,
    Jackal,
    Jammer,
    Jester,
    JumpBan,
    JumpingJack,
    KillerFlash,
    LifeSwap,
    LongKnife,
    LongZeus,
    Magnifier,
    Medic,
    MoneySwap,
    Muhammed,
    Ninja,
    NoNades,
    NoRecoil,
    Noclip,
    OneShot,
    OnlyHead,
    PawelJumper,
    Phoenix,
    PsychicDefusing,
    Pilot,
    Planter,
    Poison,
    PrimaryBan,
    Prosthesis,
    Push,
    Pyro,
    QuickShot,
    RadarHack,
    Rambo,
    RandomWeapon,
    RCMelon,
    ReZombie,
    ReactiveArmor,
    Regeneration,
    Replicator,
    Retreat,
    ReturnToSender,
    RichBoy,
    RobinHood,
    Rubber,
    Saper,
    SecondLife,
    Shade,
    ShortBomb,
    Silent,
    SniperElite,
    Soldier,
    SoundMaker,
    Spectator,
    SwapPosition,
    Teleporter,
    Thief,
    ThirdEye,
    Thorns,
    ToxicSmoke,
    Wallhack,
    Watchmaker,
    WeaponsSwap,
    Zeus,
}
