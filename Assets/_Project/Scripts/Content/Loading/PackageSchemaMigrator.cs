using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OSE.Core;

namespace OSE.Content.Loading
{
    /// <summary>
    /// Applies sequential schema migrations to raw machine.json text before
    /// deserialization. Each migration transforms JSON from one schema version
    /// to the next, allowing the runtime to always deserialize the latest format.
    ///
    /// Migrations operate on raw JSON strings (not deserialized objects) so they
    /// can handle structural changes that would fail deserialization under the
    /// current type definitions.
    /// </summary>
    public static class PackageSchemaMigrator
    {
        /// <summary>Current schema version the runtime expects.</summary>
        public const string CurrentSchemaVersion = "1.0.0";

        private static readonly List<IPackageSchemaMigration> _migrations = new();

        static PackageSchemaMigrator()
        {
            // Register migrations in order here as the schema evolves:
            // _migrations.Add(new Migration_1_0_0_To_1_1_0());
        }

        /// <summary>
        /// Migrates raw JSON from its declared schemaVersion up to
        /// <see cref="CurrentSchemaVersion"/>. Returns the (possibly modified)
        /// JSON and the final schema version.
        /// </summary>
        public static MigrationResult Migrate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new MigrationResult(json, CurrentSchemaVersion, 0);

            string detectedVersion = DetectSchemaVersion(json);
            if (string.IsNullOrEmpty(detectedVersion))
            {
                OseLog.Warn("[SchemaMigrator] No schemaVersion found in JSON. Assuming current version.");
                return new MigrationResult(json, CurrentSchemaVersion, 0);
            }

            if (string.Equals(detectedVersion, CurrentSchemaVersion, StringComparison.Ordinal))
                return new MigrationResult(json, CurrentSchemaVersion, 0);

            int applied = 0;
            string currentVersion = detectedVersion;
            string currentJson = json;

            foreach (IPackageSchemaMigration migration in _migrations)
            {
                if (!string.Equals(migration.FromVersion, currentVersion, StringComparison.Ordinal))
                    continue;

                OseLog.Info($"[SchemaMigrator] Applying migration {migration.FromVersion} → {migration.ToVersion}.");
                currentJson = migration.Apply(currentJson);
                currentVersion = migration.ToVersion;
                applied++;

                if (string.Equals(currentVersion, CurrentSchemaVersion, StringComparison.Ordinal))
                    break;
            }

            if (!string.Equals(currentVersion, CurrentSchemaVersion, StringComparison.Ordinal))
            {
                OseLog.Warn($"[SchemaMigrator] Could not migrate from '{detectedVersion}' to '{CurrentSchemaVersion}'. " +
                            $"Reached '{currentVersion}' after {applied} migration(s). Missing migration?");
            }

            return new MigrationResult(currentJson, currentVersion, applied);
        }

        /// <summary>
        /// Extracts the schemaVersion value from raw JSON without full deserialization.
        /// </summary>
        public static string DetectSchemaVersion(string json)
        {
            // Simple regex avoids allocating a full JSON parse just for version detection.
            var match = Regex.Match(json, "\"schemaVersion\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Replaces the schemaVersion value in raw JSON. Used by migrations
        /// to stamp the new version after transforming the document.
        /// </summary>
        public static string SetSchemaVersion(string json, string newVersion)
        {
            return Regex.Replace(
                json,
                "(\"schemaVersion\"\\s*:\\s*)\"[^\"]+\"",
                $"$1\"{newVersion}\"");
        }
    }

    /// <summary>
    /// A single schema migration that transforms raw JSON from one version to the next.
    /// </summary>
    public interface IPackageSchemaMigration
    {
        string FromVersion { get; }
        string ToVersion { get; }

        /// <summary>
        /// Transforms the raw JSON string. Must update the schemaVersion field
        /// to <see cref="ToVersion"/> via <see cref="PackageSchemaMigrator.SetSchemaVersion"/>.
        /// </summary>
        string Apply(string json);
    }

    public readonly struct MigrationResult
    {
        public readonly string Json;
        public readonly string FinalVersion;
        public readonly int MigrationsApplied;

        public MigrationResult(string json, string finalVersion, int migrationsApplied)
        {
            Json = json;
            FinalVersion = finalVersion;
            MigrationsApplied = migrationsApplied;
        }
    }
}
