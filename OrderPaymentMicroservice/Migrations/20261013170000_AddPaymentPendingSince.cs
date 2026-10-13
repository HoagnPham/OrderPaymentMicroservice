using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace OrderPaymentMicroservice.Migrations;

public partial class AddPaymentPendingSince:Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("PendingSince","Payments",type:"timestamp with time zone",nullable:true);
        // Historical start is unknown; last update is a conservative lower-bound age.
        migrationBuilder.Sql("UPDATE \"Payments\" SET \"PendingSince\" = \"UpdatedAt\" WHERE \"Status\" IN (4,5,6);");
        migrationBuilder.CreateIndex("IX_Payments_Status_PendingSince","Payments",new[]{"Status","PendingSince"});
        migrationBuilder.Sql("""
            CREATE FUNCTION preserve_payment_pending_since() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW."Status" IN (4,5,6) THEN
                IF TG_OP = 'UPDATE' AND OLD."Status" IN (4,5,6) AND OLD."PendingSince" IS NOT NULL THEN
                  NEW."PendingSince" := OLD."PendingSince";
                ELSE NEW."PendingSince" := COALESCE(NEW."PendingSince", NEW."UpdatedAt"); END IF;
              ELSE NEW."PendingSince" := NULL; END IF;
              RETURN NEW;
            END; $$;
            CREATE TRIGGER payment_pending_since BEFORE INSERT OR UPDATE ON "Payments"
              FOR EACH ROW EXECUTE FUNCTION preserve_payment_pending_since();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER payment_pending_since ON \"Payments\"; DROP FUNCTION preserve_payment_pending_since();");
        migrationBuilder.DropIndex("IX_Payments_Status_PendingSince","Payments");
        migrationBuilder.DropColumn("PendingSince","Payments");
    }
}
