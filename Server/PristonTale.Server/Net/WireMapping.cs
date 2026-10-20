using PristonTale.Application.Services;
using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Server.Net;

// Server-side mapping from authoritative views to original Priston structs.
internal static class WireMapping
{
    public static short ActionCode(MoveStateView state) => state switch
    {
        MoveStateView.Walking => MotionStateCode.Walk,
        MoveStateView.Running => MotionStateCode.Run,
        MoveStateView.Attack => MotionStateCode.Attack,
        MoveStateView.Damage => MotionStateCode.Damage,
        MoveStateView.Dead => MotionStateCode.Dead,
        MoveStateView.Skill => MotionStateCode.Skill,
        MoveStateView.Eat => MotionStateCode.Eat,
        _ => MotionStateCode.Stand
    };

    public static short ClampShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    public static uint ItemCode(string? catalogKey) => !string.IsNullOrEmpty(catalogKey) && LegacyItemCode.TryParse(catalogKey, out var code) ? code : 0;

    public static PlayBuffer Buffer(in EntityView view) => new(
        PristonUnits.ToFixed(view.Position.X),
        PristonUnits.ToFixed(view.Position.Y),
        PristonUnits.ToFixed(view.Position.Z),
        PristonUnits.FrameNone,
        PristonUnits.ToAngle(view.Facing),
        ActionCode(view.MoveState),
        view.AttackTargetId);

    public static TransPlayData PlayData(in EntityView view, int fieldCode, byte revision) => new(
        view.EntityId,
        view.AttackTargetId,
        unchecked((int)view.AttackSequence),
        ClampShort(view.Hp),
        ClampShort(view.MaxHp),
        view.Kind == EntityKindView.Player ? 0u : view.TemplateId,
        (short)fieldCode,
        -1,
        revision,
        (byte)Math.Clamp(view.Level, 0, 255),
        [Buffer(view)]);

    public static PlayDataCoarse Coarse(in EntityView view) => new(
        view.EntityId,
        PristonUnits.ToFixed(view.Position.X),
        PristonUnits.ToFixed(view.Position.Y),
        PristonUnits.ToFixed(view.Position.Z),
        PristonUnits.ToAngle(view.Facing),
        ActionCode(view.MoveState),
        view.Kind == EntityKindView.Player ? 0u : view.TemplateId);

    // Character info record for an object description (PLAYERINFO reply to GETPLAYERINFO).
    public static CharacterInfo Describe(in EntityView view, WorldData data)
    {
        var info = new CharacterInfo
        {
            ObjectSerial = view.EntityId,
            Level = view.Level,
            LifeCurrent = ClampShort(view.Hp),
            LifeMax = ClampShort(view.MaxHp),
            MoveSpeed = view.MoveSpeed
        };
        info.Temp[CharacterInfo.TempAttackRate] = BitConverter.SingleToUInt32Bits(view.AttackRate);
        switch (view.Kind)
        {
            case EntityKindView.Player:
            {
                var appearance = view.Appearance;
                info.State = CharacterState.User;
                info.Name = appearance.Name ?? "";
                info.ModelName = appearance.ModelName ?? "";
                info.ModelName2 = appearance.ModelName2 ?? "";
                info.JobCode = (uint)appearance.JobCode;
                info.ChangeJob = (uint)appearance.Tier;
                info.Temp[CharacterInfo.TempFaceAndHair] = (uint)(appearance.FaceIndex & 0xFF) | (uint)(appearance.HairIndex & 0xFF) << 8;
                info.Temp[CharacterInfo.TempWeaponCode] = ItemCode(appearance.WeaponKey);
                info.Temp[CharacterInfo.TempArmorCode] = ItemCode(appearance.ArmorKey);
                info.Temp[CharacterInfo.TempOffHandCode] = ItemCode(appearance.OffHandKey);
                info.Temp[CharacterInfo.TempFlags] = appearance.InVillage ? CharacterInfo.FlagInVillage : 0;
                break;
            }
            case EntityKindView.Monster when data.Monsters.TryGet((int)view.TemplateId, out var monster):
                info.State = CharacterState.Enemy;
                info.Name = monster.Name ?? "";
                info.ModelName = monster.ModelInxPath ?? "";
                info.CharSoundCode = (uint)monster.SoundCode;
                info.SizeLevel = monster.ShadowSizeLevel;
                info.Temp[CharacterInfo.TempTemplateCode] = view.TemplateId;
                info.Temp[CharacterInfo.TempModelScale] = BitConverter.SingleToUInt32Bits(monster.ModelScale);
                break;
            case EntityKindView.Npc when data.Npcs.TryGet((int)view.TemplateId, out var npc):
                info.State = CharacterState.Npc;
                info.Name = npc.Name ?? "";
                info.ModelName = npc.ModelInxPath ?? "";
                info.CharSoundCode = (uint)npc.SoundCode;
                info.Temp[CharacterInfo.TempTemplateCode] = view.TemplateId;
                info.Temp[CharacterInfo.TempModelScale] = BitConverter.SingleToUInt32Bits(npc.ModelScale);
                info.Temp[CharacterInfo.TempNpcServices] = (uint)npc.Services;
                break;
            default:
                // A template missing from the catalog is still described by kind and code.
                info.State = view.Kind == EntityKindView.Npc ? CharacterState.Npc : CharacterState.Enemy;
                info.Temp[CharacterInfo.TempTemplateCode] = view.TemplateId;
                info.Temp[CharacterInfo.TempModelScale] = BitConverter.SingleToUInt32Bits(1f);
                break;
        }

        return info;
    }

