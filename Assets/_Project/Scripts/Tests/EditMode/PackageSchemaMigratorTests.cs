using NUnit.Framework;
using OSE.Content.Loading;

namespace OSE.Tests.EditMode
{
    [TestFixture]
    public class PackageSchemaMigratorTests
    {
        private const string SampleJson =
            "{ \"schemaVersion\": \"1.0.0\", \"machine\": { \"id\": \"test\" } }";

        [Test]
        public void DetectSchemaVersion_Returns_Version_From_Json()
        {
            string version = PackageSchemaMigrator.DetectSchemaVersion(SampleJson);
            Assert.AreEqual("1.0.0", version);
        }

        [Test]
        public void DetectSchemaVersion_Returns_Null_When_Missing()
        {
            string version = PackageSchemaMigrator.DetectSchemaVersion("{ \"machine\": {} }");
            Assert.IsNull(version);
        }

        [Test]
        public void SetSchemaVersion_Replaces_Version()
        {
            string result = PackageSchemaMigrator.SetSchemaVersion(SampleJson, "2.0.0");
            Assert.IsTrue(result.Contains("\"2.0.0\""));
            Assert.IsFalse(result.Contains("\"1.0.0\""));
        }

        [Test]
        public void Migrate_Current_Version_Returns_Zero_Migrations()
        {
            MigrationResult result = PackageSchemaMigrator.Migrate(SampleJson);
            Assert.AreEqual(0, result.MigrationsApplied);
            Assert.AreEqual("1.0.0", result.FinalVersion);
            Assert.AreEqual(SampleJson, result.Json);
        }

        [Test]
        public void Migrate_Null_Json_Returns_Zero_Migrations()
        {
            MigrationResult result = PackageSchemaMigrator.Migrate(null);
            Assert.AreEqual(0, result.MigrationsApplied);
        }

        [Test]
        public void Migrate_Empty_Json_Returns_Zero_Migrations()
        {
            MigrationResult result = PackageSchemaMigrator.Migrate("");
            Assert.AreEqual(0, result.MigrationsApplied);
        }

        [Test]
        public void Migrate_Missing_SchemaVersion_Assumes_Current()
        {
            MigrationResult result = PackageSchemaMigrator.Migrate("{ \"machine\": {} }");
            Assert.AreEqual(PackageSchemaMigrator.CurrentSchemaVersion, result.FinalVersion);
            Assert.AreEqual(0, result.MigrationsApplied);
        }
    }
}
