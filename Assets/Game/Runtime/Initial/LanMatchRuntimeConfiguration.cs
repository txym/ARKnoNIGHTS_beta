using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ArknoNights.Battle.Infrastructure;
using ArknoNights.Lobby;
using ArknoNights.Match;
using ArknoNights.MatchAI;
using UnityEngine;

internal static class LanMatchRuntimeConfiguration
{
    internal const string ProtocolVersion = "lan-match-v2";
    internal const string MatchRulesVersion = "match-rules-v1";
    internal const string BattleCoreVersion = "battle-core-v1";
    private const string UnitCatalogResource = "BattleData/unit-catalog-v1";
    private const string AbilityCatalogResource = "BattleData/ability-catalog-v1";
    private static readonly HashSet<string> ShopExcludedTypeIds =
        new HashSet<string>(
            new[] { "1000", "1137", "1138", "2033", "5504", "10002" },
            StringComparer.Ordinal);

    internal static bool TryCreate(
        out LanMatchSessionConfiguration configuration,
        out string diagnosticCode)
    {
        return TryCreateWithRuntimeAssets(
            out configuration,
            out _,
            out diagnosticCode);
    }

    internal static bool TryCreateWithRuntimeAssets(
        out LanMatchSessionConfiguration configuration,
        out LanMatchRuntimeAssets runtimeAssets,
        out string diagnosticCode)
    {
        configuration = null;
        runtimeAssets = null;
        var units = UnitCatalogLoader.LoadFromResources(UnitCatalogResource);
        if (!units.Success)
        {
            diagnosticCode = units.Errors.Count == 0
                ? "match.runtime.unitCatalog.invalid"
                : units.Errors[0].Code;
            return false;
        }
        var abilities = AbilityCatalogLoader.LoadFromResources(
            AbilityCatalogResource,
            units.Catalog);
        if (!abilities.Success)
        {
            diagnosticCode = abilities.Errors.Count == 0
                ? "match.runtime.abilityCatalog.invalid"
                : abilities.Errors[0].Code;
            return false;
        }

        if (!TryComputeCatalogHashes(
            units.Catalog,
            abilities.Catalog,
            out var unitHash,
            out var abilityHash,
            out diagnosticCode))
        {
            return false;
        }
        var manifest = new MatchCompatibilityManifest(
            ProtocolVersion,
            MatchRulesVersion,
            BattleCoreVersion,
            unitHash,
            abilityHash);
        var shopCatalog = new MatchShopCatalog(
            MatchRulesVersion,
            unitHash,
            units.Catalog.Entries.Select(entry =>
                new MatchShopCatalogEntry(
                    entry.Definition.TypeId,
                    entry.Rarity,
                    !ShopExcludedTypeIds.Contains(entry.Definition.TypeId),
                    3,
                    entry.DeploymentCost,
                    entry.LegacyUnitTypeId)));
        if (!shopCatalog.TryValidate(out _, out diagnosticCode))
            return false;
        var botController = new BotController();
        var connectionControl = new LanMatchBotConnectionControl(
            botController);
        configuration = new LanMatchSessionConfiguration(
            manifest,
            shopCatalog,
            LobbyProfile.MaximumAvatarIndex
                - LobbyProfile.MinimumAvatarIndex
                + 1,
            connectionControl,
            false,
            botController);
        runtimeAssets = new LanMatchRuntimeAssets(
            units.Catalog,
            abilities.Catalog,
            shopCatalog,
            botController,
            connectionControl);
        diagnosticCode = string.Empty;
        return true;
    }

