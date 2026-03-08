using ProfilerApi.Models;

namespace ProfilerApi.Services;

/// <summary>
/// Screens wallet addresses against known sanctioned addresses (OFAC SDN list)
/// and flags interactions with sanctioned protocols like Tornado Cash.
/// </summary>
public class SanctionsService
{
    // OFAC-sanctioned Ethereum addresses (Tornado Cash + known sanctioned wallets)
    private static readonly HashSet<string> SanctionedAddresses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tornado Cash contracts
        "0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b",
        "0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc",
        "0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936",
        "0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF",
        "0xA160cdAB225685dA1d56aa342Ad8841c3b53f291",
        "0xD4B88Df4D29F5CedD6857912842cff3b20C8Cfa3",
        "0xFD8610d20aA15b7B2E3Be39B396a1bC3516c7144",
        "0xF60dD140cFf0706bAE9Cd734Ac3683731920b400",
        "0x22aaA7720ddd5388A3c0A3333430953C68f1849b",
        "0xBA214C1c1928a32Bffe790263E38B4Af9bFCD659",
        "0xb1C8094B234DcE6e03f10a5b673c1d8C69739A00",
        "0x527653eA119F3E6a1F5BD18fbF4714081D7B31ce",
        "0x58E8dCC13BE9780fC42E8723D8EaD4CF46943dF2",
        "0xD691F27f38B395864Ea86CfC7253969B409c362d",
        "0xaEaaC358560e11f52454D997AAFF2c5731B6f8a6",
        "0x1356c899D8C9467C7f71C195612F8A395aBf2f0a",
        "0xA7e5d5A720f06526557c513402f2e6B5fA20b008",
        "0x2717c5e28cf931733106C13dCef8badB2c024D6f",
        "0x03893a7c7463AE47D46bc7f091665f1893656003",
        "0x723B78e67497E85279CB204544566F4dC5d2acA0",
        "0x0E3A09dDA6B20aFbB34aC7cD4A6881493f3E7bf7",
        "0x4F47bc496083C727c5fbe3CE9CDf2B0f6496270c",
        // Lazarus Group associated
        "0x098B716B8Aaf21512996dC57EB0615e2383E2f96",
        "0xa0e1c89Ef1a489c9C7dE96311eD5Ce5D32c20E4B",
        "0x3Cffd56B47B7b41c56258D9C7731ABaDc360E460",
        "0x53b6936513e738f44FB50d2b9476730C0Ab3Bfc1",
    };

    // Categories of sanctioned interaction types
    private static readonly Dictionary<string, string> SanctionedLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b"] = "Tornado Cash Router",
        ["0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc"] = "Tornado Cash 0.1 ETH",
        ["0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936"] = "Tornado Cash 1 ETH",
        ["0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF"] = "Tornado Cash 10 ETH",
        ["0xA160cdAB225685dA1d56aa342Ad8841c3b53f291"] = "Tornado Cash 100 ETH",
        ["0x098B716B8Aaf21512996dC57EB0615e2383E2f96"] = "OFAC Sanctioned (Lazarus Group)",
    };

    public SanctionsCheck Screen(string address, List<ContractInteraction>? interactions)
    {
        var flags = new List<string>();
        var isSanctioned = false;
        var hasSanctionedInteractions = false;

        // Check if the wallet itself is sanctioned
        if (SanctionedAddresses.Contains(address))
        {
            isSanctioned = true;
            var label = SanctionedLabels.GetValueOrDefault(address, "OFAC Sanctioned Address");
            flags.Add($"Address is on OFAC sanctions list: {label}");
        }

        // Check if the wallet has interacted with sanctioned addresses
        if (interactions != null)
        {
            foreach (var interaction in interactions)
            {
                if (SanctionedAddresses.Contains(interaction.Address))
                {
                    hasSanctionedInteractions = true;
                    var label = SanctionedLabels.GetValueOrDefault(interaction.Address, "Sanctioned address");
                    flags.Add($"Interacted with {label} ({interaction.TransactionCount} txs)");
                }
            }
        }

        var riskLevel = isSanctioned ? "sanctioned"
            : hasSanctionedInteractions ? "caution"
            : "clear";

        return new SanctionsCheck
        {
            IsSanctioned = isSanctioned,
            HasSanctionedInteractions = hasSanctionedInteractions,
            RiskLevel = riskLevel,
            Flags = flags
        };
    }

    public bool IsAddressSanctioned(string address)
        => SanctionedAddresses.Contains(address);
}
