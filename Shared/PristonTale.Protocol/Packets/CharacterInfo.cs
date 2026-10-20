using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

public static class CharacterState
{
    public const int Npc = 0;
    public const int Enemy = 1;
    public const int User = 0x80;
}

// Character info record with the 32-bit layout: 464 bytes, lpMonInfo kept as a zero 4-byte slot at offset 328.
public sealed class CharacterInfo
{
    public const int Size = 464;
    public const int TempCount = 11;

    // Etervale descriptor slots inside dwTemp[11], which the original leaves zero.
    public const int TempTemplateCode = 0;
    public const int TempModelScale = 1;
    public const int TempNpcServices = 2;
    public const int TempFaceAndHair = 3;
    public const int TempWeaponCode = 4;
    public const int TempArmorCode = 5;
    public const int TempOffHandCode = 6;
    public const int TempFlags = 7;
    public const int TempAttackRate = 8;
    public const uint FlagInVillage = 1;

    public string Name { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string ModelName2 { get; set; } = "";
    public uint ModelNameCode2 { get; set; }
    public uint ObjectSerial { get; set; }
    public int ClassClan { get; set; }
    public int State { get; set; }
    public int SizeLevel { get; set; }
    public uint CharSoundCode { get; set; }
    public uint JobCode { get; set; }
    public int Level { get; set; }
    public int Strength { get; set; }
    public int Spirit { get; set; }
    public int Talent { get; set; }
    public int Dexterity { get; set; }
    public int Health { get; set; }
    public int Accuracy { get; set; }
    public int AttackRating { get; set; }
    public int AttackDamageMin { get; set; }
    public int AttackDamageMax { get; set; }
    public int AttackSpeed { get; set; }
    public int ShootingRange { get; set; }
    public int CriticalHit { get; set; }
    public int Defence { get; set; }
    public int ChanceBlock { get; set; }
    public int Absorption { get; set; }
    public int MoveSpeed { get; set; }
    public int Sight { get; set; }
    public short WeightCurrent { get; set; }
    public short WeightMax { get; set; }
    public short[] Resistance { get; } = new short[8];
    public short[] AttackResistance { get; } = new short[8];
    public short LifeCurrent { get; set; }
    public short LifeMax { get; set; }
    public short ManaCurrent { get; set; }
    public short ManaMax { get; set; }
    public short StaminaCurrent { get; set; }
    public short StaminaMax { get; set; }
    public float LifeRegen { get; set; }
    public float ManaRegen { get; set; }
    public float StaminaRegen { get; set; }
    public int Exp { get; set; }
    public int NextExp { get; set; }
    public int Money { get; set; }
    public uint Brood { get; set; }
    public int StatePoint { get; set; }
    public byte[] UpdateInfo { get; } = new byte[4];
    public short ArrowPosiX { get; set; }
    public short ArrowPosiY { get; set; }
    public int PotionSpace { get; set; }
    public int LifeFunction { get; set; }
    public int ManaFunction { get; set; }
    public int StaminaFunction { get; set; }
    public short DamageFunctionMin { get; set; }
    public short DamageFunctionMax { get; set; }
    public uint ReformCode { get; set; }
    public uint ChangeJob { get; set; }
    public uint JobBitMask { get; set; }
    public ushort PlayerKilling0 { get; set; }
    public ushort PlayerKilling1 { get; set; }
    public ushort PlayClass0 { get; set; }
    public ushort PlayClass1 { get; set; }
    public int ExpHigh { get; set; }
    public uint EventTime { get; set; }
    public short EventParam0 { get; set; }
    public short EventParam1 { get; set; }
    public short PresentItem0 { get; set; }
    public short PresentItem1 { get; set; }
    public short GravityScrollCheck0 { get; set; }
    public short GravityScrollCheck1 { get; set; }
    public uint[] Temp { get; } = new uint[TempCount];
    public uint LoginServerIp { get; set; }
    public uint LoginServerSafeKey { get; set; }
    public ushort Version0 { get; set; }
    public ushort Version1 { get; set; }

    public long Experience
    {
        get => (long)ExpHigh << 32 | (uint)Exp;
        set
        {
            Exp = unchecked((int)value);
            ExpHigh = (int)(value >> 32);
        }
    }

    public void Write(ref PacketWriter writer)
    {
        var start = writer.Position;
        writer.Text(Name, 32);
        writer.Text(ModelName, 64);
        writer.Text(ModelName2, 60);
        writer.UInt32(ModelNameCode2);
        writer.UInt32(ObjectSerial);
        writer.Int32(ClassClan);
        writer.Int32(State);
        writer.Int32(SizeLevel);
        writer.UInt32(CharSoundCode);
        writer.UInt32(JobCode);
        writer.Int32(Level);
        writer.Int32(Strength);
        writer.Int32(Spirit);
        writer.Int32(Talent);
        writer.Int32(Dexterity);
        writer.Int32(Health);
        writer.Int32(Accuracy);
        writer.Int32(AttackRating);
        writer.Int32(AttackDamageMin);
        writer.Int32(AttackDamageMax);
        writer.Int32(AttackSpeed);
        writer.Int32(ShootingRange);
        writer.Int32(CriticalHit);
        writer.Int32(Defence);
        writer.Int32(ChanceBlock);
        writer.Int32(Absorption);
        writer.Int32(MoveSpeed);
        writer.Int32(Sight);
        writer.Int16(WeightCurrent);
        writer.Int16(WeightMax);
        foreach (var value in Resistance)
        {
            writer.Int16(value);
        }

        foreach (var value in AttackResistance)
        {
            writer.Int16(value);
        }

        writer.Int16(LifeCurrent);
        writer.Int16(LifeMax);
        writer.Int16(ManaCurrent);
        writer.Int16(ManaMax);
        writer.Int16(StaminaCurrent);
        writer.Int16(StaminaMax);
        writer.Single(LifeRegen);
        writer.Single(ManaRegen);
        writer.Single(StaminaRegen);
        writer.Int32(Exp);
        writer.Int32(NextExp);
        writer.Int32(Money);
        writer.UInt32(0);
        writer.UInt32(Brood);
        writer.Int32(StatePoint);
        writer.Bytes(UpdateInfo);
        writer.Int16(ArrowPosiX);
        writer.Int16(ArrowPosiY);
        writer.Int32(PotionSpace);
        writer.Int32(LifeFunction);
        writer.Int32(ManaFunction);
        writer.Int32(StaminaFunction);
        writer.Int16(DamageFunctionMin);
        writer.Int16(DamageFunctionMax);
        writer.UInt32(ReformCode);
        writer.UInt32(ChangeJob);
        writer.UInt32(JobBitMask);
        writer.UInt16(PlayerKilling0);
        writer.UInt16(PlayerKilling1);
        writer.UInt16(PlayClass0);
        writer.UInt16(PlayClass1);
        writer.Int32(ExpHigh);
        writer.UInt32(EventTime);
        writer.Int16(EventParam0);
        writer.Int16(EventParam1);
        writer.Int16(PresentItem0);
        writer.Int16(PresentItem1);
        writer.Int16(GravityScrollCheck0);
        writer.Int16(GravityScrollCheck1);
        foreach (var value in Temp)
        {
            writer.UInt32(value);
        }

        writer.UInt32(LoginServerIp);
        writer.UInt32(LoginServerSafeKey);
        writer.UInt16(Version0);
        writer.UInt16(Version1);
        if (writer.Position - start != Size)
        {
            throw new InvalidOperationException("Character info record layout drifted from 464 bytes.");
        }
    }

    public static CharacterInfo Read(ref PacketReader reader)
    {
        var start = reader.Position;
        var info = new CharacterInfo
        {
            Name = reader.Text(32),
            ModelName = reader.Text(64),
            ModelName2 = reader.Text(60),
            ModelNameCode2 = reader.UInt32(),
            ObjectSerial = reader.UInt32(),
            ClassClan = reader.Int32(),
            State = reader.Int32(),
            SizeLevel = reader.Int32(),
            CharSoundCode = reader.UInt32(),
            JobCode = reader.UInt32(),
            Level = reader.Int32(),
            Strength = reader.Int32(),
            Spirit = reader.Int32(),
            Talent = reader.Int32(),
            Dexterity = reader.Int32(),
            Health = reader.Int32(),
            Accuracy = reader.Int32(),
            AttackRating = reader.Int32(),
            AttackDamageMin = reader.Int32(),
            AttackDamageMax = reader.Int32(),
            AttackSpeed = reader.Int32(),
            ShootingRange = reader.Int32(),
            CriticalHit = reader.Int32(),
            Defence = reader.Int32(),
            ChanceBlock = reader.Int32(),
            Absorption = reader.Int32(),
            MoveSpeed = reader.Int32(),
            Sight = reader.Int32(),
            WeightCurrent = reader.Int16(),
            WeightMax = reader.Int16()
        };
        for (var index = 0; index < info.Resistance.Length; index++)
        {
            info.Resistance[index] = reader.Int16();
        }

        for (var index = 0; index < info.AttackResistance.Length; index++)
        {
            info.AttackResistance[index] = reader.Int16();
        }

        info.LifeCurrent = reader.Int16();
        info.LifeMax = reader.Int16();
        info.ManaCurrent = reader.Int16();
        info.ManaMax = reader.Int16();
        info.StaminaCurrent = reader.Int16();
        info.StaminaMax = reader.Int16();
        info.LifeRegen = reader.Single();
        info.ManaRegen = reader.Single();
        info.StaminaRegen = reader.Single();
        info.Exp = reader.Int32();
        info.NextExp = reader.Int32();
        info.Money = reader.Int32();
        reader.Skip(4);
        info.Brood = reader.UInt32();
        info.StatePoint = reader.Int32();
        reader.Bytes(4).CopyTo(info.UpdateInfo);
        info.ArrowPosiX = reader.Int16();
        info.ArrowPosiY = reader.Int16();
        info.PotionSpace = reader.Int32();
        info.LifeFunction = reader.Int32();
        info.ManaFunction = reader.Int32();
        info.StaminaFunction = reader.Int32();
        info.DamageFunctionMin = reader.Int16();
        info.DamageFunctionMax = reader.Int16();
        info.ReformCode = reader.UInt32();
        info.ChangeJob = reader.UInt32();
        info.JobBitMask = reader.UInt32();
        info.PlayerKilling0 = reader.UInt16();
        info.PlayerKilling1 = reader.UInt16();
        info.PlayClass0 = reader.UInt16();
        info.PlayClass1 = reader.UInt16();
        info.ExpHigh = reader.Int32();
        info.EventTime = reader.UInt32();
        info.EventParam0 = reader.Int16();
        info.EventParam1 = reader.Int16();
        info.PresentItem0 = reader.Int16();
        info.PresentItem1 = reader.Int16();
        info.GravityScrollCheck0 = reader.Int16();
        info.GravityScrollCheck1 = reader.Int16();
        for (var index = 0; index < TempCount; index++)
        {
            info.Temp[index] = reader.UInt32();
        }

        info.LoginServerIp = reader.UInt32();
        info.LoginServerSafeKey = reader.UInt32();
        info.Version0 = reader.UInt16();
        info.Version1 = reader.UInt16();
        if (reader.Position - start != Size)
        {
            throw new InvalidOperationException("Character info record layout drifted from 464 bytes.");
        }

        return info;
    }
}

// PLAYERINFO packet: {size, code, character info, dwObjectSerial, x, y, z, ax, ay, az, state} = 504 bytes.
public sealed record PlayerInfoPacket(CharacterInfo Character, uint ObjectSerial, int X, int Y, int Z, int AngleX = 0, int AngleY = 0, int AngleZ = 0, int State = 0)
{
    public const int Size = 504;

    public int Write(Span<byte> destination, uint code = PristonOpcodes.PlayerInfo)
    {
        var writer = new PacketWriter(destination, code);
        Character.Write(ref writer);
        writer.UInt32(ObjectSerial);
        writer.Int32(X);
        writer.Int32(Y);
        writer.Int32(Z);
        writer.Int32(AngleX);
        writer.Int32(AngleY);
        writer.Int32(AngleZ);
        writer.Int32(State);
        return writer.Finish();
    }

    public static PlayerInfoPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        var character = CharacterInfo.Read(ref reader);
        return new PlayerInfoPacket(character, reader.UInt32(), reader.Int32(), reader.Int32(), reader.Int32(),
            reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
    }
}