    private static bool TryComputeCatalogHashes(
        UnitCatalog units,
        AbilityCatalog abilities,
        out string unitHash,
        out string abilityHash,
        out string diagnosticCode)
    {
        unitHash = null;
        abilityHash = null;
        try
        {
            var unitInputs = units.Entries
                .Select(entry => new CatalogHashInput(
                    "unit/" + entry.Definition.TypeId,
                    Encoding.UTF8.GetBytes(
                        "legacy="
                        + entry.LegacyUnitTypeId.ToString(
                            CultureInfo.InvariantCulture)
                        + "|rarity="
                        + entry.Rarity.ToString(
                            CultureInfo.InvariantCulture)
                        + "|deploymentCost="
                        + entry.DeploymentCost.ToString(
                            CultureInfo.InvariantCulture)
                        + "|definition="
                        + CanonicalSimulationValue(
                            entry.Definition))))
                .Concat(new[]
                {
                    new CatalogHashInput(
                        "catalog/id",
                        Encoding.UTF8.GetBytes(units.CatalogId)),
                    new CatalogHashInput(
                        "catalog/schema",
                        Encoding.UTF8.GetBytes(units.SchemaVersion))
                })
                .ToArray();
            var abilityInputs = abilities.Abilities
                .Select(ability => new CatalogHashInput(
                    "ability/" + ability.AbilityId,
                    Encoding.UTF8.GetBytes(
                        CanonicalSimulationValue(ability))))
                .Concat(new[]
                {
                    new CatalogHashInput(
                        "catalog/id",
                        Encoding.UTF8.GetBytes(abilities.CatalogId)),
                    new CatalogHashInput(
                        "catalog/schema",
                        Encoding.UTF8.GetBytes(abilities.SchemaVersion))
                })
                .ToArray();
            unitHash = ComputeCanonicalHashForTests(unitInputs);
            abilityHash = ComputeCanonicalHashForTests(abilityInputs);
            diagnosticCode = string.Empty;
            return true;
        }
        catch (Exception)
        {
            diagnosticCode =
                "match.runtime.catalog.canonicalization.invalid";
            return false;
        }
    }

    internal static string ComputeCanonicalHashForTests(
        params CatalogHashInput[] inputs)
    {
        if (inputs == null) throw new ArgumentNullException(nameof(inputs));
        var canonical = string.Join(
            "\n",
            inputs
                .OrderBy(input => input.Key, StringComparer.Ordinal)
                .Select(input =>
                    input.Key
                    + ":"
                    + Convert.ToBase64String(input.Bytes ?? Array.Empty<byte>())));
        return Sha256(Encoding.UTF8.GetBytes(canonical));
    }

    private static string CanonicalSimulationValue(object value)
    {
        var builder = new StringBuilder();
        AppendCanonicalSimulationValue(builder, value);
        return builder.ToString();
    }

    private static void AppendCanonicalSimulationValue(
        StringBuilder builder,
        object value)
    {
        if (value == null)
        {
            builder.Append("null;");
            return;
        }
        if (value is string text)
        {
            builder.Append("s:")
                .Append(text.Length.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':')
                .Append(text)
                .Append(';');
            return;
        }
        if (value is bool boolean)
        {
            builder.Append(boolean ? "b:1;" : "b:0;");
            return;
        }
        var type = value.GetType();
        if (type.IsEnum)
        {
            builder.Append("e:")
                .Append(Convert.ToInt64(
                    value,
                    CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture))
                .Append(';');
            return;
        }
        if (value is IFormattable formattable
            && (type.IsPrimitive || value is decimal))
        {
            builder.Append("n:")
                .Append(formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture))
                .Append(';');
            return;
        }
        if (value is IEnumerable enumerable)
        {
            builder.Append("a[");
            foreach (var item in enumerable)
                AppendCanonicalSimulationValue(builder, item);
            builder.Append("];");
            return;
        }

        builder.Append("o{");
        foreach (var property in type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.CanRead
                && property.GetIndexParameters().Length == 0
                && IsSimulationProperty(property.Name))
            .OrderBy(property =>
                property.Name,
                StringComparer.Ordinal))
        {
            builder.Append(property.Name.Length.ToString(
                    CultureInfo.InvariantCulture))
                .Append(':')
                .Append(property.Name)
                .Append('=');
            AppendCanonicalSimulationValue(
                builder,
                property.GetValue(value, null));
        }
        builder.Append("};");
    }

    private static bool IsSimulationProperty(string propertyName)
    {
        return !string.Equals(
                propertyName,
                "DisplayNameZhHans",
                StringComparison.Ordinal)
            && !string.Equals(
                propertyName,
                "DescriptionZhHans",
                StringComparison.Ordinal)
            && !string.Equals(
                propertyName,
                "CanonicalSummary",
                StringComparison.Ordinal)
            && !propertyName.EndsWith(
                "AnimationKey",
                StringComparison.Ordinal)
            && !propertyName.EndsWith(
                "AnimationSequenceKey",
                StringComparison.Ordinal)
            && !propertyName.EndsWith(
                "PresentationStateTag",
                StringComparison.Ordinal);
    }

    private static string Sha256(byte[] bytes)
    {
        using (var sha = SHA256.Create())
        {
            var hash = sha.ComputeHash(bytes ?? Array.Empty<byte>());
            var builder = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
                builder.Append(hash[index].ToString("x2"));
            return builder.ToString();
        }
    }
}

