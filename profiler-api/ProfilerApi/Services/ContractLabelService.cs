using ProfilerApi.Models;

namespace ProfilerApi.Services;

public class ContractLabelService
{
    private static readonly Dictionary<string, (string Label, string Category)> KnownContracts = new(StringComparer.OrdinalIgnoreCase)
    {
        // ==================== DEXes ====================
        ["0x7a250d5630B4cF539739dF2C5dAcb4c659F2488D"] = ("Uniswap V2 Router", "dex"),
        ["0xE592427A0AEce92De3Edee1F18E0157C05861564"] = ("Uniswap V3 Router", "dex"),
        ["0x3fC91A3afd70395Cd496C647d5a6CC9D4B2b7FAD"] = ("Uniswap Universal Router", "dex"),
        ["0x68b3465833fb72A70ecDF485E0e4C7bD8665Fc45"] = ("Uniswap V3 Router 2", "dex"),
        ["0xEf1c6E67703c7BD7107eed8303Fbe6EC2554BF6B"] = ("Uniswap Universal Router (old)", "dex"),
        ["0x000000000022D473030F116dDEE9F6B43aC78BA3"] = ("Uniswap Permit2", "dex"),
        ["0xd9e1cE17f2641f24aE83637ab66a2cca9C378B9F"] = ("SushiSwap Router", "dex"),
        ["0x1111111254EEB25477B68fb85Ed929f73A960582"] = ("1inch V5 Router", "dex"),
        ["0x1111111254fb6c44bAC0beD2854e76F90643097d"] = ("1inch V4 Router", "dex"),
        ["0x6131B5fae19EA4f9D964eAc0408E4408b66337b5"] = ("1inch V6 Router", "dex"),
        ["0xDef1C0ded9bec7F1a1670819833240f027b25EfF"] = ("0x Exchange Proxy", "dex"),
        ["0xbEbc44782C7dB0a1A60Cb6fe97d0b483032FF1C7"] = ("Curve 3pool", "dex"),
        ["0xD51a44d3FaE010294C616388b506AcdA1bfAAE46"] = ("Curve Tricrypto2", "dex"),
        ["0x99a58482BD75cbab83b27EC03CA68fF489b5788f"] = ("Curve crvUSD/USDT", "dex"),
        ["0x4DEcE678ceceb27446b35C672dC7d61F30bAD69E"] = ("Curve crvUSD/USDC", "dex"),
        ["0xDC24316b9AE028F1497c275EB9192a3Ea0f67022"] = ("Curve stETH/ETH", "dex"),
        ["0xf5F5B97624542D72A9E06f04804Bf81baA15e2B4"] = ("Curve USDT/WBTC/ETH", "dex"),
        ["0xA5407eAE9Ba41422680e2e00537571bcC53efBfD"] = ("Curve sUSD Pool", "dex"),
        ["0x6A000F20005980200259B80c5102003040001068"] = ("ParaSwap V6", "dex"),
        ["0xE66B31678d6C16E9ebf358268a790B763C133750"] = ("0x Coinbase Wallet Proxy", "dex"),
        ["0x881D40237659C251811CEC9c364ef91dC08D300C"] = ("Metamask Swap Router", "dex"),
        ["0x9008D19f58AAbD9eD0D60971565AA8510560ab41"] = ("CoW Protocol", "dex"),
        ["0xBA12222222228d8Ba445958a75a0704d566BF2C8"] = ("Balancer V2 Vault", "dex"),

        // ==================== NFT Marketplaces ====================
        ["0x00000000000000ADc04C56Bf30aC9d3c0aAF14dC"] = ("OpenSea Seaport 1.5", "nft"),
        ["0x00000000006c3852cbEf3e08E8dF289169EdE581"] = ("OpenSea Seaport 1.1", "nft"),
        ["0x0000000000000068F116a894984e2DB1123eB395"] = ("OpenSea Seaport 1.6", "nft"),
        ["0x29469395eAf6f95920E59F858042f0e28D98a20B"] = ("Blur Marketplace", "nft"),
        ["0x39da41747a83aeE658334415666f3EF92DD0D541"] = ("Blur Pool", "nft"),
        ["0xb2ecfE4E4D61f8790bbb9DE2D1259B9e2410CEA5"] = ("Blur Blend", "nft"),
        ["0x0000000000E655fAe4d56241588680F86E3b2377"] = ("LooksRare Exchange", "nft"),
        ["0x59728544B08AB483533076417FbBB2fD0B17CE3a"] = ("LooksRare V2", "nft"),
        ["0x00000000000001ad428e4906aE43D8F9852d0dD6"] = ("X2Y2 Exchange", "nft"),
        ["0x74312363e45DCaBA76c59ec49a7Aa8A65a67EeD3"] = ("X2Y2 Router", "nft"),
        ["0x2B2e8cDA09bBA9660dCA5cB6233787738Ad68329"] = ("Sudoswap AMM", "nft"),

        // ==================== Lending & Borrowing ====================
        ["0x87870Bca3F3fD6335C3F4ce8392D69350B4fA4E2"] = ("Aave V3 Pool", "lending"),
        ["0x7d2768dE32b0b80b7a3454c06BdAc94A69DDc7A9"] = ("Aave V2 Pool", "lending"),
        ["0xc3d688B66703497DAA19211EEdff47f25384cdc3"] = ("Compound V3 cUSDC", "lending"),
        ["0x3d9819210A31b4961b30EF54bE2aeD79B9c9Cd3B"] = ("Compound Comptroller", "lending"),
        ["0xA238Dd80C259a72e81d7e4664a9801593F98d1c5"] = ("Compound V3 cWETH", "lending"),
        ["0x5f4eC3Df9cbd43714FE2740f5E3616155c5b8419"] = ("Chainlink ETH/USD Feed", "oracle"),
        ["0x7f39C581F595B53c5cb19bD0b3f8dA6c935E2Ca0"] = ("Lido wstETH", "staking"),
        ["0xae7ab96520DE3A18E5e111B5EaAb095312D7fE84"] = ("Lido stETH", "staking"),
        ["0xBe9895146f7AF43049ca1c1AE358B0541Ea49704"] = ("Coinbase cbETH", "staking"),
        ["0xae78736Cd615f374D3085123A210448E74Fc6393"] = ("Rocket Pool rETH", "staking"),
        ["0xf951E335afb289353dc249e82926178EaC7DEd78"] = ("Rocket Pool rETH Swap", "staking"),
        ["0xFe2e637202056d30016725477c5da089Ab0A043A"] = ("sfrxETH", "staking"),
        ["0x5E8422345238F34275888049021821E8E08CAa1f"] = ("frxETH", "staking"),
        ["0xA35b1B31Ce002FBF2058D22F30f95D405200A15b"] = ("EtherFi eETH", "staking"),
        ["0xCd5fE23C85820F7B72D0926FC9b05b43E359b7ee"] = ("EtherFi weETH", "staking"),
        ["0xd5F7838F5C461fefF7FE49ea5ebaF7728bB0ADfa"] = ("Mantle mETH", "staking"),
        ["0xBf5495Efe5DB9ce00f80364C8B423567e58d2110"] = ("Renzo ezETH", "staking"),
        ["0xFAe103DC9cf190eD75350761e95403b7b8aFa6c0"] = ("Swell rswETH", "staking"),
        ["0xf1C9acDc66974dFB6dEcB12aA385b9cD01190E38"] = ("Kelp rsETH", "staking"),

        // ==================== Bridges ====================
        ["0x3ee18B2214AFF97000D974cf647E7C347E8fa585"] = ("Wormhole Token Bridge", "bridge"),
        ["0x8731d54E9D02c286767d56ac03e8037C07e01e98"] = ("Stargate Router", "bridge"),
        ["0x4Dbd4fc535Ac27206064B68FfCf827b0A60BAB3f"] = ("Arbitrum Inbox", "bridge"),
        ["0x99C9fc46f92E8a1c0deC1b1747d010903E884bE1"] = ("Optimism Bridge", "bridge"),
        ["0x3154Cf16ccdb4C6d922629664174b904d80F2C35"] = ("Base Bridge", "bridge"),
        ["0x49048044D57e1C92A77f79988d21Fa8fAF36CAB2"] = ("Base Portal", "bridge"),
        ["0xe5e30E7c24e4dFcb281A682562E53154C15D3332"] = ("Across V2 Bridge", "bridge"),
        ["0xb8901acB165ed027E32754E0FFe830802c896272"] = ("Hop Bridge (ETH)", "bridge"),
        ["0x1a2FCf2E60bF2e280B2fCD258439dD5a068B85BE"] = ("Hop Bridge (USDC)", "bridge"),
        ["0x66A71Dcef29A0fFBDBE3c6a460a3B5BC225Cd675"] = ("LayerZero Endpoint", "bridge"),
        ["0xd19d4B5d358258f05D7B411E21A1460D11B0876F"] = ("Polygon zkEVM Bridge", "bridge"),
        ["0x2dE1C2f3055a8037D02Be8BaA0F19F45E1c02dCA"] = ("CCIP Router", "bridge"),

        // ==================== DeFi Protocols ====================
        ["0x5f3b5DfEb7B28CDbD7FAba78963EE202a494e2A2"] = ("Curve veCRV", "defi"),
        ["0xD533a949740bb3306d119CC777fa900bA034cd52"] = ("Curve (CRV)", "defi"),
        ["0x4e3FBD56CD56c3e72c1403e103b45Db9da5B9D2B"] = ("Convex Finance (CVX)", "defi"),
        ["0xF403C135812408BFbE8713b5A23a04b3D48AAE31"] = ("Convex Booster", "defi"),
        ["0xCd6F29dC9Ca217d0973d3D21bF58eDd3CA871a86"] = ("Convex Locker", "defi"),
        ["0xC0c293ce456fF0ED870ADd98a0828Dd4d2903DBF"] = ("Aura Finance", "defi"),
        ["0x9D39A5DE30e57443BfF2A8307A4256c8797A3497"] = ("sUSDe (Ethena)", "defi"),
        ["0x4c9EDD5852cd905f086C759E8383e09bff1E68B3"] = ("USDe (Ethena)", "defi"),
        ["0x35fA164735182de50811E8e2E824cFb9B6118ac2"] = ("EtherFi Staking", "defi"),
        ["0x858646372CC42E1A627fcE94aa7A7033e7CF075A"] = ("EigenLayer Strategy Manager", "defi"),
        ["0x39053D51B77DC0d36036Fc1fCc8Cb819df8Ef37A"] = ("EigenLayer Delegation Manager", "defi"),
        ["0x93c4b944D05dfe6df7645A86cd2206016c51564D"] = ("EigenLayer stETH Strategy", "defi"),
        ["0x54945180dB7943c0ed0FEE7EdaB2Bd24620256Bc"] = ("EigenLayer cbETH Strategy", "defi"),
        ["0x1BeE69b7dFFfA4E2d53C2a2Df135C388AD25dCD2"] = ("EigenLayer rETH Strategy", "defi"),
        ["0x5aB53EE1d50eeF2C1DD3d5402789cd27bB52c1bB"] = ("MakerDAO DSR Manager", "defi"),
        ["0x197E90f9FAD81970bA7976f33CbD77088E5D7cf7"] = ("MakerDAO Pot (DSR)", "defi"),
        ["0x83F20F44975D03b1b09e64809B757c47f942BEeA"] = ("MakerDAO sDAI", "defi"),
        ["0x9759A6Ac90977b93B58547b4A71c78317f391A28"] = ("MakerDAO DaiJoin", "defi"),
        ["0xC36442b4a4522E871399CD717aBDD847Ab11FE88"] = ("Uniswap V3 Positions NFT", "defi"),
        ["0x2F0b23f53734252Bda2277357e97e1517d6B042A"] = ("Yearn Finance yDAI", "defi"),
        ["0xa354F35829Ae975e850e23e9615b11Da1B3dC4DE"] = ("Yearn Finance yvUSDC", "defi"),
        ["0xdA816459F1AB5631232FE5e97a05BBBb94970c95"] = ("Yearn Finance yvDAI", "defi"),
        ["0xa258C4606Ca8206D8aA700cE2143D7db854D168c"] = ("Yearn Finance yvWETH", "defi"),
        ["0xac3E018457B222d93114458476f3E3416Abbe38F"] = ("Frax sfrxETH Vault", "defi"),
        ["0xbAFA44EFE7901E04E39Dad13167D089C559c1138"] = ("Frax FXS Locker", "defi"),
        ["0xA17581A9E3356d9A858b789D68B4d866e593aE94"] = ("Compound Governor Bravo", "defi"),
        ["0xAB8e74017a8Cc7c15FF10571F7A171a2533E6D9C"] = ("Prisma mkUSD", "defi"),

        // ==================== Tokens ====================
        ["0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2"] = ("WETH", "token"),
        ["0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48"] = ("USDC", "token"),
        ["0xdAC17F958D2ee523a2206206994597C13D831ec7"] = ("USDT", "token"),
        ["0x6B175474E89094C44Da98b954EedeAC495271d0F"] = ("DAI", "token"),
        ["0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599"] = ("WBTC", "token"),
        ["0x514910771AF9Ca656af840dff83E8264EcF986CA"] = ("Chainlink (LINK)", "token"),
        ["0x1f9840a85d5aF5bf1D1762F925BDADdC4201F984"] = ("Uniswap (UNI)", "token"),
        ["0x7Fc66500c84A76Ad7e9c93437bFc5Ac33E2DDaE9"] = ("Aave (AAVE)", "token"),
        ["0x9f8F72aA9304c8B593d555F12eF6589cC3A579A2"] = ("Maker (MKR)", "token"),
        ["0xC011a73ee8576Fb46F5E1c5751cA3B9Fe0af2a6F"] = ("Synthetix (SNX)", "token"),
        ["0x5A98FcBEA516Cf06857215779Fd812CA3beF1B32"] = ("Lido (LDO)", "token"),
        ["0xB50721BCf8d664c30412Cfbc6cf7a15145234ad1"] = ("Arbitrum (ARB)", "token"),
        ["0x4200000000000000000000000000000000000042"] = ("Optimism (OP)", "token"),
        ["0xD33526068D116cE69F19A9ee46F0bd304F21A51f"] = ("Rocket Pool (RPL)", "token"),
        ["0xBBbbCA6A901c926F240b89EacB641d8Aec7AEafD"] = ("Loopring (LRC)", "token"),
        ["0x6982508145454Ce325dDbE47a25d4ec3d2311933"] = ("Pepe (PEPE)", "token"),
        ["0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE"] = ("Shiba Inu (SHIB)", "token"),
        ["0xfAbA6f8e4a5E8Ab82F62fe7C39859FA577269BE3"] = ("ONDO Finance", "token"),
        ["0xec53bF9167f50cDEB3Ae105f56099aaA9A8580bc"] = ("EIGEN", "token"),
        ["0x57e114B691Db790C35207b2e685D4A43181e6061"] = ("ENA (Ethena)", "token"),
        ["0x163f8C2467924be0ae7B5347228CABF260318753"] = ("Worldcoin (WLD)", "token"),
        ["0xaea46A60368A7bD060eec7DF8CBa43b7EF41Ad85"] = ("Fetch.ai (FET)", "token"),
        ["0x152649eA73beAb28c5b49B26eb48f7EAD6d4c0bA"] = ("PancakeSwap (CAKE)", "token"),

        // ==================== Mixers ====================
        ["0xd90e2f925DA726b50C4Ed8D0Fb90Ad053324F31b"] = ("Tornado Cash Router", "mixer"),
        ["0x12D66f87A04A9E220743712cE6d9bB1B5616B8Fc"] = ("Tornado Cash 0.1 ETH", "mixer"),
        ["0x47CE0C6eD5B0Ce3d3A51fdb1C52DC66a7c3c2936"] = ("Tornado Cash 1 ETH", "mixer"),
        ["0x910Cbd523D972eb0a6f4cAe4618aD62622b39DbF"] = ("Tornado Cash 10 ETH", "mixer"),
        ["0xA160cdAB225685dA1d56aa342Ad8841c3b53f291"] = ("Tornado Cash 100 ETH", "mixer"),

        // ==================== Governance & Identity ====================
        ["0x57f1887a8BF19b14fC0dF6Fd9B2acc9Af147eA85"] = ("ENS Registrar", "identity"),
        ["0xC18360217D8F7Ab5e7c516566761Ea12Ce7F9D72"] = ("ENS Token", "governance"),
        ["0x323A76393544d5ecca80cd6ef2A560C6a395b7E3"] = ("ENS Governor", "governance"),
        ["0x408ED6354d4973f66138C91495F2f2FCbd8724C3"] = ("Uniswap Governor", "governance"),
        ["0xEC568fffba86c094cf06b22134B23074DFE2252c"] = ("Aave Governor V2", "governance"),
        ["0xBE8E3e3618f7474F8cB1d074A26afFef007E98FB"] = ("Compound Governor", "governance"),
        ["0x5e4be8Bc9637f0EAA1A755019e06A68ce081D58F"] = ("MakerDAO Governance", "governance"),
        ["0xDbD27635A534A3d3169Ef0498beB56Fb9c937489"] = ("Gitcoin Grants", "governance"),
        ["0xDE1bE50c0D2d77E9cdDfe2D2Bf386bfD5b057a51"] = ("Nouns DAO Treasury", "governance"),
        ["0x6f3E6272A167e8AcCb32072d08E0957F9c79223d"] = ("Nouns DAO", "governance"),

        // ==================== CEX Hot Wallets ====================
        ["0x28C6c06298d514Db089934071355E5743bf21d60"] = ("Binance Hot Wallet", "exchange"),
        ["0x21a31Ee1afC51d94C2eFcCAa2092aD1028285549"] = ("Binance Hot Wallet 2", "exchange"),
        ["0xDFd5293D8e347dFe59E90eFd55b2956a1343963d"] = ("Binance Hot Wallet 3", "exchange"),
        ["0xA9D1e08C7793af67e9d92fe308d5697FB81d3E43"] = ("Coinbase Commerce", "exchange"),
        ["0x71660c4005BA85c37ccec55d0C4493E66Fe775d3"] = ("Coinbase Hot Wallet", "exchange"),
        ["0x503828976D22510aad0201ac7EC88293211D23Da"] = ("Coinbase Hot Wallet 2", "exchange"),
        ["0xeb2629a2734e272Bcc07BDA959863f316F4bD4Cf"] = ("Coinbase Wallet", "exchange"),
        ["0x267be1C1D684F78cb4F6a176C4911b741E4Ffdc0"] = ("Kraken Hot Wallet", "exchange"),
        ["0x2910543Af39abA0Cd09dBb2D50200b3E800A63D2"] = ("Kraken Hot Wallet 2", "exchange"),
        ["0x56Eddb7aa87536c09CCc2793473599fD21A8b17F"] = ("Binance US", "exchange"),
        ["0xFf3250A339e3dA1c4278EEE41258f8FDC8193132"] = ("Gemini Hot Wallet", "exchange"),

        // ==================== System ====================
        ["0x0000000000000000000000000000000000000000"] = ("Null Address (Contract Creation)", "system"),
        ["0x000000000000000000000000000000000000dEaD"] = ("Burn Address", "system"),
    };

    public bool IsKnownContract(string address)
        => KnownContracts.ContainsKey(address);

    public (string? Label, string? Category) GetLabel(string address)
    {
        if (KnownContracts.TryGetValue(address, out var info))
            return (info.Label, info.Category);
        return (null, null);
    }

    public List<ContractInteraction> LabelInteractions(List<(string Address, int TxCount)> rawInteractions)
    {
        return rawInteractions.Select(i =>
        {
            var (label, category) = GetLabel(i.Address);
            return new ContractInteraction
            {
                Address = i.Address,
                Label = label,
                Category = category,
                TransactionCount = i.TxCount
            };
        }).ToList();
    }
}
