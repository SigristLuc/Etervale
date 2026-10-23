using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Monster potions (count and the chance a spawn carries them; the virtual lives of three bosses) and the key
// character of each motion clip, which selects the skill a monster performs with that clip.
[Migration(21)]
public sealed class M021MonsterSkillData : Migration
{
    public override void Up()
    {
        Alter.Table("monsters")
            .AddColumn("potion_count").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("potion_percent").AsInt32().NotNullable().WithDefaultValue(0);

        Alter.Table("motion_slots")
            .AddColumn("key_code").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("key_code").FromTable("motion_slots");
        Delete.Column("potion_percent").FromTable("monsters");
        Delete.Column("potion_count").FromTable("monsters");
    }
}