internal sealed class LanMatchRuntimeAssets
{
    internal LanMatchRuntimeAssets(
        UnitCatalog units,
        AbilityCatalog abilities,
        MatchShopCatalog shop,
        BotController bots,
        LanMatchBotConnectionControl connectionControl)
    {
        Units = units ?? throw new ArgumentNullException(nameof(units));
        Abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
        Shop = shop ?? throw new ArgumentNullException(nameof(shop));
        Bots = bots ?? throw new ArgumentNullException(nameof(bots));
        ConnectionControl = connectionControl
            ?? throw new ArgumentNullException(nameof(connectionControl));
    }

    internal UnitCatalog Units { get; }
    internal AbilityCatalog Abilities { get; }
    internal MatchShopCatalog Shop { get; }
    internal BotController Bots { get; }
    internal LanMatchBotConnectionControl ConnectionControl { get; }
}

internal sealed class LanMatchBotConnectionControl :
    IMatchConnectionControlSink,
    IMatchConnectionControlBinding
{
    private readonly BotController bots;
    private IMatchBotHost host;

    internal LanMatchBotConnectionControl(BotController bots)
    {
        this.bots = bots ?? throw new ArgumentNullException(nameof(bots));
    }

    internal string LastDiagnosticCode { get; private set; } = string.Empty;

    public void Bind(IMatchBotHost value)
    {
        host = value ?? throw new ArgumentNullException(nameof(value));
    }

    public void ConnectionLost(
        string playerId,
        MatchPhase phase,
        int roundNumber)
    {
        if (host == null) return;
        LastDiagnosticCode = bots.NotifyDisconnected(
            host,
            playerId).DiagnosticCode;
    }

    public void RestoreHumanControl(
        string playerId,
        MatchPhase phase,
        int roundNumber)
    {
        if (host == null) return;
        LastDiagnosticCode = bots.RestoreHumanControl(
            host,
            playerId).DiagnosticCode;
    }

    public void ExplicitQuit(
        string playerId,
        MatchPhase phase,
        int roundNumber)
    {
        if (host == null) return;
        LastDiagnosticCode = bots.NotifyVoluntaryQuit(
            host,
            playerId).DiagnosticCode;
    }
}

internal sealed class CatalogHashInput
{
    internal CatalogHashInput(string key, byte[] bytes)
    {
        Key = key ?? string.Empty;
        Bytes = bytes == null ? Array.Empty<byte>() : (byte[])bytes.Clone();
    }

    internal string Key { get; }
    internal byte[] Bytes { get; }
}

internal sealed class PlayerPrefsLocalProfileIdentityStore :
    ILocalProfileIdentityStore
{
    private const string DefaultPlayerIdKey =
        "LanLobby.Profile.PlayerId";
    private readonly string playerIdKey;

    public PlayerPrefsLocalProfileIdentityStore(
        string playerIdKey = DefaultPlayerIdKey)
    {
        if (string.IsNullOrWhiteSpace(playerIdKey))
            throw new ArgumentException(
                "The PlayerId key is required.",
                nameof(playerIdKey));
        this.playerIdKey = playerIdKey;
    }

    public bool TryLoad(out string playerId)
    {
        playerId = PlayerPrefs.GetString(playerIdKey, string.Empty);
        return !string.IsNullOrEmpty(playerId);
    }

    public void Save(string playerId)
    {
        PlayerPrefs.SetString(playerIdKey, playerId);
        PlayerPrefs.Save();
    }

    public void Clear()
    {
        PlayerPrefs.DeleteKey(playerIdKey);
        PlayerPrefs.Save();
    }
}