    // Character info record embedded in the character record for the entering character.
    public static CharacterInfo DescribeOwner(PlayerCharacter character, CharacterSummary summary, uint serial)
    {
        var owner = OwnerState.Capture(character);
        var info = new CharacterInfo
        {
            Name = summary.Name,
            ObjectSerial = serial,
            State = CharacterState.User,
            JobCode = (uint)summary.ClassId,
            ChangeJob = (uint)summary.Tier,
            Level = owner.Level,
            Strength = owner.Strength,
            Spirit = owner.Spirit,
            Talent = owner.Talent,
            Dexterity = owner.Agility,
            Health = owner.Health,
            AttackRating = owner.AttackRating,
            AttackDamageMin = owner.DamageMin,
            AttackDamageMax = owner.DamageMax,
            Defence = owner.Defence,
            Absorption = owner.Absorption,
            MoveSpeed = owner.MoveSpeed,
            ShootingRange = (int)owner.AttackRange,
            LifeCurrent = ClampShort(owner.Hp),
            LifeMax = ClampShort(owner.MaxHp),
            ManaCurrent = ClampShort(owner.Mp),
            ManaMax = ClampShort(owner.MaxMp),
            StaminaCurrent = ClampShort(owner.Sp),
            StaminaMax = ClampShort(owner.MaxSp),
            Experience = owner.Experience,
            NextExp = (int)Math.Min(owner.NextLevel, int.MaxValue),
            Money = (int)Math.Min(owner.Money, int.MaxValue),
            StatePoint = owner.StatPoints
        };
        info.Temp[CharacterInfo.TempFaceAndHair] = (uint)(summary.FaceIndex & 0xFF) | (uint)(summary.HairIndex & 0xFF) << 8;
        info.Temp[CharacterInfo.TempWeaponCode] = ItemCode(summary.WeaponKey);
        info.Temp[CharacterInfo.TempArmorCode] = ItemCode(summary.ArmorKey);
        info.Temp[CharacterInfo.TempOffHandCode] = ItemCode(summary.OffHandKey);
        info.Temp[CharacterInfo.TempFlags] = summary.InVillage ? CharacterInfo.FlagInVillage : 0;
        return info;
    }

    public static UserCharacterEntry Entry(CharacterSummary summary) => new(
        summary.Name,
        "",
        "",
        (uint)summary.ClassId,
        summary.Level,
        0,
        ItemCode(summary.ArmorKey),
        0,
        0,
        0,
        summary.FaceIndex,
        summary.HairIndex,
        summary.Tier,
        ItemCode(summary.WeaponKey),
        ItemCode(summary.OffHandKey),
        summary.InVillage);

    public static OwnerStatusPacket Status(OwnerState owner) => new(
        owner.Level, owner.Experience, owner.LevelStart, owner.NextLevel, owner.Money,
        owner.Hp, owner.MaxHp, owner.Mp, owner.MaxMp, owner.Sp, owner.MaxSp,
        owner.StatPoints, owner.SkillPoints, owner.Strength, owner.Spirit, owner.Talent, owner.Agility, owner.Health,
        owner.DamageMin, owner.DamageMax, owner.Defence, owner.Absorption, owner.AttackRating, owner.MoveSpeed, owner.AttackRange,
        owner.InventoryRevision, owner.ProgressionRevision, owner.SkillsRevision);

    public static InventoryItemEntry Item(OwnedItemView item) => new(
        item.InstanceId, item.Code, (int)item.Kind, item.Count, item.Anchor, item.Slot, (short)item.Width, (short)item.Height, item.Name);

    public static CombatEventEntry Event(in CombatEventView view)
    {
        byte flags = 0;
        if (view.Crit)
        {
            flags |= CombatEventFlags.Critical;
        }

        if (view.Killed)
        {
            flags |= CombatEventFlags.Killed;
        }

        return new CombatEventEntry(view.AttackerId, view.TargetId, (byte)view.Kind, flags, view.DurationMs, view.HitAtMs, view.Damage,
            view.Resources, view.SourceSoundCode, PristonUnits.ToFixed(view.Position.X), PristonUnits.ToFixed(view.Position.Y),
            PristonUnits.ToFixed(view.Position.Z), PristonUnits.ToAngle(view.Facing));
    }

    public static GroundItemEntry Drop(GroundDropView drop) => new(
        drop.Handle,
        drop.Gold ? LegacyItemCode.Parse("GG101") : drop.ItemCode,
        drop.Amount,
        PristonUnits.ToFixed(drop.Position.X),
        PristonUnits.ToFixed(drop.Position.Y),
        PristonUnits.ToFixed(drop.Position.Z),
        drop.Name);
}
