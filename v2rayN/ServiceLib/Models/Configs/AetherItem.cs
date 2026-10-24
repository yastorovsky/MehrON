namespace ServiceLib.Models.Configs;

/// <summary>
/// PattN: the settings of Aether that belong to no profile (PattNG's PREF_PSIPHON_REGIONS and the PREF_AETHER_KEYS_*
/// settings of the WARP keys window). They live in guiNConfig.json and go into backups; the WARP keys themselves do not.
/// </summary>
[Serializable]
public class AetherItem
{
    /// <summary>
    /// Psiphon's last report of the countries it can leave from, sorted and comma-joined, for the exit country list of
    /// the Aether editor (PattNG PsiphonServerList.remember); null or empty before the first report.
    /// </summary>
    public string? PsiphonRegions { get; set; }

    // The settings of the WARP keys window (design 3.4, PattNG AetherKeysRepository): its own, no profile's. A null text
    // is a setting never stored, which reads as its default; see AetherKeysSource.SettingsOf.

    /// <summary>The kind of keys to get, the type of an AetherKeyKind (PattNG pref_aether_keys_kind).</summary>
    public string? KeysKind { get; set; }

    /// <summary>The API address; empty for none (PattNG pref_aether_keys_enroll_address).</summary>
    public string? KeysApiAddress { get; set; }

    /// <summary>The API server name, --api-sni; empty for none (PattNG pref_aether_keys_api_sni).</summary>
    public string? KeysApiSni { get; set; }

    /// <summary>The API host, --api-host; empty for none (PattNG pref_aether_keys_api_host).</summary>
    public string? KeysApiHost { get; set; }

    public bool KeysFragment { get; set; }
    public string? KeysFragmentSize { get; set; }
    public string? KeysFragmentDelay { get; set; }
    public bool KeysEch { get; set; }

    /// <summary>The ECH DNS, trimmed; null while it is blank or the default, so that it follows the default.</summary>
    public string? KeysEchDns { get; set; }

    /// <summary>The ECH domain, trimmed; null while it is blank or the default, so that it follows the default.</summary>
    public string? KeysEchDomain { get; set; }

    /// <summary>The type of an AetherFingerprint (PattNG pref_aether_keys_fingerprint).</summary>
    public string? KeysFingerprint { get; set; }

    /// <summary>The name of the profile a key run dials out through; empty for freedom.</summary>
    public string? KeysExitNode { get; set; }

    public string? KeysFinalMask { get; set; }
    public string? KeysDialMode { get; set; }

    /// <summary>The command written by hand; empty while the settings build it.</summary>
    public string? KeysCommand { get; set; }
}