internal sealed class PlayerPrefsReconnectCredentialStore :
    IReconnectCredentialStore
{
    private const string DefaultCredentialKey =
        "LanMatch.ReconnectCredential.v1";
    private readonly string credentialKey;

    public PlayerPrefsReconnectCredentialStore(
        string credentialKey = DefaultCredentialKey)
    {
        if (string.IsNullOrWhiteSpace(credentialKey))
            throw new ArgumentException(
                "The credential key is required.",
                nameof(credentialKey));
        this.credentialKey = credentialKey;
    }

    public bool TryLoad(out ReconnectCredential credential)
    {
        credential = null;
        var stored = PlayerPrefs.GetString(
            credentialKey,
            string.Empty);
        if (string.IsNullOrEmpty(stored)) return false;
        if (stored.Length > 16384)
        {
            Clear();
            return false;
        }
        try
        {
            var record = JsonUtility.FromJson<CredentialRecord>(stored);
            if (record == null
                || record.schemaVersion != 1
                || string.IsNullOrEmpty(record.payload)
                || record.payload.Length > 8192
                || !FixedEquals(record.checksum, Hash(record.payload)))
            {
                Clear();
                return false;
            }
            var payloadJson = Encoding.UTF8.GetString(
                Convert.FromBase64String(record.payload));
            var payload = JsonUtility.FromJson<CredentialPayload>(payloadJson);
            if (payload == null)
            {
                Clear();
                return false;
            }
            var manifest = new MatchCompatibilityManifest(
                payload.protocolVersion,
                payload.matchRulesVersion,
                payload.battleCoreVersion,
                payload.unitCatalogSha256,
                payload.abilityCatalogSha256);
            var candidate = new ReconnectCredential(
                payload.sessionId,
                payload.hostAddress,
                payload.hostPort,
                payload.playerId,
                payload.token,
                manifest);
            if (!candidate.IsValid)
            {
                Clear();
                return false;
            }
            credential = candidate;
            return true;
        }
        catch (Exception)
        {
            Clear();
            return false;
        }
    }

    public void Save(ReconnectCredential credential)
    {
        if (credential == null || !credential.IsValid)
            throw new ArgumentException(
                "The reconnect credential is invalid.",
                nameof(credential));
        var manifest = credential.CompatibilityManifest;
        var payload = new CredentialPayload
        {
            sessionId = credential.SessionId,
            hostAddress = credential.HostAddress,
            hostPort = credential.HostPort,
            playerId = credential.PlayerId,
            token = credential.Token,
            protocolVersion = manifest.ProtocolVersion,
            matchRulesVersion = manifest.MatchRulesVersion,
            battleCoreVersion = manifest.BattleCoreVersion,
            unitCatalogSha256 = manifest.UnitCatalogSha256,
            abilityCatalogSha256 = manifest.AbilityCatalogSha256
        };
        var payloadBase64 = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
        var record = new CredentialRecord
        {
            schemaVersion = 1,
            payload = payloadBase64,
            checksum = Hash(payloadBase64)
        };
        PlayerPrefs.SetString(
            credentialKey,
            JsonUtility.ToJson(record));
        PlayerPrefs.Save();
    }

    public void Clear()
    {
        PlayerPrefs.DeleteKey(credentialKey);
        PlayerPrefs.Save();
    }

    private static string Hash(string payload)
    {
        using (var sha = SHA256.Create())
        {
            var bytes = sha.ComputeHash(
                Encoding.UTF8.GetBytes(payload ?? string.Empty));
            return Convert.ToBase64String(bytes);
        }
    }

    private static bool FixedEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        var rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        var different = leftBytes.Length ^ rightBytes.Length;
        var maximum = Math.Max(leftBytes.Length, rightBytes.Length);
        for (var index = 0; index < maximum; index++)
        {
            var l = index < leftBytes.Length ? leftBytes[index] : (byte)0;
            var r = index < rightBytes.Length ? rightBytes[index] : (byte)0;
            different |= l ^ r;
        }
        return different == 0;
    }

    [Serializable]
    private sealed class CredentialRecord
    {
        public int schemaVersion;
        public string payload;
        public string checksum;
    }

    [Serializable]
    private sealed class CredentialPayload
    {
        public string sessionId;
        public string hostAddress;
        public int hostPort;
        public string playerId;
        public string token;
        public string protocolVersion;
        public string matchRulesVersion;
        public string battleCoreVersion;
        public string unitCatalogSha256;
        public string abilityCatalogSha256;
    }
}
